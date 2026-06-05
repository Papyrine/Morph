namespace Morph;

/// <summary>
/// Options for the HTML exporter.
/// </summary>
public sealed record HtmlExportOptions : ExportOptions
{
    /// <summary>
    /// When true (default), output is indented with two-space steps and each block element sits on
    /// its own line. Disable for compact / minified output.
    /// </summary>
    public bool PrettyFormat { get; init; } = true;

    /// <summary>
    /// When true (default), the output is a complete <c>&lt;!doctype html&gt;</c> document with a
    /// <c>&lt;head&gt;&lt;style&gt;…&lt;/style&gt;&lt;/head&gt;</c> block of Word-like default
    /// styles, so the file renders in a browser without any external CSS. When false the exporter
    /// emits just the body-level fragment — useful for embedding into a larger page where the
    /// surrounding stylesheet already supplies typography defaults.
    /// </summary>
    public bool EmitDocument { get; init; } = true;

    /// <summary>
    /// When true (default), images are inlined as <c>data:</c> URIs so the produced HTML is a
    /// single self-contained file. When false and no <see cref="ImageHandler"/> is supplied, image
    /// references are omitted (the surrounding paragraph is preserved).
    /// </summary>
    public bool EmbedImagesAsBase64 { get; init; } = true;

    /// <summary>
    /// Optional callback that decides how each image is referenced in the output. Receives the
    /// image bytes and metadata; returns the value to place in <c>src=</c> (an absolute URL, a
    /// relative path the caller has written to disk, a data URI of its choosing, etc.). When set
    /// it takes precedence over <see cref="EmbedImagesAsBase64"/>.
    /// </summary>
    public Func<EmbeddedImage, string>? ImageHandler { get; init; }
}
