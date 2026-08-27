class InlineStyle
{
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;
    public string? Color { get; set; }
    public string? BackgroundColor { get; set; }
    public double? TextIndent { get; set; }
    public double? LineHeight { get; set; }
    public double? MarginLeftPoints { get; set; }
    public double? MarginTopPoints { get; set; }
    public double? MarginRightPoints { get; set; }
    public double? MarginBottomPoints { get; set; }

    /// <summary>CSS <c>border</c>/<c>border-*</c> on a block element, mapped to Word's paragraph
    /// border box (w:pBdr). Null when the element declares none.</summary>
    public CellBorders? Borders { get; set; }

    /// <summary>CSS <c>padding</c> per side in points (px × 0.75). Word applies these only when a
    /// border is present — as the w:pBdr <c>w:space</c> gap between text and rule; a padded but
    /// borderless block renders as a plain band (measured on html_css_margin_padding's #DDD div).</summary>
    public double? PaddingTopPoints { get; set; }

    public double? PaddingRightPoints { get; set; }
    public double? PaddingBottomPoints { get; set; }
    public double? PaddingLeftPoints { get; set; }
}
