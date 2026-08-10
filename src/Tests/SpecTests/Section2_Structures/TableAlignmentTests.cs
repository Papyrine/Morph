/// <summary>
/// Tests for w:tblPr/w:jc parsing.
/// </summary>
public class TableAlignmentTests
{
    [Test]
    public async Task TableProperties_DefaultAlignment_IsLeft()
    {
        var props = new TableProperties();
        await Assert.That(props.Alignment).IsEqualTo(TextAlignment.Left);
    }

    [Test]
    public async Task DocumentParser_ParsesTableAlignment()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "table_alignment", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var tables = doc.Elements.OfType<TableElement>().ToList();
        await Assert.That(tables.Count).IsEqualTo(3);
        await Assert.That(tables[0].Properties.Alignment).IsEqualTo(TextAlignment.Left);
        await Assert.That(tables[1].Properties.Alignment).IsEqualTo(TextAlignment.Center);
        await Assert.That(tables[2].Properties.Alignment).IsEqualTo(TextAlignment.Right);
    }
}
