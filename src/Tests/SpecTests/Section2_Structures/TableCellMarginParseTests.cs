using DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Covers <c>DocumentParser.ParseTableCellMargin</c> and <c>ParseCellMargin</c>.
/// Both must honor the Office 2010+ <c>&lt;w:start&gt;</c>/<c>&lt;w:end&gt;</c> form
/// in addition to the legacy <c>&lt;w:left&gt;</c>/<c>&lt;w:right&gt;</c> form —
/// otherwise horizontal padding from writers like Excelsior (which emit start/end)
/// silently reads as 0.
/// </summary>
public class TableCellMarginParseTests
{
    const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    [Test]
    public async Task TableCellMarginDefault_StartEnd_IsParsed()
    {
        var margin = new TableCellMarginDefault(
            $"""<w:tblCellMar {wNs}><w:top w:w="0" w:type="dxa"/><w:start w:w="108" w:type="dxa"/><w:bottom w:w="0" w:type="dxa"/><w:end w:w="108" w:type="dxa"/></w:tblCellMar>""");

        var result = DocumentParser.ParseTableCellMargin(margin);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Top).IsEqualTo(0);
        await Assert.That(result.Bottom).IsEqualTo(0);
        await Assert.That(result.Left).IsEqualTo(108 / 20d);
        await Assert.That(result.Right).IsEqualTo(108 / 20d);
    }

    [Test]
    public async Task TableCellMarginDefault_LeftRight_IsParsed()
    {
        var margin = new TableCellMarginDefault(
            $"""<w:tblCellMar {wNs}><w:top w:w="400" w:type="dxa"/><w:left w:w="200" w:type="dxa"/><w:bottom w:w="400" w:type="dxa"/><w:right w:w="200" w:type="dxa"/></w:tblCellMar>""");

        var result = DocumentParser.ParseTableCellMargin(margin);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Top).IsEqualTo(20);
        await Assert.That(result.Bottom).IsEqualTo(20);
        await Assert.That(result.Left).IsEqualTo(10);
        await Assert.That(result.Right).IsEqualTo(10);
    }

    [Test]
    public async Task TableCellMarginDefault_StartEndPreferredOverLeftRight()
    {
        var margin = new TableCellMarginDefault(
            $"""<w:tblCellMar {wNs}><w:start w:w="108" w:type="dxa"/><w:end w:w="108" w:type="dxa"/><w:left w:w="9999" w:type="dxa"/><w:right w:w="9999" w:type="dxa"/></w:tblCellMar>""");

        var result = DocumentParser.ParseTableCellMargin(margin);

        await Assert.That(result!.Left).IsEqualTo(108 / 20d);
        await Assert.That(result.Right).IsEqualTo(108 / 20d);
    }

    [Test]
    public async Task TableCellMarginDefault_Empty_ReturnsNull()
    {
        var margin = new TableCellMarginDefault($"""<w:tblCellMar {wNs}/>""");

        var result = DocumentParser.ParseTableCellMargin(margin);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TableCellMargin_StartEnd_IsParsed()
    {
        var margin = new TableCellMargin(
            $"""<w:tcMar {wNs}><w:top w:w="0" w:type="dxa"/><w:start w:w="108" w:type="dxa"/><w:bottom w:w="0" w:type="dxa"/><w:end w:w="108" w:type="dxa"/></w:tcMar>""");

        var result = DocumentParser.ParseCellMargin(margin);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Top).IsEqualTo(0);
        await Assert.That(result.Bottom).IsEqualTo(0);
        await Assert.That(result.Left).IsEqualTo(108 / 20d);
        await Assert.That(result.Right).IsEqualTo(108 / 20d);
    }

    [Test]
    public async Task TableCellMargin_LeftRight_IsParsed()
    {
        var margin = new TableCellMargin(
            $"""<w:tcMar {wNs}><w:top w:w="400" w:type="dxa"/><w:left w:w="200" w:type="dxa"/><w:bottom w:w="400" w:type="dxa"/><w:right w:w="200" w:type="dxa"/></w:tcMar>""");

        var result = DocumentParser.ParseCellMargin(margin);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Top).IsEqualTo(20);
        await Assert.That(result.Bottom).IsEqualTo(20);
        await Assert.That(result.Left).IsEqualTo(10);
        await Assert.That(result.Right).IsEqualTo(10);
    }

    [Test]
    public async Task TableCellMargin_StartEndPreferredOverLeftRight()
    {
        var margin = new TableCellMargin(
            $"""<w:tcMar {wNs}><w:start w:w="108" w:type="dxa"/><w:end w:w="108" w:type="dxa"/><w:left w:w="9999" w:type="dxa"/><w:right w:w="9999" w:type="dxa"/></w:tcMar>""");

        var result = DocumentParser.ParseCellMargin(margin);

        await Assert.That(result!.Left).IsEqualTo(108 / 20d);
        await Assert.That(result.Right).IsEqualTo(108 / 20d);
    }

    [Test]
    public async Task TableCellMargin_Empty_ReturnsNull()
    {
        var margin = new TableCellMargin($"""<w:tcMar {wNs}/>""");

        var result = DocumentParser.ParseCellMargin(margin);

        await Assert.That(result).IsNull();
    }
}
