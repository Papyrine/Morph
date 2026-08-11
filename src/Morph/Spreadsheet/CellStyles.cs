using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Resolves a cell's <c>s</c> index into the formatting it implies, from the workbook's
/// <c>styles.xml</c>.
///
/// A cell carries no formatting of its own — only an index into <c>cellXfs</c>, which in turn
/// indexes the font, fill, border and number-format tables. Every cell in a column typically shares
/// one index, so the resolved results are cached rather than rebuilt per cell.
/// </summary>
sealed class CellStyles
{
    readonly S.Stylesheet? stylesheet;
    readonly ThemeColors? themeColors;
    readonly Dictionary<uint, CellStyle> cache = [];

    public CellStyles(WorkbookPart workbookPart, ThemeColors? themeColors)
    {
        stylesheet = workbookPart.WorkbookStylesPart?.Stylesheet;
        this.themeColors = themeColors;
    }

    /// <summary>The workbook's body font, which sets the default row height and column-width unit.</summary>
    public (string Family, double SizePoints) DefaultFont
    {
        get
        {
            var font = stylesheet?.Fonts?.Elements<S.Font>().FirstOrDefault();
            return (
                font?.FontName?.Val?.Value ?? DefaultFontSettings.DefaultFont,
                font?.FontSize?.Val?.Value ?? 11);
        }
    }

    public CellStyle Resolve(uint styleIndex)
    {
        if (cache.TryGetValue(styleIndex, out var cached))
        {
            return cached;
        }

        var resolved = Build(styleIndex);
        cache[styleIndex] = resolved;
        return resolved;
    }

    CellStyle Build(uint styleIndex)
    {
        var format = stylesheet?.CellFormats?.Elements<S.CellFormat>().ElementAtOrDefault((int) styleIndex);
        if (format == null)
        {
            return new();
        }

        var font = stylesheet?.Fonts?.Elements<S.Font>().ElementAtOrDefault((int) (format.FontId?.Value ?? 0));
        var fill = stylesheet?.Fills?.Elements<S.Fill>().ElementAtOrDefault((int) (format.FillId?.Value ?? 0));
        var border = stylesheet?.Borders?.Elements<S.Border>().ElementAtOrDefault((int) (format.BorderId?.Value ?? 0));

        return new()
        {
            NumberFormatId = (int) (format.NumberFormatId?.Value ?? 0),
            FormatCode = CustomFormatCode(format.NumberFormatId?.Value),
            FontFamily = font?.FontName?.Val?.Value,
            FontSizePoints = font?.FontSize?.Val?.Value,
            Bold = font?.Bold != null,
            Italic = font?.Italic != null,
            Underline = font?.Underline != null,
            Strikethrough = font?.Strike != null,
            ColorHex = Resolve(font?.Color),
            BackgroundColorHex = ResolveFill(fill),
            Borders = ResolveBorders(border),
            // applyAlignment gates whether the alignment block is honoured at all; without it a
            // style's inherited alignment applies instead of the one written here.
            HorizontalAlignment = MapHorizontal(format.Alignment?.Horizontal?.Value),
            VerticalAlignment = MapVertical(format.Alignment?.Vertical?.Value),
            WrapText = format.Alignment?.WrapText?.Value == true,
            IndentLevel = (int) (format.Alignment?.Indent?.Value ?? 0)
        };
    }

    /// <summary>The <c>formatCode</c> for a custom id (164+); builtins carry no code in the file.</summary>
    string? CustomFormatCode(uint? id)
    {
        if (id is not { } value)
        {
            return null;
        }

        return stylesheet?.NumberingFormats?
            .Elements<S.NumberingFormat>()
            .FirstOrDefault(_ => _.NumberFormatId?.Value == value)?
            .FormatCode?.Value;
    }

    /// <summary>
    /// A SpreadsheetML colour, which may be a literal ARGB, a theme index with a tint, or one of the
    /// legacy indexed palette entries.
    /// </summary>
    string? Resolve(S.ColorType? color)
    {
        if (color == null)
        {
            return null;
        }

        if (color.Rgb?.Value is { Length: >= 6 } argb)
        {
            // Stored ARGB; the model wants RGB.
            return argb.Length == 8 ? argb[2..] : argb;
        }

        if (color.Theme?.Value is { } theme && themeColors != null)
        {
            var name = ThemeColorName(theme);
            var tint = color.Tint?.Value ?? 0;
            var resolved = themeColors.ResolveColor(name);
            return tint == 0 ? resolved : ApplyTint(resolved, tint);
        }

        if (color.Indexed?.Value is { } indexed)
        {
            return IndexedPalette.Resolve(indexed);
        }

        return null;
    }

    /// <summary>
    /// Theme slot order in SpreadsheetML is NOT the DrawingML order: slots 0 and 1 are swapped
    /// against 2 and 3, so a cell asking for theme 0 wants the light background, not dark text.
    /// </summary>
    static string ThemeColorName(uint theme) =>
        theme switch
        {
            0 => "lt1",
            1 => "dk1",
            2 => "lt2",
            3 => "dk2",
            4 => "accent1",
            5 => "accent2",
            6 => "accent3",
            7 => "accent4",
            8 => "accent5",
            9 => "accent6",
            10 => "hlink",
            _ => "folHlink"
        };

