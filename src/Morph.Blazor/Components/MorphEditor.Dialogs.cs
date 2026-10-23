using DocumentFormat.OpenXml.Wordprocessing;

namespace Morph;

// Word's Paragraph and Font dialogs, opened from the launcher at the foot of their ribbon groups. Each
// takes where it acts when it opens, starts from what the pages drew there, and sets only what the
// reader changed.
public partial class MorphEditor
{
    const string paragraphDialog = "paragraph";
    const string fontDialog = "font";

    const double pointsPerCentimetre = 72 / 2.54;

    // Word's line spacing choices, by what the dialog's list says.
    internal static readonly (string Value, string Name)[] LineChoices =
    [
        ("single", "Single"),
        ("1.5", "1.5 lines"),
        ("double", "Double"),
        ("atleast", "At least"),
        ("exactly", "Exactly"),
        ("multiple", "Multiple")
    ];

    // Word's underline styles by their w:u value, as its Font dialog lists them.
    internal static readonly (string Value, string Name)[] Underlines =
    [
        ("none", "(none)"),
        ("single", "Single"),
        ("words", "Words only"),
        ("double", "Double"),
        ("thick", "Thick"),
        ("dotted", "Dotted"),
        ("dash", "Dashed"),
        ("wave", "Wave")
    ];

    internal sealed record ParagraphFields
    {
        public string Alignment { get; set; } = "left";
        public string Left { get; set; } = "0";
        public string Right { get; set; } = "0";
        public string Special { get; set; } = "none";
        public string By { get; set; } = "";
        public string Before { get; set; } = "0";
        public string After { get; set; } = "0";
        public string Line { get; set; } = "single";
        public string At { get; set; } = "";
        public bool WidowControl { get; set; } = true;
        public bool KeepNext { get; set; }
        public bool KeepLines { get; set; }
        public bool PageBreakBefore { get; set; }
    }

    internal sealed record FontFields
    {
        public string Font { get; set; } = "";
        public string Style { get; set; } = "regular";
        public string Size { get; set; } = "11";
        public string Color { get; set; } = automatic;
        public string Underline { get; set; } = "none";
        public bool Strike { get; set; }
        public bool DoubleStrike { get; set; }
        public bool Superscript { get; set; }
        public bool Subscript { get; set; }
        public bool SmallCaps { get; set; }
        public bool AllCaps { get; set; }
        public bool Hidden { get; set; }
    }

    internal ParagraphFields paragraphFields = new();
    ParagraphFields paragraphStart = new();
    EditTarget? paragraphTarget;
    internal string paragraphPage = "indents";

    internal FontFields fontFields = new();
    FontFields fontStart = new();
    (EditTarget Target, SourcePosition Start, SourcePosition End, int From, int To)? fontText;

    static string Number(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    static double? Parse(string text)
    {
        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            value is >= -1000 and <= 1000)
        {
            return value;
        }

        return null;
    }

    // Word's ruler (View, Ruler): shown or not, and what was last sent to the script.
    internal bool rulerShown;
    string? rulerSent;

    // The caret's page — its width and margins — and paragraph — its indents — in points, for the
    // script to draw. In a table the markers are left out: a cell's ruler is the cell's, not the page's.
    string? RulerModel()
    {
        if (!rulerShown ||
            !HasDocument ||
            CurrentPage is not { } page)
        {
            return null;
        }

        var properties = CurrentProperties;
        var first = 0.0;
        if (properties != null)
        {
            first = properties.FirstLineIndentPoints - properties.HangingIndentPoints;
        }

        var model = new Dictionary<string, object>
        {
            ["w"] = page.Width / 20.0,
            ["ml"] = page.Left / 20.0,
            ["mr"] = page.Right / 20.0,
            ["l"] = properties?.LeftIndentPoints ?? 0,
            ["r"] = properties?.RightIndentPoints ?? 0,
            ["f"] = first,
            ["t"] = InTable || !CanWrite
        };
        return System.Text.Json.JsonSerializer.Serialize(model);
    }

    Task ToggleRuler()
    {
        rulerShown = !rulerShown;
        return Task.CompletedTask;
    }

    // Sends the ruler whenever what it shows has changed, after the render that changed it.
    async Task SyncRulerAsync()
    {
        var model = RulerModel();
        if (model == rulerSent)
        {
            return;
        }

        rulerSent = model;
        await SetRulerAsync(model);
    }

