using System.Text.RegularExpressions;
using static EditDocuments;
using static ReviewDocuments;

// A table's borders, its cells' shading, cells merged and split, and its columns' widths — each
// against the markup Word wrote for the same operation on the same table (Word-probed 2026-10-19,
// see DocumentEditor.TableFormat.cs). The table is Word's 3x3 Table Grid table, each cell holding
// "r<row>c<column>", so the paragraphs r1c1 to r3c3 are ordinals 0 to 8.
public partial class TableFormatTests
{
    static readonly string tableGrid = BuiltInStyles.Definition("TableGrid")!.OuterXml;

    static readonly int[] columns = [3008, 3009, 3009];

    static byte[] Table()
    {
        var builder = new StringBuilder("<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblLayout w:type=\"fixed\"/></w:tblPr><w:tblGrid>");
        foreach (var width in columns)
        {
            builder.Append($"<w:gridCol w:w=\"{width}\"/>");
        }

        builder.Append("</w:tblGrid>");
        for (var row = 1; row <= 3; row++)
        {
            builder.Append("<w:tr>");
            for (var column = 1; column <= 3; column++)
            {
                builder.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{columns[column - 1]}\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>r{row}c{column}</w:t></w:r></w:p></w:tc>");
            }

            builder.Append("</w:tr>");
        }

        builder.Append("</w:tbl><w:p/>");
        return Build(builder.ToString(), styles: tableGrid);
    }

    // The table's markup with each paragraph shown as <p:its text>.
    static string Compact(byte[] docx)
    {
        var xml = Xml(docx);
        var table = xml[..(xml.IndexOf("</w:tbl>", StringComparison.Ordinal) + 8)];
        return Paragraph().Replace(table, _ => $"<p:{string.Concat(Text().Matches(_.Value).Select(match => match.Groups[1].Value))}>");
    }

    [GeneratedRegex("<w:p>.*?</w:p>|<w:p />")]
    private static partial Regex Paragraph();

    [GeneratedRegex("<w:t[^>]*>([^<]*)</w:t>")]
    private static partial Regex Text();

    static string Rows(byte[] docx) =>
        string.Join(" / ", Regex.Matches(Compact(docx), "<w:tr>.*?</w:tr>").Select(_ => string.Concat(Regex.Matches(_.Value, "<p:([^>]*)>").Select(paragraph => $"[{paragraph.Groups[1].Value}]"))));

    // Borders

    [Test]
    public async Task NoBorders_WritesEverySideNone()
    {
        var edited = DocumentEditor.SetTableBorders(Table(), 0, BorderPreset.None, 4, Plain);

        await Assert.That(Compact(edited)).Contains(
            "<w:tblBorders><w:top w:val=\"none\" w:color=\"auto\" w:sz=\"0\" w:space=\"0\" /><w:left w:val=\"none\" w:color=\"auto\" w:sz=\"0\" w:space=\"0\" />" +
            "<w:bottom w:val=\"none\" w:color=\"auto\" w:sz=\"0\" w:space=\"0\" /><w:right w:val=\"none\" w:color=\"auto\" w:sz=\"0\" w:space=\"0\" />" +
            "<w:insideH w:val=\"none\" w:color=\"auto\" w:sz=\"0\" w:space=\"0\" /><w:insideV w:val=\"none\" w:color=\"auto\" w:sz=\"0\" w:space=\"0\" /></w:tblBorders>");
    }

    // What Table Grid draws already is not written again.
    [Test]
    public async Task AllBorders_OnTableGrid_WritesNothing()
    {
        var none = DocumentEditor.SetTableBorders(Table(), 0, BorderPreset.None, 4, Plain);

        var all = DocumentEditor.SetTableBorders(none, 0, BorderPreset.All, 4, Plain);

        await Assert.That(Compact(all)).DoesNotContain("tblBorders");
    }

