/// <summary>
/// Represents a paragraph in the document.
/// </summary>
sealed class ParagraphElement : DocumentElement
{
    public required IReadOnlyList<Run> Runs { get; init; }
    public ParagraphProperties Properties { get; init; } = new();

    /// <summary>
    /// Set when this paragraph is a residual mark for one whose runs contained only
    /// out-of-flow anchored drawings. The layout gives it a line like any other mark, which is
    /// Word's: agendas-minutes/14's XPS puts the text under one where that line and the mark's
    /// spacing put it, and agendas-minutes/11 matches Word line for line with it laid out. Dropping
    /// the line instead regresses 104 of 108 pages. The parser reads it to tell such a mark from the
    /// end of a cell (DocumentParser's collapsed cell mark).
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

    /// <summary>
    /// The parsed paragraph this one was rebuilt from when the layout read its page fields
    /// (<c>Fragmenter.Flow.MapPageFields</c>); null for a paragraph as parsed. The second layout pass
    /// places the rebuilt paragraph, so what reads placed lines back against the parsed document
    /// (<c>PageTextReader</c>, <c>DocumentConverter.ParagraphPages</c>) finds them under this one.
    /// </summary>
    public ParagraphElement? RebuiltFrom { get; init; }

    /// <summary>
    /// The parsed paragraph this one stands for: itself, or the one it was rebuilt from.
    /// </summary>
    public ParagraphElement Parsed => RebuiltFrom ?? this;

    /// <summary>
    /// Whether a tracked change altered the paragraph's properties (<c>w:pPrChange</c>,
    /// <c>w:numberingChange</c>): Word's change bar stands beside its first line. Of a <c>w:p</c> split
    /// around a break, the first paragraph. Settable because the parser stamps it once the paragraph is
    /// built.
    /// </summary>
    public bool PropertiesRevised { get; set; }

    /// <summary>
    /// Whether a tracked change inserted, deleted, moved or reformatted the paragraph's mark: Word's
    /// change bar stands beside its last line. Of a <c>w:p</c> split around a break, the last
    /// paragraph. Settable as <see cref="PropertiesRevised"/> is.
    /// </summary>
    public bool MarkRevised { get; set; }

    /// <summary>
    /// The ordinal of the <c>w:p</c> this paragraph was read from, among the main document part's
    /// paragraphs in document order, when the parser was asked to record it (<c>captureSources</c>);
    /// null otherwise. One <c>w:p</c> split around a break is several paragraphs with one ordinal.
    /// Settable because the parser stamps it once the paragraph is built; layout never reads it.
    /// </summary>
    public int? Source { get; set; }
}