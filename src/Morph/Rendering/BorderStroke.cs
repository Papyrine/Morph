/// <summary>
/// Turns a <see cref="BorderEdge"/> into the concrete lines a painter strokes, so Skia, ImageSharp
/// and PDF cannot drift apart on what "double" or "dotDash" means.
///
/// <para><b>Bands.</b> A PARAGRAPH border is built on Word's 120-dpi layout grid, one 0.6pt pixel
/// at a time, with the declared <c>w:sz</c> floored to that grid — see <see cref="ParagraphBands"/>
/// for the per-family rules, all XPS-read. A CELL border is the same stack on the same grid, placed
/// by Word's cell geometry instead of outward from a box — see <see cref="CellEdgeLines"/>. A PAGE
/// border is the paragraph stack again, grown outward from the inner face
/// <see cref="PageBorders.EdgeRect"/> returns (XPS-read on <c>_probe_pgbdr</c>, 2026-09-05). Offsets
/// come back relative to the edge's box line, innermost band first, so a plain <c>single</c> draws
/// one band at offset 0.</para>
///
/// <para><b>Dashes.</b> Patterns are multiples of the declared width, which is how Word scales
/// them: a 3pt dashed border has visibly longer dashes than a 0.5pt one.</para>
///
/// <para><b>Bevels.</b> <c>threeDEngrave</c> and <c>threeDEmboss</c> are NOT a line/gap layout —
/// Word draws one contiguous block of three shades (a paragraph's exact rectangles are in
/// <see cref="ParagraphBands"/>), in opposite order for the two. <c>inset</c> and <c>outset</c>
/// carry no shading at all: at <c>sz=48</c> both are solid at the declared grey. First measured at
/// 6pt (<c>_probe_bevel</c>), because at the 0.75pt the fixture used originally every one of these
/// collapses to 1-2px and antialiasing is indistinguishable from a light line — reading `outset` at
/// that size suggested a highlight that does not exist.</para>
///
/// <para><b>Waves.</b> <c>wave</c> and <c>doubleWave</c> are a triangular zigzag of FIXED geometry —
/// Word ignores <c>w:sz</c> for them entirely, which is only visible if the probe sweeps widths.
/// <see cref="Waves"/> carries the measurements and <see cref="WavePoints"/> generates the shared
/// vertex list, so the three painters cannot draw different squiggles.</para>
/// </summary>
static class BorderStroke
{
    /// <summary>
    /// One stroked line within a border edge. <paramref name="Offset"/> is how far OUTWARD the
    /// line's centre sits from the border box, in points — always zero or positive, with the
    /// innermost band at zero so a plain <c>single</c> border draws exactly where it always did.
    /// <paramref name="Shade"/> multiplies the declared colour (1 = as declared, below 1 darker)
    /// for the bevel styles.
    ///
    /// <para>A painter must expand the line's SPAN by the same offset as well as displacing it, so
    /// the four edges' bands close into concentric rectangles. Drawing each band across the
    /// original box extent instead leaves the corners open — the verticals come out as stubs
    /// beside full-width horizontals — and stacking inward puts the innermost line through the
    /// text (a `triple` box rendered its label as "riple").</para>
    /// </summary>
    internal readonly record struct Band(double Offset, double Thickness, double Shade = 1);

    static readonly Band[] singleBand = [new(0, 1)];

    /// <summary>
    /// One zigzag within a <c>wave</c>/<c>doubleWave</c> edge. <paramref name="Offset"/> is the
    /// outward displacement of its centre line from the border box, as for <see cref="Band"/>.
    /// </summary>
    internal readonly record struct WaveBand(double Offset, double Amplitude, double Period, double Thickness);

