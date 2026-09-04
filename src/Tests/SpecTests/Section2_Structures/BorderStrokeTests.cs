/// <summary>
/// Covers <see cref="BorderStroke"/>, the shared recipe the three painters stroke a
/// <c>w:pBdr</c>/<c>w:tcBorders</c> edge from. The paragraph-scope numbers are Word's own, read
/// from the border rectangles in its XPS output (<c>_probe_bands</c> / <c>_probe_bands2</c>,
/// 2026-09-04): every band is a whole number of 120-dpi pixels (0.6pt) and the declared width is
/// floored to that grid. The cell-scope numbers are from the earlier PNG-read probes.
/// </summary>
public class BorderStrokeTests
{
    // One pixel of Word's layout grid.
    const double px = 0.6;

    [Test]
    public async Task Single_styles_yield_one_band_on_the_edge_at_the_floored_grid_width()
    {
        // The whole corpus is `single`, so this is the case that matters most: one band at offset
        // 0, its width the declared w:sz floored to whole 120-dpi pixels — Word draws 1.5pt (2.5px)
        // as 2px, and a 1pt rule as one pixel.
        var bands = BorderStroke.Bands(BorderLineStyle.Single, 1.5);

        await Assert.That(bands.Length).IsEqualTo(1);
        await Assert.That(bands[0].Offset).IsEqualTo(0).Within(0.0001);
        await Assert.That(bands[0].Thickness).IsEqualTo(2 * px).Within(0.0001);
        await Assert.That(BorderStroke.Bands(BorderLineStyle.Single, 1)[0].Thickness).IsEqualTo(px).Within(0.0001);
    }

    [Test]
    public async Task Declared_widths_floor_to_the_grid_with_a_one_pixel_minimum()
    {
        // _probe_bands2, `single` at odd sizes: sz=9 (1.875px) draws 1px, sz=13 (2.71px) 2px,
        // sz=20 (4.17px) 4px, sz=28 (5.83px) 5px — floor, not round. sz=4 (0.83px) still draws.
        await Assert.That(BorderStroke.Bands(BorderLineStyle.Single, 9 / 8.0)[0].Thickness).IsEqualTo(px).Within(0.0001);
        await Assert.That(BorderStroke.Bands(BorderLineStyle.Single, 13 / 8.0)[0].Thickness).IsEqualTo(2 * px).Within(0.0001);
        await Assert.That(BorderStroke.Bands(BorderLineStyle.Single, 20 / 8.0)[0].Thickness).IsEqualTo(4 * px).Within(0.0001);
        await Assert.That(BorderStroke.Bands(BorderLineStyle.Single, 28 / 8.0)[0].Thickness).IsEqualTo(5 * px).Within(0.0001);
        await Assert.That(BorderStroke.Bands(BorderLineStyle.Single, 0.5)[0].Thickness).IsEqualTo(px).Within(0.0001);
        // Exact multiples stay exact: 3pt is five pixels, 6pt ten.
        await Assert.That(BorderStroke.Bands(BorderLineStyle.Single, 3)[0].Thickness).IsEqualTo(3).Within(0.0001);
        await Assert.That(BorderStroke.Bands(BorderLineStyle.Single, 6)[0].Thickness).IsEqualTo(6).Within(0.0001);
    }

    [Test]
    public async Task None_draws_nothing()
    {
        await Assert.That(BorderStroke.Bands(BorderLineStyle.None, 1.5)).IsEmpty();
        await Assert.That(BorderStroke.Bands(BorderLineStyle.Single, 0)).IsEmpty();
    }