    // The Paragraph dialog

    async Task OpenParagraphDialogAsync()
    {
        menu = null;
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        paragraphTarget = target;
        paragraphFields = FieldsOf(PropertiesOf(target.Paragraph));
        paragraphStart = paragraphFields with { };
        paragraphPage = "indents";
        dialog = paragraphDialog;
    }

    static ParagraphFields FieldsOf(ParagraphProperties? properties)
    {
        if (properties == null)
        {
            return new();
        }

        var fields = new ParagraphFields
        {
            Alignment = properties.Alignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "justify",
                _ => "left"
            },
            Left = Number(properties.LeftIndentPoints / pointsPerCentimetre),
            Right = Number(properties.RightIndentPoints / pointsPerCentimetre),
            Before = Number(properties.SpacingBeforePoints),
            After = Number(properties.SpacingAfterPoints),
            WidowControl = properties.WidowControl,
            KeepNext = properties.KeepNext,
            KeepLines = properties.KeepLines,
            PageBreakBefore = properties.PageBreakBefore
        };
        if (properties.HangingIndentPoints > 0)
        {
            fields.Special = "hanging";
            fields.By = Number(properties.HangingIndentPoints / pointsPerCentimetre);
        }
        else if (properties.FirstLineIndentPoints > 0)
        {
            fields.Special = "first";
            fields.By = Number(properties.FirstLineIndentPoints / pointsPerCentimetre);
        }

        switch (properties.LineSpacingRule)
        {
            case LineSpacingRule.Exactly:
                fields.Line = "exactly";
                fields.At = Number(properties.LineSpacingPoints);
                break;
            case LineSpacingRule.AtLeast:
                fields.Line = "atleast";
                fields.At = Number(properties.LineSpacingPoints);
                break;
            default:
                var multiple = properties.LineSpacingMultiplier;
                fields.Line = "multiple";
                foreach (var (value, name) in (ReadOnlySpan<(double, string)>) [(1, "single"), (1.5, "1.5"), (2, "double")])
                {
                    if (Math.Abs(multiple - value) < 0.01)
                    {
                        fields.Line = name;
                    }
                }
                fields.At = Number(multiple);
                break;
        }