    /// <summary>
    /// Lightens (positive) or darkens (negative) a colour by Excel's tint rule, which works on
    /// luminance rather than on the channels directly.
    /// </summary>
    static string? ApplyTint(string? hex, double tint)
    {
        if (hex is not { Length: 6 })
        {
            return hex;
        }

        var r = Convert.ToInt32(hex[..2], 16);
        var g = Convert.ToInt32(hex[2..4], 16);
        var b = Convert.ToInt32(hex[4..], 16);

        int Apply(int channel) =>
            tint < 0
                ? (int) Math.Round(channel * (1 + tint))
                : (int) Math.Round(channel * (1 - tint) + 255 * tint);

        return $"{Apply(r):X2}{Apply(g):X2}{Apply(b):X2}";
    }

    string? ResolveFill(S.Fill? fill)
    {
        var pattern = fill?.PatternFill;
        if (pattern == null)
        {
            return null;
        }

        var type = pattern.PatternType?.Value;
        if (type == null || type == S.PatternValues.None)
        {
            return null;
        }

        // For a solid fill the FOREGROUND colour is the fill; bgColor is the pattern's backdrop and
        // is almost always the meaningless "indexed 64" placeholder.
        return Resolve(pattern.ForegroundColor) ?? Resolve(pattern.BackgroundColor);
    }

    CellBorders ResolveBorders(S.Border? border) =>
        new()
        {
            Left = Edge(border?.LeftBorder),
            Right = Edge(border?.RightBorder),
            Top = Edge(border?.TopBorder),
            Bottom = Edge(border?.BottomBorder)
        };

    BorderEdge Edge(S.BorderPropertiesType? edge)
    {
        var style = edge?.Style?.Value;
        if (style == null || style == S.BorderStyleValues.None)
        {
            return BorderEdge.None;
        }

        return new()
        {
            IsVisible = true,
            WidthPoints = Width(style.Value),
            ColorHex = Resolve(edge?.Color) ?? "000000",
            Style = style == S.BorderStyleValues.Dotted || style == S.BorderStyleValues.Hair
                ? BorderLineStyle.Dotted
                : style == S.BorderStyleValues.Dashed || style == S.BorderStyleValues.MediumDashed
                    ? BorderLineStyle.Dashed
                    : style == S.BorderStyleValues.Double
                        ? BorderLineStyle.Double
                        : BorderLineStyle.Single
        };
    }

    static double Width(S.BorderStyleValues style)
    {
        if (style == S.BorderStyleValues.Thick)
        {
            return 2;
        }

        if (style == S.BorderStyleValues.Medium ||
            style == S.BorderStyleValues.MediumDashed ||
            style == S.BorderStyleValues.Double)
        {
            return 1.5;
        }

        if (style == S.BorderStyleValues.Hair)
        {
            return 0.25;
        }

        return 0.5;
    }

    static TextAlignment? MapHorizontal(S.HorizontalAlignmentValues? alignment)
    {
        if (alignment == null)
        {
            return null;
        }

        if (alignment == S.HorizontalAlignmentValues.Center || alignment == S.HorizontalAlignmentValues.CenterContinuous)
        {
            return TextAlignment.Center;
        }

        if (alignment == S.HorizontalAlignmentValues.Right)
        {
            return TextAlignment.Right;
        }

        if (alignment == S.HorizontalAlignmentValues.Justify || alignment == S.HorizontalAlignmentValues.Distributed)
        {
            return TextAlignment.Justify;
        }

        return TextAlignment.Left;
    }

    static CellVerticalAlignment MapVertical(S.VerticalAlignmentValues? alignment)
    {
        if (alignment == S.VerticalAlignmentValues.Center)
        {
            return CellVerticalAlignment.Center;
        }

        if (alignment == S.VerticalAlignmentValues.Top)
        {
            return CellVerticalAlignment.Top;
        }

        // Excel's default is bottom, unlike a Word table cell.
        return CellVerticalAlignment.Bottom;
    }
}

/// <summary>Everything a cell's style index resolves to.</summary>
sealed record CellStyle
{
    public int NumberFormatId { get; init; }
    public string? FormatCode { get; init; }
    public string? FontFamily { get; init; }
    public double? FontSizePoints { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
    public string? ColorHex { get; init; }
    public string? BackgroundColorHex { get; init; }
    public CellBorders Borders { get; init; } = new();
    public TextAlignment? HorizontalAlignment { get; init; }
    public CellVerticalAlignment VerticalAlignment { get; init; } = CellVerticalAlignment.Bottom;
    public bool WrapText { get; init; }
    public int IndentLevel { get; init; }

    /// <summary>The effective format code: a custom one if present, else the builtin for the id.</summary>
    public string? EffectiveFormatCode => FormatCode ?? BuiltinNumberFormats.Code(NumberFormatId);
}
