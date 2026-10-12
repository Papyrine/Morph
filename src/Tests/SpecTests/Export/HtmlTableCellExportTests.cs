/// <summary>
/// Tests for table-cell rules of the HTML export that the snapshot tests do not reach: a cell
/// paragraph set against its own tab stops, and what a cell's declared width measures.
/// </summary>
public class HtmlTableCellExportTests
{
    static ParagraphElement TocEntry(string title, string pageNumber, double stopPoints) =>
        new()
        {
            Runs =
            [
                TextRun(title),
                new() {IsTab = true, Text = "\t"},
                TextRun(pageNumber)
            ],
            Properties = new()
            {
                TabStops = [new() {PositionPoints = stopPoints, Alignment = TabAlignment.Right, Leader = TabLeader.Dot}]
            }
        };

    static string ExportCell(double? cellWidthPoints, params DocumentElement[] content) =>
        HtmlExporter.Export(
            Doc(Table(new TableRow
            {
                Cells =
                [
                    new()
                    {
                        Content = content,
                        Properties = new() {WidthPoints = cellWidthPoints}
                    }
                ]
            })),
            new() {EmitDocument = false});

    /// <summary>
    /// table_of_contents/03 keeps its contents in a narrow cell, each entry tabbed to a right stop
    /// at the page's 468pt measure with a dot leader. Joined inline with the rest of the cell the
    /// tab was one space, the page number ran on after the title and the leader was lost. Word
    /// draws a stop past the cell's edge AT the edge, so the leader stretches to it.
    /// </summary>
    [Test]
    public async Task CellParagraph_TabbedToAStopPastTheCellsEdge_LeadsToTheEdge()
    {
        var html = ExportCell(160, TocEntry("Introduction", "1", 468));

        await Assert.That(html).Contains("<p style=\"display: flex; margin-bottom: 0pt\"><span>Introduction</span><span style=\"flex: 1; border-bottom: 1pt dotted currentColor; margin: 0 2pt 0.28em\"></span><span>1</span></p>");
    }

    /// <summary>
    /// The measure a stop is read against is the cell's, not the page's: a right stop in the
    /// middle of a wide cell is still a mid-line stop, laid out as a box that ends at it.
    /// </summary>
    [Test]
    public async Task CellParagraph_TabbedToAStopInsideTheCell_EndsAtTheStop()
    {
        var html = ExportCell(400, TocEntry("Introduction", "1", 150));

        await Assert.That(html).Contains("<span style=\"display: inline-flex; justify-content: space-between; min-width: 150pt\"><span>Introduction</span><span>1</span></span>");
    }

    /// <summary>A cell paragraph with no declared stops stays inline with the rest of the cell.</summary>
    [Test]
    public async Task CellParagraph_WithoutStops_StaysInline()
    {
        var html = ExportCell(160, new ParagraphElement {Runs = [TextRun("a"), new() {IsTab = true, Text = "\t"}, TextRun("b")]});

        await Assert.That(html).Contains("<td style=\"width: 160pt\">a b</td>");
    }

    /// <summary>
    /// <c>w:tcW</c> is the whole cell, its side margins included, so the stylesheet sizes a cell by
    /// its border box. Read as the content box, a 75pt column came out 91pt wide.
    /// </summary>
    [Test]
    public async Task CellWidth_MeasuresTheBorderBox()
    {
        var html = HtmlExporter.Export(Doc(Table(Row(header: false, "cell"))));

        await Assert.That(html).Contains("td, th { padding: 4pt 7pt; vertical-align: top; box-sizing: border-box; }");
    }
}
