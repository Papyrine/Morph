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

    // The width can be a bare twips count (the dxa form Word writes) or an ST_UniversalMeasure value
    // carrying an explicit unit — Aspose emits e.g. "0pt" for cell margins. TableWidthToPoints handles
    // both; the unit-bearing forms are exercised here.

    [Test]
    public async Task TableCellMargin_ZeroPointUnit_IsZero()
    {
        var margin = new TableCellMargin(
            $"""<w:tcMar {wNs}><w:top w:w="0pt" w:type="dxa"/><w:start w:w="0pt" w:type="dxa"/><w:bottom w:w="0pt" w:type="dxa"/><w:end w:w="0pt" w:type="dxa"/></w:tcMar>""");

        var result = DocumentParser.ParseCellMargin(margin);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Top).IsEqualTo(0);
        await Assert.That(result.Left).IsEqualTo(0);
        await Assert.That(result.Right).IsEqualTo(0);
        await Assert.That(result.Bottom).IsEqualTo(0);
    }

    [Test]
    public async Task TableCellMargin_UniversalMeasureUnits_ConvertToPoints()
    {
        var margin = new TableCellMargin(
            $"""<w:tcMar {wNs}><w:top w:w="18pt" w:type="dxa"/><w:start w:w="1in" w:type="dxa"/><w:bottom w:w="1pc" w:type="dxa"/><w:end w:w="72pt" w:type="dxa"/></w:tcMar>""");

        var result = DocumentParser.ParseCellMargin(margin);

        await Assert.That(result!.Top).IsEqualTo(18);   // 18pt
        await Assert.That(result.Left).IsEqualTo(72);   // 1in = 72pt
        await Assert.That(result.Bottom).IsEqualTo(12); // 1pc = 12pt
        await Assert.That(result.Right).IsEqualTo(72);  // 72pt
    }

    [Test]
    public async Task TableCellMargin_CentimetreUnit_ConvertsToPoints()
    {
        var margin = new TableCellMargin(
            $"""<w:tcMar {wNs}><w:start w:w="1cm" w:type="dxa"/><w:end w:w="1cm" w:type="dxa"/></w:tcMar>""");

        var result = DocumentParser.ParseCellMargin(margin);

        await Assert.That(result!.Left).IsEqualTo(1 * 72 / 2.54).Within(1e-9);
        await Assert.That(result.Right).IsEqualTo(1 * 72 / 2.54).Within(1e-9);
    }

    [Test]
    public async Task TableCellMargin_PercentValue_IsZero()
    {
        var margin = new TableCellMargin(
            $"""<w:tcMar {wNs}><w:top w:w="50%" w:type="pct"/></w:tcMar>""");

        var result = DocumentParser.ParseCellMargin(margin);

        await Assert.That(result!.Top).IsEqualTo(0);
    }

    // A cell-level w:tcMar overrides the table's w:tblCellMar per side: sides PRESENT in the
    // override win, sides ABSENT inherit the table default rather than collapsing to zero. This is
    // the business-plans/04 cover-template pattern — a vAlign=bottom heading cell zeroing only its
    // bottom margin while relying on the inherited 14.4pt top margin to open a gap above the heading.
    [Test]
    public async Task TableCellMargin_PartialOverride_InheritsAbsentSidesFromTableDefault()
    {
        var margin = new TableCellMargin(
            $"""<w:tcMar {wNs}><w:bottom w:w="0" w:type="dxa"/></w:tcMar>""");
        var tableDefault = new CellSpacing(top: 14.4, right: 57.6, bottom: 14.4, left: 18);

        var result = DocumentParser.ParseCellMargin(margin, tableDefault);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Bottom).IsEqualTo(0);    // overridden
        await Assert.That(result.Top).IsEqualTo(14.4);     // inherited
        await Assert.That(result.Left).IsEqualTo(18);      // inherited
        await Assert.That(result.Right).IsEqualTo(57.6);   // inherited
    }

    // Without a table default the absent sides stay zero — the pre-fix behaviour, so tables that
    // never set w:tblCellMar are untouched (the inheritance source is CellSpacing(0) there).
    [Test]
    public async Task TableCellMargin_PartialOverride_NoTableDefault_AbsentSidesZero()
    {
        var margin = new TableCellMargin(
            $"""<w:tcMar {wNs}><w:bottom w:w="240" w:type="dxa"/></w:tcMar>""");

        var result = DocumentParser.ParseCellMargin(margin, tableDefault: null);

        await Assert.That(result!.Bottom).IsEqualTo(12);
        await Assert.That(result.Top).IsEqualTo(0);
        await Assert.That(result.Left).IsEqualTo(0);
        await Assert.That(result.Right).IsEqualTo(0);
    }

    // A full four-side override is unaffected by inheritance — every side is present, so the table
    // default never shows through.
    [Test]
    public async Task TableCellMargin_FullOverride_IgnoresTableDefault()
    {
        var margin = new TableCellMargin(
            $"""<w:tcMar {wNs}><w:top w:w="100" w:type="dxa"/><w:start w:w="200" w:type="dxa"/><w:bottom w:w="300" w:type="dxa"/><w:end w:w="400" w:type="dxa"/></w:tcMar>""");
        var tableDefault = new CellSpacing(top: 99, right: 99, bottom: 99, left: 99);

        var result = DocumentParser.ParseCellMargin(margin, tableDefault);

        await Assert.That(result!.Top).IsEqualTo(5);
        await Assert.That(result.Left).IsEqualTo(10);
        await Assert.That(result.Bottom).IsEqualTo(15);
        await Assert.That(result.Right).IsEqualTo(20);
    }

    // End-to-end: business-plans/04 is a cover template whose heading cells carry a partial override
    // <w:tcMar><w:bottom w:w="0"/></w:tcMar> against a table w:tblCellMar of 288/360/288/1152 dxa
    // (14.4 / 18 / 14.4 / 57.6 pt). The absent top/left/right must inherit the table default; only
    // bottom is zeroed. This guards the call site that threads the table default into ParseCellMargin
    // — the parser unit tests above pass the default explicitly, so they would not catch a regression
    // there. Before the fix the whole override collapsed to zero, dropping the 14.4pt top margin and
    // crowding the vAlign=bottom heading against the row above (heading row 16pt vs Word's ~31.8pt).
    [Test]
    public async Task PartialCellMargin_InheritsTableDefault_EndToEnd()
    {
        var parser = new DocumentParser();
        await using var stream = File.OpenRead(
            Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "business-plans", "04", "input.docx"));
        var document = parser.Parse(stream);

        var headingCell = document.Elements
            .OfType<TableElement>()
            .SelectMany(_ => _.Rows)
            .SelectMany(_ => _.Cells)
            .First(_ => _.Content.OfType<ParagraphElement>()
                .Any(para => string.Concat(para.Runs.Select(run => run.Text))
                    .Contains("problem statement", StringComparison.OrdinalIgnoreCase)));

        await Assert.That(headingCell.Properties.Padding).IsNotNull();
        await Assert.That(headingCell.Properties.Padding!.Top).IsEqualTo(14.4);  // inherited
        await Assert.That(headingCell.Properties.Padding.Bottom).IsEqualTo(0);   // overridden
        await Assert.That(headingCell.Properties.Padding.Left).IsEqualTo(18);    // inherited
        await Assert.That(headingCell.Properties.Padding.Right).IsEqualTo(57.6); // inherited
    }
}
