/// <summary>
/// Which leader glyphs a painter actually has to draw across a tab-leader filler.
/// </summary>
/// <remarks>
/// <para>
/// A leader is the glyph repeated at its natural advance from the tab start to the tab stop, so the
/// obvious loop is <c>(width - glyph) / spacing + 1</c> iterations from the filler's own X. That
/// count is only as trustworthy as the width, and the width comes from the pen position — which a
/// malformed document can drive to roughly -5.7e8pt with a single out-of-range <c>w:sz</c>
/// (see <c>OoxmlUnits.FontSizeHalfPointsToPoints</c>), leaving a filler that spans it. Tiling that
/// literally is ~166 million glyphs; in PDF, where every one appends to the content stream, it
/// exhausts memory rather than merely wasting time.
/// </para>
/// <para>
/// So the range is clipped to the page before it is walked. The clip drops WHOLE tiles rather than
/// re-anchoring the run, which is what keeps this invisible to correct documents: every glyph stays
/// at the exact X it would have had, and a leader already on the page yields the same first index
/// and count as the unclipped arithmetic did. Only glyphs that would have landed off the page are
/// dropped — and those could not have been seen.
/// </para>
/// <para>
/// The parser clamp and this bound are deliberately independent. The clamp keeps the pen in the
/// range the engine was written for; this keeps the painters finite whatever the pen does, since a
/// font size is not the only attribute that feeds a pen position.
/// </para>
/// </remarks>
static class LeaderTiling
{
    /// <summary>
    /// Computes the tiles to draw for a leader starting at <paramref name="x"/> spanning
    /// <paramref name="width"/>, with a glyph of <paramref name="glyphWidth"/> repeated
    /// <paramref name="spacing"/> apart, clipped to the page <c>[0, <paramref name="pageWidth"/>]</c>.
    /// </summary>
    /// <remarks>
    /// Unit-agnostic: every argument is in the caller's own unit (points for the PDF painter, device
    /// pixels for the raster ones). <paramref name="startX"/> comes back as a position rather than a
    /// tile index because the index of the first visible tile can itself exceed
    /// <see cref="int.MaxValue"/> on the inputs this exists to survive; the caller draws at
    /// <c>startX + index * spacing</c>.
    /// </remarks>
    /// <returns>False when nothing is visible and the painter should draw no leader at all.</returns>
    public static bool TryGetRange(
        double x,
        double width,
        double glyphWidth,
        double spacing,
        double pageWidth,
        out double startX,
        out int count)
    {
        startX = x;
        count = 0;

        if (spacing <= 0 ||
            !double.IsFinite(x) ||
            !double.IsFinite(width) ||
            !double.IsFinite(spacing) ||
            !double.IsFinite(pageWidth))
        {
            return false;
        }

        // Skip the tiles that end before the left page edge, and stop at the last one that starts
        // before the right. Both are counted from x, in doubles, because the unclipped index of the
        // first visible tile can exceed int.MaxValue on the very inputs this exists to survive.
        var first = Math.Max(0, Math.Ceiling(-x / spacing));
        var lastOffset = Math.Min(width - glyphWidth, pageWidth - x);
        var last = Math.Floor(lastOffset / spacing);
        var tiles = last - first + 1;
        if (!(tiles >= 1))
        {
            return false;
        }

        startX = x + first * spacing;
        count = (int) Math.Min(tiles, int.MaxValue);
        return true;
    }
}
