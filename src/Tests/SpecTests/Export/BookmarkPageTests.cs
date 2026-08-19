using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
// Morph has its own Run and Paragraph in scope here, so the OOXML ones are qualified.
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Cover for <see cref="DocumentConverter.GetBookmarkPages(Stream, ImageExportOptions)"/> — the
/// page a cross-reference resolves to, which only layout can answer.
/// </summary>
public class BookmarkPageTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static ImageExportOptions Options => new()
    {
        FontDirectory = fontsDirectory,
        DeterministicRendering = true
    };

    [Test]
    public async Task BookmarksOnTheFirstPageReportPageOne()
    {
        using var docx = BuildDocument(paragraphs: 1, bookmarkAt: 0);

        var pages = DocumentConverter.GetBookmarkPages(docx, Options);

        await Assert.That(pages["target"]).IsEqualTo(1);
    }

    // The point of the API: a bookmark far enough down the document reports the page it actually
    // landed on, not the page it was authored after.
    [Test]
    public async Task ABookmarkPastAPageBreakReportsTheLaterPage()
    {
        using var docx = BuildDocument(paragraphs: 120, bookmarkAt: 119);

        var pages = DocumentConverter.GetBookmarkPages(docx, Options);

        await Assert.That(pages["target"]).IsGreaterThan(1);
    }

    // A table's cells are paragraphs too. Counting them on one side of the join and not the other
    // silently shifts every bookmark below the table, which reads as a plausible page rather than a
    // missing one — so a document with a table ahead of the anchor is the case worth pinning.
    [Test]
    public async Task ABookmarkAfterATableStillReportsItsOwnPage()
    {
        using var docx = BuildDocument(3, 2, tableBeforeParagraph: 1);

        var pages = DocumentConverter.GetBookmarkPages(docx, Options);

        await Assert.That(pages.ContainsKey("target")).IsTrue();
        await Assert.That(pages["target"]).IsEqualTo(1);
    }

    // The other half of that join, and the half that was wrong: a bookmark INSIDE a cell. A cell's
    // lines hang off its placed row rather than off the page, so the paragraph-to-page map skipped
    // every paragraph in a table and the bookmark came back absent — not at a wrong page, at no page
    // at all, which a caller writing a PAGEREF renders as the field's placeholder text.
    //
    // The expected page is read from a bookmark on the paragraph directly after the table rather
    // than written down, so the fixture cannot drift out of agreement with the height model: what is
    // being asserted is that a cell resolves to the same page as the content beside it, and that it
    // is a real page rather than the page-1 default a half-fix would return.
    [Test]
    public async Task ABookmarkInsideATableCellReportsItsPage()
    {
        using var docx = BuildDocument(
            paragraphs: 120,
            bookmarkAt: null,
            extraBookmarkAt: 60,
            tableBeforeParagraph: 60,
            bookmarkInFirstCell: true);

        var pages = DocumentConverter.GetBookmarkPages(docx, Options);

        await Assert.That(pages.ContainsKey("target")).IsTrue();
        await Assert.That(pages["target"]).IsGreaterThan(1);
        await Assert.That(pages["target"]).IsEqualTo(pages["second"]);
    }

    // A cell can hold a table of its own, so the walk over placed items has to recurse rather than
    // step one level down. Without this the one-level form passes every other test here.
    [Test]
    public async Task ABookmarkInsideANestedTableCellReportsItsPage()
    {
        using var docx = BuildDocument(
            paragraphs: 120,
            bookmarkAt: null,
            extraBookmarkAt: 60,
            tableBeforeParagraph: 60,
            bookmarkInFirstCell: true,
            nestTable: true);

        var pages = DocumentConverter.GetBookmarkPages(docx, Options);

        await Assert.That(pages.ContainsKey("target")).IsTrue();
        await Assert.That(pages["target"]).IsEqualTo(pages["second"]);
    }

    [Test]
    public async Task ADocumentWithNoBookmarksReportsNothing()
    {
        using var docx = BuildDocument(paragraphs: 3, bookmarkAt: null);

        var pages = DocumentConverter.GetBookmarkPages(docx, Options);

        await Assert.That(pages).IsEmpty();
    }

    [Test]
    public async Task EveryBookmarkIsReported()
    {
        using var docx = BuildDocument(paragraphs: 3, bookmarkAt: 0, extraBookmarkAt: 2);

        var pages = DocumentConverter.GetBookmarkPages(docx, Options);

        await Assert.That(pages.Count).IsEqualTo(2);
        await Assert.That(pages.ContainsKey("target")).IsTrue();
        await Assert.That(pages.ContainsKey("second")).IsTrue();
    }

    static MemoryStream BuildDocument(
        int paragraphs,
        int? bookmarkAt,
        int? extraBookmarkAt = null,
        int? tableBeforeParagraph = null,
        bool bookmarkInFirstCell = false,
        bool nestTable = false)
    {
        var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new W.Body();
            for (var i = 0; i < paragraphs; i++)
            {
                if (i == tableBeforeParagraph)
                {
                    body.Append(Table(bookmarkInFirstCell, nestTable));
                }

                var paragraph = new W.Paragraph();
                if (i == bookmarkAt)
                {
                    Bookmark(paragraph, "target", 1);
                }

                if (i == extraBookmarkAt)
                {
                    Bookmark(paragraph, "second", 2);
                }

                paragraph.Append(new W.Run(new W.Text($"Paragraph {i}")));
                body.Append(paragraph);
            }

            mainPart.Document = [with(body)];
            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    // Two rows of two cells: four more w:p elements that are not body-level paragraphs. With
    // bookmarkInFirstCell the "target" bookmark moves into the top-left cell, and with nest that
    // cell holds a table of its own and the bookmark goes one level further down.
    static W.Table Table(bool bookmarkInFirstCell = false, bool nest = false) =>
    [
        with(new W.TableProperties(
                new W.TableWidth
                {
                    Type = W.TableWidthUnitValues.Auto
                }),
            new W.TableGrid(new W.GridColumn(), new W.GridColumn()),
            FirstRow(bookmarkInFirstCell, nest),
            Row("c", "d"))
    ];

    static W.TableRow FirstRow(bool bookmarkInFirstCell, bool nest)
    {
        if (!bookmarkInFirstCell)
        {
            return Row("a", "b");
        }

        var anchor = new W.Paragraph(new W.Run(new W.Text("a")));
        Bookmark(anchor, "target", 1);

        // A nested table's own cell is where the anchor goes, so the outer cell holds the table and
        // the trailing paragraph w:tc requires after one.
        var first = nest
            ? new W.TableCell(
                new W.Table(
                    new W.TableProperties(
                        new W.TableWidth
                        {
                            Type = W.TableWidthUnitValues.Auto
                        }),
                    new W.TableGrid(new W.GridColumn()),
                    new W.TableRow(new W.TableCell(anchor))),
                new W.Paragraph())
            : new W.TableCell(anchor);

        return [with(first, new W.TableCell(new W.Paragraph(new W.Run(new W.Text("b")))))];
    }

    static W.TableRow Row(string left, string right) =>
    [
        with(new W.TableCell(new W.Paragraph(new W.Run(new W.Text(left)))),
            new W.TableCell(new W.Paragraph(new W.Run(new W.Text(right)))))
    ];

    static void Bookmark(W.Paragraph paragraph, string name, int id)
    {
        paragraph.Append(
            new W.BookmarkStart
            {
                Id = id.ToString(),
                Name = name
            });
        paragraph.Append(
            new W.BookmarkEnd
            {
                Id = id.ToString()
            });
    }
}
