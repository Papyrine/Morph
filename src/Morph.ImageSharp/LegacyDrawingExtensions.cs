// ImageSharp.Drawing 3.0 replaced the imperative IImageProcessingContext extension
// methods (Fill/Draw/DrawLine/DrawText) with a stateful canvas API accessed via
// ctx.Paint(canvas => ...). This file re-exposes the v2 signatures the rest of
// Morph.ImageSharp was written against, so each call site forwards into a one-shot
// Paint block. If the benchmark indicates the per-call Paint overhead matters, the
// hot paths can be batched manually later.

namespace SixLabors.ImageSharp.Processing;

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

static class LegacyDrawingExtensions
{
    public static IImageProcessingContext Fill(this IImageProcessingContext source, Color color) =>
        source.Paint(_ => _.Fill(new SolidBrush(color)));

    public static IImageProcessingContext Fill(this IImageProcessingContext source, Color color, IPath path) =>
        source.Paint(_ => _.Fill(new SolidBrush(color), path));

    public static IImageProcessingContext Fill(this IImageProcessingContext source, Brush brush, IPath path) =>
        source.Paint(_ => _.Fill(brush, path));

    public static IImageProcessingContext Fill(this IImageProcessingContext source, Color color, RectangleF rect) =>
        source.Paint(_ => _.Fill(new SolidBrush(color), ToPath(rect)));

    public static IImageProcessingContext Fill(this IImageProcessingContext source, Brush brush, RectangleF rect) =>
        source.Paint(_ => _.Fill(brush, ToPath(rect)));

    public static IImageProcessingContext Draw(this IImageProcessingContext source, Pen pen, IPath path) =>
        source.Paint(_ => _.Draw(pen, path));

    public static IImageProcessingContext Draw(this IImageProcessingContext source, Pen pen, RectangleF rect) =>
        source.Paint(_ => _.Draw(pen, ToPath(rect)));

    public static IImageProcessingContext DrawLine(this IImageProcessingContext source, Pen pen, params PointF[] points) =>
        source.Paint(_ => _.DrawLine(pen, points));

    public static IImageProcessingContext DrawText(this IImageProcessingContext source, RichTextOptions options, string text, Brush brush) =>
        source.Paint(_ => _.DrawText(options, text.AsSpan(), brush, null!));

    public static IImageProcessingContext DrawText(this IImageProcessingContext source, RichTextOptions options, string text, Pen pen) =>
        source.Paint(_ => _.DrawText(options, text.AsSpan(), null!, pen));

    public static IImageProcessingContext DrawText(this IImageProcessingContext source, string text, Font font, Color color, PointF origin) =>
        source.Paint(_ => _.DrawText(new RichTextOptions(font) {Origin = origin}, text.AsSpan(), new SolidBrush(color), null!));

    public static IImageProcessingContext DrawText(this IImageProcessingContext source, string text, Font font, Brush brush, PointF origin) =>
        source.Paint(_ => _.DrawText(new RichTextOptions(font) {Origin = origin}, text.AsSpan(), brush, null!));

    public static IImageProcessingContext DrawText(this IImageProcessingContext source, string text, Font font, Pen pen, PointF origin) =>
        source.Paint(_ => _.DrawText(new RichTextOptions(font) {Origin = origin}, text.AsSpan(), null!, pen));

    static IPath ToPath(RectangleF rect) =>
        new RectanglePolygon(rect.X, rect.Y, rect.Width, rect.Height);
}
