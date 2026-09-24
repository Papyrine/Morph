namespace Morph;

/// <summary>
/// Shows a document's rendered pages, or a progress bar while they render. Pages given a
/// <see cref="TextLayers">text layer</see> carry selectable text: the browser can select, copy and find
/// every word on them, as in a PDF viewer.
///
/// Rendering is the host's job — pass image URLs (typically <c>data:image/png;base64,…</c> built from
/// <see cref="ConversionService.RenderPages"/>) rather than document bytes, so the component stays
/// free of the threading and font-loading concerns that belong to whoever owns the conversion.
/// <see cref="MorphConverter"/> is the batteries-included version of that host.
/// </summary>
public partial class DocumentPreview
{
    /// <summary>One image URL per rendered page, in page order.</summary>
    [Parameter]
    public IReadOnlyList<string> Pages { get; set; } = [];

    /// <summary>
    /// Optional selectable text, one layer per entry in <see cref="Pages"/> (from
    /// <see cref="RenderedPage.TextLayer"/>). Without it the pages are plain images. The images must be
    /// full-page renders for the text to line up.
    /// </summary>
    [Parameter]
    public IReadOnlyList<PageTextLayer>? TextLayers { get; set; }

    /// <summary>When true, replaces the pages with a progress bar.</summary>
    [Parameter]
    public bool Busy { get; set; }

    /// <summary>Progress bar caption while <see cref="Busy"/>.</summary>
    [Parameter]
    public string? Label { get; set; }

    /// <summary>Optional trailing detail for the progress bar.</summary>
    [Parameter]
    public string? Detail { get; set; }

    /// <summary>Alt text applied to every page image that has no text layer.</summary>
    [Parameter]
    public string PageAlt { get; set; } = "Rendered page preview";

    ElementReference[] textLayerElements = [];

    // What each overlay was last filled with, so a re-render that changes nothing about a page (the
    // parent repainting for an unrelated reason) doesn't rebuild its layer.
    PageTextLayer?[] builtLayers = [];

    PageTextLayer? LayerAt(int index) =>
        TextLayers is { } layers && index < layers.Count ? layers[index] : null;

    protected override void OnParametersSet()
    {
        if (textLayerElements.Length != Pages.Count)
        {
            textLayerElements = new ElementReference[Pages.Count];
            builtLayers = new PageTextLayer?[Pages.Count];
        }

        if (Busy)
        {
            // The overlays are about to leave the DOM; whatever renders next starts from empty ones.
            Array.Clear(builtLayers);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Busy)
        {
            return;
        }

        for (var index = 0; index < Pages.Count; index++)
        {
            if (LayerAt(index) is not { } layer ||
                ReferenceEquals(builtLayers[index], layer))
            {
                continue;
            }

            builtLayers[index] = layer;
            await Interop.RenderTextLayerAsync(textLayerElements[index], layer);
        }
    }
}