    // Word draws the wave styles as a triangular zigzag of FIXED size — the geometry ignores w:sz
    // entirely. Measured off _probe_wave, which declares both styles at sz=6, 12, 24 and 48 (0.75pt
    // to 6pt): every width renders the identical squiggle, 7px tall for `wave` and 11px for
    // `doubleWave` at 150 DPI. Peak-to-peak spacing averaged over 67 and 84 cycles is 12.51px
    // (6.00pt) and 10.01px (4.81pt). Each zigzag runs peak-to-trough about 5px (2.4pt) for `wave`
    // and 4px (1.92pt) for the two in `doubleWave`, which sit 5px (2.4pt) apart, and both stroke at
    // roughly a hairline.
    const double waveStrokePoints = 0.5;

    static readonly WaveBand[] singleWave = [new(0, 2.86, 6.0, waveStrokePoints)];

    static readonly WaveBand[] doubleWave =
    [
        new(0, 1.92, 4.81, waveStrokePoints),
        new(2.4, 1.92, 4.81, waveStrokePoints)
    ];

    /// <summary>
    /// The zigzags making up a wave edge, innermost first, or empty for every other style. A
    /// painter that gets a non-empty result strokes these instead of <see cref="Bands"/>.
    /// </summary>
    internal static WaveBand[] Waves(BorderLineStyle style) => style switch
    {
        BorderLineStyle.Wave => singleWave,
        BorderLineStyle.DoubleWave => doubleWave,
        _ => []
    };

    /// <summary>
    /// Vertices of one zigzag, as (distance along the edge, displacement across it). The across
    /// value alternates between the two extremes every half period, so a painter maps them onto
    /// its own axes and strokes the polyline. Shared so the three backends cannot draw different
    /// squiggles.
    /// </summary>
    internal static List<(double Along, double Across)> WavePoints(double from, double to, double period, double amplitude)
    {
        var points = new List<(double, double)>();
        if (period <= 0 || to <= from)
        {
            return points;
        }

        var half = period / 2;
        var peak = amplitude / 2;
        var up = true;
        for (var along = from; along < to + half; along += half)
        {
            points.Add((Math.Min(along, to), up ? -peak : peak));
            up = !up;
        }

        return points;
    }

    /// <summary>
    /// Which border a stroke belongs to. Word does not scale the two the same way — see the
    /// <c>perLine</c> comment in <see cref="Bands"/> — so the caller has to say.
    /// </summary>
    internal enum Scope
    {
        Paragraph,
        Cell,

        /// <summary>
        /// A <c>w:pgBorders</c> page frame: the same grid-floored, page-ordered stack as a
        /// paragraph edge, stacked outward from the inner face <see cref="PageBorders.EdgeRect"/>
        /// returns (2026-09-05, <c>_probe_pgbdr</c>). Kept distinct so the reserve helpers can still
        /// tell the two apart.
        /// </summary>
        Page
    }

    /// <summary>
    /// The lines making up this edge, innermost first, thicknesses already scaled from
    /// <paramref name="totalWidth"/>. Returns empty for a style that draws nothing.
    /// <paramref name="trailingEdge"/> is true for the right and bottom edges: the asymmetric
    /// paragraph families are laid out in PAGE direction, not inside-out, so those two edges get
    /// the mirror image of the top and left ones (see <see cref="ParagraphBands"/>). A cell edge is
    /// not stacked outward from a box at all — its painters take <see cref="CellEdgeLines"/>.
    /// </summary>
    internal static Band[] Bands(BorderLineStyle style, double totalWidth, Scope scope = Scope.Paragraph, bool trailingEdge = false)
    {
        if (totalWidth <= 0 || style == BorderLineStyle.None)
        {
            return [];
        }

        // A page frame is built exactly like a paragraph edge since 2026-09-05 — the grid-floored
        // stack in page order, growing outward from the inner face (PageBorders.EdgeRect gives that
        // face). XPS-read on _probe_pgbdr (single at 0.75/3/6pt, double 1.5pt, thinThickSmallGap 3pt,
        // text and page offsets): a 0.75pt single draws 0.6, a 1.5pt double 1.2/1.2/1.2, and the
        // thin/thick family keeps page order on every edge — the thick line outermost on the top and
        // left, INNERMOST on the bottom and right — where the PNG-probed page model it replaces drew
        // the declared width unfloored and the thin line outside.
        return ParagraphBands(style, totalWidth, trailingEdge);
    }

