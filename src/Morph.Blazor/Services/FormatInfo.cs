namespace Morph;

/// <summary>Browser-facing metadata for an <see cref="OutputFormat"/>: how to label, name and serve it.</summary>
/// <param name="Format">The format this describes.</param>
/// <param name="DisplayName">Human name for the output, e.g. "PNG image".</param>
/// <param name="Extension">The file extension, including the dot.</param>
/// <param name="ContentType">MIME type, used when the bytes are handed back to the browser.</param>
public record FormatInfo(
    OutputFormat Format,
    string DisplayName,
    string Extension,
    string ContentType);
