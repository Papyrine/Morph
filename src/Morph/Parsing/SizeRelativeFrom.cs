/// <summary>
/// Reference frame for percentage-based sizing of anchored drawings
/// (<c>wp14:sizeRelH/@relativeFrom</c> and <c>wp14:sizeRelV/@relativeFrom</c>).
/// Horizontal axis can use Page / Margin / LeftMargin / RightMargin / InsideMargin /
/// OutsideMargin; vertical axis can use Page / Margin / TopMargin / BottomMargin /
/// InsideMargin / OutsideMargin. Morph collapses the inside/outside variants down to
/// the closest content-area reference (Margin) since mirror-margin layout isn't yet
/// honoured by the renderer.
/// </summary>
enum SizeRelativeFrom
{
    /// <summary>Percentage of the page width/height (full page).</summary>
    Page,

    /// <summary>Percentage of the content area (page minus margins).</summary>
    Margin
}