        return fields;
    }

    // What the reader changed, as a format; null where something they typed is not a number.
    internal ParagraphFormat? ParagraphChange()
    {
        var now = paragraphFields;
        var was = paragraphStart;
        var format = new ParagraphFormat();
        if (now.Alignment != was.Alignment)
        {
            format = format with
            {
                Alignment = now.Alignment switch
                {
                    "center" => TextAlignment.Center,
                    "right" => TextAlignment.Right,
                    "justify" => TextAlignment.Justify,
                    _ => TextAlignment.Left
                }
            };
        }

        if (now.Left != was.Left)
        {
            if (Parse(now.Left) is not { } left)
            {
                return null;
            }

            format = format with { LeftPoints = left * pointsPerCentimetre };
        }

        if (now.Right != was.Right)
        {
            if (Parse(now.Right) is not { } right)
            {
                return null;
            }

            format = format with { RightPoints = right * pointsPerCentimetre };
        }

        if (now.Special != was.Special ||
            now.By != was.By)
        {
            var by = 0.0;
            if (now.Special != "none")
            {
                if (Parse(now.By) is not { } value ||
                    value < 0)
                {
                    return null;
                }

                by = value * pointsPerCentimetre;
            }

            if (now.Special == "hanging")
            {
                by = -by;
            }

            format = format with { FirstLinePoints = by };
        }

        if (now.Before != was.Before)
        {
            if (Parse(now.Before) is not { } before ||
                before < 0)
            {
                return null;
            }

            format = format with { BeforePoints = before };
        }

        if (now.After != was.After)
        {
            if (Parse(now.After) is not { } after ||
                after < 0)
            {
                return null;
            }

            format = format with { AfterPoints = after };
        }

        if (now.Line != was.Line ||
            now.At != was.At)
        {
            var (rule, value) = now.Line switch
            {
                "single" => (LineSpacingRule.Auto, 1),
                "1.5" => (LineSpacingRule.Auto, 1.5),
                "double" => (LineSpacingRule.Auto, 2),
                "exactly" => (LineSpacingRule.Exactly, Parse(now.At)),
                "atleast" => (LineSpacingRule.AtLeast, Parse(now.At)),
                _ => (LineSpacingRule.Auto, Parse(now.At))
            };
            if (value is not > 0)
            {
                return null;
            }

            format = format with { LineRule = rule, LineValue = value };
        }

        if (now.WidowControl != was.WidowControl)
        {
            format = format with { WidowControl = now.WidowControl };
        }

        if (now.KeepNext != was.KeepNext)
        {
            format = format with { KeepNext = now.KeepNext };
        }

        if (now.KeepLines != was.KeepLines)
        {
            format = format with { KeepLines = now.KeepLines };
        }

        if (now.PageBreakBefore != was.PageBreakBefore)
        {
            format = format with { PageBreakBefore = now.PageBreakBefore };
        }

        return format;
    }

    // What the dialog's At box measures, for the line spacing chosen.
    internal string AtUnit =>
        paragraphFields.Line switch
        {
            "exactly" or "atleast" => "pt",
            "multiple" => "×",
            _ => ""
        };

    void OnLineChosen(ChangeEventArgs args)
    {
        var line = args.Value as string ?? "single";
        paragraphFields.Line = line;
        paragraphFields.At = line switch
        {
            "exactly" or "atleast" => "12",
            "multiple" => "1.15",
            _ => ""
        };
    }

    async Task ParagraphFromDialogAsync()
    {
        if (ParagraphChange() is not { } format ||
            paragraphTarget is not { } target)
        {
            return;
        }

        dialog = null;
        paragraphTarget = null;
        if (format == default)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.FormatParagraphs(_, target.Paragraphs, format, options));
    }

    // The Font dialog

    async Task OpenFontDialogAsync()
    {
        menu = null;
        if (await TextAsync() is not { } text)
        {
            return;
        }

        fontText = text;
        fontFields = FieldsOf(LookOf(text.Target.StartParagraph, text.From));
        fontStart = fontFields with { };
        dialog = fontDialog;
    }

    static FontFields FieldsOf(RunProperties? run)
    {
        if (run == null)
        {
            return new()
            {
                Font = DefaultFontSettings.DefaultFont
            };
        }

        var style = (run.Bold, run.Italic) switch
        {
            (true, true) => "bolditalic",
            (true, false) => "bold",
            (false, true) => "italic",
            _ => "regular"
        };
        var underline = "none";
        if (run.Underline)
        {
            underline = run.DirectUnderline ?? (run.DoubleUnderline ? "double" : "single");
        }

        return new()
        {
            Font = run.FontFamily,
            Style = style,
            Size = Number(run.FontSizePoints),
            Color = run.ColorHex?.ToUpperInvariant() ?? automatic,
            Underline = underline,
            Strike = run.Strikethrough,
            DoubleStrike = run.DirectDoubleStrikethrough == true,
            Superscript = run.VerticalAlignment == VerticalRunAlignment.Superscript,
            Subscript = run.VerticalAlignment == VerticalRunAlignment.Subscript,
            SmallCaps = run.SmallCaps,
            AllCaps = run.AllCaps,
            Hidden = run.Hidden
        };
    }

    internal RunFormat? FontChange()
    {
        var now = fontFields;
        var was = fontStart;
        var format = new RunFormat();
        if (now.Font.Trim() != was.Font &&
            now.Font.Trim().Length > 0)
        {
            format = format with { Font = now.Font.Trim() };
        }

        if (now.Style != was.Style)
        {
            format = format with
            {
                Bold = now.Style is "bold" or "bolditalic",
                Italic = now.Style is "italic" or "bolditalic"
            };
        }

        if (now.Size != was.Size)
        {
            if (Parse(now.Size) is not { } size ||
                size is < 1 or > 1638)
            {
                return null;
            }

            format = format with { SizePoints = size };
        }

        if (now.Color != was.Color)
        {
            format = format with { Color = now.Color };
        }

        if (now.Underline != was.Underline)
        {
            format = format with { UnderlineStyle = now.Underline };
        }

        if (now.Strike != was.Strike)
        {
            format = format with { Strike = now.Strike };
        }

        if (now.DoubleStrike != was.DoubleStrike)
        {
            format = format with { DoubleStrike = now.DoubleStrike };
        }

        if (now.Superscript != was.Superscript ||
            now.Subscript != was.Subscript)
        {
            var vertical = VerticalPositionValues.Baseline;
            if (now.Superscript)
            {
                vertical = VerticalPositionValues.Superscript;
            }
            else if (now.Subscript)
            {
                vertical = VerticalPositionValues.Subscript;
            }

            format = format with { Vertical = vertical };
        }

        if (now.SmallCaps != was.SmallCaps)
        {
            format = format with { SmallCaps = now.SmallCaps };
        }

        if (now.AllCaps != was.AllCaps)
        {
            format = format with { AllCaps = now.AllCaps };
        }

        if (now.Hidden != was.Hidden)
        {
            format = format with { Hidden = now.Hidden };
        }

        return format;
    }

    // Word's dialog keeps opposites apart: checking one unchecks the other.
    void SetStrike(bool on)
    {
        fontFields.Strike = on;
        if (on)
        {
            fontFields.DoubleStrike = false;
        }
    }

    void SetDoubleStrike(bool on)
    {
        fontFields.DoubleStrike = on;
        if (on)
        {
            fontFields.Strike = false;
        }
    }

    void SetSuperscript(bool on)
    {
        fontFields.Superscript = on;
        if (on)
        {
            fontFields.Subscript = false;
        }
    }

    void SetSubscript(bool on)
    {
        fontFields.Subscript = on;
        if (on)
        {
            fontFields.Superscript = false;
        }
    }

    void SetSmallCaps(bool on)
    {
        fontFields.SmallCaps = on;
        if (on)
        {
            fontFields.AllCaps = false;
        }
    }

    void SetAllCaps(bool on)
    {
        fontFields.AllCaps = on;
        if (on)
        {
            fontFields.SmallCaps = false;
        }
    }

    // The dialog's preview, drawn as the text will look.
    internal string FontPreview
    {
        get
        {
            var fields = fontFields;
            var size = Math.Clamp(Parse(fields.Size) ?? 11, 8, 28).ToString("0.#", CultureInfo.InvariantCulture);
            var css = new StringBuilder($"font-family: '{fields.Font.Replace("'", "", StringComparison.Ordinal)}', sans-serif; font-size: {size}pt;");
            if (fields.Style is "bold" or "bolditalic")
            {
                css.Append(" font-weight: 700;");
            }

            if (fields.Style is "italic" or "bolditalic")
            {
                css.Append(" font-style: italic;");
            }

            if (fields.Color != automatic)
            {
                css.Append($" color: #{fields.Color};");
            }

            var lines = new List<string>();
            if (fields.Underline != "none")
            {
                lines.Add("underline");
            }

            if (fields.Strike ||
                fields.DoubleStrike)
            {
                lines.Add("line-through");
            }

            if (lines.Count > 0)
            {
                var style = fields.Underline switch
                {
                    "double" => "double",
                    "dotted" => "dotted",
                    "dash" => "dashed",
                    "wave" => "wavy",
                    _ => "solid"
                };
                if (fields.DoubleStrike)
                {
                    style = "double";
                }

                css.Append($" text-decoration: {string.Join(' ', lines)} {style};");
                if (fields.Underline == "thick")
                {
                    css.Append(" text-decoration-thickness: 2px;");
                }
            }

            if (fields.SmallCaps)
            {
                css.Append(" font-variant: small-caps;");
            }

            if (fields.AllCaps)
            {
                css.Append(" text-transform: uppercase;");
            }

            if (fields.Superscript)
            {
                css.Append(" vertical-align: super; font-size: smaller;");
            }

            if (fields.Subscript)
            {
                css.Append(" vertical-align: sub; font-size: smaller;");
            }

            if (fields.Hidden)
            {
                css.Append(" text-decoration-line: underline; text-decoration-style: dotted; opacity: 0.6;");
            }

            return css.ToString();
        }
    }

    async Task FontFromDialogAsync()
    {
        if (FontChange() is not { } format ||
            fontText is not { } text)
        {
            return;
        }

        dialog = null;
        fontText = null;
        if (format.IsEmpty)
        {
            return;
        }

        await FormatTextAsync(text, (start, end, options) => _ => DocumentEditor.Format(_, start, end, format, options));
    }

    Task OnFormDialogKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseDialog();
        }

        return Task.CompletedTask;
    }
}