    // One pixel of Word's 120-dpi layout grid, the unit every paragraph border is built from.
    const double gridPoints = 0.6;

    // The two bevel shades, from an 808080 declaration drawing 2A2A2A (42/128) and 353535 (53/128).
    const double bevelFlankShade = 0.33;
    const double bevelCoreShade = 0.41;

    /// <summary>
    /// A paragraph border on Word's own grid. XPS-read (<c>_probe_bands</c>: fifteen styles at
    /// <c>w:sz</c> 12/24/48/96 as left-only borders; <c>_probe_bands2</c>: four-sided boxes and
    /// odd widths), every band is a whole number of 120-dpi pixels (0.6pt), and the declared width
    /// itself is FLOORED to that grid with a one-pixel minimum — <c>sz=9</c> (1.875px) draws 1px,
    /// <c>sz=13</c> (2.71px) 2px, <c>sz=28</c> (5.83px) 5px, so a 1pt rule is 0.6pt and a 1.5pt
    /// rule 1.2pt. The families, in page order (top-to-bottom / left-to-right), W being the declared
    /// width — every unit is a POINT quantity (<see cref="ParagraphLayout"/>) that the paint floors
    /// to the grid, and the flow reserves unfloored:
    /// <list type="bullet">
    /// <item><c>double</c> / <c>triple</c>: lines of W with gaps of W — the per-line rule.</item>
    /// <item>SmallGap: the thin line and the gap are 0.75pt each (one pixel drawn), the thick line is W.</item>
    /// <item>MediumGap: the thin line and the gap are W/2 each, the thick W.</item>
    /// <item>LargeGap: the thin line is 0.75pt, the thick line 1.5pt (two pixels drawn), and the gap
    /// is W — which is why this family looked as if it "divided" the declared width.</item>
    /// <item><c>threeDEngrave</c> / <c>threeDEmboss</c>: three touching bands — a flank, a core of
    /// W, a second flank — shaded 0.33 / 0.41 / 1.0 of the declared colour for the groove and
    /// 1.0 / 0.41 / 0.33 for the ridge. The flank RESERVES 1.5pt whatever W is (the 0.75pt groove
    /// in border_style_variants reserves 3.75pt an edge while drawing 1.8) and DRAWS half of W
    /// floored, between one and two pixels: 1px at 0.75 and 1.5pt, 2px from 3pt up.</item>
    /// <item><c>inset</c> / <c>outset</c> and every single-line style: one band of W.</item>
    /// </list>
    /// The asymmetric families are NOT inside-out symmetric: a four-sided <c>thinThickSmallGap</c>
    /// box draws its thick line at the smaller coordinate on every edge (outer on the top and left,
    /// INNER on the right and bottom), and the bevels shade in the same page direction. Hence
    /// <paramref name="trailingEdge"/>: the layout is built in page order and reversed for the
    /// right and bottom edges before stacking outward from the box.
    /// </summary>
    static Band[] ParagraphBands(BorderLineStyle style, double totalWidth, bool trailingEdge)
    {
        var layout = FlooredLayout(style, totalWidth);

        // Page order is outermost-first on the top and left edges; the right and bottom edges read
        // the same page order from the inside out.
        if (trailingEdge)
        {
            Array.Reverse(layout);
        }

        // Walk from the innermost band outward: it sits on the box at offset 0, and each further
        // band clears its predecessor by half of each plus any gap between them.
        var bands = new List<Band>();
        var offset = 0d;
        var previousThickness = 0d;
        var pendingGap = 0d;
        for (var i = layout.Length - 1; i >= 0; i--)
        {
            var (thickness, shade) = layout[i];
            if (shade == null)
            {
                pendingGap += thickness;
                continue;
            }

            if (bands.Count > 0)
            {
                offset += previousThickness / 2 + pendingGap + thickness / 2;
            }

            bands.Add(new(offset, thickness, shade.Value));
            previousThickness = thickness;
            pendingGap = 0;
        }

        return bands.ToArray();
    }

