/// <summary>
/// End-to-end scenario for the Office 2010+ <c>&lt;w:start&gt;</c>/<c>&lt;w:end&gt;</c>
/// form of table-default cell margins. Writers like Excelsior's WordTableRenderer
/// emit this form rather than the legacy <c>&lt;w:left&gt;</c>/<c>&lt;w:right&gt;</c>,
/// and <c>DocumentParser.ParseTableCellMargin</c> previously ignored it — so tables
/// silently rendered with zero horizontal cell padding.
/// </summary>
public class TableDefaultCellMarginStartEndScenarioTests
{
    [Test]
    public async Task DefaultCellPadding_StartEndForm_IsParsedFromDocx()
    {
        var parser = new DocumentParser();
        var path = Path.Combine(
            ProjectFiles.ProjectDirectory,
            "Inputs",
            "table_default_cell_margin_start_end",
            "input.docx");
        using var stream = File.OpenRead(path);

        var doc = parser.Parse(stream);

        var table = doc.Elements.OfType<TableElement>().Single();
        var padding = table.Properties.DefaultCellPadding;

        // <w:start w:w="108"/> and <w:end w:w="108"/> → 108 dxa / 20 twips-per-point = 5.4 pt
        await Assert.That(padding.Left).IsEqualTo(5.4);
        await Assert.That(padding.Right).IsEqualTo(5.4);
        await Assert.That(padding.Top).IsEqualTo(0);
        await Assert.That(padding.Bottom).IsEqualTo(0);
    }
}
