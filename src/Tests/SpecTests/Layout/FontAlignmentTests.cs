/// <summary>
/// <c>w:textAlignment</c> (<see cref="FontAlignment"/>): a line's smaller runs move by a fixed fraction of
/// how much smaller they are than its largest. Word-probed in <c>font_alignment</c> (see its notes.md).
/// </summary>
public class FontAlignmentTests
{
    static IReadOnlyList<LaidOutRun> Runs(FontAlignment alignment) =>
        LayoutTestFonts.Measurer.LayoutLineContents(
            new()
            {
                Runs =
                [
                    new() { Text = "Hx", Properties = new() { FontFamily = "Arial", FontSizePoints = 6 } },
                    new() { Text = "Hx", Properties = new() { FontFamily = "Arial", FontSizePoints = 24 } }
                ],
                Properties = new() { FontAlignment = alignment, LineSpacingMultiplier = 1 }
            },
            400)[0].Runs;

    [Test]
    public async Task The_parser_reads_each_paragraphs_alignment()
    {
        var document = new DocumentParser().Parse(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "font_alignment", "input.docx"));
        var alignments = document.Elements.OfType<ParagraphElement>().Take(4).Select(_ => _.Properties.FontAlignment).ToList();

        await Assert.That(alignments).IsEquivalentTo([FontAlignment.Baseline, FontAlignment.Top, FontAlignment.Center, FontAlignment.Bottom]);
    }

    [Test]
    [Arguments(nameof(FontAlignment.Baseline), 0.0)]
    [Arguments(nameof(FontAlignment.Top), 16.11)]
    [Arguments(nameof(FontAlignment.Center), 6.12)]
    [Arguments(nameof(FontAlignment.Bottom), -1.89)]
    public async Task A_smaller_run_moves_by_a_fraction_of_its_difference_from_the_largest(string alignment, double shift)
    {
        // 6pt against 24pt: 18pt smaller. Word's XPS: up 15.96, up 6.00, down 2.04.
        var runs = Runs(Enum.Parse<FontAlignment>(alignment));

        await Assert.That(runs[0].BaselineShift).IsEqualTo((float) shift).Within(0.01f);
        await Assert.That(runs[1].BaselineShift).IsEqualTo(0f);
    }
}