    // Word's thin unit and its "two-pixel" unit are POINT quantities — 0.75pt (sz=6) and 1.5pt
    // (sz=12) — that happen to floor to one and two grid pixels when drawn.
    const double hairPoints = 0.75;
    const double twoPoints = 1.5;

    /// <summary>
    /// A paragraph family's stack in POINTS and in page order, before the grid floor — (thickness,
    /// shade), a null shade being a gap. This is what the flow reserves (<see cref="Extent"/>):
    /// <c>_probe_reserve</c> (XPS baselines of a bordered paragraph between two plain ones, Calibri
    /// 12) measured the per-edge reserve at the DECLARED stack, not the drawn one — a 1pt single
    /// reserves 0.97pt while drawing 0.6, a 3.5pt single 3.37 while drawing 3.0, `double` at 1.5pt
    /// 4.56 (three 1.5s) while drawing 3.6, `triple` at 1.5pt 7.56, `thinThickSmallGap` at 3pt
    /// 4.58 (3 + 0.75 + 0.75) while drawing 4.2, `thinThickLargeGap` at 3pt 5.17 (0.75 + 3 + 1.5)
    /// while drawing 4.8, and the 6pt groove 9.08 (1.5 + 6 + 1.5) while drawing 8.4. Reserving the
    /// drawn stack instead drifted html_css_borders' thirteen boxes 9px up the page.
    /// </summary>
    static (double Points, double? Shade, double? Drawn)[] ParagraphLayout(BorderLineStyle style, double w)
    {
        var half = w / 2;
        // A bevel flank reserves a full 1.5pt but draws floor(W/2) pixels, capped at two.
        var flankDrawn = Math.Min(2 * gridPoints, GridFloor(half));
        return style switch
        {
            BorderLineStyle.Double or BorderLineStyle.DoubleWave => [(w, 1, null), (w, null, null), (w, 1, null)],
            BorderLineStyle.Triple => [(w, 1, null), (w, null, null), (w, 1, null), (w, null, null), (w, 1, null)],
            BorderLineStyle.ThinThickSmallGap => [(w, 1, null), (hairPoints, null, null), (hairPoints, 1, null)],
            BorderLineStyle.ThickThinSmallGap => [(hairPoints, 1, null), (hairPoints, null, null), (w, 1, null)],
            BorderLineStyle.ThinThickThinSmallGap => [(hairPoints, 1, null), (hairPoints, null, null), (w, 1, null), (hairPoints, null, null), (hairPoints, 1, null)],
            BorderLineStyle.ThinThickMediumGap => [(w, 1, null), (half, null, null), (half, 1, null)],
            BorderLineStyle.ThickThinMediumGap => [(half, 1, null), (half, null, null), (w, 1, null)],
            BorderLineStyle.ThinThickThinMediumGap => [(half, 1, null), (half, null, null), (w, 1, null), (half, null, null), (half, 1, null)],
            BorderLineStyle.ThinThickLargeGap => [(twoPoints, 1, null), (w, null, null), (hairPoints, 1, null)],
            BorderLineStyle.ThickThinLargeGap => [(hairPoints, 1, null), (w, null, null), (twoPoints, 1, null)],
            BorderLineStyle.ThinThickThinLargeGap => [(hairPoints, 1, null), (w, null, null), (twoPoints, 1, null), (w, null, null), (hairPoints, 1, null)],
            BorderLineStyle.ThreeDEngrave => [(twoPoints, bevelFlankShade, flankDrawn), (w, bevelCoreShade, null), (twoPoints, 1, flankDrawn)],
            BorderLineStyle.ThreeDEmboss => [(twoPoints, 1, flankDrawn), (w, bevelCoreShade, null), (twoPoints, bevelFlankShade, flankDrawn)],
            _ => [(w, 1, null)]
        };
    }

