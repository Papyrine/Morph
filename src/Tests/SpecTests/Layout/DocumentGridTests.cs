/// <summary>
/// The document grid (<c>w:docGrid</c>) and spacing counted in lines (<c>w:beforeLines</c> /
/// <c>w:afterLines</c>). Word-probed in <c>document_grid_lines</c>, <c>document_grid_table</c> and
/// <c>spacing_line_units</c> (see their notes.md): an auto line rounds up to whole grid pitches and is
/// centred in them, and a line unit is the grid pitch, or 12pt off a grid.
/// </summary>
public class DocumentGridTests
{
    static List<ParagraphElement> Paragraphs(string scenario) =>
        new DocumentParser().Parse(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", scenario, "input.docx"))
            .Elements.OfType<ParagraphElement>().ToList();

    static ParagraphElement Labelled(List<ParagraphElement> paragraphs, string label) =>
        paragraphs.First(_ => _.Runs.Any(run => run.Text.StartsWith(label)));

    [Test]
    public async Task A_paragraph_on_a_typed_grid_snaps_to_its_pitch()
    {
        var paragraphs = Paragraphs("document_grid_lines");

        await Assert.That(Labelled(paragraphs, "A0").Properties.GridLinePitchPoints).IsEqualTo(36);
        await Assert.That(Labelled(paragraphs, "E0").Properties.GridLinePitchPoints).IsEqualTo(0);
    }

    [Test]
    public async Task An_untyped_grid_lays_out_nothing()
    {
        var paragraphs = Paragraphs("spacing_line_units");

        await Assert.That(paragraphs.All(_ => _.Properties.GridLinePitchPoints == 0)).IsTrue();
    }

    [Test]
    public async Task A_table_cell_snaps_under_adjustLineHeightInTable()
    {
        var document = new DocumentParser().Parse(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "document_grid_table", "input.docx"));
        var cell = document.Elements.OfType<TableElement>().Single().Rows[0].Cells[0].Content.OfType<ParagraphElement>().First();

        await Assert.That(cell.Properties.GridLinePitchPoints).IsEqualTo(36);
    }

    [Test]
    [Arguments("document_grid_lines", "K0", 36.0, 0.0)]
    [Arguments("document_grid_lines", "L0", 0.0, 18.0)]
    [Arguments("document_grid_lines", "M0", 0.0, 24.0)]
    [Arguments("document_grid_lines", "O0", 18.0, 0.0)]
    [Arguments("spacing_line_units", "K0", 12.0, 0.0)]
    [Arguments("spacing_line_units", "L0", 0.0, 6.0)]
    [Arguments("spacing_line_units", "M0", 0.0, 24.0)]
    [Arguments("spacing_line_units", "O0", 6.0, 0.0)]
    public async Task Line_units_count_in_the_grid_pitch_or_twelve_points(string scenario, string label, double before, double after)
    {
        var properties = Labelled(Paragraphs(scenario), label).Properties;

        await Assert.That(properties.SpacingBeforePoints).IsEqualTo(before).Within(0.001);
        await Assert.That(properties.SpacingAfterPoints).IsEqualTo(after).Within(0.001);
    }

    [Test]
    [Arguments(13.8, 36.0, 1.0, 36.0)]
    [Arguments(34.4, 36.0, 1.0, 36.0)]
    [Arguments(46.0, 36.0, 1.0, 72.0)]
    [Arguments(82.8, 36.0, 1.0, 108.0)]
    [Arguments(82.8, 25.0, 1.0, 100.0)]
    [Arguments(13.8, 36.0, 1.5, 54.0)]
    [Arguments(13.8, 25.0, 1.5, 37.5)]
    [Arguments(13.8, 36.0, 2.0, 72.0)]
    public async Task An_auto_line_rounds_up_to_whole_pitches_then_takes_its_multiple(double natural, double pitch, double multiple, double height)
    {
        var (lineHeight, shift) = CanonicalTextMeasurer.GridLine(natural, pitch, multiple);

        await Assert.That(lineHeight).IsEqualTo(height).Within(0.001);
        await Assert.That(shift).IsEqualTo((height - natural) / 2).Within(0.001);
    }

    [Test]
    public async Task The_measurer_lays_a_gridded_line_at_the_grid_and_an_exact_one_off_it()
    {
        var gridded = Line(new() { GridLinePitchPoints = 36, LineSpacingMultiplier = 1 });
        var exact = Line(new() { GridLinePitchPoints = 36, LineSpacingRule = LineSpacingRule.Exactly, LineSpacingPoints = 20 });

        await Assert.That(gridded).IsEqualTo(36f).Within(0.001f);
        await Assert.That(exact).IsEqualTo(20f).Within(0.001f);
    }

    static float Line(ParagraphProperties properties) =>
        LayoutTestFonts.Measurer.LayoutLines(
            new()
            {
                Runs = [new() { Text = "grid", Properties = new() { FontFamily = "Arial", FontSizePoints = 12 } }],
                Properties = properties
            },
            400)[0].Height;
}
