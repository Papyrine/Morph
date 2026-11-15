using DocumentFormat.OpenXml.Packaging;
using static EditDocuments;

/// <summary>
/// A body text box the text wraps round is placed, and the text beside it keeps clear of it as it would of
/// a picture. Until 2026-11-10 the Fragmenter placed only wrap-none boxes and dropped this one whole, text
/// and all. The fixture is WordProbe's own, as Word wrote it: a 160x80pt box square-wrapped 228pt into the
/// column, anchored in the first paragraph, 9pt clear of the text either side — whose first paragraph is
/// made long enough here to run beside the box and on below it.
/// </summary>
public class WrappedTextBoxLayoutTests
{
    const string words = "Words to fill the measure and run on past the box below it, ";

    static LaidOutPage Layout()
    {
        var fixture = File.ReadAllBytes(Path.Combine(ProjectFiles.ProjectDirectory, "..", "WordProbe", "Fixtures", "text-box.docx"));

        // The first paragraph is the anchor (unit 0) and its text (unit 1).
        var docx = DocumentEditor.Rewrite(fixture, 0, [Reads(K(0), T(1, string.Concat(Enumerable.Repeat(words, 12))))], Plain).Document;
        using var stream = new MemoryStream(docx);
        var document = new DocumentParser().Parse(stream);
        return new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility)
            .Layout(
                document.Elements, document.PageSettings, document.Header, document.Footer,
                document.FirstPageHeader, document.FirstPageFooter, document.EvenPageHeader, document.EvenPageFooter,
                DocumentNotes.From(document))
            .Pages[0];
    }

    static float Right(PlacedLine line) =>
        line.Runs.Max(_ => _.X + _.Width);

    [Test]
    public async Task TheBox_IsPlaced_WithItsText()
    {
        var page = Layout();

        var box = page.Items.OfType<PlacedLine>().Where(_ => _.Runs.Any(run => run.Text.Contains("Box line"))).ToList();
        await Assert.That(box.Count).IsEqualTo(2);

        // At the column's left plus 228pt, inside the box's inset.
        await Assert.That(box[0].Runs[0].X).IsGreaterThan(300).And.IsLessThan(310);
    }

    // Below the box the paragraph keeps the measure it started beside it, as with a picture: Word widens
    // the lines again past the float's foot, the engine holds a paragraph's band for its whole height
    // (docs/word-features.md, Text Wrapping). Only the lines beside the box are checked.
    [Test]
    public async Task TheTextBesideIt_KeepsClear()
    {
        var page = Layout();

        var beside = page.Items.OfType<PlacedLine>()
            .Where(_ => _.Runs.Any(run => run.Text.Contains("Words to fill")) && _.Y < 72 + 80)
            .ToList();
        await Assert.That(beside.Count).IsGreaterThan(2);

        // The box starts 300pt in, and the text keeps 9pt clear of it.
        foreach (var line in beside)
        {
            await Assert.That(Right(line)).IsLessThanOrEqualTo(291.5f);
        }
    }
}
