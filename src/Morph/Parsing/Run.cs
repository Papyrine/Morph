/// <summary>
/// A run of text with consistent formatting. Can also represent an inline image.
/// </summary>
sealed class Run
{
    public required string Text { get; init; }
    public RunProperties Properties { get; init; } = new();

    /// <summary>Inline image data (when the run represents an inline image).</summary>
    public byte[]? InlineImageData { get; init; }

    /// <summary>Width of inline image in points.</summary>
    public double InlineImageWidthPoints { get; init; }

    /// <summary>Height of inline image in points.</summary>
    public double InlineImageHeightPoints { get; init; }

    /// <summary>Content type of inline image (e.g., "image/png", "image/svg+xml").</summary>
    public string? InlineImageContentType { get; init; }

    /// <summary>Alt text for the inline image, from <c>wp:docPr</c> / <c>pic:cNvPr</c>
    /// (@descr, else @title). Null when the source supplies none.</summary>
    public string? InlineImageDescription { get; init; }

    /// <summary>Raster bytes from the primary <c>a:blip r:embed</c>, retained when
    /// <see cref="InlineImageData"/> holds the SVG variant so backends without SVG
    /// support can use this fallback.</summary>
    public byte[]? InlineImageRasterFallbackData { get; init; }

    /// <summary>Content type for <see cref="InlineImageRasterFallbackData"/>.</summary>
    public string? InlineImageRasterFallbackContentType { get; init; }

    /// <summary>Inline image rotation in degrees (clockwise). 0 means no rotation.</summary>
    public double InlineImageRotationDegrees { get; init; }

    /// <summary>Horizontal mirror for the inline image (a:xfrm/@flipH).</summary>
    public bool InlineImageFlipHorizontal { get; init; }

    /// <summary>Vertical mirror for the inline image (a:xfrm/@flipV).</summary>
    public bool InlineImageFlipVertical { get; init; }

    /// <summary>Inline image source-rectangle crop (a:srcRect). Null = no crop.</summary>
    public ImageCrop? InlineImageCrop { get; init; }

    /// <summary>Colour-transform effect for the inline image (a:duotone / a:grayscl / a:lum).</summary>
    public BlipColorEffect InlineImageColorEffect { get; init; } = BlipColorEffect.None;

    /// <summary>Duotone dark end for the inline image; see <see cref="ImageElement.DuotoneColorHex"/>.</summary>
    public string? InlineImageDuotoneColorHex { get; init; }

    /// <summary>Duotone light end for the inline image; see <see cref="ImageElement.DuotoneLightColorHex"/>.</summary>
    public string? InlineImageDuotoneLightColorHex { get; init; }

    /// <summary>Constant transparency for the inline image; see <see cref="ImageElement.Opacity"/>.</summary>
    public double InlineImageOpacity { get; init; } = 1;

    /// <summary>
    /// True when this run represents a single w:tab character.
    /// When true, <see cref="Text"/> is "\t" and the renderer snaps the cursor to the next tab stop.
    /// </summary>
    public bool IsTab { get; init; }

    /// <summary>When set, this tab run is an absolute position tab (<c>w:ptab</c>) rather than a
    /// stop-list tab: it jumps to a position derived from the text area instead of snapping to the
    /// paragraph's <c>w:tabs</c>. <see cref="IsTab"/> is true alongside it.</summary>
    public PositionalTab? PositionalTab { get; init; }

    /// <summary>When set, this run is a footnote-reference marker (empty <see cref="Text"/>); the
    /// id keys into <see cref="ParsedDocument.Footnotes"/>. The raster renderers ignore it; the
    /// text exporters emit an inline marker and collect the note into a trailing notes section.</summary>
    public string? FootnoteReferenceId { get; init; }

    /// <summary>When set, this run is an endnote-reference marker (empty <see cref="Text"/>); the
    /// id keys into <see cref="ParsedDocument.Endnotes"/>.</summary>
    public string? EndnoteReferenceId { get; init; }

    /// <summary>
    /// Target of the <c>w:hyperlink</c> that wraps this run (external URI or <c>#anchor</c> for
    /// internal bookmarks). Null when the run is not part of a hyperlink. The raster renderers
    /// ignore this; the HTML/Markdown exporters use it to emit links.
    /// </summary>
    public string? HyperlinkUrl { get; init; }

