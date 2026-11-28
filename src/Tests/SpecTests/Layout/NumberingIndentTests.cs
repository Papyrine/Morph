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
        var title = AllParagraphs(document.Elements)
            .First(_ => _.Properties.StyleId == "Title" &&
                        _.Runs.Any(run => run.Text == "Business"));
        var heading = AllParagraphs(document.Elements)
            .First(_ => _.Properties.StyleId == "Heading1");

        await Assert.That(title.Properties.LeftIndentPoints).IsEqualTo(0).Within(0.01);
        await Assert.That(title.Properties.HangingIndentPoints).IsEqualTo(0).Within(0.01);
        await Assert.That(title.Properties.Numbering).IsNull();
        // The numbered heading keeps its list indentation.
        await Assert.That(heading.Properties.LeftIndentPoints).IsGreaterThan(0);
    }

    /// <summary>
    /// A paragraph's own <c>w:firstLine</c> replaces the hanging indent of its style's list level, as
    /// <c>w:firstLine</c> and <c>w:hanging</c> are one value. ole_chart_preview's Heading 1 states
    /// <c>left=0 firstLine=360</c> over its level's <c>left=792 hanging=432</c>; Word sets its first line
    /// 0.25in in and its second at the margin, where keeping the level's hanging as well hung the first
    /// line 72 twips out into the margin.
    /// </summary>
    [Test]
    public async Task A_paragraph_first_line_indent_replaces_its_list_level_hanging_indent()
    {
        var fixture = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "ole_chart_preview", "input.docx");
        var document = new DocumentParser().Parse(fixture);
        var heading = AllParagraphs(document.Elements)
            .First(_ => _.Properties.StyleId == "Heading1" &&
                        _.Runs.Any(run => run.Text.StartsWith("Lorem ipsum dolor", StringComparison.Ordinal)));

        await Assert.That(heading.Properties.LeftIndentPoints).IsEqualTo(0).Within(0.01);
        await Assert.That(heading.Properties.FirstLineIndentPoints).IsEqualTo(18).Within(0.01);
        await Assert.That(heading.Properties.HangingIndentPoints).IsEqualTo(0).Within(0.01);
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
