namespace Morph.Web.Services;

/// <summary>Browser-facing metadata for an <see cref="OutputFormat"/>: how to label, name and serve it.</summary>
public record FormatInfo(
    OutputFormat Format,
    string DisplayName,
    string Extension,
    string ContentType);
