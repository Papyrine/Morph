// ImageSharp.Drawing 3.0 exposes a stateful, deferred canvas via ctx.Paint(canvas => ...).
// Morph holds one DrawingCanvas per page so every Fill/Draw/DrawText is recorded onto the
// same batcher and flushed in one backend pass on disposal. These extensions translate the
// v2-style call shape (Color, RectangleF, etc.) onto the new canvas API.

static class CanvasDrawingExtensions
{
    public static void Fill(this DrawingCanvas canvas, Brush brush, RectangleF rect) =>
        canvas.Fill(brush, ToPath(rect));

    public static void Fill(this DrawingCanvas canvas, Color color, RectangleF rect) =>
        canvas.Fill(new SolidBrush(color), ToPath(rect));

    public static void Fill(this DrawingCanvas canvas, Color color, IPath path) =>
        canvas.Fill(new SolidBrush(color), path);

    public static void Fill(this DrawingCanvas canvas, Color color) =>
        canvas.Fill(new SolidBrush(color), new RectanglePolygon(canvas.Bounds));

    public static void Draw(this DrawingCanvas canvas, Pen pen, RectangleF rect) =>
        canvas.Draw(pen, ToPath(rect));

    public static void DrawText(this DrawingCanvas canvas, RichTextOptions options, string text, Brush brush) =>
        canvas.DrawText(options, text.AsSpan(), brush, null);

    public static void DrawText(this DrawingCanvas canvas, RichTextOptions options, string text, Pen pen) =>
        canvas.DrawText(options, text.AsSpan(), null, pen);

    public static void DrawText(this DrawingCanvas canvas, string text, Font font, Color color, PointF origin) =>
        canvas.DrawText(new(font) {Origin = origin}, text.AsSpan(), new SolidBrush(color), null);

    public static void DrawText(this DrawingCanvas canvas, string text, Font font, Pen pen, PointF origin) =>
        canvas.DrawText(new(font) {Origin = origin}, text.AsSpan(), null, pen);

    // Apply(...) defers the inner action to canvas-replay time, which doesn't survive a caller
    // `using var img = ...` block. canvas.DrawImage performs eager image work (crop/scale/transform
    // bake → ImageBrush) up front and queues only the resulting brush, so the source image is
    // safe to dispose immediately after this call returns.
    public static void DrawImage(this DrawingCanvas canvas, Image image, Point location, float opacity)
    {
        var sourceRect = new Rectangle(0, 0, image.Width, image.Height);
        var destRect = new RectangleF(location.X, location.Y, image.Width, image.Height);
        canvas.DrawImage(image, sourceRect, destRect);
    }

    static IPath ToPath(RectangleF rect) =>
        new RectanglePolygon(rect.X, rect.Y, rect.Width, rect.Height);
}
