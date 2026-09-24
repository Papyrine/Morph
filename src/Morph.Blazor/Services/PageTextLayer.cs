namespace Morph;

/// <summary>
/// The selectable text of one rendered page: every run of text the page image shows, positioned where it
/// was drawn, ready to lay over the image as a transparent text layer — the technique PDF.js uses to make
/// a rendered page selectable, copyable and searchable. Hand it to <see cref="DocumentPreview.TextLayers"/>,
/// or to <see cref="MorphInterop.RenderTextLayerAsync"/> for a custom page view.
///
/// Opaque by design: the positions travel to the browser in an internal format that changes with the
/// script that reads it, so only the plain <see cref="Text"/> is public.
/// </summary>
public sealed class PageTextLayer
{
    internal PageTextLayer(string json, string text)
    {
        Json = json;
        Text = text;
    }

    // The positioned runs, in points, for morph-text.js. See TextLayerBuilder for the format.
    internal string Json { get; }

    /// <summary>
    /// The page's text in reading order, exactly as selecting all of the layer and copying it yields:
    /// words re-spaced where a justified line dropped its spaces, a tab between table cells and a line
    /// break after each paragraph and row.
    /// </summary>
    public string Text { get; }
}
