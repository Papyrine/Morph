/// <summary>
/// Represents a WordArt text element with special formatting.
/// </summary>
sealed class WordArtElement : DocumentElement, IWordArtVisual
{
    /// <summary>The text content of the WordArt.</summary>
    public required string Text { get; init; }

    /// <summary>Width in points.</summary>
    public required double WidthPoints { get; init; }

    /// <summary>Height in points.</summary>
    public required double HeightPoints { get; init; }

    /// <summary>Font family for the text.</summary>
    public string FontFamily { get; init; } = DefaultFontSettings.DefaultFont;

    /// <summary>Font size in points.</summary>
    public double FontSizePoints { get; init; } = 36;

    /// <summary>Whether the text is bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Whether the text is italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Text fill color (hex). Null for default black.</summary>
    public string? FillColorHex { get; init; }

    /// <summary>Text outline color (hex). Null for no outline.</summary>
    public string? OutlineColorHex { get; init; }

    /// <summary>Text outline width in points.</summary>
    public double OutlineWidthPoints { get; init; }

    /// <summary>
    /// Box border colour (hex) from the shape's <c>a:ln</c>, for UNWARPED shapes only — a
    /// text-carrying wsp without a real text warp is Word's inline text box, and its
    /// <c>a:ln</c> is the box frame, not a glyph stroke (business/06's LOGO box). Warped
    /// WordArt keeps the legacy glyph-outline interpretation.
    /// </summary>
    public string? BoxLineColorHex { get; init; }

    /// <summary>Box border width in points.</summary>
    public double BoxLineWidthPoints { get; init; }

    /// <summary>Box border opacity (0..1).</summary>
    public double BoxLineAlpha { get; init; } = 1;

    /// <summary>
    /// Box BACKGROUND colour (hex) from the shape's own <c>a:solidFill</c>, for UNWARPED shapes
    /// only and on the same reasoning as <see cref="BoxLineColorHex"/>: the fill of Word's inline
    /// text box paints behind the text rather than colouring the glyphs.
    /// </summary>
    public string? BoxFillColorHex { get; init; }

    /// <summary>
    /// Box contours when it is richer than a rectangle, normalized to the unit square exactly like
    /// <see cref="FloatingTextBoxElement.Subpaths"/> and filled even-odd so a ring stays hollow.
    /// brochures/08's "Contoso Logo" is a <c>prstGeom prst="frame"</c> — a white RING, which is why
    /// filling its box as a solid rectangle painted a block Word never draws.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<(double X, double Y)>>? BoxSubpaths { get; init; }

    /// <summary>
    /// Whether the box is an ellipse. Carried separately from <see cref="BoxSubpaths"/> because
    /// <c>PresetShapeGeometry</c> has no ellipse builder and the backends draw a true oval.
    /// </summary>
    public bool BoxIsEllipse { get; init; }

    /// <summary>
    /// Alignment of the containing paragraph. <c>ParseWordArt</c> emits the WordArt as a SIBLING of
    /// its paragraph, so without this the paragraph's <c>w:jc</c> is lost and the box always draws
    /// at the content-box left edge — brochures/08's logo frame landed 43px left of Word's.
    /// </summary>
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;

    /// <summary>Whether the text has a shadow effect.</summary>
    public bool HasShadow { get; init; }

    /// <summary>Whether the text has a reflection effect.</summary>
    public bool HasReflection { get; init; }

    /// <summary>Whether the text has a glow effect.</summary>
    public bool HasGlow { get; init; }

    /// <summary>The preset text transform/warp type.</summary>
    public WordArtTransform Transform { get; init; } = WordArtTransform.None;
}