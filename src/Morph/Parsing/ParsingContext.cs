namespace WordRender;

/// <summary>
/// Shared context for parsing operations, providing access to theme data,
/// styles, and document parts needed by various parsers.
/// </summary>
public sealed class ParsingContext
{
    /// <summary>
    /// Theme colors extracted from the document.
    /// </summary>
    public ThemeColors? ThemeColors { get; init; }

    /// <summary>
    /// Theme fonts extracted from the document.
    /// </summary>
    public ThemeFonts? ThemeFonts { get; init; }

    /// <summary>
    /// Style definitions: styleId -> full run properties.
    /// </summary>
    public Dictionary<string, RunProperties>? StyleRunProperties { get; init; }
}
