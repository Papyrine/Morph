/// <summary>
/// Direction the text flows within a table cell (w:textDirection).
/// </summary>
enum CellTextDirection
{
    /// <summary>Default left-to-right horizontal flow (lrTb).</summary>
    LeftToRight,

    /// <summary>Bottom-to-top vertical flow (btLr) — reads bottom-up along the left edge.</summary>
    BottomToTop,

    /// <summary>Top-to-bottom vertical flow (tbRl) — reads top-down along the right edge.</summary>
    TopToBottom
}
