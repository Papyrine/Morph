using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// Rules of the styles part and of run text that Word applies before anything is laid out, each
/// Word-probed 2026-10-11.
/// </summary>
public class StyleDefinitionRulesTests
{
    const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    static ParsedDocument Parse(string body, string styles)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new($"<w:document {wNs}><w:body>{body}</w:body></w:document>");
            main.AddNewPart<StyleDefinitionsPart>().Styles = new($"<w:styles {wNs}>{styles}</w:styles>");
        }

        stream.Position = 0;
        return new DocumentParser().Parse(stream);
    }

    static string Heading(string properties) =>
        $"""<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/>{properties}</w:style>""";

    const string headingParagraph = """<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Heading</w:t></w:r></w:p>""";

    // complex_tables' styles part defines Heading1 twice: blue 16pt first, bold black 18pt with space
    // above and below at its end. Word set the heading at 18pt on the second definition's spacing
    // (_probe_headname_a), and italics, centring and an indent added to the FIRST moved nothing (_f).
    [Test]
    public async Task A_style_defined_twice_is_its_last_definition()
    {
        var first = Heading("""<w:pPr><w:jc w:val="center"/><w:ind w:left="1440"/></w:pPr><w:rPr><w:i/><w:color w:val="2E74B5"/><w:sz w:val="32"/></w:rPr>""");
        var second = Heading("""<w:pPr><w:spacing w:before="480" w:after="240"/></w:pPr><w:rPr><w:b/><w:color w:val="000000"/><w:sz w:val="36"/></w:rPr>""");

        var paragraph = (ParagraphElement) Parse(headingParagraph, first + second).Elements[0];
        var run = paragraph.Runs[0].Properties;

        await Assert.That(run.FontSizePoints).IsEqualTo(18);
        await Assert.That(run.Bold).IsTrue();
        await Assert.That(run.Italic).IsFalse();
        await Assert.That(run.ColorHex).IsEqualTo("000000");
        await Assert.That(paragraph.Properties.SpacingBeforePoints).IsEqualTo(24);
        await Assert.That(paragraph.Properties.SpacingAfterPoints).IsEqualTo(12);
        await Assert.That(paragraph.Properties.Alignment).IsEqualTo(TextAlignment.Left);
        await Assert.That(paragraph.Properties.LeftIndentPoints).IsEqualTo(0);
    }

    [Test]
    public async Task A_style_defined_once_is_untouched()
    {
        var paragraph = (ParagraphElement) Parse(headingParagraph, Heading("""<w:rPr><w:sz w:val="32"/></w:rPr>""")).Elements[0];

        await Assert.That(paragraph.Runs[0].Properties.FontSizePoints).IsEqualTo(16);
    }

    // A literal line feed in w:t is not a break: complex_tables carries "COMPLEX" LF "MERGE" in one
    // w:t, and Word's page has the two words on one line with a space between. Only w:br breaks.
    [Test]
    public async Task A_line_feed_inside_run_text_is_a_space()
    {
        var document = Parse("<w:p><w:r><w:t xml:space=\"preserve\">COMPLEX\nMERGE</w:t></w:r></w:p><w:p><w:r><w:t>one</w:t><w:br/><w:t>two</w:t></w:r></w:p>", "");

        var literal = string.Concat(((ParagraphElement) document.Elements[0]).Runs.Select(_ => _.Text));
        var broken = string.Concat(((ParagraphElement) document.Elements[1]).Runs.Select(_ => _.Text));

        await Assert.That(literal).IsEqualTo("COMPLEX MERGE");
        await Assert.That(broken).Contains("\n");
    }

    const string gridStyles =
        """<w:style w:type="table" w:default="1" w:styleId="TableNormal"><w:name w:val="Normal Table"/><w:tblPr><w:tblCellMar><w:top w:w="0" w:type="dxa"/><w:left w:w="108" w:type="dxa"/><w:bottom w:w="0" w:type="dxa"/><w:right w:w="108" w:type="dxa"/></w:tblCellMar></w:tblPr></w:style>""" +
        """<w:style w:type="table" w:styleId="TableGrid"><w:name w:val="Table Grid"/><w:basedOn w:val="TableNormal"/><w:tcPr><w:tcMar><w:top w:w="300" w:type="dxa"/><w:left w:w="600" w:type="dxa"/><w:bottom w:w="300" w:type="dxa"/></w:tcMar></w:tcPr></w:style>""";

    static string Table(string tableMargins = "", string cellMargins = "") =>
        $"""<w:tbl><w:tblPr><w:tblStyle w:val="TableGrid"/><w:tblW w:w="3000" w:type="dxa"/>{tableMargins}</w:tblPr><w:tblGrid><w:gridCol w:w="3000"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w="3000" w:type="dxa"/>{cellMargins}</w:tcPr><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>""";

    static CellSpacing Padding(string tableMargins = "", string cellMargins = "")
    {
        var table = (TableElement) Parse(Table(tableMargins, cellMargins), gridStyles).Elements[0];
        return TableLayout.GetEffectivePadding(table.Rows[0].Cells[0].Properties, table.Properties, table.Rows[0]);
    }

    // _probe_styletcmar: a table style's whole-table w:tcPr/w:tcMar (15pt above and below, 30pt on
    // the left) reaches every cell, the side it does not state coming from the w:tblCellMar chain.
    [Test]
    public async Task A_table_styles_cell_margins_reach_every_cell()
    {
        var padding = Padding();

        await Assert.That(padding.Top).IsEqualTo(15);
        await Assert.That(padding.Bottom).IsEqualTo(15);
        await Assert.That(padding.Left).IsEqualTo(30);
        await Assert.That(padding.Right).IsEqualTo(5.4);
    }

    // The table's OWN w:tblCellMar of 30pt, 30pt and 5pt did not displace them.
    [Test]
    public async Task They_stand_above_the_tables_own_default_margins()
    {
        var padding = Padding(tableMargins: """<w:tblCellMar><w:top w:w="600" w:type="dxa"/><w:left w:w="100" w:type="dxa"/><w:bottom w:w="600" w:type="dxa"/></w:tblCellMar>""");

        await Assert.That(padding.Top).IsEqualTo(15);
        await Assert.That(padding.Bottom).IsEqualTo(15);
        await Assert.That(padding.Left).IsEqualTo(30);
    }

    // A cell's own w:tcMar did.
    [Test]
    public async Task A_cells_own_margins_stand_above_them()
    {
        var padding = Padding(cellMargins: """<w:tcMar><w:top w:w="0" w:type="dxa"/><w:left w:w="0" w:type="dxa"/><w:bottom w:w="0" w:type="dxa"/></w:tcMar>""");

        await Assert.That(padding.Top).IsEqualTo(0);
        await Assert.That(padding.Bottom).IsEqualTo(0);
        await Assert.That(padding.Left).IsEqualTo(0);
    }
}
