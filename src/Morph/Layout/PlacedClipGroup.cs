/// <summary>
/// Placed items drawn only where they fall inside the group's box, which is in points from the page's
/// top-left like every other <see cref="PlacedItem"/>. The items keep their own absolute positions and
/// may reach outside the box; a painter clips to it, draws them and lifts the clip.
///
/// <para>This is how a spreadsheet's drawings follow its grid across pages: a picture standing over
/// rows that a page break parts is printed by Excel in two pieces, the top on one page and the rest on
/// the next, each cut at the rows its page holds (see <c>Fragmenter.SpreadFloatsOverTable</c>).</para>
/// </summary>
sealed record PlacedClipGroup(
    float X,
    float Y,
    float Width,
    float Height,
    IReadOnlyList<PlacedItem> Items) : PlacedItem(X, Y, Width, Height);
