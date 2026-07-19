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
    /// Optional right-edge X (absolute, in points) of the visible content area — the paragraph's
    /// cell or column content width, NOT reduced by the paragraph's right indent (Word honours
    /// explicit stops inside the indent zone). A Right/Center/Decimal stop past this edge clamps
    /// to it — Word right-aligns TOC page numbers at the cell edge when the TOC style's stop
    /// exceeds the cell (business-plans/12/13, table_of_contents/03, verified against Word).
    /// </param>
    /// <returns>
    /// <c>destinationX</c> in points, the matched <see cref="TabStop"/> (null for default-tab
    /// snap), and <c>suppressFollowing</c> (always false today; retained so callers keep their
    /// drop-content plumbing for any future suppression case). If no valid destination is found
    /// past the cursor, returns <c>(cursorX, null, false)</c> — the tab collapses.
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

            // Word CLAMPS a Right/Center/Decimal stop past the paragraph's wrap width to the wrap
            // width itself: business-plans/12's TOC cell inherits TOC1's 539.5pt stop inside a
            // ~310pt cell, and Word right-aligns the page numbers at the CELL's edge (verified
            // against its render; business-plans/13's stop sits just past its cell and lands the
            // same way). table_of_contents/03 — dot leaders to the cell edge and no number — is
            // the same clamp: that fixture's entries have NO text after the tab at all, so the
            // leader filling to the clamped stop is everything Word shows. An earlier reading of
            // it ("drop the following text when it would start past the edge") mistook that
            // vacuous case for a suppression rule.
            if (availableEndX is { } endX &&
                stopPosition > endX &&
                stop.Alignment is TabAlignment.Right or TabAlignment.Center or TabAlignment.Decimal)
            {
                stopPosition = endX;
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

            if (stopPosition <= cursorX)
            {
                continue;
            }

            if (destination > cursorX)
            {
                return (destination, stop, false);
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