    // What a point quantity DRAWS as: floored to whole 120-dpi pixels, one pixel at least.
    static double GridFloor(double points) =>
        Math.Max(1, Math.Floor(points / gridPoints + 1e-6)) * gridPoints;

    // A family's stack as drawn, in page order: every point quantity floored to the grid, a null
    // shade being a gap.
    static (double Thickness, double? Shade)[] FlooredLayout(BorderLineStyle style, double totalWidth)
    {
        var layout = ParagraphLayout(style, totalWidth);
        var result = new (double, double?)[layout.Length];
        for (var i = 0; i < layout.Length; i++)
        {
            var (points, shade, drawn) = layout[i];
            result[i] = (drawn ?? GridFloor(points), shade);
        }

        return result;
    }

    /// <summary>
    /// A cell edge's drawn stack in page order — top-to-bottom for a horizontal edge, left-to-right
    /// for a vertical one — every band floored to the grid, a null shade being a gap. The SAME
    /// families as a paragraph edge (<see cref="ParagraphLayout"/>): XPS-read on <c>_probe_cellfam</c>
    /// (fifteen styles at 3pt and 6pt) the stacks match band for band, the bevels shade 2A/35/7F from
    /// an 808080 declaration the same way, and the asymmetric families keep page order on every
    /// edge — a cell's thick line sits at the smaller coordinate on all four sides, so unlike the
    /// paragraph painters nothing is mirrored for the right and bottom. Empty for an edge that
    /// draws nothing and for the waves, which keep their own geometry (<see cref="Waves"/>).
    /// </summary>
    internal static (double Thickness, double? Shade)[] CellStack(BorderEdge edge) =>
        Draws(edge) && Waves(edge.Style).Length == 0
            ? FlooredLayout(edge.Style, edge.WidthPoints)
            : [];

    /// <summary>
    /// The whole drawn stack of an edge on the grid — every band and gap of <see cref="CellStack"/>
    /// summed, so a `single` is its floored width and a 3pt `double` is 9pt. Zero for an edge that
    /// draws nothing or waves.
    /// </summary>
    internal static double DrawnStack(BorderEdge edge)
    {
        var total = 0d;
        foreach (var (thickness, _) in CellStack(edge))
        {
            total += thickness;
        }

        return total;
    }

    /// <summary>
    /// A diagonal cell rule's stroke width: its single band floored to the grid, like any other cell
    /// edge (Word draws <c>table_diagonal_borders/01</c>'s 0.5pt diagonals as one-pixel hairlines).
    /// </summary>
    internal static double DiagonalThickness(BorderEdge edge) =>
        DrawnStack(edge);

    /// <summary>
    /// One line of a cell border to stroke, in points on the page: its centre runs from
    /// <paramref name="From"/> to <paramref name="To"/> along the edge, at <paramref name="At"/>
    /// across it, <paramref name="Thickness"/> wide and shaded like a <see cref="Band"/>.
    /// </summary>
    internal readonly record struct EdgeLine(BorderEdge Edge, bool Horizontal, double At, double From, double To, double Thickness, double Shade);

