/// <summary>
/// Shared tab-stop snapping math used by both rendering backends.
/// All coordinates are in points, measured from the left margin.
/// </summary>
static class TabStopResolver
{
    /// <summary>
    /// Resolves the destination X coordinate for a tab character starting at <paramref name="cursorX"/>.
    /// </summary>
    /// <param name="cursorX">Current cursor X (points from left margin).</param>
    /// <param name="measureFollowingWidth">
    /// Measures the width of the text fragment that follows the tab, up to the next tab or
    /// end-of-line. Only Center and Right stops (and Decimal without a prefix) need it, so it is
    /// invoked lazily — Left/default tabs, the common case, skip the measurement entirely (the
    /// layout loop re-measures the same text word-by-word right after).
    /// </param>
    /// <param name="tabStops">Paragraph tab stops sorted ascending by <see cref="TabStop.PositionPoints"/>.</param>
    /// <param name="defaultTabStopPoints">Document-level default tab width (typically 36 pt = 0.5").</param>
    /// <param name="leftIndentPoints">Paragraph left indent; default tabs are multiples of this base.</param>
    /// <param name="decimalPrefixWidth">
    /// Width of the following text up to (but excluding) its decimal point, used to align
    /// <see cref="TabAlignment.Decimal"/> stops. When null or no decimal point is present,
    /// decimal stops fall back to right-alignment.
    /// </param>
    /// <param name="availableEndX">
    /// Optional right-edge X (absolute, in points) past which the following text must not extend
    /// — typically the right edge of the paragraph's content area or table cell. When supplied,
    /// Right/Center/Decimal stops whose natural position lies past this edge are clamped so the
    /// following text terminates exactly at it. This matches Word's behavior for TOC entries
    /// whose style-defined right-tab (e.g. 10790 twips) lands inside a narrower table cell:
    /// the page number still right-aligns at the cell edge, with the leader filling the gap.
    /// </param>
    /// <returns>
    /// <c>destinationX</c> in points, the matched <see cref="TabStop"/> (null for default-tab snap),
    /// and <c>suppressFollowing</c>: true when a Right/Center/Decimal stop was clamped to
    /// <paramref name="availableEndX"/> because its real position lay past the cell — in that case
    /// the tab fills with leader to the cell edge and the post-tab content (typically a TOC page
    /// number) is hidden, matching Word's behaviour.
    /// If no valid destination is found past the cursor, returns <c>(cursorX, null, false)</c> —
    /// the tab collapses.
    /// </returns>
    public static (double destinationX, TabStop? stop, bool suppressFollowing) Resolve(
        double cursorX,
        Func<double> measureFollowingWidth,
        IReadOnlyList<TabStop> tabStops,
        double defaultTabStopPoints,
        double leftIndentPoints,
        double? decimalPrefixWidth = null,
        double? availableEndX = null)
    {
        double? measuredFollowingWidth = null;
        double FollowingWidth() => measuredFollowingWidth ??= measureFollowingWidth();

        // Try each explicit stop whose position is strictly beyond the cursor.
        // For Right/Center/Decimal, the effective destination may land behind the cursor if the
        // measured following text is too wide — in that case skip to the next stop.
        foreach (var stop in tabStops)
        {
            var stopPosition = stop.PositionPoints;
            var suppressFollowing = false;
            // Clamp out-of-bounds Right/Center/Decimal stops to the available right edge.
            // Word's behaviour for a TOC entry inside a narrow table cell: leader fills the
            // cell width and the page number after the tab is hidden (it would have lived past
            // the cell's right edge). We mirror that by clamping the destination to the cell
            // edge (no following-width subtraction) and signalling the caller to drop the
            // post-tab content.
            if (availableEndX is { } endX &&
                stopPosition > endX &&
                stop.Alignment is TabAlignment.Right or TabAlignment.Center or TabAlignment.Decimal)
            {
                if (endX > cursorX)
                {
                    return (endX, stop, true);
                }

                stopPosition = endX;
                suppressFollowing = true;
            }

            if (stopPosition <= cursorX)
            {
                continue;
            }

            var destination = stop.Alignment switch
            {
                TabAlignment.Center => stopPosition - FollowingWidth() / 2.0,
                TabAlignment.Right => stopPosition - FollowingWidth(),
                // Decimal: align the decimal point of the following text at the tab position.
                // If the caller didn't measure a decimal-prefix width (or the following text has no
                // decimal point), behave like Right — that's what Word does as a fallback.
                TabAlignment.Decimal => stopPosition - (decimalPrefixWidth ?? FollowingWidth()),
                _ => stopPosition
            };

            if (destination > cursorX)
            {
                return (destination, stop, suppressFollowing);
            }
        }

        // No explicit stop matched — fall through to default tab stops.
        // Defaults are only consulted past the last explicit stop (per OOXML).
        if (defaultTabStopPoints <= 0)
        {
            return (cursorX, null, false);
        }

        var lastExplicit = tabStops.Count > 0 ? tabStops[^1].PositionPoints : leftIndentPoints;
        var basePosition = Math.Max(leftIndentPoints, lastExplicit);

        // Snap to the next multiple of defaultTabStopPoints past cursorX, measured from basePosition.
        var offsetFromBase = cursorX - basePosition;
        var nextMultipleIndex = Math.Floor(offsetFromBase / defaultTabStopPoints) + 1;
        var destinationX = basePosition + nextMultipleIndex * defaultTabStopPoints;

        if (destinationX <= cursorX)
        {
            return (cursorX, null, false);
        }

        return (destinationX, null, false);
    }
}
