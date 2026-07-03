namespace Morph.Web.Services;

/// <summary>The output formats this app can produce from an uploaded DOCX.</summary>
public enum OutputFormat
{
    /// <summary>Rendered page images (one PNG per page; multi-page downloads as a zip).</summary>
    Png,

    /// <summary>Plain-text rendition, extracted from the HTML export.</summary>
    Text,

    /// <summary>Markdown.</summary>
    Markdown,

    /// <summary>Vector-text PDF.</summary>
    Pdf,
}
