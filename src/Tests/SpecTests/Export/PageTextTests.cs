using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
// Morph has its own Run and Paragraph in scope here, so the OOXML ones are qualified.
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Cover for <see cref="DocumentConverter.GetPageTexts(Stream, ImageExportOptions)"/> — the text of
/// a document divided by the page it is on, which only layout can do.
/// </summary>
public class PageTextTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static ImageExportOptions Options => new()
    {
        FontDirectory = fontsDirectory,
        DeterministicRendering = true
    };

    [Test]
    public async Task AParagraphIsALine()
    {
        using var docx = BuildDocument(Paragraph("First"), Paragraph("Second"));

        var pages = DocumentConverter.GetPageTexts(docx, Options);

        await Assert.That(pages.Count).IsEqualTo(1);
        await Assert.That(pages[0]).IsEqualTo("First\nSecond");
    }

    // The point of the API: what is after a page break is in the text of the next page, and only
    // there.
    [Test]
    public async Task APageBreakDividesTheText()
    {
        using var docx = BuildDocument(Paragraph("Before"), PageBreak(), Paragraph("After"));

        var pages = DocumentConverter.GetPageTexts(docx, Options);

        await Assert.That(pages.Count).IsEqualTo(2);
        await Assert.That(pages[0]).IsEqualTo("Before");
        await Assert.That(pages[1]).IsEqualTo("After");
    }

    // Nothing is told where to break here, so where it does is the fragmenter's to say. What can be
    // asserted without writing its answer down is that every paragraph is on exactly one page, and
    // that the pages read in the order of the document.
    [Test]
    public async Task ParagraphsThatOverflowAreDividedInOrder()
    {
        var expected = Enumerable.Range(0, 120).Select(_ => $"Paragraph {_}").ToList();
        using var docx = BuildDocument(expected.Select(Paragraph).ToArray());

        var pages = DocumentConverter.GetPageTexts(docx, Options);

        await Assert.That(pages.Count).IsGreaterThan(1);
        foreach (var page in pages)
        {
            await Assert.That(page).IsNotEmpty();
        }

        await Assert.That(string.Join('\n', pages)).IsEqualTo(string.Join('\n', expected));
    }

    // A paragraph longer than a page is on both, divided at the line the page ends on rather than
    // given whole to the page it starts on. Its wrapped lines come back as one line of text, so the
    // words of the two halves are the words of the paragraph.
    [Test]
    public async Task AParagraphOverAPageEndIsDividedWhereThePageEnds()
    {
        var words = Enumerable.Range(0, 2000).Select(_ => $"word{_}").ToList();
        using var docx = BuildDocument(Paragraph(string.Join(' ', words)));

        var pages = DocumentConverter.GetPageTexts(docx, Options);

        await Assert.That(pages.Count).IsGreaterThan(1);
        foreach (var page in pages)
        {
            await Assert.That(page.Contains('\n')).IsFalse();
        }

        await Assert.That(string.Join(' ', pages)).IsEqualTo(string.Join(' ', words));
    }

    [Test]
    public async Task ATableRowIsALineWithATabBetweenCells()
    {
        using var docx = BuildDocument(Paragraph("Above"), Table(("a", "b"), ("c", "d")), Paragraph("Below"));

        var pages = DocumentConverter.GetPageTexts(docx, Options);

        await Assert.That(pages.Count).IsEqualTo(1);
        await Assert.That(pages[0]).IsEqualTo("Above\na\tb\nc\td\nBelow");
    }

    // A page with nothing to read still has an entry, so the count is the number of pages and an
    // index is a page number less one.
    [Test]
    public async Task APageWithNoTextIsEmpty()
    {
        using var docx = BuildDocument(Paragraph("First"), PageBreak(), PageBreak(), Paragraph("Third"));

        var pages = DocumentConverter.GetPageTexts(docx, Options);

        await Assert.That(pages.Count).IsEqualTo(3);
        await Assert.That(pages[0]).IsEqualTo("First");
        await Assert.That(pages[1]).IsEqualTo("");
        await Assert.That(pages[2]).IsEqualTo("Third");
    }

    // The same pagination as the bookmarks get, so the two agree about which page something is on.
    [Test]
    public async Task ABookmarkIsOnThePageItsTextIsOn()
    {
        var paragraphs = Enumerable.Range(0, 120).Select(_ => Paragraph($"Paragraph {_}")).ToArray();
        paragraphs[119].Append(
            new W.BookmarkStart
            {
                Id = "1",
                Name = "target"
            },
            new W.BookmarkEnd
            {
                Id = "1"
            });
        using var docx = BuildDocument(paragraphs);

        var bookmarks = DocumentConverter.GetBookmarkPages(docx, Options);
        docx.Position = 0;
        var pages = DocumentConverter.GetPageTexts(docx, Options);

        await Assert.That(pages[bookmarks["target"] - 1].Split('\n')).Contains("Paragraph 119");
    }

    // A PAGEREF reads the page its bookmark lands on, so a document holding one is laid out a second
    // time with the field's result filled in, and that pass rebuilds the paragraph holding it. The
    // text is read off the rebuilt paragraph's lines, so a cell holding only the field reads as the
    // renderer draws it rather than as nothing (Parchment's contents table read "Delivery\t").
    [Test]
    public async Task APageReferenceInACellReadsThePageItsBookmarkIsOn()
    {
        var target = Paragraph("Target");
        target.PrependChild(new W.ParagraphProperties(new W.PageBreakBefore()));
        target.Append(
            new W.BookmarkStart
            {
                Id = "1",
                Name = "target"
            },
            new W.BookmarkEnd
            {
                Id = "1"
            });
        var table = Table(("Target", ""));
        table.GetFirstChild<W.TableRow>()!.Elements<W.TableCell>().Last().ReplaceChild(Field(" PAGEREF target \\h "), table.Descendants<W.Paragraph>().Last());
        using var docx = BuildDocument(table, target);

        var pages = DocumentConverter.GetPageTexts(docx, Options);

        await Assert.That(pages.Count).IsEqualTo(2);
        await Assert.That(pages[0]).IsEqualTo("Target\t2");
        await Assert.That(pages[1]).IsEqualTo("Target");
    }

    // A w:p holding only a page break is no paragraph of the model, so a bookmark that counted w:p
    // elements found the paragraph after its own, and past the last one none: the field kept the
    // "9" it was saved with.
    [Test]
    public async Task APageReferenceAfterAPageBreakReadsThePageItsBookmarkIsOn()
    {
        var table = Table(("Target", ""));
        table.GetFirstChild<W.TableRow>()!.Elements<W.TableCell>().Last().ReplaceChild(Field(" PAGEREF target \\h "), table.Descendants<W.Paragraph>().Last());
        using var docx = BuildDocument(table, PageBreak(), Bookmarked(Paragraph("Target")));

        var pages = DocumentConverter.GetPageTexts(docx, Options);
        docx.Position = 0;
        var bookmarks = DocumentConverter.GetBookmarkPages(docx, Options);

        await Assert.That(pages.Count).IsEqualTo(2);
        await Assert.That(pages[0]).IsEqualTo("Target\t2");
        await Assert.That(bookmarks["target"]).IsEqualTo(2);
    }

    // The other way round: a w:p with a page break between its text is two paragraphs of the model,
    // so counting w:p elements found the paragraph before the bookmark's own.
    [Test]
    public async Task ABookmarkAfterAParagraphSplitByAPageBreakIsOnItsOwnPage()
    {
        var split = Paragraph("Before");
        split.Append(
            new W.Run(
                new W.Break
                {
                    Type = W.BreakValues.Page
                }),
            new W.Run(new W.Text("After")));
        var target = Bookmarked(Paragraph("Target"));
        target.PrependChild(new W.ParagraphProperties(new W.PageBreakBefore()));
        using var docx = BuildDocument(split, target);

        var pages = DocumentConverter.GetPageTexts(docx, Options);
        docx.Position = 0;
        var bookmarks = DocumentConverter.GetBookmarkPages(docx, Options);

        await Assert.That(pages.Count).IsEqualTo(3);
        await Assert.That(pages[2]).IsEqualTo("Target");
        await Assert.That(bookmarks["target"]).IsEqualTo(3);
    }

    static W.Paragraph Bookmarked(W.Paragraph paragraph)
    {
        paragraph.Append(
            new W.BookmarkStart
            {
                Id = "1",
                Name = "target"
            },
            new W.BookmarkEnd
            {
                Id = "1"
            });
        return paragraph;
    }

    // The same second pass rebuilds a body paragraph holding a PAGE field.
    [Test]
    public async Task APageFieldInTheBodyReadsThePageItIsOn()
    {
        using var docx = BuildDocument(Paragraph("First"), PageBreak(), Field(" PAGE "));

        var pages = DocumentConverter.GetPageTexts(docx, Options);

        await Assert.That(pages.Count).IsEqualTo(2);
        await Assert.That(pages[0]).IsEqualTo("First");
        await Assert.That(pages[1]).IsEqualTo("2");
    }

    // A complex field whose cached result, "9", is stale, so a live result tells itself apart.
    internal static W.Paragraph Field(string instruction) =>
    [
        with(
            new W.Run(
                new W.FieldChar
                {
                    FieldCharType = W.FieldCharValues.Begin
                }),
            new W.Run(
                new W.FieldCode(instruction)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }),
            new W.Run(
                new W.FieldChar
                {
                    FieldCharType = W.FieldCharValues.Separate
                }),
            new W.Run(new W.Text("9")),
            new W.Run(
                new W.FieldChar
                {
                    FieldCharType = W.FieldCharValues.End
                }))
    ];

    internal static MemoryStream BuildDocument(params OpenXmlElement[] content)
    {
        var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new W.Body();
            body.Append(content);
            mainPart.Document = [with(body)];
            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    internal static W.Paragraph Paragraph(string text) =>
        [with(new W.Run(new W.Text(text)))];

    internal static W.Paragraph PageBreak() =>
    [
        with(new W.Run(
            new W.Break
            {
                Type = W.BreakValues.Page
            }))
    ];

    static W.Table Table(params (string left, string right)[] rows)
    {
        var table = new W.Table(
            new W.TableProperties(
                new W.TableWidth
                {
                    Type = W.TableWidthUnitValues.Auto
                }),
            new W.TableGrid(new W.GridColumn(), new W.GridColumn()));
        foreach (var (left, right) in rows)
        {
            table.Append(
                new W.TableRow(
                    new W.TableCell(Paragraph(left)),
                    new W.TableCell(Paragraph(right))));
        }

        return table;
    }
}