    /// <summary>
    /// Inline shape group (<c>wpg:wgp</c>) attached to this run, when the run hosts a primitive
    /// drawing made of connector lines / rectangles instead of a picture. Mutually exclusive
    /// with <see cref="InlineImageData"/>.
    /// </summary>
    public InlineShapeGroup? InlineShapeGroup { get; init; }

    /// <summary>
    /// When not <see cref="PageFieldKind.None"/>, this run is the result of a page-numbering
    /// field (PAGE / NUMPAGES / SECTIONPAGES). <see cref="Text"/> holds Word's cached value; the
    /// engine replaces it with the live value per page, and
    /// the text exporters emit the cached text as-is.
    /// </summary>
    public PageFieldKind PageField { get; init; } = PageFieldKind.None;

    /// <summary>The field's <c>\*</c> numeric-format switch (e.g. <c>roman</c>, <c>ALPHABETIC</c>)
    /// when present; null renders the page number as decimal.</summary>
    public string? PageFieldNumberFormat { get; init; }

    /// <summary>
    /// Returns a copy of this run with <see cref="Text"/> replaced and the page-field marker
    /// cleared (the text is now the resolved literal value). Used by the renderers to substitute
    /// a live page number in place of a <see cref="PageField"/> run's cached text.
    /// </summary>
    /// <summary>
    /// Returns a copy of this run with <see cref="Properties"/> replaced and every other member —
    /// including the page-field marker and note/hyperlink linkage — preserved. Used by the
    /// table-style conditional-formatting cascade, which must not strip a run's identity.
    /// </summary>
    public Run WithProperties(RunProperties properties) =>
        new()
        {
            Text = Text,
            Properties = properties,
            InlineImageData = InlineImageData,
            InlineImageWidthPoints = InlineImageWidthPoints,
            InlineImageHeightPoints = InlineImageHeightPoints,
            InlineImageContentType = InlineImageContentType,
            InlineImageDescription = InlineImageDescription,
            InlineImageRasterFallbackData = InlineImageRasterFallbackData,
            InlineImageRasterFallbackContentType = InlineImageRasterFallbackContentType,
            InlineImageRotationDegrees = InlineImageRotationDegrees,
            InlineImageFlipHorizontal = InlineImageFlipHorizontal,
            InlineImageFlipVertical = InlineImageFlipVertical,
            InlineImageCrop = InlineImageCrop,
            InlineImageColorEffect = InlineImageColorEffect,
            InlineImageDuotoneColorHex = InlineImageDuotoneColorHex,
            InlineImageDuotoneLightColorHex = InlineImageDuotoneLightColorHex,
            InlineImageOpacity = InlineImageOpacity,
            IsTab = IsTab,
            PositionalTab = PositionalTab,
            FootnoteReferenceId = FootnoteReferenceId,
            EndnoteReferenceId = EndnoteReferenceId,
            HyperlinkUrl = HyperlinkUrl,
            InlineShapeGroup = InlineShapeGroup,
            PageField = PageField,
            PageFieldNumberFormat = PageFieldNumberFormat
        };

    public Run WithText(string text) =>
        new()
        {
            Text = text,
            Properties = Properties,
            InlineImageData = InlineImageData,
            InlineImageWidthPoints = InlineImageWidthPoints,
            InlineImageHeightPoints = InlineImageHeightPoints,
            InlineImageContentType = InlineImageContentType,
            InlineImageDescription = InlineImageDescription,
            InlineImageRasterFallbackData = InlineImageRasterFallbackData,
            InlineImageRasterFallbackContentType = InlineImageRasterFallbackContentType,
            InlineImageRotationDegrees = InlineImageRotationDegrees,
            InlineImageFlipHorizontal = InlineImageFlipHorizontal,
            InlineImageFlipVertical = InlineImageFlipVertical,
            InlineImageCrop = InlineImageCrop,
            InlineImageColorEffect = InlineImageColorEffect,
            InlineImageDuotoneColorHex = InlineImageDuotoneColorHex,
            InlineImageDuotoneLightColorHex = InlineImageDuotoneLightColorHex,
            InlineImageOpacity = InlineImageOpacity,
            IsTab = IsTab,
            PositionalTab = PositionalTab,
            FootnoteReferenceId = FootnoteReferenceId,
            EndnoteReferenceId = EndnoteReferenceId,
            HyperlinkUrl = HyperlinkUrl,
            InlineShapeGroup = InlineShapeGroup
        };
}