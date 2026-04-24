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
    /// <param name="followingWidth">
    /// Width of the text fragment that follows the tab, up to the next tab or end-of-line.
    /// Used by Center and Right tabs to compute the destination.
    /// </param>
    /// <param name="tabStops">Paragraph tab stops sorted ascending by <see cref="TabStop.PositionPoints"/>.</param>
    /// <param name="defaultTabStopPoints">Document-level default tab width (typically 36 pt = 0.5").</param>
    /// <param name="leftIndentPoints">Paragraph left indent; default tabs are multiples of this base.</param>
    /// <returns>
    /// <c>destinationX</c> in points and the matched <see cref="TabStop"/> (null for default-tab snap).
    /// If no valid destination is found past the cursor, returns <c>(cursorX, null)</c> — tab collapses.
    /// </returns>
    public static (double destinationX, TabStop? stop) Resolve(
        double cursorX,
        double followingWidth,
        IReadOnlyList<TabStop> tabStops,
        double defaultTabStopPoints,
        double leftIndentPoints)
    {
        // Try each explicit stop whose position is strictly beyond the cursor.
        // For Right/Center, the effective destination may land behind the cursor if followingWidth
        // is large relative to the stop position — in that case skip to the next stop.
        foreach (var stop in tabStops)
        {
            if (stop.PositionPoints <= cursorX)
            {
                continue;
            }

            var destination = stop.Alignment switch
            {
                TabAlignment.Center => stop.PositionPoints - (followingWidth / 2.0),
                TabAlignment.Right => stop.PositionPoints - followingWidth,
                // Decimal falls back to Left until decimal alignment is implemented.
                _ => stop.PositionPoints
            };

            if (destination > cursorX)
            {
                return (destination, stop);
            }
        }

        // No explicit stop matched — fall through to default tab stops.
        // Defaults are only consulted past the last explicit stop (per OOXML).
        if (defaultTabStopPoints <= 0)
        {
            return (cursorX, null);
        }

        var lastExplicit = tabStops.Count > 0 ? tabStops[^1].PositionPoints : leftIndentPoints;
        var basePosition = Math.Max(leftIndentPoints, lastExplicit);

        // Snap to the next multiple of defaultTabStopPoints past cursorX, measured from basePosition.
        var offsetFromBase = cursorX - basePosition;
        var nextMultipleIndex = Math.Floor(offsetFromBase / defaultTabStopPoints) + 1;
        var destinationX = basePosition + (nextMultipleIndex * defaultTabStopPoints);

        if (destinationX <= cursorX)
        {
            return (cursorX, null);
        }

        return (destinationX, null);
    }
}
