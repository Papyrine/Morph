/// <summary>
/// Word's bold for a family with no bold face (<see cref="BoldSimulation"/>), Word-probed on
/// <c>_probe_synbold</c> and <c>_probe_synwrap</c>: the regular face is emboldened, each glyph is drawn
/// 0.6pt wider, and the line is still broken on the regular advances.
/// </summary>
public class BoldSimulationTests
{
    const string sample = "Hamburg fonts ivy";

    static ParagraphElement Paragraph(string text, string family, bool bold, TextAlignment alignment = TextAlignment.Left, double size = 12) => new()
    {
        Runs = [new() {Text = text, Properties = new() {FontFamily = family, FontSizePoints = size, Bold = bold}}],
        Properties = new() {Alignment = alignment}
    };

    static LaidOutLine Line(string family, bool bold, float measure = 400, string text = sample) =>
        LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(text, family, bold), measure)[0];

    [Test]
    [Arguments(400, true)]
    [Arguments(300, true)]
    [Arguments(599, true)]
    [Arguments(600, false)]
    [Arguments(700, false)]
    [Arguments(900, false)]
    [Arguments(0, false)]
    public async Task A_bold_request_is_emboldened_below_weight_600(int faceWeight, bool emboldened)
    {
        await Assert.That(BoldSimulation.Applies(true, faceWeight)).IsEqualTo(emboldened);
        await Assert.That(BoldSimulation.Applies(false, faceWeight)).IsFalse();
    }

    // Franklin Gothic Book has no bold face: Word draws the Book face with the simulation.
    [Test]
    public async Task A_bold_run_in_a_face_with_no_bold_is_flagged_for_the_simulation()
    {
        var bold = Line("Franklin Gothic Book", bold: true);
        var regular = Line("Franklin Gothic Book", bold: false);

        await Assert.That(bold.Runs[0].Properties.SyntheticBold).IsEqualTo(SyntheticBold.Simulated);
        await Assert.That(regular.Runs[0].Properties.SyntheticBold).IsEqualTo(SyntheticBold.None);
    }

    // A family with a bold face of its own draws in it and is left alone.
    [Test]
    public async Task A_bold_run_in_a_family_with_a_bold_face_is_not_simulated()
    {
        var bold = Line("Arial", bold: true);

        await Assert.That(bold.Runs[0].Properties.SyntheticBold).IsEqualTo(SyntheticBold.None);
        await Assert.That(bold.Runs[0].Properties.CharacterSpacingPoints).IsEqualTo(0);
    }

    // _probe_synbold: seventeen glyphs start 10.2pt further left right-aligned, at 10pt and 40pt alike.
    [Test]
    [Arguments(10)]
    [Arguments(40)]
    public async Task A_simulated_glyph_is_drawn_six_tenths_of_a_point_wider_at_any_size(double size)
    {
        var bold = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(sample, "Franklin Gothic Book", true, size: size), 500)[0];
        var regular = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(sample, "Franklin Gothic Book", false, size: size), 500)[0];

        await Assert.That(bold.Width - regular.Width).IsEqualTo(17 * 0.6f).Within(0.01f);
        await Assert.That(bold.Runs[0].Width - regular.Runs[0].Width).IsEqualTo(17 * 0.6f).Within(0.01f);
        await Assert.That(bold.Runs[0].Properties.CharacterSpacingPoints).IsEqualTo(0.6).Within(0.0001);
    }

    // _probe_synwrap: a bold sentence wraps at the very measure the regular one does.
    [Test]
    public async Task The_line_is_broken_on_the_regular_advances()
    {
        var text = string.Join(" ", Enumerable.Repeat("nnnnnn", 40));
        for (var measure = 180f; measure <= 320f; measure += 7f)
        {
            var bold = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(text, "Franklin Gothic Book", true), measure);
            var regular = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(text, "Franklin Gothic Book", false), measure);

            await Assert.That(bold.Count).IsEqualTo(regular.Count);
            for (var index = 0; index < bold.Count; index++)
            {
                await Assert.That(bold[index].Runs[0].Text).IsEqualTo(regular[index].Runs[0].Text);
            }
        }
    }

    // A line that fills its measure has no room to be drawn wider, and one with a little takes only that.
    [Test]
    public async Task The_widening_never_carries_a_line_past_its_measure()
    {
        var text = string.Join(" ", Enumerable.Repeat("nnnnnn", 40));
        const float measure = 250f;
        var lines = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(text, "Franklin Gothic Book", true), measure);

        await Assert.That(lines.Count).IsGreaterThan(2);
        foreach (var line in lines)
        {
            await Assert.That(line.Width).IsLessThanOrEqualTo(measure + 0.01f);
        }
    }

    // A justified line has given its room to its spaces: nothing is left to widen with.
    [Test]
    public async Task A_justified_line_is_not_widened()
    {
        var text = string.Join(" ", Enumerable.Repeat("nnnnnn", 40));
        var justified = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(text, "Franklin Gothic Book", true, TextAlignment.Justify), 250f);

        await Assert.That(justified[0].Runs[0].Properties.SyntheticBold).IsEqualTo(SyntheticBold.Simulated);
        await Assert.That(justified[0].Runs[0].Properties.CharacterSpacingPoints).IsEqualTo(0);
    }

    // Bahnschrift is a variable font: Word draws its bold as an instance of the weight axis, at the
    // regular advances, so it is emboldened without being widened.
    [Test]
    public async Task A_variable_font_is_emboldened_at_its_regular_advances()
    {
        var bold = Line("Bahnschrift", bold: true);
        var regular = Line("Bahnschrift", bold: false);

        await Assert.That(bold.Runs[0].Properties.SyntheticBold).IsEqualTo(SyntheticBold.VariableInstance);
        await Assert.That(bold.Width).IsEqualTo(regular.Width).Within(0.001f);
    }

    // Strokes grow by 2.7% of the em, 5% for a variable font's instance, and by no less than a pixel.
    [Test]
    public async Task The_stroke_is_a_fraction_of_the_em_and_never_under_a_pixel()
    {
        const double pixel = 72.0 / 150;

        await Assert.That(BoldSimulation.StrokeWidthPoints(SyntheticBold.Simulated, 40, pixel)).IsEqualTo(1.08).Within(0.0001);
        await Assert.That(BoldSimulation.StrokeWidthPoints(SyntheticBold.VariableInstance, 40, pixel)).IsEqualTo(2.0).Within(0.0001);
        await Assert.That(BoldSimulation.StrokeWidthPoints(SyntheticBold.Simulated, 10, pixel)).IsEqualTo(pixel).Within(0.0001);
    }

    // A list marker is not one of the paragraph's runs, and is flagged where it is placed.
    [Test]
    public async Task A_bold_marker_in_a_face_with_no_bold_is_flagged_too()
    {
        var marker = new RunProperties {FontFamily = "Franklin Gothic Book", FontSizePoints = 12, Bold = true};

        await Assert.That(LayoutTestFonts.Measurer.Drawn(marker).SyntheticBold).IsEqualTo(SyntheticBold.Simulated);
        await Assert.That(LayoutTestFonts.Measurer.Drawn(marker with {Bold = false}).SyntheticBold).IsEqualTo(SyntheticBold.None);
        await Assert.That(LayoutTestFonts.Measurer.Drawn(marker with {FontFamily = "Arial"}).SyntheticBold).IsEqualTo(SyntheticBold.None);
    }
}
