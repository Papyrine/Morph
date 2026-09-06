/// <summary>
/// Switching numbering off (<c>w:numPr/w:numId="0"</c>) drops the indentation the numbered base style
/// carried for it. business-plans/08's Title is basedOn a numbered Heading 1 whose
/// <c>w:ind left=720 hanging=720</c> is the level's; Word sets both title lines at the cell edge
/// (XPS x=60.05 for "Business" and "proposal" alike), where the inherited hanging indent put the
/// second 36pt in.
/// </summary>
public class NumberingIndentTests
{
    static string Fixture => Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "business-plans", "08", "input.docx");

    [Test]
    public async Task A_style_that_switches_numbering_off_sheds_the_base_style_numbering_indent()
    {
        var document = new DocumentParser().Parse(Fixture);
        var title = AllParagraphs(document.Elements).First(_ => _.Properties.StyleId == "Title" && _.Runs.Any(run => run.Text == "Business"));
        var heading = AllParagraphs(document.Elements).First(_ => _.Properties.StyleId == "Heading1");

        await Assert.That(title.Properties.LeftIndentPoints).IsEqualTo(0).Within(0.01);
        await Assert.That(title.Properties.HangingIndentPoints).IsEqualTo(0).Within(0.01);
        await Assert.That(title.Properties.Numbering).IsNull();
        // The numbered heading keeps its list indentation.
        await Assert.That(heading.Properties.LeftIndentPoints).IsGreaterThan(0);
    }

    static IEnumerable<ParagraphElement> AllParagraphs(IEnumerable<DocumentElement> elements)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case ParagraphElement paragraph:
                    yield return paragraph;
                    break;
                case TableElement table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var nested in AllParagraphs(cell.Content))
                            {
                                yield return nested;
                            }
                        }
                    }

                    break;
            }
        }
    }
}
