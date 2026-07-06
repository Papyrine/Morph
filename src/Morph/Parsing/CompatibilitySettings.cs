/// <summary>
/// Word compatibility settings that affect layout behavior.
/// Based on settings from settings.xml w:compat section.
/// </summary>
sealed class CompatibilitySettings
{
    /// <summary>
    /// Word compatibility mode version.
    /// 11 = Word 2003, 12 = Word 2007 (ECMA-376), 14 = Word 2010, 15 = Word 2013+
    /// The record default of 15 (modern behavior) applies to HTML-sourced documents; the DOCX
    /// parser supplies 12 when the document declares no compatibilityMode, matching Word.
    /// </summary>
    public int CompatibilityMode { get; init; } = 15;

    /// <summary>
    /// Whether to use legacy line spacing in table cells.
    /// For compatibility mode 14 or lower, table cells may use different line spacing rules.
    /// </summary>
    public bool UseLegacyTableLineSpacing => CompatibilityMode <= 14;

    /// <summary>
    /// Whether to add extra line spacing to table cells (Word 2013+ behavior).
    /// In mode 15+, table cells may get additional line spacing for single-spaced text.
    /// </summary>
    public bool AddLineSpacingToTableCells => CompatibilityMode >= 15;
}