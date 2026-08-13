/// <summary>
/// Turns a <see cref="BorderEdge"/> into the concrete lines a painter strokes, so Skia, ImageSharp
/// and PDF cannot drift apart on what "double" or "dotDash" means.
///
/// <para><b>Bands.</b> A multi-line style lays out as units of line and gap: <c>double</c> is 1-1-1
/// (line, gap, line), <c>triple</c> is 1-1-1-1-1, <c>thinThickSmallGap</c> is 1-1-2, and the Medium
/// and Large variants widen only the gap. What one unit MEASURES differs by family, and both halves
/// are Word-probed (see <c>_probe_bordersp</c> and the comment in <see cref="Bands"/>): for the
/// symmetric families a unit is the declared <c>w:sz</c> itself, so a 3pt <c>double</c> stacks to
/// 9pt, while the thin/thick family divides the declared width between its units. Offsets come back
/// relative to the edge's CENTRE, which is where the single-line styles already draw — so this
/// changes nothing for the plain <c>single</c> borders that make up almost the whole corpus.</para>
///
/// <para><b>Dashes.</b> Patterns are multiples of the declared width, which is how Word scales
/// them: a 3pt dashed border has visibly longer dashes than a 0.5pt one.</para>
///
/// <para><b>Not modelled.</b> <c>wave</c> and <c>doubleWave</c> stroke straight (one and two lines)
/// — the sine path would need geometry in three painters and no corpus document uses either.
/// <c>threeDEmboss</c>/<c>threeDEngrave</c> draw as two lines and <c>outset</c>/<c>inset</c> as one,
/// matching Word's flat-on-paper appearance; the light/dark bevel shading is not reproduced.</para>
/// </summary>
static class BorderStroke
{
    /// <summary>One stroked line within a border edge: <paramref name="Offset"/> is the
    /// perpendicular displacement from the edge's centre line, in points.</summary>
    internal readonly record struct Band(double Offset, double Thickness);

    static readonly Band[] singleBand = [new(0, 1)];

    /// <summary>
    /// Which border a stroke belongs to. Word does not scale the two the same way — see the
    /// <c>perLine</c> comment in <see cref="Bands"/> — so the caller has to say.
    /// </summary>
    internal enum Scope
    {
        Paragraph,
        Cell
    }