    /// <summary>
    /// The lines that stroke a cell's four edges around its box, in Word's cell geometry — XPS-read
    /// on <c>_probe_cellw</c> (a `single` at nine widths from 0.75pt to 12pt), <c>_probe_cellfam</c>
    /// (fifteen families at two widths) and <c>_probe_cellmix</c> (margins, one-sided and
    /// conflicting edges):
    /// <list type="bullet">
    /// <item>A HORIZONTAL edge hangs DOWN from its grid line. The top edge's first band starts on the
    /// box's top face and the row's content sits under the whole declared stack; the bottom edge's
    /// starts <paramref name="bottomInset"/> above the bottom face — the stack the row reserved for
    /// it when it is the table's last row or a split fragment, and zero for an interior row, whose
    /// bottom edge hangs into the row below's reserve where that row draws the same rectangle as its
    /// own top edge.</item>
    /// <item>A VERTICAL edge is CENTRED on its grid line, outer and inner alike. Word biases the
    /// stack by up to half a 120-dpi pixel (an outer 5px rule sits 3px outside and 2px inside), which
    /// is below anything a painter resolves at 150 DPI.</item>
    /// <item>The horizontals run out to the vertical stacks' outer faces, and the verticals from the
    /// top face down to the bottom band's bottom face, so the corners fill.</item>
    /// </list>
    /// </summary>
    internal static List<EdgeLine> CellEdgeLines(double x, double y, double width, double height, CellBorders borders, double bottomInset)
    {
        var top = CellStack(borders.Top);
        var bottom = CellStack(borders.Bottom);
        var left = CellStack(borders.Left);
        var right = CellStack(borders.Right);
        var leftExtent = Sum(left);
        var rightExtent = Sum(right);
        var bottomExtent = Sum(bottom);

        var leftAnchor = x - leftExtent / 2;
        var rightAnchor = x + width - rightExtent / 2;
        var bottomAnchor = y + height - bottomInset;

        var horizontalFrom = leftExtent > 0 ? leftAnchor : x;
        var horizontalTo = rightExtent > 0 ? rightAnchor + rightExtent : x + width;
        var verticalTo = bottomExtent > 0 ? bottomAnchor + bottomExtent : y + height;

        var lines = new List<EdgeLine>();
        Add(lines, borders.Top, top, horizontal: true, y, horizontalFrom, horizontalTo);
        Add(lines, borders.Bottom, bottom, horizontal: true, bottomAnchor, horizontalFrom, horizontalTo);
        Add(lines, borders.Left, left, horizontal: false, leftAnchor, y, verticalTo);
        Add(lines, borders.Right, right, horizontal: false, rightAnchor, y, verticalTo);
        return lines;

        static double Sum((double Thickness, double? Shade)[] stack)
        {
            var total = 0d;
            foreach (var (thickness, _) in stack)
            {
                total += thickness;
            }

            return total;
        }

        static void Add(List<EdgeLine> lines, BorderEdge edge, (double Thickness, double? Shade)[] stack, bool horizontal, double anchor, double from, double to)
        {
            var at = anchor;
            foreach (var (thickness, shade) in stack)
            {
                if (shade is { } visible)
                {
                    lines.Add(new(edge, horizontal, at + thickness / 2, from, to, thickness, visible));
                }

                at += thickness;
            }
        }
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
    /// What this edge reserves in the flow. For a PARAGRAPH or CELL border that is the declared
    /// stack in points (<see cref="ParagraphLayout"/>) — Word charges a 1pt single its full point
    /// while drawing it as one 0.6pt pixel, and a `double` at sz=6 three 0.75s; a cell row reserves
    /// the same stack under its top edge (<c>_probe_cellfam</c>: a 3pt `double` opens 9pt above the
    /// row's text, a 3pt `thinThickSmallGap` 4.5) and insets its content by half of it on the sides.
    /// For a page edge it is what the edge draws.
    /// </summary>
    internal static double Extent(BorderLineStyle style, double totalWidth, Scope scope = Scope.Paragraph)
    {
        if (totalWidth <= 0 || style == BorderLineStyle.None)
        {
            return 0;
        }

        // A wave's extent is its own fixed geometry, not anything w:sz implies.
        if (Waves(style) is {Length: > 0} waves)
        {
            var span = 0d;
            foreach (var wave in waves)
            {
                span = Math.Max(span, wave.Offset + wave.Amplitude / 2 + wave.Thickness);
            }

            return span + waves[0].Amplitude / 2 + waves[0].Thickness;
        }

        if (scope != Scope.Page)
        {
            var total = 0d;
            foreach (var (points, _, _) in ParagraphLayout(style, totalWidth))
            {
                total += points;
            }

            return total;
        }

        // The full thickness the stack occupies, from its innermost face to its outermost. Written
        // from the band offsets rather than assuming a direction, because a cell stack is centred
        // on its edge and so has bands on both sides of it.
        var bands = Bands(style, totalWidth, scope);
        if (bands.Length == 0)
        {
            return 0;
        }

        var inward = 0d;
        var outward = 0d;
        foreach (var band in bands)
        {
            inward = Math.Max(inward, band.Thickness / 2 - band.Offset);
            outward = Math.Max(outward, band.Offset + band.Thickness / 2);
        }

        return inward + outward;
    }

    /// <summary>
    /// How far OUTWARD the whole stack shifts so its inner face sits on the border box rather than
    /// straddling it, in points. Word strokes a paragraph border outward from the box edge:
    /// Word-probed at 3pt, 6pt and 12pt (<c>_probe_pbdr</c>), a left-only border's INNER edge stays
    /// at the same x across all three while its outer edge moves left, where a centred stack would
    /// have moved both. Shifting by half the INNERMOST band's thickness puts that face on the box
    /// without disturbing the gaps inside the stack, so the multi-line families keep the geometry
    /// <see cref="Bands"/> measured for them.
    ///
    /// <para>It also makes the paint agree with what the flow already reserves: the fragmenter
    /// charges the full <see cref="Extent"/> from the box, which a centred stack only half used.</para>
    ///
    /// <para>Zero for a CELL edge, which straddles its shared grid line by design (see
    /// <see cref="Bands"/>), and zero when nothing draws.</para>
    /// </summary>
    internal static double OutwardShift(Band[] bands, Scope scope) =>
        scope == Scope.Cell || bands.Length == 0
            ? 0
            : bands[0].Thickness / 2;

    /// <summary>
    /// What a RUN border (<c>w:bdr</c>) reserves on each side of the font's line box: the declared
    /// stack plus <c>w:space</c>, in points, unfloored — the same reserve law as a paragraph edge.
    /// XPS-read (<c>_probe_runbdr</c>, Calibri 12 single-spaced, four styles): a `single` at 0.75pt
    /// with 1pt space grows the line pitch from 14.65 to 18.3 (2 × 1.75), 3pt / 4pt to 28.5
    /// (2 × 7), 6pt / 0 to 26.7 (2 × 6), and a 1.5pt `double` with 2pt space to 27.6
    /// (2 × (4.5 + 2)). The line box itself — the vertical rules' extent — stays the font box.
    /// </summary>
    internal static double RunBorderReserve(BorderEdge edge) =>
        Draws(edge) ? Extent(edge.Style, edge.WidthPoints) + edge.SpacePoints : 0;

    /// <summary>
    /// How far outside the font's line box a run border's INNER face sits: <c>w:space</c> floored to
    /// the grid, zero allowed. The same probe put the rules' outer faces at the line box plus the
    /// drawn stack plus this — 1.2pt for 0.75pt / 1pt (0.6 + 0.6), 6.6pt for 3pt / 4pt (3 + 3.6),
    /// 6.0pt for 6pt / 0, 5.4pt for the 1.5pt double with 2pt (3.6 + 1.8) — and pushed the run's
    /// first glyph right by exactly the same amount.
    /// </summary>
    internal static double RunBorderInset(BorderEdge edge) =>
        Math.Floor(edge.SpacePoints / gridPoints + 1e-6) * gridPoints;

    /// <summary>
    /// How far a run border pushes the run's first glyph right of where the run would otherwise
    /// start, and holds its last glyph short of where the next run starts: the drawn stack plus the
    /// floored <c>w:space</c> — the outer-face offset of <see cref="RunBorderInset"/>. XPS-read on
    /// <c>_probe_runbdr</c>: 1.2pt for a 0.75pt `single` with 1pt space (73.28 against the
    /// paragraph's 72.08), 6.6pt for 3pt / 4pt (78.67). The measurer reserves it on both sides of
    /// a bordered run (<c>CanonicalParagraphMeasurer.Flatten</c>), so the box the painters stroke
    /// from the glyph run outward lands with its outer faces on the reserve.
    /// </summary>
    internal static double RunBorderGlyphInset(BorderEdge edge) =>
        Draws(edge) ? DrawnStack(edge) + RunBorderInset(edge) : 0;

    /// <summary>
    /// Whether a bordered run grows its line and draws its rules outside the font's line box, or
    /// draws them INSIDE it with no reserve. XPS-read on <c>_probe_runbdr4</c> (2026-09-05, Calibri 10
    /// lines, `single` at 0.75/2.25/3/4.5pt with 0/1/8pt space, boxes mid-line, first and last): the
    /// one thing that separates the two is the run's first character. " boxed " draws inside — top band
    /// on the line top, bottom band ending on the line bottom, a 10pt line staying 12.6 — at every size
    /// and position, while "boxed" reserves (16.2 at 2.25pt/1pt, 18.0 at 4.5pt/1pt, 22.8 at 2.25pt/8pt).
    /// This is the case <c>_probe_runbdr2</c> found and <c>_probe_runbdr3</c> could not separate: its
    /// mid-line runs all began with a space. The horizontal glyph inset applies either way.
    /// </summary>
    internal static bool RunBorderReserves(string text) =>
        text.Length == 0 || text[0] != ' ';

    /// <summary>The tallest <see cref="RunBorderReserve"/> among a line's reserving runs — what the measurer grew the line by on each side.</summary>
    internal static double LinePad(IReadOnlyList<PlacedRun> runs)
    {
        var pad = 0d;
        foreach (var run in runs)
        {
            if (run.Properties.Border is { } border && RunBorderReserves(run.Text))
            {
                pad = Math.Max(pad, RunBorderReserve(border));
            }
        }

        return pad;
    }

    /// <summary>
    /// The rectangle a painter strokes a run border from (inner faces), so that with the outward
    /// stroke the rules' OUTER faces land the drawn stack plus the floored space outside the font's
    /// line box — the line box being the placed line shrunk by <paramref name="linePad"/> on each
    /// side. Horizontally the box grows outward from the glyph run by the same inset, into the
    /// reserve the measurer holds open on each side of a bordered run
    /// (<see cref="RunBorderGlyphInset"/>) — Word pushes the glyphs right by it, and so does the
    /// engine since 2026-09-05.
    /// </summary>
    internal static (double X, double Y, double Width, double Height) RunBorderBox(BorderEdge edge, double runX, double runWidth, double lineY, double lineHeight, double linePad, bool reserves = true)
    {
        var inset = RunBorderInset(edge);
        if (!reserves)
        {
            // Drawn inside the font's line box (RunBorderReserves): the rules' OUTER faces sit on the
            // line box, so the inner rect the painter strokes outward from is the box shrunk by the
            // drawn stack.
            var drawn = DrawnStack(edge);
            return (runX - inset, lineY + linePad + drawn, runWidth + 2 * inset, lineHeight - 2 * linePad - 2 * drawn);
        }

        return (runX - inset, lineY + linePad - inset, runWidth + 2 * inset, lineHeight - 2 * linePad + 2 * inset);
    }

    /// <summary>Whether this edge draws anything at all.</summary>
    internal static bool Draws(BorderEdge edge) =>
        edge.IsVisible && edge.Style != BorderLineStyle.None && edge.WidthPoints > 0;

    internal static Band[] SingleBand => singleBand;
}
