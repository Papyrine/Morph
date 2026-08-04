/// <summary>
/// Rasterizes a WordArt shape to a transparent-background PNG with the SixLabors.ImageSharp
/// backend. It draws through <see cref="ImageSharpWordArtDrawer"/> onto an image sized exactly to
/// the WordArt box (plus the shared overflow padding), reproducing the geometry the page renderer
/// used when this went through a single-element page render — same content origin, same encode —
/// without constructing a page renderer. Discovered reflectively by <c>Morph.Pdf</c> to embed
/// high-fidelity WordArt.
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
        context.SetHeaderFooterSpace(0, 0);

        // A fresh Image<Rgba32> is already transparent, so no background fill is needed.
        using var image = new Image<Rgba32>(context.PageWidthPixels, context.PageHeightPixels);

        // The element draws at the content origin — the same place the page render put it, with
        // the box the full content area (the raster page's margins ARE the overflow padding).
        var element = WordArtRasterPage.ToInlineElement(visual);
        var x = context.PointsToPixels(context.ContentLeft) +
                ImageSharpWordArtDrawer.AlignWordArtOffset(
                    element,
                    context.PointsToPixels(context.ContentWidth),
                    context.PointsToPixels((float) element.WidthPoints));
        var y = context.PointsToPixels(context.ContentTop);
        var width = context.PointsToPixels((float) element.WidthPoints);
        var pixelHeight = context.PointsToPixels((float) element.HeightPoints);

        // Disposing the canvas flushes its recorded timeline through the backend; it must close
        // before the PNG encode or the saved bytes would miss every queued command.
        using (var canvas = image.Frames.RootFrame.CreateCanvas(Configuration.Default, new()))
        {
            new ImageSharpWordArtDrawer(context, canvas).DrawInline(element, x, y, width, pixelHeight);
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
