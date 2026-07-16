/// <summary>
/// Rasterizes a WordArt shape to a transparent-background PNG with the SixLabors.ImageSharp
/// backend. It lays the shape out on a single-element page sized exactly to the WordArt box, so
/// all of <see cref="ImageSharpPageRenderer"/>'s WordArt drawing is reused verbatim. Discovered
/// reflectively by <c>Morph.Pdf</c> to embed high-fidelity WordArt.
/// </summary>
sealed class ImageSharpWordArtRasterizer : IWordArtRasterizer
{
    public byte[]? Render(IWordArtVisual visual, WordArtRasterOptions options)
    {
        if (visual.WidthPoints <= 0 ||
            visual.HeightPoints <= 0)
        {
            return null;
        }

        var pageSettings = WordArtRasterPage.Build(visual);
        using var context = new ImageSharpRenderContext(
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
        using var renderer = new ImageSharpPageRenderer(context);

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
