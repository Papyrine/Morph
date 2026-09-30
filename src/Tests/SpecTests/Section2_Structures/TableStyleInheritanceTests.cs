using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// What a table style hands the rows and paragraphs inside it beyond borders and padding: a row's
/// <c>w:cantSplit</c> (<c>DocumentParser.ResolveStyleCannotSplit</c>) and the run size and face an
/// empty paragraph's mark resolves to. Both surfaced in COMPASS's stocktake report, whose rows take
/// cantSplit from the PM&amp;C table style alone and whose summary cells name a paragraph style the
/// package never defines — and both are Word-read off that report (2026-09-30): Word carried a
/// straddling row whole that it split once the style's cantSplit was removed, and sized an empty cell
/// at the table style's 9pt.
/// </summary>
public class TableStyleInheritanceTests
{
    const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    // A 9pt table style that stops rows splitting, a style based on it that lets them split again, and
    // an 11pt Arial document default.
    const string styles =
        $"""
         <w:styles {wNs}>
           <w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults>
           <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
           <w:style w:type="table" w:styleId="Lined"><w:name w:val="Lined"/><w:rPr><w:sz w:val="18"/></w:rPr><w:trPr><w:cantSplit/></w:trPr></w:style>
           <w:style w:type="table" w:styleId="Loose"><w:name w:val="Loose"/><w:basedOn w:val="Lined"/><w:trPr><w:cantSplit w:val="0"/></w:trPr></w:style>
         </w:styles>
         """;

    static string Table(string style, string rows) =>
        $"""<w:tbl><w:tblPr><w:tblStyle w:val="{style}"/></w:tblPr><w:tblGrid><w:gridCol w:w="4000"/></w:tblGrid>{rows}</w:tbl>""";

    const string textRow = """<w:tr><w:tc><w:p><w:r><w:t>text</w:t></w:r></w:p></w:tc></w:tr>""";

    // A row that lets itself split whatever its style says, holding an empty paragraph that names a
    // style the package does not define.
    const string emptyRow = """<w:tr><w:trPr><w:cantSplit w:val="0"/></w:trPr><w:tc><w:p><w:pPr><w:pStyle w:val="Undefined"/></w:pPr></w:p></w:tc></w:tr>""";

    static ParsedDocument Parse()
    {
        var body =
            $"""
             <w:document {wNs}><w:body>
               {Table("Lined", textRow + emptyRow)}
               {Table("Loose", textRow)}
               <w:p><w:pPr><w:pStyle w:val="Undefined"/></w:pPr></w:p>
             </w:body></w:document>
             """;

        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new(body);
            main.AddNewPart<StyleDefinitionsPart>().Styles = new(styles);
        }

        stream.Position = 0;
        return new DocumentParser().Parse(stream);
    }

    [Test]
    public async Task A_row_takes_cantSplit_from_its_table_style()
    {
        var table = Parse().Elements.OfType<TableElement>().First();

        await Assert.That(table.Rows[0].CannotSplit).IsTrue();
    }

    [Test]
    public async Task A_row_s_own_cantSplit_wins_over_its_table_style()
    {
        var table = Parse().Elements.OfType<TableElement>().First();

        await Assert.That(table.Rows[1].CannotSplit).IsFalse();
    }

    [Test]
    public async Task A_derived_table_style_can_switch_its_base_s_cantSplit_off()
    {
        var table = Parse().Elements.OfType<TableElement>().Skip(1).First();

        await Assert.That(table.Rows[0].CannotSplit).IsFalse();
    }

    [Test]
    public async Task The_nearest_cantSplit_on_the_basedOn_chain_wins()
    {
        Style Style(string xml) => new(xml);
        var lined = Style($"""<w:style {wNs} w:type="table" w:styleId="Lined"><w:trPr><w:cantSplit/></w:trPr></w:style>""");
        var silent = Style($"""<w:style {wNs} w:type="table" w:styleId="Silent"><w:basedOn w:val="Lined"/></w:style>""");
        var loose = Style($"""<w:style {wNs} w:type="table" w:styleId="Loose"><w:basedOn w:val="Silent"/><w:trPr><w:cantSplit w:val="0"/></w:trPr></w:style>""");
        var styles = new Dictionary<string, Style>
        {
            ["Lined"] = lined,
            ["Silent"] = silent,
            ["Loose"] = loose
        };

        await Assert.That(DocumentParser.ResolveStyleCannotSplit(silent, styles)).IsTrue();
        await Assert.That(DocumentParser.ResolveStyleCannotSplit(loose, styles)).IsFalse();
    }

    // The empty cell paragraph names a style that does not exist and has no run to stand in for its
    // mark, so the mark resolves as a bare run in that cell would: the document default's face at the
    // table style's size.
    [Test]
    public async Task An_empty_cell_paragraph_s_mark_takes_the_table_style_s_size()
    {
        var table = Parse().Elements.OfType<TableElement>().First();
        var empty = table.Rows[1].Cells[0].Content.OfType<ParagraphElement>().Single();

        await Assert.That(empty.Runs.Count).IsEqualTo(0);
        await Assert.That(empty.Properties.ParagraphMarkFontSizePoints).IsEqualTo(9);
        await Assert.That(empty.Properties.ParagraphMarkFontFamily).IsEqualTo("Arial");
    }

    // Outside a table the same dangling style falls to the document default, where it used to leave the
    // mark with nothing and measure it against the record's default face.
    [Test]
    public async Task An_empty_paragraph_naming_an_undefined_style_takes_the_document_default()
    {
        var empty = Parse().Elements.OfType<ParagraphElement>().Last();

        await Assert.That(empty.Properties.ParagraphMarkFontSizePoints).IsEqualTo(11);
        await Assert.That(empty.Properties.ParagraphMarkFontFamily).IsEqualTo("Arial");
    }
}
