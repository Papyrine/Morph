namespace Morph;

/// <summary>
/// Where this package's static web assets live. A Razor class library's <c>wwwroot</c> is served by the
/// host app under <c>_content/{PackageId}/</c>, so every asset path the library uses — the stylesheet, the
/// JavaScript module, the bundled fonts and samples — is built from this one root.
/// </summary>
public static class MorphAssets
{
    /// <summary>The base path the package's static web assets are served from.</summary>
    public const string ContentRoot = "_content/Morph.Blazor";

    /// <summary>
    /// The bundled stylesheet. A host app must link this for the components to look like anything —
    /// see the package README for the <c>--morph-*</c> custom properties it reads for theming.
    /// </summary>
    public const string StyleSheet = ContentRoot + "/morph.css";

    /// <summary>The JavaScript module the components import for downloads, blob URLs and viewport width.</summary>
    internal const string Script = ContentRoot + "/morph.js";

    /// <summary>The module that builds a page's selectable text layer, shared by the preview and the viewer.</summary>
    internal const string TextScript = ContentRoot + "/morph-text.js";

    /// <summary>The viewer's controller: pages, zoom, scrolling, presentation mode, printing and find highlights.</summary>
    internal const string ViewerScript = ContentRoot + "/morph-viewer.js";
}
