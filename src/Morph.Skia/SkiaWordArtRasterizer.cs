/// <summary>
/// Rasterizes a WordArt shape to a transparent-background PNG with the SkiaSharp backend. It lays
/// the shape out on a single-element page sized exactly to the WordArt box, so all of
/// <see cref="SkiaPageRenderer"/>'s WordArt drawing (warps, outline, shadow, glow, reflection) is
/// reused verbatim. Discovered reflectively by <c>Morph.Pdf</c> to embed high-fidelity WordArt.
/// </summary>
sealed class SkiaWordArtRasterizer : IWordArtRasterizer
{
    public byte[]? Render(IWordArtVisual visual, WordArtRasterOptions options)
    {
        if (visual.WidthPoints <= 0 ||
            visual.HeightPoints <= 0)
        {
            return null;
        }

        var pageSettings = WordArtRasterPage.Build(visual);
        using var context = new SkiaRenderContext(
            pageSettings,
            options.Dpi,
            compatibility: null,
            options.FontWidthScale,
            options.FontFallback,
            options.FontDirectory,
            options.Deterministic)
        {
            TransparentBackground = true
        };
        using var renderer = new SkiaPageRenderer(context);

        var document = new ParsedDocument
        {
            PageSettings = pageSettings,
            Elements = [WordArtRasterPage.ToInlineElement(visual)]
        };

        byte[]? png = null;
        renderer.RenderDocument(document, writePng =>
        {
            // The box is a single page; keep the first (only) page's bytes.
            if (png != null)
            {
                return;
            }

            using var stream = new MemoryStream();
            writePng(stream);
            png = stream.ToArray();
        });

        return png;
    }
}