    [Test]
    public async Task OutsideBorders_TakeTheInsideOnesAway()
    {
        var edited = DocumentEditor.SetTableBorders(Table(), 4, BorderPreset.Outside, 4, Plain);

        await Assert.That(Compact(edited)).Contains(
            "<w:tblBorders><w:insideH w:val=\"none\" w:color=\"auto\" w:sz=\"0\" w:space=\"0\" /><w:insideV w:val=\"none\" w:color=\"auto\" w:sz=\"0\" w:space=\"0\" /></w:tblBorders>");
    }

    [Test]
    public async Task OutsideBorders_Wider_AreWrittenAtTheirWidth()
    {
        var edited = DocumentEditor.SetTableBorders(Table(), 0, BorderPreset.Outside, 18, Plain);

        await Assert.That(Compact(edited)).Contains(
            "<w:tblBorders><w:top w:val=\"single\" w:color=\"auto\" w:sz=\"18\" w:space=\"0\" /><w:left w:val=\"single\" w:color=\"auto\" w:sz=\"18\" w:space=\"0\" />" +
            "<w:bottom w:val=\"single\" w:color=\"auto\" w:sz=\"18\" w:space=\"0\" /><w:right w:val=\"single\" w:color=\"auto\" w:sz=\"18\" w:space=\"0\" />");
    }

    // Shading

    [Test]
    public async Task Shading_IsTheCells_AndAutomaticTakesItAway()
    {
        var shaded = DocumentEditor.ShadeCells(Table(), 0, 0, "ffff00", Plain);
        var cleared = DocumentEditor.ShadeCells(shaded, 0, 0, null, Plain);

        await Assert.That(Compact(shaded)).Contains("<w:tcPr><w:tcW w:w=\"3008\" w:type=\"dxa\" /><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"FFFF00\" /></w:tcPr><p:r1c1>");
        await Assert.That(Regex.Matches(Compact(shaded), "w:shd").Count).IsEqualTo(1);
        await Assert.That(Compact(cleared)).DoesNotContain("w:shd");
    }

    // From one cell to another, the cells between them as a rectangle, as Word selects them.
    [Test]
    public async Task Shading_FromOneCellToAnother_IsTheRectangleBetween()
    {
        var shaded = DocumentEditor.ShadeCells(Table(), 1, 5, "D9D9D9", Plain);

        await Assert.That(Regex.Matches(Compact(shaded), "w:fill=\"D9D9D9\"").Count).IsEqualTo(4);
    }

    // Merging

