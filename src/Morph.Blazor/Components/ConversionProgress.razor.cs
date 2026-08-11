namespace Morph;

/// <summary>
/// An indeterminate progress bar for the read / render / convert phases. Morph's converters run to
/// completion without a per-item progress callback, so the bar always animates.
/// </summary>
public partial class ConversionProgress
{
    /// <summary>Human-readable description of the current phase, e.g. "Rendering preview…".</summary>
    [Parameter]
    public string? Label { get; set; }

    /// <summary>Optional trailing detail, e.g. "5 pages" or the uploaded file's size.</summary>
    [Parameter]
    public string? Detail { get; set; }
}
