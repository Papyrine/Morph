/// <summary>
/// <c>w:w</c> scales a run's advances (<see cref="RunProperties.CharacterScalePercent"/>). Word-probed in
/// <c>character_scale</c> (see its notes.md): the XPS draws a scaled run under a pure x-scale matrix, and
/// lines break where the scaled widths say.
/// </summary>
public class CharacterScaleTests
{
    static RunProperties Arial(double scale = 100) =>
        new() { FontFamily = "Arial", FontSizePoints = 24, CharacterScalePercent = scale };

    [Test]
    public async Task The_parser_reads_each_runs_scale()
    {
        var document = new DocumentParser().Parse(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "character_scale", "input.docx"));
        var scales = document.Elements.OfType<ParagraphElement>().Take(6).Select(_ => _.Runs[0].Properties.CharacterScalePercent).ToList();

        await Assert.That(scales).IsEquivalentTo([100.0, 50.0, 66.0, 150.0, 200.0, 300.0]);
    }

    [Test]
    [Arguments(50.0)]
    [Arguments(200.0)]
    [Arguments(300.0)]
    public async Task A_run_measures_at_its_scale(double scale)
    {
        var plain = LayoutTestFonts.Measurer.MeasureRunWidth("HHHH", Arial());
        var scaled = LayoutTestFonts.Measurer.MeasureRunWidth("HHHH", Arial(scale));

        // Widths land on Word's 120-dpi grid, so a pixel (0.6pt) either way.
        await Assert.That(scaled).IsEqualTo((float) (plain * scale / 100)).Within(0.61f);
    }

    [Test]
    public async Task The_scale_moves_the_line_breaks()
    {
        static int Lines(double scale) =>
            LayoutTestFonts.Measurer.LayoutLines(
                new()
                {
                    Runs = [new() { Text = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog again and again ", 3)), Properties = Arial(scale) with { FontSizePoints = 12 } }]
                },
                468).Count;

        await Assert.That(Lines(50)).IsLessThan(Lines(100));
        await Assert.That(Lines(200)).IsGreaterThan(Lines(100));
    }
}