    [Test]
    public async Task Merged_AcrossARow_IsOneCellSpanningTheColumns_HoldingBoth()
    {
        var merged = DocumentEditor.MergeCells(Table(), 0, 1, Plain);

        await Assert.That(Compact(merged.Document)).Contains("<w:tr><w:tc><w:tcPr><w:tcW w:w=\"6017\" w:type=\"dxa\" /><w:gridSpan w:val=\"2\" /></w:tcPr><p:r1c1><p:r1c2></w:tc><w:tc>");
        await Assert.That(Rows(merged.Document)).IsEqualTo("[r1c1][r1c2][r1c3] / [r2c1][r2c2][r2c3] / [r3c1][r3c2][r3c3]");
        await Assert.That((merged.Paragraph, merged.Offset)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task Merged_DownAColumn_IsTheTopCell_TheOnesBelowMergedIntoIt()
    {
        var merged = DocumentEditor.MergeCells(Table(), 3, 6, Plain);

        await Assert.That(Compact(merged.Document)).Contains("<w:tc><w:tcPr><w:tcW w:w=\"3008\" w:type=\"dxa\" /><w:vMerge w:val=\"restart\" /></w:tcPr><p:r2c1><p:r3c1></w:tc>");
        await Assert.That(Compact(merged.Document)).Contains("<w:tc><w:tcPr><w:tcW w:w=\"3008\" w:type=\"dxa\" /><w:vMerge /></w:tcPr><p:></w:tc>");
    }

    [Test]
    public async Task OneCell_CannotBeMerged() =>
        await Assert.That(() => DocumentEditor.MergeCells(Table(), 4, 4, Plain)).Throws<InvalidOperationException>();

    // Splitting

    [Test]
    public async Task AMergedCell_SplitAgain_IsTheCellsItWas_AParagraphToEach()
    {
        var merged = DocumentEditor.MergeCells(Table(), 0, 1, Plain).Document;

        var split = DocumentEditor.SplitCell(merged, 0, 2, 1, Plain).Document;

        await Assert.That(Compact(split)).StartsWith(Compact(Table()).Split("<w:tr>")[0]);
        await Assert.That(Compact(split)).Contains("<w:tr><w:tc><w:tcPr><w:tcW w:w=\"3008\" w:type=\"dxa\" /></w:tcPr><p:r1c1></w:tc><w:tc><w:tcPr><w:tcW w:w=\"3009\" w:type=\"dxa\" /></w:tcPr><p:r1c2></w:tc>");
    }

    [Test]
    public async Task ACell_SplitIntoColumns_DividesItsGridColumn()
    {
        var split = DocumentEditor.SplitCell(Table(), 4, 2, 1, Plain).Document;

        var compact = Compact(split);
        await Assert.That(compact).Contains("<w:tblGrid><w:gridCol w:w=\"3008\" /><w:gridCol w:w=\"1504\" /><w:gridCol w:w=\"1505\" /><w:gridCol w:w=\"3009\" /></w:tblGrid>");
        await Assert.That(compact).Contains("<w:tc><w:tcPr><w:tcW w:w=\"3009\" w:type=\"dxa\" /><w:gridSpan w:val=\"2\" /></w:tcPr><p:r1c2></w:tc>");
        await Assert.That(compact).Contains("<w:tc><w:tcPr><w:tcW w:w=\"1504\" w:type=\"dxa\" /></w:tcPr><p:r2c2></w:tc><w:tc><w:tcPr><w:tcW w:w=\"1505\" w:type=\"dxa\" /></w:tcPr><p:></w:tc>");
        await Assert.That(compact).Contains("<w:tc><w:tcPr><w:tcW w:w=\"3009\" w:type=\"dxa\" /><w:gridSpan w:val=\"2\" /></w:tcPr><p:r3c2></w:tc>");
    }

    [Test]
    public async Task ACell_SplitIntoRows_GainsARow_TheRowsOtherCellsMergedDownIntoIt()
    {
        var split = DocumentEditor.SplitCell(Table(), 4, 1, 2, Plain).Document;

        await Assert.That(Rows(split)).IsEqualTo("[r1c1][r1c2][r1c3] / [r2c1][r2c2][r2c3] / [][][] / [r3c1][r3c2][r3c3]");
        var compact = Compact(split);
        await Assert.That(compact).Contains("<w:tc><w:tcPr><w:tcW w:w=\"3008\" w:type=\"dxa\" /><w:vMerge w:val=\"restart\" /></w:tcPr><p:r2c1></w:tc>");
        await Assert.That(compact).Contains("<w:tr><w:tc><w:tcPr><w:tcW w:w=\"3008\" w:type=\"dxa\" /><w:vMerge /></w:tcPr><p:></w:tc><w:tc><w:tcPr><w:tcW w:w=\"3009\" w:type=\"dxa\" /></w:tcPr><p:></w:tc><w:tc><w:tcPr><w:tcW w:w=\"3009\" w:type=\"dxa\" /><w:vMerge /></w:tcPr><p:></w:tc></w:tr>");
    }

    [Test]
    public async Task CellsMergedDown_SplitIntoRows_AreTheCellsTheyWere()
    {
        var merged = DocumentEditor.MergeCells(Table(), 3, 6, Plain).Document;

        var split = DocumentEditor.SplitCell(merged, 3, 1, 2, Plain).Document;

        await Assert.That(Compact(split)).DoesNotContain("vMerge");
        await Assert.That(Rows(split)).IsEqualTo("[r1c1][r1c2][r1c3] / [r2c1][r2c2][r2c3] / [r3c1][r3c2][r3c3]");
    }

    // Widths

    [Test]
    public async Task AColumnsWidth_IsItsGridColumns_AndItsCells()
    {
        var edited = DocumentEditor.SetColumnWidth(Table(), 1, 2880, Plain);

        var compact = Compact(edited);
        await Assert.That(compact).Contains("<w:tblGrid><w:gridCol w:w=\"3008\" /><w:gridCol w:w=\"2880\" /><w:gridCol w:w=\"3009\" /></w:tblGrid>");
        await Assert.That(Regex.Matches(compact, "<w:tcW w:w=\"2880\" w:type=\"dxa\" />").Count).IsEqualTo(3);
    }

    [Test]
    public async Task Distributed_TheColumnsShareTheWidthAlike()
    {
        var narrowed = DocumentEditor.SetColumnWidth(Table(), 1, 2880, Plain);

        var edited = DocumentEditor.DistributeColumns(narrowed, 0, Plain);

        var compact = Compact(edited);
        await Assert.That(compact).Contains("<w:tblGrid><w:gridCol w:w=\"2965\" /><w:gridCol w:w=\"2966\" /><w:gridCol w:w=\"2966\" /></w:tblGrid>");
        await Assert.That(compact).Contains("<w:tr><w:tc><w:tcPr><w:tcW w:w=\"2965\" w:type=\"dxa\" /></w:tcPr><p:r1c1></w:tc><w:tc><w:tcPr><w:tcW w:w=\"2966\" w:type=\"dxa\" />");
    }

    [Test]
    public async Task AutoFit_ToContents_MakesEveryWidthAutomatic()
    {
        var edited = DocumentEditor.FitTable(Table(), 0, TableFit.Contents, Plain);

        var compact = Compact(edited);
        await Assert.That(compact).DoesNotContain("tblLayout");
        await Assert.That(Regex.Matches(compact, "<w:tcW w:w=\"0\" w:type=\"auto\" />").Count).IsEqualTo(9);
    }

    [Test]
    public async Task AutoFit_ToTheWindow_IsTheTextsWidth_EachCellItsShare()
    {
        var distributed = DocumentEditor.DistributeColumns(DocumentEditor.SetColumnWidth(Table(), 1, 2880, Plain), 0, Plain);

        var edited = DocumentEditor.FitTable(distributed, 0, TableFit.Window, Plain);

        var compact = Compact(edited);
        await Assert.That(compact).Contains("<w:tblW w:w=\"5000\" w:type=\"pct\" />");
        await Assert.That(compact).DoesNotContain("tblLayout");
        await Assert.That(compact).Contains("<w:tr><w:tc><w:tcPr><w:tcW w:w=\"1666\" w:type=\"pct\" /></w:tcPr><p:r1c1></w:tc><w:tc><w:tcPr><w:tcW w:w=\"1667\" w:type=\"pct\" /></w:tcPr><p:r1c2></w:tc><w:tc><w:tcPr><w:tcW w:w=\"1667\" w:type=\"pct\" /></w:tcPr><p:r1c3></w:tc></w:tr>");
    }

    [Test]
    public async Task AutoFit_Fixed_PutsTheLayoutAndTheWidthsBack()
    {
        var window = DocumentEditor.FitTable(Table(), 0, TableFit.Window, Plain);

        var edited = DocumentEditor.FitTable(window, 0, TableFit.Fixed, Plain);

        var compact = Compact(edited);
        await Assert.That(compact).Contains("<w:tblW w:w=\"0\" w:type=\"auto\" /><w:tblLayout w:type=\"fixed\" />");
        await Assert.That(compact).Contains("<w:tcW w:w=\"3008\" w:type=\"dxa\" />");
    }

    // Outside a table, there is no table to format.
    [Test]
    public async Task OutsideATable_SaysSo() =>
        await Assert.That(() => DocumentEditor.SetTableBorders(Table(), 9, BorderPreset.All, 4, Plain)).Throws<InvalidOperationException>();
}
