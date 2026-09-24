namespace Morph;

/// <summary>The zoom <see cref="MorphViewer"/> opens a document at — the named presets of a browser PDF viewer.</summary>
public enum ViewerZoom
{
    /// <summary>
    /// Page width for a portrait page and page fit for a landscape one, never above 125% — PDF.js's
    /// "Automatic zoom", which reads a document comfortably and shows a whole slide.
    /// </summary>
    Auto,

    /// <summary>The whole page fits in the view.</summary>
    PageFit,

    /// <summary>The page fills the view's width.</summary>
    PageWidth,

    /// <summary>100%: one point of the page is one point on screen.</summary>
    ActualSize,
}
