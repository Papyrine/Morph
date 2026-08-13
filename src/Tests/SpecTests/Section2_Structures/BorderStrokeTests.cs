/// <summary>
/// Covers <see cref="BorderStroke"/>, the shared recipe the three painters stroke a
/// <c>w:pBdr</c>/<c>w:tcBorders</c> edge from. The numbers here are Word's, measured off its own
/// render of the `border_style_variants` fixture at 150 DPI.
/// </summary>
public class BorderStrokeTests
{
    [Test]
    public async Task Single_styles_yield_one_band_on_the_edge_centre()
    {
        // The whole corpus is `single`, so this is the case that must not move: one band, full
        // declared width, no offset — exactly where the painters drew before bands existed.
        var bands = BorderStroke.Bands(BorderLineStyle.Single, 1.5);

        await Assert.That(bands.Length).IsEqualTo(1);
        await Assert.That(bands[0].Offset).IsEqualTo(0).Within(0.0001);
        await Assert.That(bands[0].Thickness).IsEqualTo(1.5).Within(0.0001);
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
        // lines with a 3pt gap, 9pt overall. Word-probed — see BorderStroke.Bands. Reading sz as the
        // total instead left the border 26px short of Word's at sz=24.
        var bands = BorderStroke.Bands(BorderLineStyle.Double, 3);

        await Assert.That(bands.Length).IsEqualTo(2);
        await Assert.That(bands[0].Thickness).IsEqualTo(3).Within(0.0001);
        await Assert.That(bands[1].Thickness).IsEqualTo(3).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.Double, 3)).IsEqualTo(9).Within(0.0001);
        // Symmetric about the edge, so a double border sits where a single one would.
        await Assert.That(bands[0].Offset + bands[1].Offset).IsEqualTo(0).Within(0.0001);
    }

    [Test]
    public async Task Triple_draws_three_lines_with_the_middle_on_the_edge()
    {
        var bands = BorderStroke.Bands(BorderLineStyle.Triple, 1);

        await Assert.That(bands.Length).IsEqualTo(3);
        await Assert.That(bands[1].Offset).IsEqualTo(0).Within(0.0001);
        await Assert.That(bands[0].Offset).IsEqualTo(-2).Within(0.0001);
        await Assert.That(bands[2].Offset).IsEqualTo(2).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.Triple, 1)).IsEqualTo(5).Within(0.0001);
    }

    [Test]
    public async Task The_thin_thick_family_divides_the_declared_width_instead()
    {
        // The other half of the measurement: thinThickLargeGap at sz=24 (3pt) reserves ~5pt in
        // Word, not the 18pt six units of 3pt would give, so this family divides rather than
        // repeating. Fitted to the probe, not derived — see BorderStroke.Bands.
        await Assert.That(BorderStroke.Extent(BorderLineStyle.ThinThickLargeGap, 3)).IsLessThan(7);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.Double, 3)).IsEqualTo(9).Within(0.0001);
    }

    [Test]
    public async Task Extent_is_what_the_edge_draws_so_the_flow_reserve_matches()
    {
        // A single edge reserves exactly its declared width; the multi-line families reserve their
        // whole stack. Charging the declared width for a stacked border packed paragraphs tighter
        // than Word's.
        await Assert.That(BorderStroke.Extent(BorderLineStyle.Single, 2)).IsEqualTo(2).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.None, 2)).IsEqualTo(0).Within(0.0001);
        await Assert.That(BorderStroke.Extent(BorderLineStyle.Double, 2)).IsGreaterThan(2);
    }

    [Test]
    public async Task Thin_thick_pairs_keep_their_asymmetry()
    {
        // 1-1-2: a thin outer line, a gap, then a thick inner one at twice the thickness.
        var thinThick = BorderStroke.Bands(BorderLineStyle.ThinThickSmallGap, 4);
        await Assert.That(thinThick.Length).IsEqualTo(2);
        await Assert.That(thinThick[0].Thickness).IsEqualTo(1).Within(0.0001);
        await Assert.That(thinThick[1].Thickness).IsEqualTo(2).Within(0.0001);

        // The mirror image, so the thick line comes first.
        var thickThin = BorderStroke.Bands(BorderLineStyle.ThickThinSmallGap, 4);
        await Assert.That(thickThin[0].Thickness).IsEqualTo(2).Within(0.0001);
        await Assert.That(thickThin[1].Thickness).IsEqualTo(1).Within(0.0001);
    }

    [Test]
    public async Task A_narrow_multi_line_border_is_floored_rather_than_collapsed()
    {
        // sz=6 is 0.75pt, which split three ways would be 0.25pt a line — invisible at any DPI a
        // document is read at. Word floors instead and lets the stack exceed w:sz: its own render
        // draws this as two 1-2px lines with a 1px gap at 150 DPI. Without the floor every
        // multi-line style collapses into one grey line at the widths real documents use.
        var bands = BorderStroke.Bands(BorderLineStyle.Double, 0.75);

        await Assert.That(bands.Length).IsEqualTo(2);
        await Assert.That(bands[0].Thickness).IsGreaterThanOrEqualTo(0.5);
        // A clear gap survives between the two lines.
        var gap = bands[1].Offset - bands[0].Offset - bands[0].Thickness / 2 - bands[1].Thickness / 2;
        await Assert.That(gap).IsGreaterThanOrEqualTo(0.5);
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
        await Assert.That(BorderStroke.DashPattern(BorderLineStyle.Dotted, 1)).IsEquivalentTo(new[] {1f, 1f});
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
        BorderEdge Edge(BorderLineStyle style) => new()
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
}
