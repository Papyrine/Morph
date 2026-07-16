/// <summary>
/// Represents an explicit page break.
/// </summary>
sealed class PageBreakElement : DocumentElement
{
    /// <summary>
    /// True when the break was synthesized from a <c>w:lastRenderedPageBreak</c> pagination
    /// hint rather than authored (<c>w:br w:type="page"</c>). Hint breaks that coincide with
    /// a boundary the flow already breaks at are dropped after parsing — an authored break
    /// there would be an intentional blank page, a hint break is just Word recording the
    /// same boundary twice.
    /// </summary>
    public bool FromPaginationHint { get; init; }
}