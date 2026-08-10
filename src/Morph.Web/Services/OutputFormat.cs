namespace Morph.Web.Services;

/// <summary>The output formats this app can produce, from any <see cref="InputFormat"/>.</summary>
public enum OutputFormat
{
    /// <summary>Rendered page images (one PNG per page; multi-page downloads as a zip).</summary>
    Png,

    /// <summary>Plain-text rendition, extracted from the HTML export.</summary>
    Text,

    /// <summary>Self-contained HTML document (styles inline, images embedded as data URIs).</summary>
    Html,

    /// <summary>Markdown.</summary>
    Markdown,

    /// <summary>Vector-text PDF.</summary>
    Pdf,
}
