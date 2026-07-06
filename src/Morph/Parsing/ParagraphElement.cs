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
    /// Set when this paragraph is the unavoidable empty end-of-cell mark directly after a
    /// nested table. Word collapses it to zero height — the renderers skip synthesizing
    /// its empty line.
    /// </summary>
    public bool IsCollapsedCellMark { get; init; }
}