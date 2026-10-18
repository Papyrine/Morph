using static EditDocuments;
using static ReviewDocuments;

// Rows and columns added to and taken from the table a caret is in, and the table deleted. Asserted
// on ReviewDocuments.Describe's table notation — rows by /, cells by | — and on the grid.
public class TableEditTests
{
    // Two rows of two cells, 2000 twips a column, and a paragraph after the table.
    static byte[] Grid(string after = "<w:p><w:r><w:t>After</w:t></w:r></w:p>") =>
        Build(
            "<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>" +
            Row("a", "b") +
            Row("c", "d") +
            "</w:tbl>" +
            after);

    static string Row(params string[] cells) =>
        "<w:tr>" + string.Concat(cells.Select(_ => $"<w:tc><w:tcPr><w:tcW w:w=\"2000\" w:type=\"dxa\"/></w:tcPr><w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:t>{_}</w:t></w:r></w:p></w:tc>")) + "</w:tr>";

    static string Widths(byte[] docx) =>
        string.Join(",", Read(docx, _ => _.MainDocumentPart!.Document!.Descendants<DocumentFormat.OpenXml.Wordprocessing.GridColumn>().Select(column => column.Width!.Value).ToList()));

    // Paragraph 0 is cell a, 1 is b, 2 is c, 3 is d.

    [Test]
    public async Task ARowBelow_IsEmpty_AndFormattedAsTheRowItCameFrom()
    {
        var result = DocumentEditor.EditTable(Grid(), 0, TableEdit.InsertRowBelow, Plain);

        await Assert.That(Describe(result.Document)).StartsWith("<a|b/|/c|d>");
        await Assert.That(Xml(result.Document)).Contains("<w:tr><w:tc><w:tcPr><w:tcW w:w=\"2000\" w:type=\"dxa\" /></w:tcPr><w:p><w:pPr><w:jc w:val=\"center\" /></w:pPr></w:p></w:tc>");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((2, 0));
    }

    [Test]
    public async Task ARowAbove_GoesAbove()
    {
        var result = DocumentEditor.EditTable(Grid(), 3, TableEdit.InsertRowAbove, Plain);

        await Assert.That(Describe(result.Document)).StartsWith("<a|b/|/c|d>");
        await Assert.That(result.Paragraph).IsEqualTo(3);
    }

    // The table stays as wide as it was: every column narrows alike to make room.
    [Test]
    public async Task AColumnRight_KeepsTheTablesWidth()
    {
        var result = DocumentEditor.EditTable(Grid(), 0, TableEdit.InsertColumnRight, Plain);

        await Assert.That(Describe(result.Document)).StartsWith("<a||b/c||d>");
        await Assert.That(Widths(result.Document)).IsEqualTo("1333,1333,1333");
        await Assert.That(Xml(result.Document)).Contains("<w:tcW w:w=\"1333\" w:type=\"dxa\" />");
        await Assert.That(result.Paragraph).IsEqualTo(1);
    }

    [Test]
    public async Task AColumnLeft_GoesLeft()
    {
        var result = DocumentEditor.EditTable(Grid(), 1, TableEdit.InsertColumnLeft, Plain);

        await Assert.That(Describe(result.Document)).StartsWith("<a||b/c||d>");
    }

    // A cell merged across where the column goes takes it in.
    [Test]
    public async Task AColumn_ThroughAMergedCell_WidensIt()
    {
        var docx = Build(
            "<w:tbl><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>" +
            "<w:tr><w:tc><w:tcPr><w:gridSpan w:val=\"2\"/></w:tcPr><w:p><w:r><w:t>wide</w:t></w:r></w:p></w:tc></w:tr>" +
            Row("c", "d") +
            "</w:tbl><w:p/>");

        var result = DocumentEditor.EditTable(docx, 1, TableEdit.InsertColumnRight, Plain);

        await Assert.That(Describe(result.Document)).StartsWith("<wide/c||d>");
        await Assert.That(Xml(result.Document)).Contains("<w:gridSpan w:val=\"3\" />");
    }

    [Test]
    public async Task ARowDeleted_Goes()
    {
        var result = DocumentEditor.EditTable(Grid(), 0, TableEdit.DeleteRow, Plain);

        await Assert.That(Describe(result.Document)).StartsWith("<c|d>");
        await Assert.That(result.Paragraph).IsEqualTo(0);
    }

    [Test]
    public async Task AColumnDeleted_Goes_AndTheGridWithIt()
    {
        var result = DocumentEditor.EditTable(Grid(), 0, TableEdit.DeleteColumn, Plain);

        await Assert.That(Describe(result.Document)).StartsWith("<b/d>");
        await Assert.That(Widths(result.Document)).IsEqualTo("2000");
    }

    [Test]
    public async Task TheLastRowDeleted_TakesTheTable()
    {
        var docx = DocumentEditor.EditTable(Grid(), 0, TableEdit.DeleteRow, Plain).Document;

        var result = DocumentEditor.EditTable(docx, 0, TableEdit.DeleteRow, Plain);

        await Assert.That(Describe(result.Document)).IsEqualTo("After¶");
        await Assert.That(result.Paragraph).IsEqualTo(0);
    }

    [Test]
    public async Task ATableDeleted_LeavesTheCaretAfterIt()
    {
        var result = DocumentEditor.EditTable(Grid(), 2, TableEdit.DeleteTable, Plain);

        await Assert.That(Describe(result.Document)).IsEqualTo("After¶");
        await Assert.That(result.Paragraph).IsEqualTo(0);
    }

    [Test]
    public async Task OutsideATable_IsRefused()
    {
        var docx = Grid();

        await Assert.That(() => DocumentEditor.EditTable(docx, 4, TableEdit.DeleteRow, Plain)).Throws<InvalidOperationException>();
    }

    // Tracked, a row added and a row deleted are each a change to accept or reject.

    [Test]
    public async Task ARowAdded_Tracked_IsARowInsertion_ThatRejectingTakesAway()
    {
        var result = DocumentEditor.EditTable(Grid(), 0, TableEdit.InsertRowBelow, Tracked);

        await Assert.That(DocumentReview.Read(result.Document).Changes.Select(_ => _.Kind)).Contains(ReviewChangeKind.RowInsertion);
        var rejected = ReviewEditor.ResolveAll(result.Document, accept: false);
        await Assert.That(Describe(rejected)).StartsWith("<a|b/c|d>");
    }

    [Test]
    public async Task ARowDeleted_Tracked_StaysUntilAccepted()
    {
        var result = DocumentEditor.EditTable(Grid(), 0, TableEdit.DeleteRow, Tracked);

        await Assert.That(DocumentReview.Read(result.Document).Changes.Select(_ => _.Kind)).Contains(ReviewChangeKind.RowDeletion);
        var accepted = ReviewEditor.ResolveAll(result.Document, accept: true);
        await Assert.That(Describe(accepted)).StartsWith("<c|d>");
        var rejected = ReviewEditor.ResolveAll(result.Document, accept: false);
        await Assert.That(Describe(rejected)).StartsWith("<a|b/c|d>");
    }
}
