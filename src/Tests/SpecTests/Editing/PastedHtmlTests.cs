// HTML as a browser, Google Docs and Word put it on the clipboard, read for what a paste keeps of it:
// paragraphs, headings, lists, alignment, links and emphasis — not fonts, sizes or colours.
//
// Shown as paragraphs between " | ": a style, "•" or "#" with the level for a bulleted or numbered
// list, an alignment in brackets, then each run in square brackets — its emphasis before a colon
// (b, i, u, s, ^ superscript, _ subscript), its text, and "->" with the address it links to.
public class PastedHtmlTests
{
    static string Show(string html) =>
        string.Join(" | ", PastedHtml.Read(html).Select(Show));

    static string Show(PastedParagraph paragraph)
    {
        var builder = new StringBuilder();
        if (paragraph.Style != null)
        {
            builder.Append(paragraph.Style).Append(' ');
        }

        if (paragraph.List != ListKind.None)
        {
            builder.Append(paragraph.List == ListKind.Bullet ? '•' : '#').Append(paragraph.Level).Append(' ');
        }

        if (paragraph.Alignment is { } alignment)
        {
            builder.Append('(').Append(alignment).Append(") ");
        }

        foreach (var run in paragraph.Runs)
        {
            var marks = new StringBuilder();
            var format = run.Format;
            if (format.Bold == true)
            {
                marks.Append('b');
            }

            if (format.Italic == true)
            {
                marks.Append('i');
            }

            if (format.Underline == true)
            {
                marks.Append('u');
            }

            if (format.Strike == true)
            {
                marks.Append('s');
            }

            if (format.Vertical == DocumentFormat.OpenXml.Wordprocessing.VerticalPositionValues.Superscript)
            {
                marks.Append('^');
            }

            if (format.Vertical == DocumentFormat.OpenXml.Wordprocessing.VerticalPositionValues.Subscript)
            {
                marks.Append('_');
            }

            builder.Append('[');
            if (marks.Length > 0)
            {
                builder.Append(marks).Append(':');
            }

            builder.Append(run.Text.Replace("\n", "\\n").Replace("\t", "\\t"));
            if (run.Link != null)
            {
                builder.Append("->").Append(run.Link);
            }

            builder.Append(']');
        }

        return builder.ToString().TrimEnd();
    }

    [Test]
    public async Task Paragraphs_AndTheirEmphasis() =>
        await Assert.That(Show("<p>One <b>two</b> <i>three</i> <u>four</u> <s>five</s></p><p>Six</p>"))
            .IsEqualTo("[One ][b:two][ ][i:three][ ][u:four][ ][s:five] | [Six]");

    [Test]
    public async Task WhiteSpace_CollapsesAsABrowsersDoes() =>
        await Assert.That(Show("<p>\n   spread \n\t out   </p>  <p>x&nbsp;&nbsp;y</p>")).IsEqualTo("[spread out] | [x  y]");

    [Test]
    public async Task ABreak_IsALineBreak_AndOneAtTheEndIsNothing() =>
        await Assert.That(Show("<p>a<br>  b<br></p>")).IsEqualTo("[a\\nb]");

    [Test]
    public async Task Headings_AndAlignment() =>
        await Assert.That(Show("<h1>Title</h1><h3>Part</h3><p style=\"text-align: center\">middle</p><p align=\"right\">end</p><div style=\"text-align:left\">left</div>"))
            .IsEqualTo("Heading1 [Title] | Heading3 [Part] | (Center) [middle] | (Right) [end] | [left]");

    // A list nested in an item is a level further in; an ordered list is numbered.
    [Test]
    public async Task Lists_AndTheirLevels() =>
        await Assert.That(Show("<ul><li>a<ul><li>b<ol><li>c</li></ol></li></ul></li><li>d</li></ul><ol><li>e</li></ol>"))
            .IsEqualTo("•0 [a] | •1 [b] | #2 [c] | •0 [d] | #0 [e]");

    // A second paragraph in the same item is not numbered again.
    [Test]
    public async Task AnItemOfTwoParagraphs_IsNumberedOnce() =>
        await Assert.That(Show("<ol><li><p>first</p><p>more</p></li><li>second</li></ol>")).IsEqualTo("#0 [first] | [more] | #0 [second]");

    // CSS turns emphasis on and off as tags do — Google Docs wraps all it copies in a bold that says
    // it is not.
    [Test]
    public async Task CssEmphasis_OverridesTheTags() =>
        await Assert.That(Show(
            "<p><b style=\"font-weight:normal\">plain <span style=\"font-weight:700\">bold</span></b> " +
            "<span style=\"font-style:italic\">it</span> <span style=\"text-decoration: underline line-through\">both</span> " +
            "<span style=\"vertical-align:super\">up</span><sub>down</sub></p>"))
            .IsEqualTo("[plain ][b:bold][ ][i:it][ ][us:both][ ][^:up][_:down]");

