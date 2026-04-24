/// <summary>
/// Represents a parsed DOCX document.
/// </summary>
sealed class ParsedDocument
{
    public required PageSettings PageSettings { get; init; }
    public required IReadOnlyList<DocumentElement> Elements { get; init; }
    public HeaderFooterContent? Header { get; init; }
    public HeaderFooterContent? Footer { get; init; }
    public HeaderFooterContent? FirstPageHeader { get; init; }
    public HeaderFooterContent? FirstPageFooter { get; init; }

    /// <summary>
    /// Document-level hyphenation settings.
    /// </summary>
    public HyphenationSettings Hyphenation { get; init; } = new();

    /// <summary>
    /// Theme colors from the document theme.
    /// </summary>
    public ThemeColors? ThemeColors { get; init; }

    /// <summary>
    /// Theme fonts from the document theme.
    /// </summary>
    public ThemeFonts? ThemeFonts { get; init; }

    /// <summary>
    /// Word compatibility settings from the document.
    /// </summary>
    public CompatibilitySettings Compatibility { get; init; } = new();
}