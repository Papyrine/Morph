using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// Word's "Auto" paragraph spacing (<c>w:beforeAutospacing</c> / <c>w:afterAutospacing</c>), Word-probed
/// 2026-10-11 (<c>_probe_autospace_a</c> to <c>_d</c>: one-line paragraphs of exact 20pt lines, baselines
/// read off the XPS, compatibility modes 12 and 15 alike).
/// </summary>
public class AutoSpacingTests
{
    const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    static ParsedDocument Parse(string body, string styles = "", string compat = "", string numbering = "")
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = [with($"<w:document {wNs}><w:body>{body}</w:body></w:document>")];
            main.AddNewPart<StyleDefinitionsPart>().Styles = [with($"<w:styles {wNs}>{styles}</w:styles>")];
            main.AddNewPart<DocumentSettingsPart>().Settings = [with($"<w:settings {wNs}><w:compat>{compat}</w:compat></w:settings>")];
            if (numbering.Length > 0)
            {
                main.AddNewPart<NumberingDefinitionsPart>().Numbering = [with($"<w:numbering {wNs}>{numbering}</w:numbering>")];
            }
        }

        stream.Position = 0;
        return new DocumentParser().Parse(stream);
    }

    static string Paragraph(string text, string spacing, string extra = "") =>
        $"<w:p><w:pPr>{extra}<w:spacing {spacing}/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    const string autoBoth = """w:before="100" w:beforeAutospacing="1" w:after="100" w:afterAutospacing="1" """;

    static ParagraphProperties Properties(ParsedDocument document, int index) =>
        document.Elements.OfType<ParagraphElement>().ElementAt(index).Properties;

    // B1: 5pt declared either side opened 14.2pt above and below; D1: 40pt declared opened 13.6.
    [Test]
    public async Task Auto_is_fourteen_points_whatever_is_declared_beside_it()
    {
        var document = Parse(
            Paragraph("plain", """w:before="0" w:after="0" """) +
            Paragraph("auto", autoBoth) +
            Paragraph("declared forty", """w:before="800" w:beforeAutospacing="1" w:after="0" """));

        await Assert.That(Properties(document, 1).SpacingBeforePoints).IsEqualTo(14);
        await Assert.That(Properties(document, 1).SpacingAfterPoints).IsEqualTo(14);
        await Assert.That(Properties(document, 2).SpacingBeforePoints).IsEqualTo(14);
        await Assert.That(Properties(document, 2).SpacingAfterPoints).IsEqualTo(0);
        await Assert.That(Properties(document, 0).SpacingBeforePoints).IsEqualTo(0);
    }

    // newsletters/02's Title: the flag is on the style, the paragraph names only the style.
    [Test]
    public async Task The_flag_comes_down_from_a_style_and_a_paragraph_can_switch_it_off()
    {
        const string title = """<w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:pPr><w:spacing w:before="100" w:beforeAutospacing="1"/></w:pPr></w:style>""";
        var document = Parse(
            "<w:p><w:r><w:t>lead</w:t></w:r></w:p>" +
            """<w:p><w:pPr><w:pStyle w:val="Title"/></w:pPr><w:r><w:t>styled</w:t></w:r></w:p>""" +
            """<w:p><w:pPr><w:pStyle w:val="Title"/><w:spacing w:before="400" w:beforeAutospacing="0"/></w:pPr><w:r><w:t>overridden</w:t></w:r></w:p>""",
            title);

        await Assert.That(Properties(document, 1).SpacingBeforePoints).IsEqualTo(14);
        await Assert.That(Properties(document, 2).SpacingBeforePoints).IsEqualTo(20);
    }

    // G and H: a cell's only paragraph sat on the baseline of the plain one in the next cell and added
    // nothing under itself; of three, the first took no auto before, the last no auto after, and the
    // middle one 14.2 either side. J: the same of an empty paragraph.
    [Test]
    public async Task At_the_edges_of_a_cell_auto_is_nothing()
    {
        var cell = Paragraph("first", autoBoth) + Paragraph("middle", autoBoth) + Paragraph("last", autoBoth);
        var document = Parse($"""<w:tbl><w:tblGrid><w:gridCol w:w="4000"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w="4000" w:type="dxa"/></w:tcPr>{cell}</w:tc></w:tr></w:tbl><w:p/>""");
        var paragraphs = ((TableElement) document.Elements[0]).Rows[0].Cells[0].Content.OfType<ParagraphElement>().Select(_ => _.Properties).ToList();

        await Assert.That(paragraphs[0].SpacingBeforePoints).IsEqualTo(0);
        await Assert.That(paragraphs[0].SpacingAfterPoints).IsEqualTo(14);
        await Assert.That(paragraphs[1].SpacingBeforePoints).IsEqualTo(14);
        await Assert.That(paragraphs[1].SpacingAfterPoints).IsEqualTo(14);
        await Assert.That(paragraphs[2].SpacingBeforePoints).IsEqualTo(14);
        await Assert.That(paragraphs[2].SpacingAfterPoints).IsEqualTo(0);
    }

    // F: three list items, auto both sides, stood 20.4 and 19.8 apart with 13.6 over the first and 14.2
    // under the last.
    [Test]
    public async Task Between_two_items_of_one_list_auto_is_nothing()
    {
        const string numbering = """<w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum><w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>""";
        const string item = """<w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr>""";
        var document = Parse(
            Paragraph("lead", """w:before="0" w:after="0" """) +
            Paragraph("one", autoBoth, item) +
            Paragraph("two", autoBoth, item) +
            Paragraph("three", autoBoth, item) +
            Paragraph("tail", """w:before="0" w:after="0" """),
            numbering: numbering);

        await Assert.That(Properties(document, 1).SpacingBeforePoints).IsEqualTo(14);
        await Assert.That(Properties(document, 1).SpacingAfterPoints).IsEqualTo(0);
        await Assert.That(Properties(document, 2).SpacingBeforePoints).IsEqualTo(0);
        await Assert.That(Properties(document, 2).SpacingAfterPoints).IsEqualTo(0);
        await Assert.That(Properties(document, 3).SpacingBeforePoints).IsEqualTo(0);
        await Assert.That(Properties(document, 3).SpacingAfterPoints).IsEqualTo(14);
    }

    // _probe_autospace_b: with the setting, 5pt declared reads 4.8 at a cell's top and 5.4 under its
    // last line, and 40pt declared opens 39.5.
    [Test]
    public async Task The_compatibility_setting_keeps_the_declared_amounts_everywhere()
    {
        var cell = Paragraph("only", autoBoth);
        var document = Parse(
            Paragraph("declared forty", """w:before="800" w:beforeAutospacing="1" w:after="0" """) +
            $"""<w:tbl><w:tblGrid><w:gridCol w:w="4000"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w="4000" w:type="dxa"/></w:tcPr>{cell}</w:tc></w:tr></w:tbl><w:p/>""",
            compat: "<w:doNotUseHTMLParagraphAutoSpacing/>");
        var inCell = ((ParagraphElement) document.Elements.OfType<TableElement>().Single().Rows[0].Cells[0].Content[0]).Properties;

        await Assert.That(Properties(document, 0).SpacingBeforePoints).IsEqualTo(40);
        await Assert.That(inCell.SpacingBeforePoints).IsEqualTo(5);
        await Assert.That(inCell.SpacingAfterPoints).IsEqualTo(5);
        await Assert.That(document.Compatibility.UsesFixedAutoSpacing).IsTrue();
    }
}
