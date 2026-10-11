/// <summary>
/// What HTML text is set at when nothing says otherwise, read off Word's own import of html_complex
/// (<c>_probe_htmlsize</c>, 2026-10-11, the XPS glyph runs): an em of 12.00 in paragraphs, table cells and
/// list items alike, a two-line paragraph's baselines 13.8pt apart, and the source's line ends set as
/// spaces.
/// </summary>
public class HtmlTextDefaultsTests
{
    static ParagraphElement Paragraph(string html) =>
        HtmlParser.Parse(html).OfType<ParagraphElement>().First();

    [Test]
    public async Task Text_is_twelve_points_in_a_paragraph_a_cell_and_a_list_item()
    {
        var elements = HtmlParser.Parse("<p>body</p><table><tr><td>cell</td></tr></table><ul><li>item</li></ul>");
        var paragraph = elements.OfType<ParagraphElement>().First();
        var cell = (ParagraphElement) elements.OfType<TableElement>().Single().Rows[0].Cells[0].Content[0];
        var item = elements.OfType<ParagraphElement>().Single(_ => _.Properties.Numbering != null);

        await Assert.That(paragraph.Runs[0].Properties.FontSizePoints).IsEqualTo(12);
        await Assert.That(cell.Runs[0].Properties.FontSizePoints).IsEqualTo(12);
        await Assert.That(item.Runs[0].Properties.FontSizePoints).IsEqualTo(12);
    }

    [Test]
    public async Task A_paragraph_and_a_list_item_are_single_spaced()
    {
        var elements = HtmlParser.Parse("<p>body</p><ul><li>item</li></ul>");

        await Assert.That(elements.OfType<ParagraphElement>().All(_ => _.Properties.LineSpacingMultiplier == 1.0)).IsTrue();
        await Assert.That(Paragraph("<p style=\"line-height: 1.5\">loose</p>").Properties.LineSpacingMultiplier).IsEqualTo(1.5);
    }

    // html_complex's intro is typed over three source lines and sets as two in Word; each line end had
    // become a break of its own.
    [Test]
    public async Task Line_ends_and_runs_of_spaces_in_the_source_are_one_space()
    {
        var paragraph = Paragraph("<p>one,   two\n<b>three</b> and\r\n   four  </p>");

        await Assert.That(string.Concat(paragraph.Runs.Select(_ => _.Text))).IsEqualTo("one, two three and four");
        await Assert.That(paragraph.Runs.Any(_ => _.Text.Contains('\n'))).IsFalse();
    }

    [Test]
    public async Task A_space_is_not_doubled_across_an_inline_element()
    {
        var paragraph = Paragraph("<p>one <b> two</b> <i>three</i></p>");

        await Assert.That(string.Concat(paragraph.Runs.Select(_ => _.Text))).IsEqualTo("one two three");
    }

    [Test]
    public async Task Preformatted_text_keeps_its_whitespace()
    {
        var elements = HtmlParser.Parse("<pre>a  b\n  c</pre><p style=\"white-space: pre-wrap\">d  e</p>");
        var text = string.Concat(elements.OfType<ParagraphElement>().SelectMany(_ => _.Runs).Select(_ => _.Text));

        await Assert.That(text).Contains("a  b");
        await Assert.That(text).Contains("d  e");
    }
}
