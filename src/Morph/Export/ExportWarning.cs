namespace Morph;

/// <summary>
/// A notice that a feature in the source document was either dropped or degraded when producing
/// the chosen output format. Delivered to <see cref="ExportOptions.OnWarning"/>.
/// </summary>
/// <param name="Kind">Category of the loss (use for filtering / counting).</param>
/// <param name="Message">Human-readable detail.</param>
public sealed record ExportWarning(WarningKind Kind, string Message);

/// <summary>Categories of <see cref="ExportWarning"/>.</summary>
public enum WarningKind
{
    /// <summary>The output format has no representation for this source element (e.g. ink strokes
    /// in HTML, foreground floating shapes in PDF).</summary>
    UnsupportedElement,

    /// <summary>A font referenced by the document could not be resolved; substituted with the
    /// default font.</summary>
    MissingFont,

    /// <summary>An embedded image could not be decoded or embedded (e.g. unsupported format with
    /// no raster fallback).</summary>
    ImageRenderingFailed
}
