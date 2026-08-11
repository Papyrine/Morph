/// <summary>
/// Run properties as DECLARED by one rung of the style ladder, as against the resolved values
/// <see cref="RunProperties"/> carries. Every member is nullable so "this rung said nothing" stays
/// distinguishable from "this rung said the default" — the distinction the table-style cascade turns on,
/// since a table style outranks the document defaults but is itself outranked by a paragraph style
/// (ECMA-376 §17.7.2).
/// </summary>
sealed record DeclaredRunProperties
{
    public string? FontFamily { get; init; }
    public double? FontSizePoints { get; init; }
    public string? ColorHex { get; init; }
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public bool? Underline { get; init; }
    public bool? Strikethrough { get; init; }
    public bool? AllCaps { get; init; }
    public bool? SmallCaps { get; init; }

    public bool HasAny =>
        FontFamily != null ||
        FontSizePoints != null ||
        ColorHex != null ||
        Bold != null ||
        Italic != null ||
        Underline != null ||
        Strikethrough != null ||
        AllCaps != null ||
        SmallCaps != null;

    /// <summary>
    /// Layers <paramref name="over"/> on top of this rung — anything it declares wins, anything it
    /// leaves unsaid falls through. Used to fold a table style's whole-table rPr and the matching
    /// conditional region into one set, the conditional winning.
    /// </summary>
    public DeclaredRunProperties Layer(DeclaredRunProperties? over)
    {
        if (over == null)
        {
            return this;
        }

        return new()
        {
            FontFamily = over.FontFamily ?? FontFamily,
            FontSizePoints = over.FontSizePoints ?? FontSizePoints,
            ColorHex = over.ColorHex ?? ColorHex,
            Bold = over.Bold ?? Bold,
            Italic = over.Italic ?? Italic,
            Underline = over.Underline ?? Underline,
            Strikethrough = over.Strikethrough ?? Strikethrough,
            AllCaps = over.AllCaps ?? AllCaps,
            SmallCaps = over.SmallCaps ?? SmallCaps
        };
    }

    /// <summary>
    /// Combines a toggle property (<c>w:b</c>, <c>w:i</c>, <c>w:caps</c>, …) across two STYLE rungs.
    ///
    /// Toggles do not override — they XOR (ECMA-376 §17.7.3), and Word really does behave this way:
    /// probed with a table style's firstRow declaring <c>w:b</c>, a paragraph style that also declares
    /// <c>w:b</c> renders NOT bold, while one declaring <c>w:b w:val="0"</c> renders bold. Direct
    /// run formatting is not a style rung and overrides the result outright.
    /// </summary>
    public static bool ToggleAcross(bool? styleLevel, bool? paragraphLevel) =>
        (styleLevel ?? false) ^ (paragraphLevel ?? false);
}
