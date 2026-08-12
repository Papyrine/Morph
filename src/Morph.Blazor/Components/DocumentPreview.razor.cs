namespace Morph;

/// <summary>
/// Shows a document's rendered pages, or a progress bar while they render.
///
/// Rendering is the host's job — pass image URLs (typically <c>data:image/png;base64,…</c> built from
/// <see cref="ConversionService.RenderPngPages"/>) rather than document bytes, so the component stays
/// free of the threading and font-loading concerns that belong to whoever owns the conversion.
/// <see cref="MorphConverter"/> is the batteries-included version of that host.
/// </summary>
public partial class DocumentPreview
{
    /// <summary>One image URL per rendered page, in page order.</summary>
    [Parameter]
    public IReadOnlyList<string> Pages { get; set; } = [];

    /// <summary>When true, replaces the pages with a progress bar.</summary>
    [Parameter]
    public bool Busy { get; set; }

    /// <summary>Progress bar caption while <see cref="Busy"/>.</summary>
    [Parameter]
    public string? Label { get; set; }

    /// <summary>Optional trailing detail for the progress bar.</summary>
    [Parameter]
    public string? Detail { get; set; }

    /// <summary>Alt text applied to every page image.</summary>
    [Parameter]
    public string PageAlt { get; set; } = "Rendered page preview";
}
