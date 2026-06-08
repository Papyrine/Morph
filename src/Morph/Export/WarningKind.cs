namespace Morph;

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
