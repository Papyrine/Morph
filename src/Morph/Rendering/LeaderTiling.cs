/// <summary>
/// Where Word draws the glyphs of a tab-leader filler: how far apart, and which of them.
/// </summary>
/// <remarks>
/// <para>
/// Word draws a leader as its glyph repeated on a grid of the PAGE, not from where the filler starts.
/// The grid's pitch is the glyph's advance in whole pixels of Word's 120-dpi grid, at the em rounded
/// to a whole pixel there (<see cref="PitchPoints(FontMetrics, int, double, double)"/>), and its places are whole multiples of that
/// pitch from the page's left edge — whatever the margin, the indent or the table cell. The first
/// glyph stands on the first place at or after the filler's start, and the last is the last whose
/// glyph ends by the filler's end. So the dots of a table of contents line up from entry to entry,
/// and a leader can start up to a pitch past the text before its tab.
/// </para>
/// <para>
/// Word-probed 2026-11-03 (<c>_probe_leader</c>, read off Word's XPS): Calibri, Times New Roman and
/// Arial at 8 to 48pt drew their dots 3 to 22 whole pixels apart, every one
/// <c>round(advance × round(ppem))</c>, in compatibility modes 14 and 15 alike; the first dot of a
/// 24pt Calibri leader stood on the same 6pt grid of the page as the text before its tab grew a
/// letter at a time, as the indent grew by fractions of the pitch, in a section whose margin was 3pt
/// wider, and in a table cell starting 50.15pt into the text. An underscore leader and a heavy one
/// are both drawn as underscores on the same grid. What the probe left open: Word draws each run of
/// 32 glyphs from a position about 0.1% further on than the grid puts it, so a page-wide leader's
/// last dots stand up to 0.4pt right of the grid; and at 48pt the pitch came out 0.025pt over its
/// whole pixels. Neither is modelled.
/// </para>
/// <para>
/// The grid's places are counted in doubles, and clipped to the page before they are walked, because
/// the filler's own extent is only as trustworthy as the pen position — which a malformed document
/// can drive to roughly -5.7e8pt with a single out-of-range <c>w:sz</c> (see
/// <c>OoxmlUnits.FontSizeHalfPointsToPoints</c>), leaving a filler that spans it. Tiling that
/// literally is ~166 million glyphs; in PDF, where every one appends to the content stream, it
/// exhausts memory rather than merely wasting time. The parser clamp and this bound are
/// deliberately independent: the clamp keeps the pen in the range the engine was written for, and
/// this keeps the painters finite whatever the pen does, since a font size is not the only
/// attribute that feeds a pen position.
/// </para>
/// </remarks>
static class LeaderTiling
{
    // How far, as a share of the pitch, a filler's ends may miss a grid place and still count as on
    // it: the layout's float positions can land a hair either side of a place they stand on.
    const double placeSlack = 0.001;

    /// <summary>
    /// The glyph Word repeats across a leader. An underscore leader and a heavy one are both drawn
    /// as underscores.
    /// </summary>
    public static char Glyph(TabLeader leader) =>
        leader switch
        {
            TabLeader.Hyphen => '-',
            TabLeader.MiddleDot => '·',
            TabLeader.Underscore or TabLeader.Heavy => '_',
            _ => '.'
        };

    /// <summary>
    /// Whether a leader is drawn as one rule across the extent of its underscores rather than glyph
    /// by glyph.
    /// </summary>
    public static bool IsRule(TabLeader leader) =>
        leader is TabLeader.Underscore or TabLeader.Heavy;

    /// <summary>
    /// <see cref="PitchPoints(FontMetrics, int, double, double)"/> for a leader filler's own glyph,
    /// face and size, read from the tables the layout measures with.
    /// </summary>
    public static double PitchPoints(RenderContextBase context, PlacedRun run, double naturalPoints)
    {
        var properties = run.Properties;
        var metrics = context.LayoutMetrics(properties.FontFamily, properties.Bold, properties.Italic);
        return PitchPoints(metrics, Glyph(run.Leader), VerticalRunPosition.RenderSizePoints(properties), naturalPoints);
    }

    /// <summary>
    /// The pitch, in points, Word draws the leader glyph <paramref name="codepoint"/> at, set at
    /// <paramref name="sizePoints"/> in the face whose tables are <paramref name="metrics"/>: its
    /// advance at the em rounded to a whole 120-dpi pixel, the 26.6 scaled advance rounded to a whole
    /// pixel. <paramref name="naturalPoints"/> where there are no tables to read it from.
    /// </summary>
    public static double PitchPoints(FontMetrics? metrics, int codepoint, double sizePoints, double naturalPoints)
    {
        if (metrics is not {UnitsPerEm: > 0} ||
            !double.IsFinite(sizePoints) ||
            sizePoints <= 0)
        {
            return naturalPoints;
        }

        var ppem = Math.Round(sizePoints * CanonicalTextMeasurer.ReferenceDpi / 72, MidpointRounding.AwayFromZero);
        var scaled = Math.Round(metrics.AdvanceUnits(codepoint) * ppem * 64 / metrics.UnitsPerEm, MidpointRounding.AwayFromZero);
        var pixels = Math.Floor((scaled + 32) / 64);
        if (pixels <= 0)
        {
            return naturalPoints;
        }

        return pixels * 72 / CanonicalTextMeasurer.ReferenceDpi;
    }

    /// <summary>
    /// The grid places to draw for a leader filling <paramref name="width"/> from <paramref name="x"/>,
    /// its glyphs <paramref name="pitch"/> apart on the page's grid, clipped to the page
    /// <c>[0, <paramref name="pageWidth"/>]</c>.
    /// </summary>
    /// <remarks>
    /// Unit-agnostic, as long as every argument is in the same unit and <paramref name="x"/> is
    /// measured from the page's left edge. <paramref name="startX"/> comes back as a position rather
    /// than a place index because the index can itself exceed <see cref="int.MaxValue"/> on the
    /// inputs the clip exists to survive; the caller draws at <c>startX + index * pitch</c>.
    /// </remarks>
    /// <returns>False when no glyph fits and the painter should draw no leader at all.</returns>
    public static bool TryGetRange(
        double x,
        double width,
        double pitch,
        double pageWidth,
        out double startX,
        out int count)
    {
        startX = x;
        count = 0;

        if (pitch <= 0 ||
            !double.IsFinite(x) ||
            !double.IsFinite(width) ||
            !double.IsFinite(pitch) ||
            !double.IsFinite(pageWidth))
        {
            return false;
        }

        // The first place at or after the filler's start and the page's left edge, and the last whose
        // glyph ends by the filler's end and the page's right edge.
        var first = Math.Ceiling(Math.Max(x, 0) / pitch - placeSlack);
        var last = Math.Floor(Math.Min(x + width, pageWidth) / pitch + placeSlack) - 1;
        var places = last - first + 1;
        if (!(places >= 1))
        {
            return false;
        }

        startX = first * pitch;
        count = (int) Math.Min(places, int.MaxValue);
        return true;
    }
}
