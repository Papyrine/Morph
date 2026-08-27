using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
// Morph has its own TableRow, TableCell, TableProperties and Run in scope here, so the OOXML
// ones are qualified through this alias where the names collide.
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// A cell's <c>w:gridSpan</c> is an unbounded ST_DecimalNumber in the file. Left raw it flows through
/// <see cref="TableLayout.GetColumnCount"/> into <c>new float[colCount]</c> allocations and per-column
/// layout loops, so a sub-1KB crafted document with a single giant span could exhaust memory (OOM)
/// before a page was even measured. Both the DOCX parse path and the HTML <c>colspan</c> path clamp
/// the span to a ceiling far above any real table (Word itself caps a table at 63 columns), which
/// leaves every legitimate document untouched while neutralising the crafted value.
/// </summary>
public class TableGridSpanClampTests
{
    const int maxGridSpan = 1000;

    [Test]
    public async Task DocumentParser_ClampsHugeGridSpan()
    {
        using var stream = BuildGiantGridSpanDocument(2_000_000_000);
        var document = new DocumentParser().Parse(stream);

        var table = document.Elements.OfType<TableElement>().Single();
        var cell = table.Rows.Single().Cells.Single();

        await Assert.That(cell.Properties.GridSpan).IsEqualTo(maxGridSpan);
        // The sink that would have allocated float[colCount] is now bounded.
        await Assert.That(TableLayout.GetColumnCount(table)).IsEqualTo(maxGridSpan);
    }

    [Test]
    public async Task DocumentParser_LeavesLegitimateGridSpanUntouched()
    {
        using var stream = BuildGiantGridSpanDocument(3);
        var document = new DocumentParser().Parse(stream);

        var cell = document.Elements.OfType<TableElement>().Single().Rows.Single().Cells.Single();

        await Assert.That(cell.Properties.GridSpan).IsEqualTo(3);
    }

    [Test]
    public async Task HtmlParser_ClampsHugeColspan()
    {
        var result = HtmlParser.Parse("<table><tr><td colspan=\"2000000000\">x</td></tr></table>");

        var table = result.OfType<TableElement>().Single();
        var cell = table.Rows.Single().Cells.Single();

        await Assert.That(cell.Properties.GridSpan).IsEqualTo(maxGridSpan);
        await Assert.That(TableLayout.GetColumnCount(table)).IsEqualTo(maxGridSpan);
    }

    static MemoryStream BuildGiantGridSpanDocument(int gridSpan)
    {
        var table = new Table(
            new W.TableRow(
                new W.TableCell(
                    new W.TableCellProperties(new GridSpan { Val = gridSpan }),
                    new Paragraph(new W.Run(new Text("x"))))));

        var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = [with(new Body(table))];
        }

        stream.Position = 0;
        return stream;
    }
}
