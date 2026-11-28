/// <summary>
/// What the measurer lays a run's text out as, and where, for rules Word-probed on 2026-11-28: a soft
/// hyphen shows nothing (<c>_probe_smallcaps</c>), a non-breaking hyphen is the face's own hyphen, small
/// caps are capitals at 80% of the size rounded to half points, tracking is drawn as whole pixels of
/// Word's grid on every glyph (<c>_probe_track</c>), a decimal stop stands the text's decimal point on
/// the stop (decimal_tabs/01), and an at-least line on the document grid keeps its text where a single
/// grid line would set it (<c>_probe_gridleast</c>).
/// </summary>
public class TextDrawingTests
{
    static readonly CanonicalParagraphMeasurer measurer = LayoutTestFonts.Measurer;

    static ParagraphElement Paragraph(string text, RunProperties? properties = null, ParagraphProperties? paragraph = null) =>
        new()
        {
            Properties = paragraph ?? new(),
            Runs = [new() {Text = text, Properties = properties ?? new() {FontFamily = "Arial", FontSizePoints = 12}}]
        };

    static IReadOnlyList<LaidOutLine> Lines(ParagraphElement paragraph, float width = 400) =>
        measurer.LayoutLineContents(paragraph, width);

    static string Drawn(LaidOutLine line) =>
        string.Concat(line.Runs.Select(_ => _.Text));

    [Test]
    public async Task A_soft_hyphen_shows_nothing_and_takes_no_room()
    {
        var soft = Lines(Paragraph("hy\u00ADphens"))[0];
        var plain = Lines(Paragraph("hyphens"))[0];

        await Assert.That(Drawn(soft)).IsEqualTo("hyphens");
        await Assert.That(soft.Width).IsEqualTo(plain.Width).Within(0.01f);
    }

    [Test]
    public async Task A_non_breaking_hyphen_is_drawn_as_a_hyphen_and_holds_its_word_together()
    {
        // Narrower than the word, wider than "non-": a hyphen breaks after itself, a non-breaking one
        // does not, and the word is drawn with the face's hyphen
        var narrow = Lines(Paragraph("non-br"))[0].Width + 1;
        var held = Lines(Paragraph("non\u2011breaking"), narrow);
        var broken = Lines(Paragraph("non-breaking"), narrow);

        await Assert.That(string.Concat(held.Select(Drawn))).IsEqualTo("non-breaking");
        await Assert.That(Drawn(held[0])).IsNotEqualTo("non-");
        await Assert.That(Drawn(broken[0])).IsEqualTo("non-");
    }

    [Test]
    public async Task Small_caps_lay_lowercase_out_as_smaller_capitals()
    {
        var line = Lines(Paragraph("Hx", new() {FontFamily = "Arial", FontSizePoints = 12, SmallCaps = true}))[0];

        await Assert.That(Drawn(line)).IsEqualTo("HX");
        await Assert.That(line.Runs[0].Properties.FontSizePoints).IsEqualTo(12);
        await Assert.That(line.Runs[^1].Properties.FontSizePoints).IsEqualTo(9.5);
    }

    // 0.3pt is half a pixel of Word's 120-dpi grid, which every glyph draws as a whole one, in
    // compatibility mode 12 as in 15
    [Test]
    public async Task Tracking_is_drawn_as_whole_pixels_on_every_glyph()
    {
        var legacy = new CanonicalParagraphMeasurer(LayoutTestFonts.Resolve, compatibility: new() {CompatibilityMode = 12});
        var plain = legacy.LayoutLineContents(Paragraph("nnnn"), 400)[0].Runs[0].GlyphX!;
        var tracked = legacy.LayoutLineContents(Paragraph("nnnn", new() {FontFamily = "Arial", FontSizePoints = 12, CharacterSpacingPoints = 0.3}), 400)[0].Runs[0].GlyphX!;

        await Assert.That(tracked[1] - tracked[0] - (plain[1] - plain[0])).IsEqualTo(0.6f).Within(0.001f);
        await Assert.That(tracked[3] - plain[3]).IsEqualTo(1.8f).Within(0.001f);
    }

    [Test]
    public async Task A_decimal_stop_stands_the_decimal_point_on_the_stop()
    {
        var paragraph = new ParagraphElement
        {
            Properties = new() {TabStops = [new() {PositionPoints = 200, Alignment = TabAlignment.Decimal}]},
            Runs =
            [
                new() {Text = "A", Properties = new() {FontFamily = "Arial", FontSizePoints = 12}},
                new() {Text = "\t", IsTab = true, Properties = new() {FontFamily = "Arial", FontSizePoints = 12}},
                new() {Text = "12.50", Properties = new() {FontFamily = "Arial", FontSizePoints = 12}}
            ]
        };
        var number = Lines(paragraph)[0].Runs.Last(_ => _.Text.Contains('.'));
        var integerPart = Lines(Paragraph("12"))[0].Width;

        await Assert.That(number.X + integerPart).IsEqualTo(200).Within(1f);
    }

    // agendas-minutes/14's List Number: no hanging indent, its "I." right-aligned at the LeftIndent, and
    // Word sets the text at the LeftIndent, just after the numeral, not at the next default stop
    [Test]
    public async Task A_right_aligned_marker_ending_at_the_indent_leaves_its_text_there()
    {
        var paragraph = new ParagraphElement
        {
            Properties = new()
            {
                LeftIndentPoints = 14.4,
                Numbering = new() {Text = "I.", MarkerRightAligned = true, Suffix = ListMarkerSuffix.Tab}
            },
            Runs = [new() {Text = "CALL TO ORDER", Properties = new() {FontFamily = "Arial", FontSizePoints = 10}}]
        };
        var (markerOffset, textShift) = measurer.MarkerPlacement(paragraph);

        await Assert.That(textShift).IsEqualTo(0);
        await Assert.That(markerOffset).IsLessThan(0);
    }

    // A 50pt at-least line on a 36pt grid: its text stands 14pt lower than on a single grid line
    [Test]
    public async Task An_at_least_line_on_the_grid_keeps_its_text_where_a_grid_line_sets_it()
    {
        var single = Lines(Paragraph("text", paragraph: new() {GridLinePitchPoints = 36, LineSpacingMultiplier = 1}))[0];
        var atLeast = Lines(Paragraph("text", paragraph: new() {GridLinePitchPoints = 36, LineSpacingRule = LineSpacingRule.AtLeast, LineSpacingPoints = 50}))[0];

        await Assert.That(single.Height).IsEqualTo(36).Within(0.01f);
        await Assert.That(atLeast.Height).IsEqualTo(50).Within(0.01f);
        await Assert.That(atLeast.Ascent - single.Ascent).IsEqualTo(14).Within(0.01f);
    }
}
