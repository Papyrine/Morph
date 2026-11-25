using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// A sheet's conditional formatting, resolved against cached cell values.
///
/// A rule pairs a condition with a <em>differential</em> format (<c>dxf</c>) — a partial style that
/// overlays the cell's own rather than replacing it, so a rule supplying only a fill leaves the font
/// alone. That partiality is the whole design, and it is why this returns an overlay rather than a
/// <see cref="CellStyle"/>.
///
/// Rules are decided from the cells' cached values. An <c>expression</c> rule's formula is worked out
/// by the formula engine over those values, written against the first cell of the block's first
/// range and moved to each cell as a relative reference moves; one resting on TODAY, NOW or RAND is
/// skipped, since what it gave when the reference was printed is not what it gives now.
/// <c>dataBar</c> draws inside the cell (<see cref="DataBarFor"/>); <c>iconSet</c> is skipped.
/// </summary>
sealed class ConditionalFormats
{
    readonly List<(SheetRange Range, List<Rule> Rules)> blocks = [];
    readonly S.Stylesheet? stylesheet;
    readonly ThemeColors? themeColors;
    readonly Func<(FormulaWorkbook Book, int Sheet)?>? formulas;
    (FormulaWorkbook Book, int Sheet)? context;
    bool contextRead;

    /// <param name="worksheet">The sheet whose rules and cached values are read.</param>
    /// <param name="stylesheet">The workbook's styles, for the rules' differential formats.</param>
    /// <param name="themeColors">The theme, for the rules' theme colours.</param>
    /// <param name="formulas">The workbook's formula engine and this sheet's index in it, asked for
    /// only when a cell is under an expression rule; null leaves expression rules unapplied.</param>
    public ConditionalFormats(S.Worksheet worksheet, S.Stylesheet? stylesheet, ThemeColors? themeColors, Func<(FormulaWorkbook Book, int Sheet)?>? formulas = null)
    {
        this.stylesheet = stylesheet;
        this.themeColors = themeColors;
        this.formulas = formulas;
        ParseIconSets(worksheet);

        foreach (var formatting in worksheet.Elements<S.ConditionalFormatting>())
        {
            // sqref is a space-separated list of ranges the block applies to.
            var ranges = (formatting.SequenceOfReferences?.InnerText ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(CellReference.ParseRange)
                .OfType<SheetRange>()
                .ToArray();

            var origin = ranges.Length > 0 ? (ranges[0].FirstRow, ranges[0].FirstColumn) : (1, 1);
            foreach (var element in formatting.Elements<S.ConditionalFormattingRule>())
            {
                if (ParseDataBar(worksheet, element, ranges) is { } bar)
                {
                    dataBars.Add((ranges, element.Priority?.Value ?? int.MaxValue, bar));
                }
            }

            var rules = formatting.Elements<S.ConditionalFormattingRule>()
                .Select(_ => Parse(_, origin, worksheet, ranges))
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

    readonly List<(SheetRange[] Ranges, int Priority, DataBar Bar)> dataBars = [];

    public bool IsEmpty => blocks.Count == 0 && dataBars.Count == 0 && iconSets.Count == 0;

    /// <summary>
    /// The data bar a cell holding <paramref name="number"/> draws, as the fraction of the cell it
    /// fills, its colour and whether the value still shows; null when no data bar covers it. Of
    /// several, the highest priority's.
    /// </summary>
    public (double Fraction, string ColorHex, bool ShowValue)? DataBarFor(int row, int column, double? number)
    {
        if (number is not { } value)
        {
            return null;
        }

        foreach (var (ranges, _, bar) in dataBars.OrderBy(_ => _.Priority))
        {
            if (!ranges.Any(_ => row >= _.FirstRow && row <= _.LastRow && column >= _.FirstColumn && column <= _.LastColumn))
            {
                continue;
            }

            var share = bar.High > bar.Low ? Math.Clamp((value - bar.Low) / (bar.High - bar.Low), 0, 1) : value >= bar.High ? 1 : 0;
            return ((bar.MinLength + share * (bar.MaxLength - bar.MinLength)) / 100, bar.ColorHex, bar.ShowValue);
        }

        return null;
    }

    /// <summary>
    /// A <c>dataBar</c> rule with its two ends worked out over the numbers its ranges hold. Excel 2010
    /// writes the bar's lengths and fill in a twin rule under the worksheet's <c>extLst</c>, joined by
    /// id: <c>minLength</c>/<c>maxLength</c> default to 10 and 90 percent (to-do-list-for-projects'
    /// 0% task draws a sliver, its 100% one 90% of the cell), and to-do-list says 0 and 100. Without the
    /// twin a bar is Excel 2007's, which draws 10 to 90 percent too.
    /// </summary>
    DataBar? ParseDataBar(S.Worksheet worksheet, S.ConditionalFormattingRule rule, SheetRange[] ranges)
    {
        if (rule.GetFirstChild<S.DataBar>() is not { } bar ||
            Resolve(bar.GetFirstChild<S.Color>()) is not { } color)
        {
            return null;
        }

        var ends = bar.Elements<S.ConditionalFormatValueObject>().ToArray();
        if (ends.Length < 2)
        {
            return null;
        }

        var id = rule.Descendants().FirstOrDefault(_ => _.LocalName == "id")?.InnerText;
        var twin = id == null
            ? null
            : worksheet.Descendants().FirstOrDefault(_ => _.LocalName == "cfRule" &&
                                                          _.GetAttributes().Any(attribute => attribute.LocalName == "id" && attribute.Value == id))?
                .ChildElements.FirstOrDefault(_ => _.LocalName == "dataBar");

        double Length(string name, double fallback) =>
            twin?.GetAttributes().FirstOrDefault(_ => _.LocalName == name).Value is { } text &&
            double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var length)
                ? length
                : fallback;

        var numbers = Numbers(worksheet, ranges);
        var low = End(ends[0], numbers, true);
        var high = End(ends[1], numbers, false);
        if (low is not { } min || high is not { } max)
        {
            return null;
        }

        return new(min, max, Length("minLength", 10), Length("maxLength", 90), color, bar.ShowValue?.Value != false);
    }

    /// <summary>One end of a bar: a stated number, or the least or greatest of the range's numbers.</summary>
    static double? End(S.ConditionalFormatValueObject end, List<double> numbers, bool low)
    {
        var type = end.Type?.Value is { } value ? ((IEnumValue) value).Value : null;
        var stated = double.TryParse(end.Val?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : (double?) null;
        return type switch
        {
            "num" => stated,
            "percent" when stated is { } percent && numbers.Count > 0 => numbers.Min() + (numbers.Max() - numbers.Min()) * percent / 100,
            _ when numbers.Count == 0 => null,
            "autoMin" => Math.Min(0, numbers.Min()),
            "autoMax" => Math.Max(0, numbers.Max()),
            _ => low ? numbers.Min() : numbers.Max()
        };
    }

    /// <summary>The cached numbers of the cells in <paramref name="ranges"/>.</summary>
    static List<double> Numbers(S.Worksheet worksheet, SheetRange[] ranges)
    {
        List<double> numbers = [];
        foreach (var row in worksheet.GetFirstChild<S.SheetData>()?.Elements<S.Row>() ?? [])
        {
            foreach (var cell in row.Elements<S.Cell>())
            {
                var reference = cell.CellReference?.Value;
                var index = CellReference.RowOf(reference);
                var column = CellReference.ColumnOf(reference);
                if ((cell.DataType == null || cell.DataType.Value == S.CellValues.Number) &&
                    ranges.Any(_ => index >= _.FirstRow && index <= _.LastRow && column >= _.FirstColumn && column <= _.LastColumn) &&
                    double.TryParse(cell.CellValue?.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                {
                    numbers.Add(number);
                }
            }
        }

        return numbers;
    }

    sealed record DataBar(double Low, double High, double MinLength, double MaxLength, string ColorHex, bool ShowValue);

    readonly List<(SheetRange[] Ranges, int Priority, IconRule Rule)> iconSets = [];

    /// <summary>
    /// The icon an icon set gives a cell holding <paramref name="number"/>, as its set and id, and
    /// whether the value still shows; null when no icon set covers it or its place has no icon.
    /// </summary>
    public (string Set, int Id, bool ShowValue)? IconFor(int row, int column, double? number)
    {
        if (number is not { } value)
        {
            return null;
        }

        foreach (var (ranges, _, rule) in iconSets.OrderBy(_ => _.Priority))
        {
            if (!ranges.Any(_ => row >= _.FirstRow && row <= _.LastRow && column >= _.FirstColumn && column <= _.LastColumn))
            {
                continue;
            }

            // The highest threshold the value reaches picks the place; the first always holds
            var place = 0;
            for (var index = 1; index < rule.Thresholds.Count; index++)
            {
                var (threshold, inclusive) = rule.Thresholds[index];
                if (inclusive ? value >= threshold : value > threshold)
                {
                    place = index;
                }
            }

            var (set, id) = rule.Icons[place];
            return set == "NoIcons" ? (set, -1, rule.ShowValue) : (set, id, rule.ShowValue);
        }

        return null;
    }

    /// <summary>
    /// The sheet's icon sets: Excel 2007's in <c>conditionalFormatting</c> and Excel 2010's, which
    /// alone can give each place an icon of its own set (<c>cfIcon</c>) or none, in the worksheet's
    /// <c>extLst</c>. inventory-list-with-highlighting flags an item to reorder with a red flag on 1
    /// and nothing below it, the value hidden.
    /// </summary>
    void ParseIconSets(S.Worksheet worksheet)
    {
        foreach (var rule in worksheet.Descendants().Where(_ => _.LocalName == "cfRule" && Attribute(_, "type") == "iconSet"))
        {
            var block = rule.Parent;
            var sqref = block?.LocalName == "conditionalFormatting"
                ? Attribute(block, "sqref") ?? block.ChildElements.FirstOrDefault(_ => _.LocalName == "sqref")?.InnerText
                : null;
            var ranges = (sqref ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(CellReference.ParseRange)
                .OfType<SheetRange>()
                .ToArray();
            if (ranges.Length == 0 ||
                rule.ChildElements.FirstOrDefault(_ => _.LocalName == "iconSet") is not { } set)
            {
                continue;
            }

            var name = Attribute(set, "iconSet") ?? "3TrafficLights1";
            var numbers = Numbers(worksheet, ranges);
            List<(double, bool)> thresholds = [];
            foreach (var end in set.ChildElements.Where(_ => _.LocalName == "cfvo"))
            {
                var text = Attribute(end, "val") ?? end.ChildElements.FirstOrDefault(_ => _.LocalName == "f")?.InnerText;
                var stated = double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : 0;
                var threshold = Attribute(end, "type") switch
                {
                    "percent" or "percentile" when numbers.Count > 0 => numbers.Min() + (numbers.Max() - numbers.Min()) * stated / 100,
                    _ => stated
                };
                thresholds.Add((threshold, Attribute(end, "gte") != "0"));
            }

            if (thresholds.Count == 0)
            {
                continue;
            }

            var reverse = Attribute(set, "reverse") == "1";
            var custom = set.ChildElements.Where(_ => _.LocalName == "cfIcon").ToArray();
            var icons = Enumerable.Range(0, thresholds.Count)
                .Select(place => custom.Length == thresholds.Count
                    ? (Attribute(custom[place], "iconSet") ?? name, int.TryParse(Attribute(custom[place], "iconId"), out var id) ? id : place)
                    : (name, reverse ? thresholds.Count - 1 - place : place))
                .ToList();
            var priority = int.TryParse(Attribute(rule, "priority"), out var ranked) ? ranked : int.MaxValue;
            iconSets.Add((ranges, priority, new(thresholds, icons, Attribute(set, "showValue") != "0")));
        }
    }

    static string? Attribute(OpenXmlElement element, string name) =>
        element.GetAttributes().FirstOrDefault(_ => _.LocalName == name).Value;

    sealed record IconRule(List<(double Threshold, bool Inclusive)> Thresholds, List<(string Set, int Id)> Icons, bool ShowValue);

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
            .Where(_ => row >= _.Range.FirstRow &&
                        row <= _.Range.LastRow &&
                        column >= _.Range.FirstColumn &&
                        column <= _.Range.LastColumn)
            .SelectMany(_ => _.Rules)
            .Where(_ => Matches(_, text, number, row, column))
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
            return result with
            {
                BackgroundColorHex = scale.Resolve(number) ?? result.BackgroundColorHex
            };
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
            // A dxf states what it turns OFF as well: <i val="0"/> keeps a cell upright, and was read
            // as italic for being present (inventory-list-with-highlighting's highlight rules)
            Bold = format.Font?.Bold is { } bold ? bold.Val?.Value != false : result.Bold,
            Italic = format.Font?.Italic is { } italic ? italic.Val?.Value != false : result.Italic,
            Strikethrough = format.Font?.Strike is { } strike ? strike.Val?.Value != false : result.Strikethrough
        };
    }

    bool Matches(Rule rule, string text, double? number, int row, int column) =>
        rule.Type switch
        {
            "containsBlanks" => text.Length == 0,
            "notContainsBlanks" => text.Length > 0,
            "colorScale" => number != null,
            "cellIs" => CellIs(rule, text, number),
            "expression" => Expression(rule, row, column),
            _ => false
        };

    /// <summary>
    /// Whether an <c>expression</c> rule's formula is true at the cell. to-do-list colours a task's
    /// status by <c>$F6="completed"</c>, which no cached value records.
    /// </summary>
    bool Expression(Rule rule, int row, int column)
    {
        if (rule.Formulas.Count == 0)
        {
            return false;
        }

        if (!contextRead)
        {
            context = formulas?.Invoke();
            contextRead = true;
        }

        if (context is not var (book, sheet))
        {
            return false;
        }

        var formula = FormulaShifter.Shift(rule.Formulas[0], row - rule.Origin.Row, column - rule.Origin.Column);
        if (book.Parse(formula) is not { } node)
        {
            return false;
        }

        var evaluator = new FormulaEvaluator(book, sheet, row, column);
        var value = FormulaCoercion.ToBoolean(evaluator.Scalar(evaluator.Evaluate(node)));
        return evaluator is
               {
                   Volatile: false,
                   Unsupported: false
               } &&
               value is
               {
                   IsBoolean: true,
                   Boolean: true
               };
    }

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

        if (first.StartsWith('"') &&
            first.EndsWith('"') &&
            first.Length >= 2)
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
            "between" => second is { } high &&
                         value >= Math.Min(operand, high) &&
                         value <= Math.Max(operand, high),
            "notBetween" => second is { } bound &&
                            (value < Math.Min(operand, bound) || value > Math.Max(operand, bound)),
            _ => false
        };
    }

    Rule? Parse(S.ConditionalFormattingRule rule, (int Row, int Column) origin, S.Worksheet worksheet, SheetRange[] ranges)
    {
        var type = rule.Type?.Value is { } value ? ((IEnumValue) value).Value : null;
        if (type == null)
        {
            return null;
        }

        var scale = type == "colorScale" ? ParseColorScale(rule, worksheet, ranges) : null;
        if (type == "colorScale" &&
            scale == null)
        {
            return null;
        }

        return new(
            type,
            rule.Operator?.Value is { } op ? ((IEnumValue) op).Value : null,
            [.. rule.Elements<S.Formula>().Select(_ => _.Text.Trim())],
            rule.FormatId?.Value,
            rule.Priority?.Value ?? int.MaxValue,
            scale,
            origin);
    }

    /// <summary>
    /// A colour scale's stops, each a value worked out over the numbers its ranges hold — <c>min</c>,
    /// <c>max</c>, <c>percent</c> of the way between them, <c>percentile</c> of them, or a stated
    /// <c>num</c> — paired with its colour. Two stops or three: a three-colour scale passes through its
    /// middle colour at its middle stop, where blending the ends alone turned red-yellow-green into a
    /// muddy brown at the midpoint.
    /// </summary>
    ColorScale? ParseColorScale(S.ConditionalFormattingRule rule, S.Worksheet worksheet, SheetRange[] ranges)
    {
        var scale = rule.GetFirstChild<S.ColorScale>();
        if (scale == null)
        {
            return null;
        }

        var colors = scale.Elements<S.Color>().Select(Resolve).ToArray();
        var numbers = Numbers(worksheet, ranges);
        numbers.Sort();
        var values = scale.Elements<S.ConditionalFormatValueObject>()
            .Select(_ =>
            {
                var type = _.Type?.Value is { } kind ? ((IEnumValue) kind).Value : null;
                var stated = double.TryParse(_.Val?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : (double?) null;
                if (numbers.Count == 0)
                {
                    return type is "num" or "formula" ? stated : null;
                }

                return type switch
                {
                    "min" => numbers[0],
                    "max" => numbers[^1],
                    "percent" when stated is { } percent => numbers[0] + (numbers[^1] - numbers[0]) * percent / 100,
                    "percentile" when stated is { } percentile => Percentile(numbers, percentile / 100),
                    _ => stated
                };
            })
            .ToArray();

        if (colors.Length >= 2)
        {
            return new(colors, values);
        }

        return null;
    }

    // Excel's PERCENTILE.INC over sorted numbers: interpolated between the two ranks either side.
    static double Percentile(List<double> sorted, double fraction)
    {
        var rank = Math.Clamp(fraction, 0, 1) * (sorted.Count - 1);
        var below = (int) Math.Floor(rank);
        var above = Math.Min(below + 1, sorted.Count - 1);
        return sorted[below] + (sorted[above] - sorted[below]) * (rank - below);
    }

    // A rule's colours resolve as a cell style's do, tint included: to-do-list's colour scale and
    // the dxf fills of several templates name a theme colour lightened by a tint, which was dropped.
    string? Resolve(S.ColorType? color) =>
        CellStyles.Resolve(color, themeColors);

    sealed record Rule(
        string Type,
        string? Operator,
        List<string> Formulas,
        uint? DifferentialFormatId,
        int Priority,
        ColorScale? ColorScale,
        (int Row, int Column) Origin);

    sealed record ColorScale(string?[] Colors, double?[] Values)
    {
        /// <summary>
        /// The colour for a value, interpolated between the two stops either side of it; at or past an
        /// end stop, that stop's colour. Without usable stop values the middle colour stands in, which
        /// keeps the scale's palette without inventing a position for the value.
        /// </summary>
        public string? Resolve(double? number)
        {
            var count = Math.Min(Colors.Length, Values.Length);
            if (number is not { } value ||
                count < 2 ||
                Values.Take(count).Any(_ => _ == null))
            {
                return Colors[Colors.Length / 2];
            }

            if (value <= Values[0]!.Value)
            {
                return Colors[0];
            }

            for (var index = 1; index < count; index++)
            {
                var low = Values[index - 1]!.Value;
                var high = Values[index]!.Value;
                if (value <= high)
                {
                    return high > low ? Blend(Colors[index - 1], Colors[index], (value - low) / (high - low)) : Colors[index];
                }
            }

            return Colors[count - 1];
        }

        static string? Blend(string? from, string? to, double fraction)
        {
            if (from is not { Length: 6 } || to is not { Length: 6 })
            {
                return from ?? to;
            }

            int Channel(int offset) =>
                (int) Math.Round(
                    int.Parse(from.AsSpan(offset, 2), NumberStyles.HexNumber) * (1 - fraction) +
                    int.Parse(to.AsSpan(offset, 2), NumberStyles.HexNumber) * fraction);

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
    public bool? Strikethrough { get; init; }
}
