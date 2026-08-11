using PdfSharp.Pdf.IO;

/// <summary>
/// Validates the layout engine's first painter (<c>docs/layout-engine.md</c>, step 5):
/// <c>PdfPainter</c> draws a <c>Fragmenter</c>-produced <see cref="LaidOutDocument"/> to a PDF with the
/// tree's pages and geometry — no pagination or measurement of its own. This proves the tree drives real
/// PDF output. Text rendering (ink) is confirmed by rasterising a page; here the focus is the structural
/// contract: the painter emits exactly the tree's pages, at the tree's page sizes, from real content.
/// </summary>
public class PdfPainterTests
{
    static readonly Fragmenter fragmenter = new(LayoutTestFonts.Measurer);
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    // US Letter, 1-inch margins.
    static PageSettings LetterPage =>
        new()
        {
            WidthPoints = 612,
            HeightPoints = 792,
            MarginTop = 72,
            MarginBottom = 72,
            MarginLeft = 72,
            MarginRight = 72
        };

    static ParagraphElement P(string text) =>
        new()
        {
            Runs =
            [
                new()
                {
                    Text = text,
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                }
            ],
            Properties = new()
        };

    [Test]
    public async Task Paints_the_tree_to_a_valid_multipage_pdf_at_the_trees_geometry()
    {
        // Enough paragraphs to span more than one page.
        var elements = Enumerable.Range(0, 120)
            .Select(DocumentElement (_) => P($"Paragraph {_} with several words to fill out a line of the page."))
            .ToList();
        var tree = fragmenter.Layout(elements, LetterPage);
        await Assert.That(tree.Pages.Count > 1).IsTrue();

        // The painter's input is real content: the tree's placed lines carry the paragraph text.
        var runs = tree.Pages[0].Items.OfType<PlacedLine>().SelectMany(_ => _.Runs).ToList();
        await Assert.That(runs.Count > 0).IsTrue();
        await Assert.That(runs[0].Text.Contains("Paragraph")).IsTrue();

        var pdf = PdfPainter.Paint(tree, fontsDirectory);
        using var stream = new MemoryStream();
        await pdf.SaveAsync(stream);

        // Reopen the saved bytes: a valid PDF with exactly the tree's pages at the tree's geometry — the
        // painter added no pages and paginated nothing.
        stream.Position = 0;
        using var reopened = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        await Assert.That(reopened.PageCount).IsEqualTo(tree.Pages.Count);
        await Assert.That(reopened.Pages[0].Width.Point).IsEqualTo(612d).Within(0.5);
        await Assert.That(reopened.Pages[0].Height.Point).IsEqualTo(792d).Within(0.5);
    }
}
