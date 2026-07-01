namespace Morph.Web.Components;

public partial class ConversionProgress
{
    /// <summary>Human-readable description of the current phase, e.g. "Rendering preview…".</summary>
    [Parameter]
    public string? Label { get; set; }

    /// <summary>Optional trailing detail, e.g. "5 pages" or the uploaded file's size.</summary>
    [Parameter]
    public string? Detail { get; set; }
}