    [Test]
    public async Task Double_draws_two_lines_each_at_the_declared_width()
    {
        // w:sz is the width of EACH line for this family, not of the stack: a 3pt double is two 3pt
        // lines with a 3pt gap, 9pt overall (_probe_bands sz=48: 6.0 / 6.0 / 6.0 at 6pt, and
        // 12.03 / 12.0 / 12.02 at 12pt). Reading sz as the total instead left the border 26px
        // short of Word's at sz=24.
        var bands = BorderStroke.Bands(BorderLineStyle.Double, 3);

        await Assert.That(bands.Length).IsEqualTo(2);
        await Assert.That(bands[0].Thickness).IsEqualTo(3).Within(0.0001);
        await Assert.That(bands[1].Thickness).IsEqualTo(3).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.Double, 3)).IsEqualTo(9).Within(0.0001);
        // Stacked OUTWARD from the box, innermost first: the inner line sits where a single one
        // would (offset 0) and the outer one clears it by its own half, the gap and the other half.
        await Assert.That(bands[0].Offset).IsEqualTo(0).Within(0.0001);
        await Assert.That(bands[1].Offset).IsEqualTo(6).Within(0.0001);
    }

    [Test]
    public async Task Triple_draws_three_nested_lines_from_the_box_outward()
    {
        // 1pt floors to one pixel, so the stack is five pixels: 0.6 line, 0.6 gap, and so on.
        var bands = BorderStroke.Bands(BorderLineStyle.Triple, 1);

        await Assert.That(bands.Length).IsEqualTo(3);
        await Assert.That(bands[0].Offset).IsEqualTo(0).Within(0.0001);
        await Assert.That(bands[1].Offset).IsEqualTo(2 * px).Within(0.0001);
        await Assert.That(bands[2].Offset).IsEqualTo(4 * px).Within(0.0001);
        // The flow reserves the DECLARED stack — five 1pt units — even though it draws five pixels.
        await Assert.That(BorderStroke.Extent(BorderLineStyle.Triple, 1)).IsEqualTo(5).Within(0.0001);
    }

    /// <summary>
    /// Word's rectangles for the thin/thick families, left edge, innermost band first, as
    /// (thickness, gap before the next band) in points. From <c>_probe_bands</c>; the gap is what
    /// separates the bands' faces.
    /// </summary>
    public static IEnumerable<(string Style, double SizePoints, string Bands, string Gaps)> WordThinThickGeometry() =>
    [
        // SmallGap: thin = 1px, gap = 1px, thick = W. The THIN line is innermost on the left edge.
        ("ThinThickSmallGap", 3, "0.6, 3.0", "0.6"),
        ("ThinThickSmallGap", 12, "0.6, 12.0", "0.6"),
        ("ThickThinSmallGap", 3, "3.0, 0.6", "0.6"),
        ("ThinThickThinSmallGap", 6, "0.6, 6.0, 0.6", "0.6, 0.6"),
        // MediumGap: thin = W/2, gap = W/2 (floored to the grid), thick = W.
        ("ThinThickMediumGap", 3, "1.2, 3.0", "1.2"),
        ("ThinThickMediumGap", 6, "3.0, 6.0", "3.0"),
        ("ThickThinMediumGap", 12, "12.0, 6.0", "6.0"),
        ("ThinThickThinMediumGap", 6, "3.0, 6.0, 3.0", "3.0, 3.0"),
        // LargeGap: thin = 1px, thick = 2px, gap = W — the declared width goes into the GAP.
        ("ThinThickLargeGap", 3, "0.6, 1.2", "3.0"),
        ("ThinThickLargeGap", 12, "0.6, 1.2", "12.0"),
        ("ThickThinLargeGap", 6, "1.2, 0.6", "6.0"),
        ("ThinThickThinLargeGap", 6, "0.6, 1.2, 0.6", "6.0, 6.0"),
        // At sz=12 (1.5pt) W is 2px, so the medium family's halves are one pixel.
        ("ThinThickMediumGap", 1.5, "0.6, 1.2", "0.6"),
        ("ThinThickLargeGap", 1.5, "0.6, 1.2", "1.2")
    ];

    [Test]
    [MethodDataSource(nameof(WordThinThickGeometry))]
    public async Task Thin_thick_families_reproduce_Words_rectangles(string styleName, double sizePoints, string bandList, string gapList)
    {
        var style = Enum.Parse<BorderLineStyle>(styleName);
        var expectedBands = bandList.Split(',').Select(_ => double.Parse(_, CultureInfo.InvariantCulture)).ToArray();
        var expectedGaps = gapList.Split(',').Select(_ => double.Parse(_, CultureInfo.InvariantCulture)).ToArray();
        var bands = BorderStroke.Bands(style, sizePoints);

        await Assert.That(bands.Length).IsEqualTo(expectedBands.Length);
        for (var i = 0; i < bands.Length; i++)
        {
            await Assert.That(bands[i].Thickness).IsEqualTo(expectedBands[i]).Within(0.0001);
            if (i > 0)
            {
                var gap = bands[i].Offset - bands[i - 1].Offset - bands[i].Thickness / 2 - bands[i - 1].Thickness / 2;
                await Assert.That(gap).IsEqualTo(expectedGaps[i - 1]).Within(0.0001);
            }
        }
    }

    [Test]
    public async Task Asymmetric_families_mirror_on_the_right_and_bottom_edges()
    {
        // _probe_bands2, a four-sided thinThickSmallGap box at 6pt: the thick line sits at the
        // smaller coordinate on EVERY edge — outer on the top and left, inner on the right and
        // bottom — so the trailing edges get the reversed layout.
        var leading = BorderStroke.Bands(BorderLineStyle.ThinThickSmallGap, 6);
        var trailing = BorderStroke.Bands(BorderLineStyle.ThinThickSmallGap, 6, trailingEdge: true);

        await Assert.That(leading[0].Thickness).IsEqualTo(0.6).Within(0.0001);
        await Assert.That(leading[1].Thickness).IsEqualTo(6).Within(0.0001);
        await Assert.That(trailing[0].Thickness).IsEqualTo(6).Within(0.0001);
        await Assert.That(trailing[1].Thickness).IsEqualTo(0.6).Within(0.0001);
        // Same drawn stack, whichever way round — measured from the inner face, which
        // OutwardShift puts on the box (half the innermost band), to the outer face: 6 + 0.6 + 0.6.
        static double Span(BorderStroke.Band[] bands) => bands[0].Thickness / 2 + bands[^1].Offset + bands[^1].Thickness / 2;
        await Assert.That(Span(trailing)).IsEqualTo(Span(leading)).Within(0.0001);
        await Assert.That(Span(leading)).IsEqualTo(7.2).Within(0.0001);

        // The symmetric families are unaffected.
        var doubleLeading = BorderStroke.Bands(BorderLineStyle.Double, 3);
        var doubleTrailing = BorderStroke.Bands(BorderLineStyle.Double, 3, trailingEdge: true);
        await Assert.That(doubleTrailing[1].Offset).IsEqualTo(doubleLeading[1].Offset).Within(0.0001);
    }

    [Test]
    public async Task Bevels_are_three_touching_bands_shaded_in_page_direction()
    {
        // _probe_bands at 6pt, 808080 declared: 1.2pt at 2A2A2A, 6.0pt at 353535, 1.2pt at 7F7F7F
        // for the groove, the ridge the other way round. Total drawn 8.4pt, not the 9.0 a 1.5x
        // model gave — while the RESERVE is 1.5 + 6 + 1.5 = 9.0 (_probe_reserve: 9.08).
        var groove = BorderStroke.Bands(BorderLineStyle.ThreeDEngrave, 6);

        await Assert.That(groove.Length).IsEqualTo(3);
        // Innermost first on a leading edge: the light flank is inner, the dark flank outer.
        await Assert.That(groove[0].Thickness).IsEqualTo(1.2).Within(0.0001);
        await Assert.That(groove[0].Shade).IsEqualTo(1).Within(0.0001);
        await Assert.That(groove[1].Thickness).IsEqualTo(6).Within(0.0001);
        await Assert.That(groove[1].Shade).IsEqualTo(0.41).Within(0.0001);
        await Assert.That(groove[2].Thickness).IsEqualTo(1.2).Within(0.0001);
        await Assert.That(groove[2].Shade).IsEqualTo(0.33).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.ThreeDEngrave, 6)).IsEqualTo(9).Within(0.0001);
        // Touching: no gap between the bands.
        await Assert.That(groove[1].Offset - groove[0].Offset).IsEqualTo(3.6).Within(0.0001);

        var ridge = BorderStroke.Bands(BorderLineStyle.ThreeDEmboss, 6);
        await Assert.That(ridge[0].Shade).IsEqualTo(0.33).Within(0.0001);
        await Assert.That(ridge[2].Shade).IsEqualTo(1).Within(0.0001);

        // The flank draws floor(W/2) pixels, one at least, two at most, but always reserves 1.5pt:
        // at sz=12 Word drew 0.6 / 1.2 / 0.6, and border_style_variants' sz=6 groove draws three
        // single pixels (1.8pt) inside a box that reserves 3.75pt an edge.
        var narrow = BorderStroke.Bands(BorderLineStyle.ThreeDEngrave, 1.5);
        await Assert.That(narrow[0].Thickness).IsEqualTo(0.6).Within(0.0001);
        await Assert.That(narrow[1].Thickness).IsEqualTo(1.2).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.ThreeDEngrave, 1.5)).IsEqualTo(4.5).Within(0.0001);
        var hairline = BorderStroke.Bands(BorderLineStyle.ThreeDEngrave, 0.75);
        await Assert.That(hairline[0].Thickness + hairline[1].Thickness + hairline[2].Thickness).IsEqualTo(1.8).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.ThreeDEngrave, 0.75)).IsEqualTo(3.75).Within(0.0001);

        // inset / outset carry no shading at any width.
        var inset = BorderStroke.Bands(BorderLineStyle.Inset, 6);
        await Assert.That(inset.Length).IsEqualTo(1);
        await Assert.That(inset[0].Shade).IsEqualTo(1).Within(0.0001);
        await Assert.That(inset[0].Thickness).IsEqualTo(6).Within(0.0001);
    }

    [Test]
    public async Task Extent_is_the_declared_point_stack_not_the_floored_paint()
    {
        // _probe_reserve (XPS baselines, Calibri 12, a bordered paragraph between two plain ones):
        // Word reserves the DECLARED stack in points and draws the grid-floored one. A 1pt single
        // reserved 0.97pt while drawing 0.6; a 3.5pt single 3.37 while drawing 3.0; a 1.5pt
        // double 4.56 (three 1.5s) while drawing 3.6. Reserving the drawn stack drifted
        // html_css_borders' thirteen boxes 9px up the page.
        await Assert.That(BorderStroke.Extent(BorderLineStyle.Single, 1)).IsEqualTo(1).Within(0.0001);
        await Assert.That(BorderStroke.Bands(BorderLineStyle.Single, 1)[0].Thickness).IsEqualTo(0.6).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.Single, 3.5)).IsEqualTo(3.5).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.Single, 2)).IsEqualTo(2).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.None, 2)).IsEqualTo(0).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.Double, 1.5)).IsEqualTo(4.5).Within(0.0001);
        // The thin/thick units are 0.75pt and 1.5pt: thinThickLargeGap at 3pt reserves 0.75 + 3 + 1.5
        // (measured 5.17) and thinThickSmallGap 3 + 0.75 + 0.75 (measured 4.58).
        await Assert.That(BorderStroke.Extent(BorderLineStyle.ThinThickLargeGap, 3)).IsEqualTo(5.25).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.ThinThickSmallGap, 3)).IsEqualTo(4.5).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.ThinThickMediumGap, 6)).IsEqualTo(12).Within(0.0001);
    }

    [Test]
    public async Task A_narrow_multi_line_border_is_floored_rather_than_collapsed()
    {
        // sz=6 is 0.75pt, which split three ways would be 0.25pt a line — invisible at any DPI a
        // document is read at. Word floors each unit to one grid pixel instead and lets the stack
        // exceed w:sz: its own render draws this double as two 1px lines with a 1px gap.
        var bands = BorderStroke.Bands(BorderLineStyle.Double, 0.75);

        await Assert.That(bands.Length).IsEqualTo(2);
        await Assert.That(bands[0].Thickness).IsEqualTo(px).Within(0.0001);
        var gap = bands[1].Offset - bands[0].Offset - bands[0].Thickness / 2 - bands[1].Thickness / 2;
        await Assert.That(gap).IsEqualTo(px).Within(0.0001);
    }

    [Test]
    public async Task Every_band_stacks_outward_so_none_intrudes_on_the_content()
    {
        // The bug this guards: bands used to straddle the box, so a wide stack put its innermost
        // line INSIDE the text — a `triple` box rendered its label as "riple" — and each band was
        // drawn across the original box extent, leaving the corners open and the vertical bands
        // sticking out as stubs. Offsets are outward-only and the painters expand each band's span
        // by the same amount, which closes them into concentric rectangles.
        foreach (var style in new[]
                 {
                     BorderLineStyle.Double, BorderLineStyle.Triple, BorderLineStyle.ThinThickLargeGap,
                     BorderLineStyle.ThreeDEngrave, BorderLineStyle.ThreeDEmboss, BorderLineStyle.Single
                 })
        {
            foreach (var trailing in new[] {false, true})
            {
                var bands = BorderStroke.Bands(style, 3, trailingEdge: trailing);
                await Assert.That(bands[0].Offset).IsEqualTo(0).Within(0.0001);
                foreach (var band in bands)
                {
                    await Assert.That(band.Offset).IsGreaterThanOrEqualTo(0);
                }

                for (var i = 1; i < bands.Length; i++)
                {
                    // Strictly outward, and never overlapping the band it stacks on.
                    var clearance = bands[i].Offset - bands[i - 1].Offset
                        - bands[i].Thickness / 2 - bands[i - 1].Thickness / 2;
                    await Assert.That(clearance).IsGreaterThanOrEqualTo(-0.0001);
                }
            }
        }
    }

    [Test]
    public async Task Cell_scope_keeps_its_own_measured_model()
    {
        // The cell probes (_probe_celldouble / _probe_celltriple) were read from PNGs at the
        // declared widths, and a cell stack straddles its shared edge; that model is untouched by
        // the paragraph grid — a 1.5pt cell single still draws 1.5pt, centred.
        var single = BorderStroke.Bands(BorderLineStyle.Single, 1.5, BorderStroke.Scope.Cell);
        await Assert.That(single[0].Thickness).IsEqualTo(1.5).Within(0.0001);

        var cellDouble = BorderStroke.Bands(BorderLineStyle.Double, 3, BorderStroke.Scope.Cell);
        await Assert.That(cellDouble.Length).IsEqualTo(2);
        await Assert.That(cellDouble[0].Offset).IsEqualTo(-cellDouble[1].Offset).Within(0.0001);
    }

    [Test]
    public async Task Dash_patterns_scale_with_the_declared_width()
    {
        // Word's dashes grow with the border: a 3pt dashed edge has visibly longer dashes than a
        // 0.5pt one, so the pattern is expressed in multiples of the width rather than absolutely.
        var thin = BorderStroke.DashPattern(BorderLineStyle.Dashed, 1)!;
        var thick = BorderStroke.DashPattern(BorderLineStyle.Dashed, 3)!;

        await Assert.That(thin[0]).IsEqualTo(3).Within(0.0001f);
        await Assert.That(thick[0]).IsEqualTo(9).Within(0.0001f);

        // Dotted is on/off square; dotDotDash carries the two dots before its dash.
        await Assert.That(BorderStroke.DashPattern(BorderLineStyle.Dotted, 1)).IsEquivalentTo([1f, 1f]);
        await Assert.That(BorderStroke.DashPattern(BorderLineStyle.DotDotDash, 1)!.Length).IsEqualTo(6);
        // Solid styles opt out entirely.
        await Assert.That(BorderStroke.DashPattern(BorderLineStyle.Single, 1)).IsNull();
        await Assert.That(BorderStroke.DashPattern(BorderLineStyle.Double, 1)).IsNull();
    }

    [Test]
    public async Task Distinct_ooxml_styles_stay_distinct_so_border_groups_do_not_merge()
    {
        // The regression this whole model exists to prevent: these four collapsed to `Single`,
        // which made ParagraphProperties.SharesBorderGroupWith read four differently-bordered
        // paragraphs as one group and draw a single box around the lot.
        static BorderEdge Edge(BorderLineStyle style) => new()
        {
            IsVisible = true,
            WidthPoints = 0.75,
            ColorHex = "808080",
            Style = style
        };

        var styles = new[]
        {
            BorderLineStyle.ThreeDEngrave, BorderLineStyle.ThreeDEmboss,
            BorderLineStyle.Inset, BorderLineStyle.Outset
        };

        var edges = styles.Select(Edge).ToList();
        foreach (var edge in edges)
        {
            var matches = edges.Count(_ => _ == edge);
            await Assert.That(matches).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Paragraph_stack_shifts_outward_by_half_its_innermost_line()
    {
        // Word strokes a paragraph border outward from the box: probed at 3pt, 6pt and 12pt
        // (_probe_pbdr), a left-only border's INNER edge stays put while its outer edge moves.
        // Half the innermost thickness is what puts that face on the box.
        var single = BorderStroke.Bands(BorderLineStyle.Single, 3);

        await Assert.That(BorderStroke.OutwardShift(single, BorderStroke.Scope.Paragraph)).IsEqualTo(1.5).Within(0.0001);
    }

    [Test]
    public async Task Shift_uses_the_innermost_line_so_the_stack_keeps_its_gaps()
    {
        // A double's three units are all the declared width, so the shift is half of ONE line and
        // every band moves by the same amount — the gaps Bands measured are untouched.
        var bands = BorderStroke.Bands(BorderLineStyle.Double, 3);
        var shift = BorderStroke.OutwardShift(bands, BorderStroke.Scope.Paragraph);

        await Assert.That(shift).IsEqualTo(1.5).Within(0.0001);
        // Inner face on the box, outer face at the full Extent — which is what the flow reserves.
        await Assert.That(bands[0].Offset + shift - bands[0].Thickness / 2).IsEqualTo(0).Within(0.0001);
        await Assert.That(bands[^1].Offset + shift + bands[^1].Thickness / 2)
            .IsEqualTo(BorderStroke.Extent(BorderLineStyle.Double, 3)).Within(0.0001);
    }

    [Test]
    public async Task Cell_and_page_edges_do_not_shift()
    {
        // A cell edge straddles the grid line it shares with its neighbour, and a page frame
        // arrives already centred from PageBorders.EdgeRect — shifting that one regressed
        // page_borders/01 by +0.0096 AE before the scope existed.
        var cell = BorderStroke.Bands(BorderLineStyle.Single, 3, BorderStroke.Scope.Cell);
        var page = BorderStroke.Bands(BorderLineStyle.Single, 3, BorderStroke.Scope.Page);

        await Assert.That(BorderStroke.OutwardShift(cell, BorderStroke.Scope.Cell)).IsEqualTo(0);
        await Assert.That(BorderStroke.OutwardShift(page, BorderStroke.Scope.Page)).IsEqualTo(0);
        await Assert.That(BorderStroke.OutwardShift([], BorderStroke.Scope.Paragraph)).IsEqualTo(0);
    }
}
