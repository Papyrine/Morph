using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
// Morph has its own TableRow, TableCell, TableProperties and Run in scope here, so the OOXML
// ones are qualified through this alias where the names collide.
using W = DocumentFormat.OpenXml.Wordprocessing;

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

        // 18pt
        await Assert.That(result!.Top).IsEqualTo(18);
        // 1in = 72pt
        await Assert.That(result.Left).IsEqualTo(72);
        // 1pc = 12pt
        await Assert.That(result.Bottom).IsEqualTo(12);
        // 72pt
        await Assert.That(result.Right).IsEqualTo(72);
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
        // overridden
        await Assert.That(result!.Bottom).IsEqualTo(0);
        // inherited
        await Assert.That(result.Top).IsEqualTo(14.4);
        // inherited
        await Assert.That(result.Left).IsEqualTo(18);
        // inherited
        await Assert.That(result.Right).IsEqualTo(57.6);
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
            Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "business-plans", "04", "input.docx"));
        var document = parser.Parse(stream);

        var headingCell = document.Elements
            .OfType<TableElement>()
            .SelectMany(_ => _.Rows)
            .SelectMany(_ => _.Cells)
            .First(_ => _.Content.OfType<ParagraphElement>()
                .Any(para => string.Concat(para.Runs.Select(run => run.Text))
                    .Contains("problem statement", StringComparison.OrdinalIgnoreCase)));

        await Assert.That(headingCell.Properties.Padding).IsNotNull();
        // inherited
        await Assert.That(headingCell.Properties.Padding!.Top).IsEqualTo(14.4);
        // overridden
        await Assert.That(headingCell.Properties.Padding.Bottom).IsEqualTo(0);
        // inherited
        await Assert.That(headingCell.Properties.Padding.Left).IsEqualTo(18);
        // inherited
        await Assert.That(headingCell.Properties.Padding.Right).IsEqualTo(57.6);
    }

    static Style TableStyle(string styleId, string? basedOn, string? cellMargin)
    {
        var basedOnXml = basedOn == null ? "" : $"""<w:basedOn w:val="{basedOn}"/>""";
        var tblPr = cellMargin == null ? "" : $"<w:tblPr>{cellMargin}</w:tblPr>";
        return new($"""<w:style {wNs} w:type="table" w:styleId="{styleId}">{basedOnXml}{tblPr}</w:style>""");
    }

    // A table style's w:tblCellMar merges with its w:basedOn ancestor's PER SIDE. Real templates
    // rely on it: a house style that sets only top/bottom over TableNormal's left/right (108 dxa)
    // is the common shape, and taking the first w:tblCellMar found whole zeroed the horizontal
    // padding — cell text rendered flush against the column rules.
    [Test]
    public async Task ResolveStyleCellPadding_PartialStyle_InheritsAbsentSidesFromBasedOn()
    {
        var normal = TableStyle("TableNormal", null,
            $"""<w:tblCellMar {wNs}><w:top w:w="0" w:type="dxa"/><w:left w:w="108" w:type="dxa"/><w:bottom w:w="0" w:type="dxa"/><w:right w:w="108" w:type="dxa"/></w:tblCellMar>""");
        var linedRows = TableStyle("LinedRows", "TableNormal",
            $"""<w:tblCellMar {wNs}><w:top w:w="57" w:type="dxa"/><w:bottom w:w="57" w:type="dxa"/></w:tblCellMar>""");

        var result = DocumentParser.ResolveStyleCellPadding(linedRows,
            new() { ["TableNormal"] = normal, ["LinedRows"] = linedRows });

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Top).IsEqualTo(57 / 20d);
        await Assert.That(result.Bottom).IsEqualTo(57 / 20d);
        // inherited from TableNormal
        await Assert.That(result.Left).IsEqualTo(108 / 20d);
        // inherited from TableNormal
        await Assert.That(result.Right).IsEqualTo(108 / 20d);
    }

    // The merge spans the whole basedOn chain, and a style with no w:tblCellMar of its own is
    // transparent. This is the shape that reported the bug: PMClinedcolumns -> PMClinedrows
    // (top/bottom only) -> TableNormal (left/right only).
    [Test]
    public async Task ResolveStyleCellPadding_MergesAcrossMultiLevelChain()
    {
        var normal = TableStyle("TableNormal", null,
            $"""<w:tblCellMar {wNs}><w:left w:w="108" w:type="dxa"/><w:right w:w="108" w:type="dxa"/></w:tblCellMar>""");
        var linedRows = TableStyle("LinedRows", "TableNormal",
            $"""<w:tblCellMar {wNs}><w:top w:w="57" w:type="dxa"/><w:bottom w:w="57" w:type="dxa"/></w:tblCellMar>""");
        var linedColumns = TableStyle("LinedColumns", "LinedRows", cellMargin: null);

        var result = DocumentParser.ResolveStyleCellPadding(linedColumns,
            new()
            {
                ["TableNormal"] = normal,
                ["LinedRows"] = linedRows,
                ["LinedColumns"] = linedColumns
            });

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Top).IsEqualTo(57 / 20d);
        await Assert.That(result.Bottom).IsEqualTo(57 / 20d);
        await Assert.That(result.Left).IsEqualTo(108 / 20d);
        await Assert.That(result.Right).IsEqualTo(108 / 20d);
    }

    // Nearest ancestor wins per side — a side the derived style states is never overwritten by
    // the base, including when it explicitly states zero.
    [Test]
    public async Task ResolveStyleCellPadding_DerivedSideWinsOverBasedOn()
    {
        var normal = TableStyle("TableNormal", null,
            $"""<w:tblCellMar {wNs}><w:top w:w="200" w:type="dxa"/><w:left w:w="108" w:type="dxa"/></w:tblCellMar>""");
        var derived = TableStyle("Derived", "TableNormal",
            $"""<w:tblCellMar {wNs}><w:top w:w="57" w:type="dxa"/><w:left w:w="0" w:type="dxa"/></w:tblCellMar>""");

        var result = DocumentParser.ResolveStyleCellPadding(derived,
            new() { ["TableNormal"] = normal, ["Derived"] = derived });

        await Assert.That(result!.Top).IsEqualTo(57 / 20d);
        await Assert.That(result.Left).IsEqualTo(0);
    }

    // No w:tblCellMar anywhere in the chain stays null, so the table falls back to its own
    // w:tblPr default rather than adopting a zero box from the style.
    [Test]
    public async Task ResolveStyleCellPadding_NoCellMarginInChain_ReturnsNull()
    {
        var normal = TableStyle("TableNormal", null, cellMargin: null);
        var derived = TableStyle("Derived", "TableNormal", cellMargin: null);

        var result = DocumentParser.ResolveStyleCellPadding(derived,
            new() { ["TableNormal"] = normal, ["Derived"] = derived });

        await Assert.That(result).IsNull();
    }

    // The same per-side merge applies one level up: a document's own w:tblPr/w:tblCellMar merges
    // with the table style's resolved padding rather than replacing it. Settled with a Word probe
    // (see docs/word-features.md, Cell Padding) — a table stating only top/bottom rendered at the
    // style's 108-twip horizontal padding, identical to the same table with no w:tblCellMar.
    [Test]
    public async Task MergeTableCellMargin_PartialTableLevel_InheritsAbsentSidesFromStyle()
    {
        var margin = new TableCellMarginDefault(
            $"""<w:tblCellMar {wNs}><w:top w:w="400" w:type="dxa"/><w:bottom w:w="400" w:type="dxa"/></w:tblCellMar>""");
        var stylePadding = new CellSpacing(top: 2.85, right: 5.4, bottom: 2.85, left: 5.4);

        var result = DocumentParser.MergeTableCellMargin(margin, stylePadding);

        await Assert.That(result!.Top).IsEqualTo(20);
        await Assert.That(result.Bottom).IsEqualTo(20);
        // inherited from the style
        await Assert.That(result.Left).IsEqualTo(5.4);
        // inherited from the style
        await Assert.That(result.Right).IsEqualTo(5.4);
    }

    // An explicitly stated zero is a real value, not an absence — Word renders such a table flush
    // against the rules even when the style supplies horizontal padding (probe variant T3).
    [Test]
    public async Task MergeTableCellMargin_ExplicitZero_OverridesStyle()
    {
        var margin = new TableCellMarginDefault(
            $"""<w:tblCellMar {wNs}><w:left w:w="0" w:type="dxa"/><w:right w:w="0" w:type="dxa"/></w:tblCellMar>""");
        var stylePadding = new CellSpacing(top: 2.85, right: 5.4, bottom: 2.85, left: 5.4);

        var result = DocumentParser.MergeTableCellMargin(margin, stylePadding);

        await Assert.That(result!.Left).IsEqualTo(0);
        await Assert.That(result.Right).IsEqualTo(0);
        await Assert.That(result.Top).IsEqualTo(2.85);
        await Assert.That(result.Bottom).IsEqualTo(2.85);
    }

    // End-to-end guard for the w:tblPrEx wiring. The corpus cannot cover this: newsletters/09 is
    // the only scenario whose w:tblPrEx carries a w:tblCellMar, and its enclosing table resolves to
    // an all-zero default, so merging and replacing produce the same numbers there. Only a fixture
    // with a NON-ZERO table default tells the two apart, hence this in-memory document.
    //
    // Word's behaviour was settled with a probe (docs/word-features.md, Cell Padding): over a table
    // default of 360 twips, a row whose w:tblPrEx stated only top/bottom rendered at 360 twips
    // horizontally — identical to a row with no w:tblPrEx, and visibly taller, so the element was
    // honoured. Replacing wholesale would zero the horizontal sides.
    [Test]
    public async Task RowOverrideCellMargin_PartialTblPrEx_InheritsTableDefault_EndToEnd()
    {
        using var stream = BuildTblPrExDocument();
        var document = new DocumentParser().Parse(stream);

        var table = document.Elements.OfType<TableElement>().Single();
        var plainRow = table.Rows[0];
        var overrideRow = table.Rows[1];

        // the row without w:tblPrEx takes the table default wholesale
        await Assert.That(plainRow.OverrideCellPadding).IsNull();
        await Assert.That(table.Properties.DefaultCellPadding.Left).IsEqualTo(18);

        await Assert.That(overrideRow.OverrideCellPadding).IsNotNull();
        // stated in the w:tblPrEx
        await Assert.That(overrideRow.OverrideCellPadding!.Top).IsEqualTo(25);
        await Assert.That(overrideRow.OverrideCellPadding.Bottom).IsEqualTo(25);
        // absent -> inherited from the table default, NOT zeroed
        await Assert.That(overrideRow.OverrideCellPadding.Left).IsEqualTo(18);
        await Assert.That(overrideRow.OverrideCellPadding.Right).IsEqualTo(18);
    }

    // Two rows of one table: the first plain, the second carrying a w:tblPrEx that states only
    // top/bottom. The table default is 200/360/200/360 dxa (10 / 18 / 10 / 18 pt) so the merged
    // and replaced results differ on the horizontal sides.
    static MemoryStream BuildTblPrExDocument()
    {
        static W.TableCell Cell() => new(new W.Paragraph(new W.Run(new Text("x"))));

        var table = new Table(
            new W.TableProperties(
                new TableCellMarginDefault(
                    new TopMargin { Width = "200", Type = TableWidthUnitValues.Dxa },
                    new TableCellLeftMargin { Width = 360, Type = TableWidthValues.Dxa },
                    new BottomMargin { Width = "200", Type = TableWidthUnitValues.Dxa },
                    new TableCellRightMargin { Width = 360, Type = TableWidthValues.Dxa })),
            new W.TableRow(Cell(), Cell()),
            new W.TableRow(
                new TablePropertyExceptions(
                    new TableCellMarginDefault(
                        new TopMargin { Width = "500", Type = TableWidthUnitValues.Dxa },
                        new BottomMargin { Width = "500", Type = TableWidthUnitValues.Dxa })),
                Cell(),
                Cell()));

        var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new(new Body(table));
        }

        stream.Position = 0;
        return stream;
    }

    // With no style padding to inherit, absent sides stay zero — tables in documents whose style
    // sets no w:tblCellMar are untouched by the merge.
    [Test]
    public async Task MergeTableCellMargin_NoStylePadding_AbsentSidesZero()
    {
        var margin = new TableCellMarginDefault(
            $"""<w:tblCellMar {wNs}><w:top w:w="400" w:type="dxa"/><w:bottom w:w="400" w:type="dxa"/></w:tblCellMar>""");

        var result = DocumentParser.MergeTableCellMargin(margin, fallback: null);

        await Assert.That(result!.Top).IsEqualTo(20);
        await Assert.That(result.Bottom).IsEqualTo(20);
        await Assert.That(result.Left).IsEqualTo(0);
        await Assert.That(result.Right).IsEqualTo(0);
    }
}
