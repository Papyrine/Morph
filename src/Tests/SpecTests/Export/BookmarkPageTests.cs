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

    static MemoryStream BuildDocument(int paragraphs, int? bookmarkAt, int? extraBookmarkAt = null, int? tableBeforeParagraph = null)
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
                    body.Append(Table());
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

    // Two rows of two cells: four more w:p elements that are not body-level paragraphs.
    static W.Table Table() =>
    [
        with(new W.TableProperties(
                new W.TableWidth
                {
                    Type = W.TableWidthUnitValues.Auto
                }),
            new W.TableGrid(new W.GridColumn(), new W.GridColumn()),
            Row("a", "b"),
            Row("c", "d"))
    ];

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
