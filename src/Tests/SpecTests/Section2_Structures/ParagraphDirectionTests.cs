using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// Where a paragraph's alignment and indents put its lines: a right-to-left paragraph (<c>w:bidi</c>)
/// reads its <c>w:jc</c> and <c>w:ind</c> as start and end sides, Word-probed (<c>_probe_bidi</c>,
/// 2026-11-28, 14pt lines on a Letter page); <c>w:jc="end"</c> is the right in a left-to-right one; and
/// a style's own <c>w:firstLine</c> replaces the hanging indent of the list level it puts its paragraphs
/// in (resumes/14's List Bullet).
/// </summary>
public class ParagraphDirectionTests
{
    const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    static ParsedDocument Parse(string body, string styles = "", string numbering = "")
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = [with($"<w:document {wNs}><w:body>{body}</w:body></w:document>")];
            main.AddNewPart<StyleDefinitionsPart>().Styles = [with($"<w:styles {wNs}>{styles}</w:styles>")];
            if (numbering.Length > 0)
            {
                main.AddNewPart<NumberingDefinitionsPart>().Numbering = [with($"<w:numbering {wNs}>{numbering}</w:numbering>")];
            }
        }

        stream.Position = 0;
        return new DocumentParser().Parse(stream);
    }

    static string Paragraph(string properties, string text = "text") =>
        $"<w:p><w:pPr>{properties}</w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    static ParagraphProperties Properties(ParsedDocument document, int index) =>
        document.Elements.OfType<ParagraphElement>().ElementAt(index).Properties;

    // No jc, left and start stand the line at the right margin; right and end at the left; center stays
    [Test]
    public async Task A_right_to_left_paragraph_reads_its_alignment_as_start_and_end()
    {
        var document = Parse(
            Paragraph("<w:bidi/>") +
            Paragraph("""<w:bidi/><w:jc w:val="left"/>""") +
            Paragraph("""<w:bidi/><w:jc w:val="start"/>""") +
            Paragraph("""<w:bidi/><w:jc w:val="right"/>""") +
            Paragraph("""<w:bidi/><w:jc w:val="end"/>""") +
            Paragraph("""<w:bidi/><w:jc w:val="center"/>"""));

        await Assert.That(Properties(document, 0).Alignment).IsEqualTo(TextAlignment.Right);
        await Assert.That(Properties(document, 1).Alignment).IsEqualTo(TextAlignment.Right);
        await Assert.That(Properties(document, 2).Alignment).IsEqualTo(TextAlignment.Right);
        await Assert.That(Properties(document, 3).Alignment).IsEqualTo(TextAlignment.Left);
        await Assert.That(Properties(document, 4).Alignment).IsEqualTo(TextAlignment.Left);
        await Assert.That(Properties(document, 5).Alignment).IsEqualTo(TextAlignment.Center);
    }

    // w:left="1440" stands the line an inch in from the right margin; w:right="1440" an inch from the left
    [Test]
    public async Task A_right_to_left_paragraph_reads_its_indents_as_start_and_end()
    {
        var document = Parse(
            Paragraph("""<w:bidi/><w:ind w:left="1440"/>""") +
            Paragraph("""<w:bidi/><w:ind w:right="1440"/>"""));

        await Assert.That(Properties(document, 0).RightIndentPoints).IsEqualTo(72);
        await Assert.That(Properties(document, 0).LeftIndentPoints).IsEqualTo(0);
        await Assert.That(Properties(document, 1).LeftIndentPoints).IsEqualTo(72);
        await Assert.That(Properties(document, 1).RightIndentPoints).IsEqualTo(0);
    }

    [Test]
    public async Task End_aligns_a_left_to_right_paragraph_right() =>
        await Assert.That(Properties(Parse(Paragraph("""<w:jc w:val="end"/>""")), 0).Alignment).IsEqualTo(TextAlignment.Right);

    // resumes/14: List Bullet names its list (level 0: left=2520 hanging=360) and indents left=288
    // firstLine=0; Word sets the bullet at 288 and the text at the next default stop
    [Test]
    public async Task A_styles_first_line_replaces_its_list_levels_hanging_indent()
    {
        const string styles = """<w:style w:type="paragraph" w:styleId="ListBullet"><w:name w:val="List Bullet"/><w:pPr><w:numPr><w:numId w:val="1"/></w:numPr><w:ind w:left="288" w:firstLine="0"/></w:pPr></w:style>""";
        const string numbering = """<w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:pStyle w:val="ListBullet"/><w:lvlText w:val="•"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="2520" w:hanging="360"/></w:pPr></w:lvl></w:abstractNum><w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>""";
        var properties = Properties(Parse(Paragraph("""<w:pStyle w:val="ListBullet"/>"""), styles, numbering), 0);

        await Assert.That(properties.LeftIndentPoints).IsEqualTo(14.4).Within(0.01);
        await Assert.That(properties.FirstLineIndentPoints).IsEqualTo(0);
        await Assert.That(properties.HangingIndentPoints).IsEqualTo(0);
    }
}
