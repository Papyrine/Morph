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

    /// <summary>
    /// Whether a justified line may take one more word than fits by narrowing its spaces (Word 2013+
    /// behavior). In mode 14 and older a justified line breaks exactly where a left-aligned one does.
    /// The rule and its Word probe are on <c>CanonicalParagraphMeasurer.BuildWrap</c>.
    /// </summary>
    public bool SqueezesJustifiedLines => CompatibilityMode >= 15;

    /// <summary>
    /// Whether a paragraph that opens its page with <c>w:pageBreakBefore</c> keeps the part of its
    /// spacing-before that exceeds the previous paragraph's spacing-after (Word 2010 and older
    /// behavior). In mode 15 it keeps none. The Word probe is on <c>Fragmenter.PlaceParagraph</c>.
    /// </summary>
    public bool KeepsSpacingBeforeAfterPageBreakBefore => CompatibilityMode <= 14;

    /// <summary>
    /// <c>w:doNotUseHTMLParagraphAutoSpacing</c>: a paragraph whose spacing is "Auto"
    /// (<c>w:beforeAutospacing</c> / <c>w:afterAutospacing</c>) takes the amount declared beside the
    /// flag, everywhere, where a document without the setting gives it 14pt and nothing at a cell's
    /// edge or inside a list. The probe is on <c>DocumentParser.ResolveAutoSpacing</c>.
    /// </summary>
    public bool UsesFixedAutoSpacing { get; init; }
}