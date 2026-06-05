namespace Morph;

/// <summary>
/// Options for the Markdown exporter.
/// </summary>
public sealed record MarkdownExportOptions : ExportOptions
{
    /// <summary>
    /// Optional callback that decides how each image is referenced in the output. Receives the
    /// image bytes and metadata; returns the value to place between the parentheses of an
    /// <c>![]()</c> reference. When null, images are inlined as base64 <c>data:</c> URIs.
    /// </summary>
    public Func<EmbeddedImage, string>? ImageHandler { get; init; }
}
