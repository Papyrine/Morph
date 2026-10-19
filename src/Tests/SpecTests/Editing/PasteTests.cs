using static EditDocuments;
using static ReviewDocuments;

// Paragraphs read from the clipboard, pasted at a place in a paragraph: the text goes in as typing
// does, a paragraph the paste makes whole is given its heading, list and alignment, and links are links.
public class PasteTests
{
    const string styles =
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";

    static PastedParagraph Para(params PastedRun[] runs) =>
        new(runs);

    static PastedRun Text(string text) =>
        new(text);

    static PastedRun Bold(string text) =>
        new(text, new(Bold: true));

    // Each paragraph's style and list, as " | "-separated "style•level" (or "style#level" when numbered).
    static string Kinds(byte[] docx)
    {
        var outline = DocumentOutline.Read(docx);
        return string.Join(" | ", outline.Paragraphs.Select(paragraph =>
        {
            var style = Read(docx, _ => _.MainDocumentPart!.Document!.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .ElementAt(paragraph.Ordinal).ParagraphProperties?.ParagraphStyleId?.Val?.Value) ?? "";
            if (paragraph.ListLevel is not { } level)
            {
                return style;
            }

            var mark = DocumentEditor.ListOf(docx, paragraph.Ordinal) == ListKind.Number ? '#' : '•';
            return $"{style}{mark}{level}";
        }));
    }

    [Test]
    public async Task OneParagraph_GoesInAtThePlace_WithItsEmphasis()
    {
        var docx = Build(P(R("Hello world")), styles: styles);

        var result = DocumentEditor.Paste(docx, 0, 6, [Para(Bold("big"), Text(" new "))], Plain);

        await Assert.That(Shape(result.Document)).IsEqualTo("[Hello ][b:big][ new world] ¶");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((0, 14));
    }

    // The first pasted paragraph ends the one pasted into, the last begins what was after the place:
    // those two keep the paragraph's look; the one between is the paste's, and a heading.
    [Test]
    public async Task Paragraphs_SplitTheParagraph()
    {
        var docx = Build(P("<w:pPr><w:jc w:val=\"center\"/></w:pPr>", R("Hello world")), styles: styles);
        PastedParagraph[] content =
        [
            Para(Text("one")),
            Para(Text("two")) with
            {
                Style = "Heading1"
            },
            Para(Text("three "))
        ];

        var result = DocumentEditor.Paste(docx, 0, 6, content, Plain);

        await Assert.That(Shape(result.Document)).IsEqualTo("[Hello one] ¶ [two] ¶ [three world] ¶");
        await Assert.That(Kinds(result.Document)).IsEqualTo(" | Heading1 | ");
        await Assert.That(Xml(result.Document)).StartsWith("<w:p><w:pPr><w:jc w:val=\"center\" /></w:pPr>");
        await Assert.That(Xml(result.Document)).EndsWith("<w:p><w:pPr><w:jc w:val=\"center\" /></w:pPr><w:r><w:t xml:space=\"preserve\">three world</w:t></w:r></w:p>");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((2, 6));
    }

    // Into an empty paragraph, every pasted paragraph is the paste's own: lists, levels, alignment.
    [Test]
    public async Task IntoAnEmptyParagraph_EveryParagraphIsAsItWas()
    {
        var docx = Build(P(R("Before")) + "<w:p/>" + P(R("After")), styles: styles);
        PastedParagraph[] content =
        [
            Para(Text("Title")) with
            {
                Style = "Heading2"
            },
            Para(Text("a")) with
            {
                List = ListKind.Bullet
            },
            Para(Text("b")) with
            {
                List = ListKind.Bullet,
                Level = 1
            },
            Para(Text("c")) with
            {
                List = ListKind.Number
            },
            Para(Text("end")) with
            {
                Alignment = TextAlignment.Right
            }
        ];

        var result = DocumentEditor.Paste(docx, 1, 0, content, Plain);

        await Assert.That(Shape(result.Document)).IsEqualTo("[Before] ¶ [Title] ¶ [a] ¶ [b] ¶ [c] ¶ [end] ¶ [After] ¶");
        await Assert.That(Kinds(result.Document)).IsEqualTo(" | Heading2 | ListParagraph•0 | ListParagraph•1 | ListParagraph#0 |  | ");
        await Assert.That(Xml(result.Document)).Contains("<w:p><w:pPr><w:jc w:val=\"right\" /></w:pPr><w:r><w:t xml:space=\"preserve\">end</w:t>");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((5, 3));
    }

    // Paragraphs pasted into the middle of a list's item: those the paste made whole are out of the
    // list unless they were in one.
    [Test]
    public async Task IntoAListsItem_TheParagraphsBetweenAreTheirOwn()
    {
        var docx = DocumentEditor.SetList(Build(P(R("item text")), styles: styles), [0], ListKind.Bullet, Plain);

        var result = DocumentEditor.Paste(docx, 0, 5, [Para(Text("A")), Para(Text("plain")), Para(Text("B"))], Plain);

        await Assert.That(Shape(result.Document)).IsEqualTo("[item A] ¶ [plain] ¶ [Btext] ¶");
        await Assert.That(Kinds(result.Document)).IsEqualTo("ListParagraph•0 |  | ListParagraph•0");
    }

    [Test]
    public async Task Links_AreLinks()
    {
        var docx = Build(P(R("See ")), styles: styles);

        var result = DocumentEditor.Paste(docx, 0, 4, [Para(new("the ", default, "https://example.com/"), new("site", new(Bold: true), "https://example.com/"), Text(" or "), new("mail", default, "mailto:a@example.com"))], Plain);

        await Assert.That(Shape(result.Document)).IsEqualTo("[See ]<[the ][b:site]>[ or ]<[mail]> ¶");
        var addresses = Read(result.Document, _ => _.MainDocumentPart!.HyperlinkRelationships.Select(link => link.Uri.OriginalString).ToList());
        await Assert.That(addresses).IsEquivalentTo(["https://example.com/", "mailto:a@example.com"]);
    }

    [Test]
    public async Task Tracked_IsAnInsertion_ThatRejectingTakesAway()
    {
        var docx = Build(P(R("Hello world")), styles: styles);

        var result = DocumentEditor.Paste(docx, 0, 6, [Para(Text("one")), Para(Bold("two")) with {Style = "Heading1"}, Para(Text("three "))], Tracked);

        await Assert.That(DocumentReview.Read(result.Document).Changes.Select(_ => _.Kind)).Contains(ReviewChangeKind.Insertion);
        await Assert.That(Shape(ReviewEditor.ResolveAll(result.Document, accept: false))).IsEqualTo("[Hello ][world] ¶");
        await Assert.That(Shape(ReviewEditor.ResolveAll(result.Document, accept: true))).IsEqualTo("[Hello ][one] ¶ [b:two] ¶ [three ][world] ¶");
    }

    // HTML from the clipboard, read and pasted.
    [Test]
    public async Task Html_ReadAndPasted()
    {
        var docx = Build("<w:p/>", styles: styles);
        var content = PastedHtml.Read("<h1>Notes</h1><p>Read <a href=\"https://example.com/\">this</a> <b>now</b>.</p><ul><li>one</li><li>two</li></ul>");

        var result = DocumentEditor.Paste(docx, 0, 0, content, Plain);

        await Assert.That(Shape(result.Document)).IsEqualTo("[Notes] ¶ [Read ]<[this]>[ ][b:now][.] ¶ [one] ¶ [two] ¶");
        await Assert.That(Kinds(result.Document)).IsEqualTo("Heading1 |  | ListParagraph•0 | ListParagraph•0");
    }
}
