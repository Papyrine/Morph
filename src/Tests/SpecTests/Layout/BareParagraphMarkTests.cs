/// <summary>
/// A bare <c>&lt;w:p/&gt;</c> — no pPr, no rPr, no run — still sizes its line by the paragraph
/// style chain's run properties, inside a table cell as much as in the body. letters/13 stacks
/// such spacers between its letter paragraphs under a Posterama 11 docDefaults; Word's XPS steps
/// 29.42pt across each (two 14.7pt lines), and measuring the spacer at the record's default face
/// fell 1.2pt short per spacer until the letter body sat a line up the page by its signature.
/// </summary>
public class BareParagraphMarkTests
{
    static readonly string fixture = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "letters", "13", "input.docx");

    static IEnumerable<ParagraphElement> CellParagraphs(ParsedDocument document)
    {
        foreach (var element in document.Elements)
        {
            if (element is not TableElement table)
            {
                continue;
            }

            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    foreach (var child in cell.Content)
                    {
                        if (child is ParagraphElement paragraph)
                        {
                            yield return paragraph;
                        }
                    }
                }
            }
        }
    }

    [Test]
    public async Task A_bare_cell_paragraph_takes_its_mark_from_the_style_chain()
    {
        var document = new DocumentParser().Parse(fixture);
        var spacer = CellParagraphs(document).First(_ => _.Runs.Count == 0);

        await Assert.That(spacer.Properties.ParagraphMarkFontFamily).IsEqualTo("Posterama");
        await Assert.That(spacer.Properties.ParagraphMarkFontSizePoints).IsEqualTo(11);
    }

    [Test]
    public async Task A_bare_cell_spacer_measures_the_chain_face_pitch()
    {
        var document = new DocumentParser().Parse(fixture);
        var spacer = CellParagraphs(document).First(_ => _.Runs.Count == 0);
        var dated = CellParagraphs(document).First(_ => string.Concat(_.Runs.Select(run => run.Text)).StartsWith("September"));

        var spacerLine = LayoutTestFonts.Measurer.LayoutLineContents(spacer, 500)[0];
        var textLine = LayoutTestFonts.Measurer.LayoutLineContents(dated, 500)[0];

        // Posterama 11's pitch is 14.63pt; the text line beside it measures the same.
        await Assert.That(spacerLine.Height).IsEqualTo(textLine.Height).Within(0.01f);
        await Assert.That(spacerLine.Height).IsEqualTo(14.63f).Within(0.05f);
    }
}
