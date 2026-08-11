using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// A sheet's conditional formatting, resolved against cached cell values.
///
/// A rule pairs a condition with a <em>differential</em> format (<c>dxf</c>) — a partial style that
/// overlays the cell's own rather than replacing it, so a rule supplying only a fill leaves the font
/// alone. That partiality is the whole design, and it is why this returns an overlay rather than a
/// <see cref="CellStyle"/>.
///
/// Only rules decidable from a cached value are applied. <c>expression</c> rules carry a formula and
/// there is no evaluator here, so they are skipped rather than guessed at; <c>dataBar</c> and
/// <c>iconSet</c> are skipped because they draw chrome inside the cell that the model has no way to
/// express. Corpus coverage: 31 of 51 rules across 7 workbooks.
/// </summary>
sealed class ConditionalFormats
{
    readonly List<(SheetRange Range, List<Rule> Rules)> blocks = [];
    readonly S.Stylesheet? stylesheet;
    readonly ThemeColors? themeColors;

    public ConditionalFormats(S.Worksheet worksheet, S.Stylesheet? stylesheet, ThemeColors? themeColors)
    {
        this.stylesheet = stylesheet;
        this.themeColors = themeColors;

        foreach (var formatting in worksheet.Elements<S.ConditionalFormatting>())
        {
            // sqref is a space-separated list of ranges the block applies to.
            var ranges = (formatting.SequenceOfReferences?.InnerText ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(CellReference.ParseRange)
                .OfType<SheetRange>()
                .ToArray();

            var rules = formatting.Elements<S.ConditionalFormattingRule>()
                .Select(Parse)
                .OfType<Rule>()
                .ToList();

            if (rules.Count == 0)
            {
                continue;
            }

            foreach (var range in ranges)
            {
                blocks.Add((range, rules));
            }
        }
    }

    public bool IsEmpty => blocks.Count == 0;

    /// <summary>
    /// The overlay for a cell, or null when no rule matches.
    ///
    /// Matching rules are applied lowest priority FIRST so the highest-priority one lands last and
    /// wins per property, which is how Excel resolves overlapping rules — they compose rather than
    /// the first simply taking effect.
    /// </summary>
    public ConditionalOverlay? For(int row, int column, string text, double? number)
    {
        ConditionalOverlay? overlay = null;

        var matches = blocks
            .Where(_ => row >= _.Range.FirstRow && row <= _.Range.LastRow &&
                        column >= _.Range.FirstColumn && column <= _.Range.LastColumn)
            .SelectMany(_ => _.Rules)
            .Where(_ => Matches(_, text, number))
            .OrderByDescending(_ => _.Priority);

        foreach (var rule in matches)
        {
            overlay = Apply(overlay, rule, number);
        }

        return overlay;
    }

    ConditionalOverlay Apply(ConditionalOverlay? existing, Rule rule, double? number)
    {
        var result = existing ?? new ConditionalOverlay();

        if (rule.ColorScale is { } scale)
        {
            return result with { BackgroundColorHex = scale.Resolve(number) ?? result.BackgroundColorHex };
        }

        var format = rule.DifferentialFormatId is { } id
            ? stylesheet?.DifferentialFormats?.Elements<S.DifferentialFormat>().ElementAtOrDefault((int) id)
            : null;
        if (format == null)
        {
            return result;
        }

        var fill = format.Fill?.PatternFill;
        var background = Resolve(fill?.BackgroundColor) ?? Resolve(fill?.ForegroundColor);

        return result with
        {
            // A dxf fill states its colour in bgColor, the reverse of a cell style's solid fill —
            // one of the few places SpreadsheetML swaps the two.
            BackgroundColorHex = background ?? result.BackgroundColorHex,
            ColorHex = Resolve(format.Font?.Color) ?? result.ColorHex,
            Bold = format.Font?.Bold != null || result.Bold,
            Italic = format.Font?.Italic != null || result.Italic
        };
    }

    static bool Matches(Rule rule, string text, double? number) =>
        rule.Type switch
        {
            "containsBlanks" => text.Length == 0,
            "notContainsBlanks" => text.Length > 0,
            "colorScale" => number != null,
            "cellIs" => CellIs(rule, text, number),
            _ => false
        };

    /// <summary>
    /// A <c>cellIs</c> comparison against the rule's constant operands. A quoted operand compares as
    /// text, an unquoted numeric one as a number; anything else (a cell reference, a function) is a
    /// formula this cannot evaluate and so never matches.
    /// </summary>
    static bool CellIs(Rule rule, string text, double? number)
    {
        if (rule.Formulas.Count == 0)
        {
            return false;
        }

        var first = rule.Formulas[0];

        if (first.StartsWith('"') && first.EndsWith('"') && first.Length >= 2)
        {
            var literal = first[1..^1];
            return rule.Operator switch
            {
                "equal" => string.Equals(text, literal, StringComparison.OrdinalIgnoreCase),
                "notEqual" => !string.Equals(text, literal, StringComparison.OrdinalIgnoreCase),
                "containsText" => text.Contains(literal, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        if (number is not { } value ||
            !double.TryParse(first, NumberStyles.Any, CultureInfo.InvariantCulture, out var operand))
        {
            return false;
        }

        var second = rule.Formulas.Count > 1 &&
                     double.TryParse(rule.Formulas[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var other)
            ? other
            : (double?) null;

        return rule.Operator switch
        {
            "equal" => Math.Abs(value - operand) <= double.Epsilon,
            "notEqual" => Math.Abs(value - operand) > double.Epsilon,
            "greaterThan" => value > operand,
            "greaterThanOrEqual" => value >= operand,
            "lessThan" => value < operand,
            "lessThanOrEqual" => value <= operand,
            "between" => second is { } high && value >= Math.Min(operand, high) && value <= Math.Max(operand, high),
            "notBetween" => second is { } bound && (value < Math.Min(operand, bound) || value > Math.Max(operand, bound)),
            _ => false
        };
    }

    Rule? Parse(S.ConditionalFormattingRule rule)
    {
        var type = rule.Type?.Value is { } value ? ((IEnumValue) value).Value : null;
        if (type == null)
        {
            return null;
        }

        var scale = type == "colorScale" ? ParseColorScale(rule) : null;
        if (type == "colorScale" && scale == null)
        {
            return null;
        }

        return new(
            type,
            rule.Operator?.Value is { } op ? ((IEnumValue) op).Value : null,
            rule.Elements<S.Formula>().Select(_ => _.Text.Trim()).ToList(),
            rule.FormatId?.Value,
            rule.Priority?.Value ?? int.MaxValue,
            scale);
    }

    /// <summary>
    /// A colour scale interpolates between stops by value. Only min/max stops are honoured; a
    /// percentile or formula stop has no fixed value to interpolate against here.
    /// </summary>
    ColorScale? ParseColorScale(S.ConditionalFormattingRule rule)
    {
        var scale = rule.GetFirstChild<S.ColorScale>();
        if (scale == null)
        {
            return null;
        }

        var colors = scale.Elements<S.Color>().Select(Resolve).ToArray();
        var values = scale.Elements<S.ConditionalFormatValueObject>()
            .Select(_ => double.TryParse(_.Val?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (double?) null)
            .ToArray();

        return colors.Length >= 2 ? new ColorScale(colors, values) : null;
    }

    string? Resolve(S.ColorType? color)
    {
        if (color == null)
        {
            return null;
        }

        if (color.Rgb?.Value is { Length: >= 6 } argb)
        {
            return argb.Length == 8 ? argb[2..] : argb;
        }

        if (color.Theme?.Value is { } theme && themeColors != null)
        {
            return themeColors.ResolveColor(theme switch
            {
                0 => "lt1",
                1 => "dk1",
                2 => "lt2",
                3 => "dk2",
                _ => "accent" + Math.Max(1, theme - 3)
            });
        }

        return color.Indexed?.Value is { } indexed ? IndexedPalette.Resolve(indexed) : null;
    }

    sealed record Rule(
        string Type,
        string? Operator,
        List<string> Formulas,
        uint? DifferentialFormatId,
        int Priority,
        ColorScale? ColorScale);

    sealed record ColorScale(string?[] Colors, double?[] Values)
    {
        /// <summary>
        /// The colour for a value, interpolated between the first and last stop. Without usable stop
        /// values the midpoint colour stands in, which keeps the scale's palette without inventing a
        /// position for the value.
        /// </summary>
        public string? Resolve(double? number)
        {
            var low = Values.Length > 0 ? Values[0] : null;
            var high = Values.Length > 1 ? Values[^1] : null;

            if (number is not { } value || low is not { } min || high is not { } max || max <= min)
            {
                return Colors[Colors.Length / 2];
            }

            var fraction = Math.Clamp((value - min) / (max - min), 0, 1);
            return Blend(Colors[0], Colors[^1], fraction);
        }

        static string? Blend(string? from, string? to, double fraction)
        {
            if (from is not { Length: 6 } || to is not { Length: 6 })
            {
                return from ?? to;
            }

            int Channel(int offset) =>
                (int) Math.Round(
                    Convert.ToInt32(from.Substring(offset, 2), 16) * (1 - fraction) +
                    Convert.ToInt32(to.Substring(offset, 2), 16) * fraction);

            return $"{Channel(0):X2}{Channel(2):X2}{Channel(4):X2}";
        }
    }
}

/// <summary>
/// The partial format a conditional rule contributes. Null members mean "leave the cell's own", which
/// is what makes a <c>dxf</c> differential rather than a replacement.
/// </summary>
readonly record struct ConditionalOverlay
{
    public string? BackgroundColorHex { get; init; }
    public string? ColorHex { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
}
