using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// The indent of a style that switches numbering off, Word-probed on <c>_probe_numoff</c> (first glyph
/// x of each line read off the XPS, against a base style numbered at <c>left=1440 hanging=720</c>):
/// such a style drops the indent its BASE declared beside the numbering and then applies its own
/// <c>w:ind</c> like any other style. The own indent used to go with the numbering, which set
/// resumes/07's "College info" lines at the margin where Word indents them 18pt.
/// </summary>
public class NumberingOffStyleIndentTests
{
    const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    const string numbering =
        """
        <w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:pPr><w:ind w:left="1440" w:hanging="720"/></w:pPr></w:lvl></w:abstractNum>
        <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
        """;

    const string numbered =
        """<w:style w:type="paragraph" w:styleId="Numbered"><w:name w:val="Numbered"/><w:pPr><w:numPr><w:numId w:val="1"/></w:numPr><w:ind w:left="1440" w:hanging="720"/></w:pPr></w:style>""";

    static ParagraphProperties Parse(string style)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new($"""<w:document {wNs}><w:body><w:p><w:pPr><w:pStyle w:val="Probe"/></w:pPr><w:r><w:t>text</w:t></w:r></w:p></w:body></w:document>""");
            main.AddNewPart<StyleDefinitionsPart>().Styles = new($"<w:styles {wNs}>{numbered}{style}</w:styles>");
            main.AddNewPart<NumberingDefinitionsPart>().Numbering = new($"<w:numbering {wNs}>{numbering}</w:numbering>");
        }

        stream.Position = 0;
        return new DocumentParser().Parse(stream).Elements.OfType<ParagraphElement>().Single().Properties;
    }

    static string Style(string basedOn, string paragraphProperties) =>
        $"""<w:style w:type="paragraph" w:styleId="Probe"><w:name w:val="Probe"/>{basedOn}<w:pPr>{paragraphProperties}</w:pPr></w:style>""";

    const string onNumbered = """<w:basedOn w:val="Numbered"/>""";
    const string off = """<w:numPr><w:numId w:val="0"/></w:numPr>""";

    // Both lines of a wrapped paragraph at the margin: business-plans/08's Title.
    [Test]
    public async Task With_no_indent_of_its_own_the_bases_numbering_indent_is_dropped()
    {
        var properties = Parse(Style(onNumbered, off));

        await Assert.That(properties.Numbering).IsNull();
        await Assert.That(properties.LeftIndentPoints).IsEqualTo(0);
        await Assert.That(properties.HangingIndentPoints).IsEqualTo(0);
    }

    // The text starts at 108pt, with no hanging: the base's hanging went with its numbering.
    [Test]
    public async Task Its_own_left_indent_applies_without_the_bases_hanging()
    {
        var properties = Parse(Style(onNumbered, off + """<w:ind w:left="2160"/>"""));

        await Assert.That(properties.Numbering).IsNull();
        await Assert.That(properties.LeftIndentPoints).IsEqualTo(108);
        await Assert.That(properties.HangingIndentPoints).IsEqualTo(0);
    }

    // First line at 72pt and the second at 108pt.
    [Test]
    public async Task Its_own_hanging_indent_applies()
    {
        var properties = Parse(Style(onNumbered, off + """<w:ind w:left="2160" w:hanging="720"/>"""));

        await Assert.That(properties.LeftIndentPoints).IsEqualTo(108);
        await Assert.That(properties.HangingIndentPoints).IsEqualTo(36);
    }

    // A style based on nothing numbered, with numId 0 or with only a level: text at 72pt.
    [Test]
    [Arguments(off)]
    [Arguments("""<w:numPr><w:ilvl w:val="0"/></w:numPr>""")]
    public async Task With_no_numbered_base_the_indent_is_the_styles_own(string numberingProperties)
    {
        var properties = Parse(Style("", numberingProperties + """<w:ind w:left="1440"/>"""));

        await Assert.That(properties.Numbering).IsNull();
        await Assert.That(properties.LeftIndentPoints).IsEqualTo(72);
    }
}
