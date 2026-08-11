/// <summary>
/// What a <c>w:ptab</c>'s position is measured against (<c>w:relativeTo</c>).
/// </summary>
enum PositionalTabBase
{
    Margin,
    Indent,
    Page
}

/// <summary>
/// An absolute position tab (<c>w:ptab</c>): unlike <c>w:tab</c> it snaps to no stop list, it jumps
/// straight to a position derived from the text area and aligns the following text there. Word uses it
/// for the header/footer furniture a stop list would otherwise have to encode — a page number pinned to
/// the right margin, a marking centred across the band.
///
/// Held on the run rather than resolved at parse time because the position depends on the measure the
/// paragraph is finally laid out in (a column, a table cell), which the parser does not know.
/// </summary>
sealed record PositionalTab
{
    public required TabAlignment Alignment { get; init; }
    public TabLeader Leader { get; init; } = TabLeader.None;
    public PositionalTabBase RelativeTo { get; init; } = PositionalTabBase.Margin;
}
