/// <summary>
/// The visual properties of a WordArt shape that are common to the inline
/// (<see cref="WordArtElement"/>) and floating (<see cref="FloatingWordArtElement"/>) variants.
/// A raster backend only needs these to draw the shape; position is the caller's concern.
/// </summary>
interface IWordArtVisual
{
    string Text { get; }
    double WidthPoints { get; }
    double HeightPoints { get; }
    string FontFamily { get; }
    double FontSizePoints { get; }
    bool Bold { get; }
    bool Italic { get; }
    string? FillColorHex { get; }
    string? OutlineColorHex { get; }
    double OutlineWidthPoints { get; }
    bool HasShadow { get; }
    bool HasReflection { get; }
    bool HasGlow { get; }
    WordArtTransform Transform { get; }
}

/// <summary>
/// Settings a raster backend uses to rasterize a WordArt shape. Font resolution mirrors the
/// values the calling converter was given.
/// </summary>
sealed record WordArtRasterOptions
{
    /// <summary>Resolution of the produced PNG in dots per inch.</summary>
    public required int Dpi { get; init; }

    /// <summary>Font width scale, forwarded from the export options.</summary>
    public double FontWidthScale { get; init; } = 1.0;

    /// <summary>Optional missing-font resolver, forwarded from the export options.</summary>
    public Func<string, string?>? FontFallback { get; init; }

    /// <summary>Optional font directory, forwarded from the export options.</summary>
    public string? FontDirectory { get; init; }

    /// <summary>
    /// When true (the default) glyphs rasterize with greyscale AA and integer positions so the
    /// bytes are identical across machines — required for a byte-reproducible PDF.
    /// </summary>
    public bool Deterministic { get; init; } = true;
}

/// <summary>
/// Renders a WordArt shape to a transparent-background PNG. Implemented by the raster backends
/// (<c>Morph.Skia</c> / <c>Morph.ImageSharp</c>) and discovered reflectively by the PDF backend so
/// it can embed high-fidelity WordArt without a compile-time dependency on either engine.
/// </summary>
interface IWordArtRasterizer
{
    /// <summary>
    /// Rasterizes <paramref name="visual"/> to a PNG sized to its box at
    /// <see cref="WordArtRasterOptions.Dpi"/>, with a transparent background. Returns
    /// <c>null</c> when nothing could be produced (e.g. a zero-sized box).
    /// </summary>
    byte[]? Render(IWordArtVisual visual, WordArtRasterOptions options);
}

/// <summary>
/// Shared plumbing for the raster backends' WordArt rasterizers: a single-element page sized
/// exactly to the WordArt box (no margins) so the shape draws at the page origin and fills the
/// bitmap. Keeping this in core lets both backends reuse the identical page/element construction.
/// </summary>
static class WordArtRasterPage
{
    /// <summary>
    /// Padding (points) added around the WordArt box on every side. Several warps draw glyphs
    /// beyond the declared box — arch/circle text reaches up to a full ascent past the box edge,
    /// and the raster backends let that spill onto the surrounding page rather than clipping it.
    /// The rasterizer reserves the same margin so the embedded image captures the overflow instead
    /// of cropping it at the box. One-and-a-half em comfortably covers the worst-case ascent, since
    /// the rendered glyph size never exceeds the declared <see cref="IWordArtVisual.FontSizePoints"/>.
    /// </summary>
    public static double Padding(IWordArtVisual visual) => visual.FontSizePoints * 1.5;

    /// <summary>
    /// Page settings whose content area is the WordArt box, surrounded by <see cref="Padding"/> on
    /// every side (as page margins) so the shape draws at the padded offset and its overflow has
    /// room. The caller embeds the resulting image shifted back by the same padding.
    /// </summary>
    public static PageSettings Build(IWordArtVisual visual)
    {
        var pad = Padding(visual);
        return new()
        {
            WidthPoints = visual.WidthPoints + 2 * pad,
            HeightPoints = visual.HeightPoints + 2 * pad,
            MarginTop = pad,
            MarginBottom = pad,
            MarginLeft = pad,
            MarginRight = pad,
            HeaderDistance = 0,
            FooterDistance = 0
        };
    }

    /// <summary>
    /// A normalized inline WordArt element carrying only <paramref name="visual"/>'s visual
    /// properties — the floating variant's anchor/position is intentionally dropped, since the
    /// raster fills the whole page and the caller positions the image.
    /// </summary>
    public static WordArtElement ToInlineElement(IWordArtVisual visual) =>
        new()
        {
            Text = visual.Text,
            WidthPoints = visual.WidthPoints,
            HeightPoints = visual.HeightPoints,
            FontFamily = visual.FontFamily,
            FontSizePoints = visual.FontSizePoints,
            Bold = visual.Bold,
            Italic = visual.Italic,
            FillColorHex = visual.FillColorHex,
            OutlineColorHex = visual.OutlineColorHex,
            OutlineWidthPoints = visual.OutlineWidthPoints,
            HasShadow = visual.HasShadow,
            HasReflection = visual.HasReflection,
            HasGlow = visual.HasGlow,
            Transform = visual.Transform
        };
}