    /// <summary>
    /// The lines making up this edge, thicknesses already scaled from <paramref name="totalWidth"/>.
    /// Returns empty for a style that draws nothing.
    /// </summary>
    internal static Band[] Bands(BorderLineStyle style, double totalWidth, Scope scope = Scope.Paragraph)
    {
        if (totalWidth <= 0 || style == BorderLineStyle.None)
        {
            return [];
        }

        // Line/gap layout in units, outermost first. Odd entries are gaps.
        int[]? layout = style switch
        {
            BorderLineStyle.Double or BorderLineStyle.DoubleWave or
                BorderLineStyle.ThreeDEmboss or BorderLineStyle.ThreeDEngrave => [1, 1, 1],
            BorderLineStyle.Triple => [1, 1, 1, 1, 1],
            BorderLineStyle.ThinThickSmallGap => [1, 1, 2],
            BorderLineStyle.ThickThinSmallGap => [2, 1, 1],
            BorderLineStyle.ThinThickThinSmallGap => [1, 1, 2, 1, 1],
            BorderLineStyle.ThinThickMediumGap => [1, 2, 2],
            BorderLineStyle.ThickThinMediumGap => [2, 2, 1],
            BorderLineStyle.ThinThickThinMediumGap => [1, 2, 2, 2, 1],
            BorderLineStyle.ThinThickLargeGap => [1, 3, 2],
            BorderLineStyle.ThickThinLargeGap => [2, 3, 1],
            BorderLineStyle.ThinThickThinLargeGap => [1, 3, 2, 3, 1],
            _ => null
        };

        if (layout == null)
        {
            return [new(0, totalWidth)];
        }

        var units = 0;
        foreach (var segment in layout)
        {
            units += segment;
        }

        // Word does NOT divide a small declared width down into invisible slivers — it floors each
        // line and gap and lets the stack exceed w:sz. Measured off Word's own render of
        // border_style_variants at 150 DPI: at sz=6 (0.75pt declared) a `double` edge draws two
        // 1-2px lines with a 1px gap, ~5px total against the 1.6px the declared width would allow,
        // and `triple` spans ~7px. Without a floor every multi-line style collapses into one grey
        // line at the sz=2..8 widths that dominate real documents.
        //
        // The floor is 0.75pt rather than the 0.5pt (= 1px at 150 DPI) Word actually uses, because
        // Word draws these rules pixel-aligned and unantialiased while Morph antialiases them: at
        // 0.5pt the gap is ~1px and the two lines' antialiasing closes it, and which edges survive
        // depends on pixel phase — rendering the fixture at 0.5pt merged the double box's TOP edge
        // into one 4px run while its bottom edge still split into two. 0.75pt separates reliably on
        // every edge. That trades a ~0.5px wider gap for reproducing the structure Word draws,
        // which is the part a reader sees.
        const double minUnitPoints = 0.75;

        // For the symmetric families w:sz is the width of EACH LINE, not of the whole stack —
        // Word-probed with _probe_bordersp (an unbordered mark above and below each bordered
        // paragraph, so the mark-to-mark distance isolates exactly what the border reserves, A4,
        // 150 DPI). A `single` at sz=24 measured 109px mark-to-mark and a `double` at the same
        // sz=24 measured 134px: 25px = 12pt more, which is 6pt per edge on top of the 3pt a single
        // draws, i.e. two 3pt lines with a 3pt gap. Dividing sz between the lines instead gave 108px
        // and left that border 26px short of Word's. `double` at sz=6 measured 105px against
        // single's 100px, again the 1-1-1 stack at the declared width, and `triple` at sz=6 measured
        // 111px against a predicted 3.75pt stack.
        //
        // The thin/thick family does NOT follow that rule — thinThickLargeGap at sz=24 measured
        // 118px, a ~5pt stack rather than the 18pt six units of 3pt would give — so it keeps
        // dividing the declared width, which lands within 3px of Word. What Word actually does
        // there is unresolved; this is fitted to the measurement, not derived.
        //
        // Nor does a TABLE CELL border, which is why the scope is a parameter. Measured directly off
        // Word's render of table_default_style, whose table style declares `double` at sz=12: the
        // drawn rule is 3px at 150 DPI — 1.44pt, the declared width as a total — where per-line
        // would be 4.5pt / 9.4px. The paragraph measurements above are equally direct (the sz=24
        // double draws lines at y=386 and y=399 of border_style_variants p3, a 19px stack), so the
        // two scopes genuinely differ in Word rather than one reading being wrong. Applying the
        // paragraph rule to cells widened table_default_style's rules and cost it 0.0524 -> 0.0546 AE.
        var perLine = scope == Scope.Paragraph &&
            style is BorderLineStyle.Double or BorderLineStyle.Triple or
                BorderLineStyle.DoubleWave or BorderLineStyle.ThreeDEmboss or BorderLineStyle.ThreeDEngrave;

        var unit = Math.Max(perLine ? totalWidth : totalWidth / units, minUnitPoints);
        totalWidth = unit * units;
        var bands = new Band[(layout.Length + 1) / 2];
        // Walk outward-edge to inward-edge, taking each line's centre as we go, then re-centre
        // the whole stack on the edge so a multi-line border straddles the same line a single one
        // would draw.
        var cursor = 0d;
        var index = 0;
        for (var i = 0; i < layout.Length; i++)
        {
            var length = layout[i] * unit;
            if (i % 2 == 0)
            {
                bands[index++] = new(cursor + length / 2 - totalWidth / 2, length);
            }

            cursor += length;
        }

        return bands;
    }

    /// <summary>
    /// The dash pattern for this style as alternating on/off lengths in points, or null for a
    /// solid stroke.
    /// </summary>
    internal static float[]? DashPattern(BorderLineStyle style, double totalWidth)
    {
        if (totalWidth <= 0)
        {
            return null;
        }

        var w = (float) totalWidth;
        return style switch
        {
            BorderLineStyle.Dotted => [w, w],
            BorderLineStyle.Dashed => [w * 3, w * 2],
            BorderLineStyle.DashSmallGap => [w * 3, w],
            BorderLineStyle.DashDotStroked => [w * 3, w, w, w],
            BorderLineStyle.DotDash => [w * 3, w, w, w],
            BorderLineStyle.DotDotDash => [w * 3, w, w, w, w, w],
            _ => null
        };
    }

    /// <summary>
    /// How thick this edge actually DRAWS, which is what it should reserve in the flow — not the
    /// declared <c>w:sz</c>. They differ whenever the floor above kicks in: a `double` at sz=6
    /// declares 0.75pt but stacks to 2.25pt, and reserving only the declared width packs more
    /// paragraphs onto a page than Word fits.
    /// </summary>
    internal static double Extent(BorderLineStyle style, double totalWidth, Scope scope = Scope.Paragraph)
    {
        var extent = 0d;
        foreach (var band in Bands(style, totalWidth, scope))
        {
            extent = Math.Max(extent, Math.Abs(band.Offset) + band.Thickness / 2);
        }

        return extent * 2;
    }

    /// <summary>Whether this edge draws anything at all.</summary>
    internal static bool Draws(BorderEdge edge) =>
        edge.IsVisible && edge.Style != BorderLineStyle.None && edge.WidthPoints > 0;

    internal static Band[] SingleBand => singleBand;
}
