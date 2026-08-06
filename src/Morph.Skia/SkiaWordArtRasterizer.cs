/// <summary>
/// Rasterizes a WordArt shape to a transparent-background PNG with the SkiaSharp backend. It draws
/// through <see cref="SkiaWordArtDrawer"/> onto a bitmap sized exactly to the WordArt box (plus the
/// shared overflow padding), reproducing the geometry the page renderer used when this went through
/// a single-element page render — same content origin, same bitmap format, same encode — without
/// constructing a page renderer. Discovered reflectively by <c>Morph.Pdf</c> to embed high-fidelity
/// WordArt.
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
        using var bitmap = new SKBitmap(
            context.PageWidthPixels,
            context.PageHeightPixels,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // The element draws at the content origin — the same place the page render put it, with
        // the box the full content area (the raster page's margins ARE the overflow padding).
        var element = WordArtRasterPage.ToInlineElement(visual);
        var x = context.PointsToPixels(context.ContentLeft) +
                SkiaWordArtDrawer.AlignWordArtOffset(
                    element,
                    context.PointsToPixels(context.ContentWidth),
                    context.PointsToPixels((float) element.WidthPoints));
        var y = context.PointsToPixels(context.ContentTop);
        var width = context.PointsToPixels((float) element.WidthPoints);
        var pixelHeight = context.PointsToPixels((float) element.HeightPoints);

        new SkiaWordArtDrawer(context, canvas).DrawInline(element, x, y, width, pixelHeight);

        using var pixmap = bitmap.PeekPixels();
        using var data = pixmap.Encode(SKEncodedImageFormat.Png, 100)!;
        return data.ToArray();
    }
}
