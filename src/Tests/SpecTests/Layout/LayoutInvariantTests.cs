/// <summary>
/// Geometry every laid-out Word scenario must keep, whatever Word's reference looks like: no body text
/// off the page, no text escaping a cell that does not clip, and no two body paragraphs' lines drawn
/// over each other. Each is a bug a reference comparison can miss — a few stray pixels against a whole
/// page — and the first sweep found one: business-plans/13's "Marketing/Advertising", a word wider
/// than its cell, ran on into the next column where Word breaks it mid-word.
/// </summary>
public class LayoutInvariantTests
{
    // A point of slack, for rounding at the edges of a line or cell.
    const float tolerance = 1;

    public static IEnumerable<string> Scenarios() =>
        ScenarioInputs.Directories(ScenarioFormat.Word).Select(ScenarioInputs.ScenarioName).Order();

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task Holds(string scenario)
    {
        var input = Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), scenario, "input.docx");
        var document = new DocumentParser().Parse(input);
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility).Layout(
            document.Elements, document.PageSettings, document.Header, document.Footer,
            document.FirstPageHeader, document.FirstPageFooter, document.EvenPageHeader, document.EvenPageFooter,
            DocumentNotes.From(document));

        await Assert.That(Violations(document, laidOut)).IsEmpty();
    }

    static List<string> Violations(ParsedDocument document, LaidOutDocument laidOut)
    {
        var body = document.Elements.OfType<ParagraphElement>().ToHashSet(ReferenceEqualityComparer.Instance);
        var violations = new List<string>();
        foreach (var page in laidOut.Pages)
        {
            var width = (float) page.Settings.WidthPoints;
            var bodyLines = new List<PlacedLine>();
            foreach (var item in page.Items)
            {
                if (item is PlacedLine line && body.Contains(line.Paragraph))
                {
                    bodyLines.Add(line);
                    foreach (var run in Inked(line))
                    {
                        if (run.X < -tolerance || run.X + run.Width > width + tolerance)
                        {
                            violations.Add($"page {page.Number}: '{run.Text}' at {run.X:F1}..{run.X + run.Width:F1} is off a page {width:F0} wide");
                        }
                    }
                }

                if (item is PlacedTableRow row)
                {
                    foreach (var cell in row.Cells.Where(_ => !_.ClipContent))
                    {
                        foreach (var run in cell.Content.OfType<PlacedLine>().SelectMany(Inked))
                        {
                            if (run.X < cell.X - tolerance || run.X + run.Width > cell.X + cell.Width + tolerance)
                            {
                                violations.Add($"page {page.Number}: '{run.Text}' at {run.X:F1}..{run.X + run.Width:F1} escapes its cell at {cell.X:F1}..{cell.X + cell.Width:F1}");
                            }
                        }
                    }
                }
            }

            for (var i = 0; i < bodyLines.Count; i++)
            {
                for (var j = i + 1; j < bodyLines.Count; j++)
                {
                    var a = bodyLines[i];
                    var b = bodyLines[j];
                    if (ReferenceEquals(a.Paragraph, b.Paragraph) || !Inked(a).Any() || !Inked(b).Any())
                    {
                        continue;
                    }

                    var horizontal = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
                    var vertical = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
                    if (horizontal > tolerance && vertical > tolerance)
                    {
                        violations.Add($"page {page.Number}: '{Inked(a).First().Text}' and '{Inked(b).First().Text}' overlap by {vertical:F1}pt");
                    }
                }
            }
        }

        return violations;
    }

    static IEnumerable<PlacedRun> Inked(PlacedLine line) =>
        line.Runs.Where(_ => !string.IsNullOrWhiteSpace(_.Text));
}
