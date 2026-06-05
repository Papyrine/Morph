namespace Morph;

/// <summary>
/// Information about an image being emitted into an HTML or Markdown export. Passed to
/// <see cref="HtmlExportOptions.ImageHandler"/> / <see cref="MarkdownExportOptions.ImageHandler"/>
/// so callers can decide how the image is referenced — inlined as base64 (the default), written to
/// a media directory, uploaded to a CDN, etc.
/// </summary>
/// <param name="Data">Raw image bytes from the source document.</param>
/// <param name="ContentType">MIME type (e.g. <c>image/png</c>, <c>image/svg+xml</c>). May be null
/// when the source didn't declare one.</param>
/// <param name="WidthPoints">Display width in points (1/72 inch); zero when unspecified.</param>
/// <param name="HeightPoints">Display height in points; zero when unspecified.</param>
/// <param name="Index">Zero-based index of this image in encounter order across the document.</param>
public sealed record EmbeddedImage(
    byte[] Data,
    string? ContentType,
    double WidthPoints,
    double HeightPoints,
    int Index);