    // Google Docs, as Chrome puts it on the clipboard: a wrapping bold that is not, list items flat
    // with their level in aria-level, a paragraph inside each.
    [Test]
    public async Task GoogleDocs()
    {
        const string html =
            "<meta charset=\"utf-8\"><b style=\"font-weight:normal;\" id=\"docs-internal-guid-6a0b\">" +
            "<h2 dir=\"ltr\" style=\"line-height:1.38;margin-top:18pt\"><span style=\"font-size:16pt;font-family:Arial,sans-serif;color:#000000;font-weight:400\">Plan</span></h2>" +
            "<p dir=\"ltr\" style=\"line-height:1.38;\"><span style=\"font-size:11pt;font-weight:400;font-style:normal;\">plain </span><span style=\"font-size:11pt;font-weight:700;\">bold</span></p>" +
            "<ul style=\"margin-top:0;margin-bottom:0;\"><li dir=\"ltr\" aria-level=\"1\" style=\"list-style-type:disc;\"><p dir=\"ltr\" role=\"presentation\"><span style=\"font-weight:400\">one</span></p></li>" +
            "<li dir=\"ltr\" aria-level=\"2\" style=\"list-style-type:circle;\"><p dir=\"ltr\" role=\"presentation\"><a href=\"https://example.com/\" style=\"text-decoration:none;\"><span style=\"color:#1155cc;text-decoration:underline;\">two</span></a></p></li></ul>" +
            "</b><br>";

        await Assert.That(Show(html)).IsEqualTo("Heading2 [Plan] | [plain ][b:bold] | •0 [one] | •1 [two->https://example.com/]");
    }

    // Word, as it puts its own HTML on the clipboard: list items are paragraphs naming their level in
    // mso-list, their number or bullet in a span to be ignored; an empty paragraph is a space in o:p.
    [Test]
    public async Task Word()
    {
        const string html =
            "<html xmlns:o=\"urn:schemas-microsoft-com:office:office\"><body lang=EN-AU>" +
            "<!--StartFragment--><p class=MsoTitle>Report<o:p></o:p></p>" +
            "<p class=MsoListParagraphCxSpFirst style='text-indent:-18.0pt;mso-list:l0 level1 lfo1'><![if !supportLists]><span style='font-family:Symbol'><span style='mso-list:Ignore'>·<span style='font:7.0pt \"Times New Roman\"'>&nbsp;&nbsp;&nbsp; </span></span></span><![endif]>First <b>point</b><o:p></o:p></p>" +
            "<p class=MsoListParagraphCxSpLast style='margin-left:72.0pt;mso-list:l1 level2 lfo2'><![if !supportLists]><span style='mso-list:Ignore'>a.<span style='font:7.0pt \"Times New Roman\"'>&nbsp;&nbsp; </span></span><![endif]>Second<o:p></o:p></p>" +
            "<p class=MsoNormal><o:p>&nbsp;</o:p></p>" +
            "<p class=MsoNormal align=center style='text-align:center'>After<o:p></o:p></p><!--EndFragment-->" +
            "</body></html>";

        await Assert.That(Show(html)).IsEqualTo("Title [Report] | •0 [First ][b:point] | #1 [Second] |  | (Center) [After]");
    }

    // A link to the web or to mail is kept; one into the page it came from, or relative to it, has
    // nowhere to go in the document. A link's own style underlines it.
    [Test]
    public async Task Links() =>
        await Assert.That(Show("<p>See <a href=\"https://example.com/x\">the <b>site</b></a>, <a href=\"mailto:a@example.com\">mail</a>, <a href=\"#top\">top</a> or <a href=\"/rel\"><u>rel</u></a>.</p>"))
            .IsEqualTo("[See ][the ->https://example.com/x][b:site->https://example.com/x][, ][mail->mailto:a@example.com][, top or ][u:rel][.]");

    [Test]
    public async Task ATablesRows_AreParagraphs_TheirCellsATabApart() =>
        await Assert.That(Show("<table><tr><th>Name</th><th>Size</th></tr><tr><td>a</td><td><p>b</p><p>c</p></td></tr></table>"))
            .IsEqualTo("[Name\\tSize] | [a\\tb c]");

    [Test]
    public async Task Preformatted_KeepsItsSpaces_ALineToAParagraph() =>
        await Assert.That(Show("<pre>line one\n  indented\tthen\n</pre>")).IsEqualTo("[line one] | [  indented\\tthen]");

    [Test]
    public async Task WhatIsNotText_IsNotRead() =>
        await Assert.That(Show("<style>p { color: red }</style><p>a<span style=\"display:none\">hidden</span><img src=\"x.png\" alt=\"pic\"><script>b()</script>c</p>"))
            .IsEqualTo("[ac]");

    // Text with no element around it is a paragraph, and empty ones at either end are dropped.
    [Test]
    public async Task BareText_AndEmptyParagraphsAtTheEnds() =>
        await Assert.That(Show("<p></p><p><br></p>loose <b>text</b><p><br></p>")).IsEqualTo("[loose ][b:text]");
}
