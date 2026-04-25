/// <summary>
/// Tests for the table / row / cell capture fields added during the recent pass:
/// w:tblHeader, w:tblLayout/@type, w:textDirection.
/// </summary>
public class TableCaptureTests
{
    [Test]
    public async Task TableProperties_DefaultIsAutoFit()
    {
        var props = new TableProperties();
        await Assert.That(props.IsAutoFit).IsTrue();
    }

    [Test]
    public async Task TableRow_DefaultIsHeader_IsFalse()
    {
        var row = new TableRow { Cells = [] };
        await Assert.That(row.IsHeader).IsFalse();
    }

    [Test]
    public async Task TableCellProperties_DefaultTextDirection_IsLeftToRight()
    {
        var props = new TableCellProperties();
        await Assert.That(props.TextDirection).IsEqualTo(CellTextDirection.LeftToRight);
    }

    [Test]
    public async Task DocumentParser_ParsesFixedTableLayout()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "feature_capture", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var table = doc.Elements.OfType<TableElement>().Single();
        await Assert.That(table.Properties.IsAutoFit).IsFalse();
    }

    [Test]
    public async Task DocumentParser_ParsesHeaderRow()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "feature_capture", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var table = doc.Elements.OfType<TableElement>().Single();
        await Assert.That(table.Rows[0].IsHeader).IsTrue();
        await Assert.That(table.Rows[1].IsHeader).IsFalse();
    }

    [Test]
    public async Task DocumentParser_ParsesCellTextDirection()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "feature_capture", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var table = doc.Elements.OfType<TableElement>().Single();
        await Assert.That(table.Rows[0].Cells[0].Properties.TextDirection).IsEqualTo(CellTextDirection.BottomToTop);
        await Assert.That(table.Rows[0].Cells[1].Properties.TextDirection).IsEqualTo(CellTextDirection.LeftToRight);
    }
}
