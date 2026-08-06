/// <summary>
/// Represents a paragraph in the document.
/// </summary>
sealed class ParagraphElement : DocumentElement
{
    public required IReadOnlyList<Run> Runs { get; init; }
    public ParagraphProperties Properties { get; init; } = new();

    /// <summary>
    /// Set when this paragraph is a residual mark for one whose runs contained only
    /// out-of-flow anchored drawings. Word does not render a line for the paragraph
    /// mark in this case — the renderer should skip synthesizing an empty line and
    /// only honour the paragraph's spacing-after.
    /// </summary>
    public bool IsAnchorOnlyMark { get; init; }

    /// <summary>
    /// This runless paragraph exists only to carry a section break's <c>sectPr</c>. Word prints no line
    /// for it — only its spacing survives — so the measurer gives it no line box. Word-probed
    /// (<c>_probe_sect_break</c>, 2026-08-06): ALPHA / empty paragraph / BRAVO at single spacing puts
    /// BRAVO two line pitches below ALPHA when the empty paragraph is ordinary (27.3pt) but ONE pitch
    /// below (13.4pt) when that paragraph carries the <c>sectPr</c>.
    /// </summary>
    public bool IsSectionBreakMark { get; init; }

    /// <summary>
    /// Set when this paragraph is the unavoidable empty end-of-cell mark directly after a
    /// nested table. Word collapses it to zero height — the renderers skip synthesizing
    /// its empty line.
    /// </summary>
    public bool IsCollapsedCellMark { get; init; }
}