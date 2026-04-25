/// <summary>
/// Tests for w:bookmarkStart parsing.
/// </summary>
public class BookmarksTests
{
    [Test]
    public async Task Bookmark_RequiresIdAndName()
    {
        var bookmark = new Bookmark { Id = "1", Name = "intro" };
        await Assert.That(bookmark.Id).IsEqualTo("1");
        await Assert.That(bookmark.Name).IsEqualTo("intro");
    }

    [Test]
    public async Task DocumentParser_CapturesBookmarks()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "cards", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.Bookmarks).IsNotEmpty();
        await Assert.That(doc.Bookmarks.Any(_ => _.Name == "_Hlk16025230")).IsTrue();
    }

    [Test]
    public async Task DocumentParser_BookmarkParagraphIndex_PointsAtEnclosingParagraph()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "cards", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        // At least one bookmark anchors inside a paragraph; every present index is non-negative.
        await Assert.That(doc.Bookmarks.Any(_ => _.ParagraphIndex.HasValue)).IsTrue();
        await Assert.That(doc.Bookmarks.All(_ => _.ParagraphIndex is null or >= 0)).IsTrue();
    }

    [Test]
    public async Task DocumentParser_NoBookmarks_EmptyList()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "all_caps", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.Bookmarks).IsEmpty();
    }
}
