using static EditDocuments;
using static ReviewDocuments;

// Bookmarks, as Word writes them (Word-probed 2026-10-21, see DocumentEditor.Bookmarks.cs): a start
// before the text's first run and an end after its last, or the two together at a place.
public class BookmarkTests
{
    static byte[] Document() =>
        Build(P(R("Bookmarked words here")) + P(R("Second")));

    // Over selected text: the run is divided where the text starts and ends, the start before it and
    // the end after it.
    [Test]
    public async Task OverText_ItsStartAndEnd_StandEitherSide()
    {
        var result = DocumentEditor.AddBookmark(Document(), 0, 16, new(0, 0), new(0, 16), "Target", Plain);

        await Assert.That(Xml(result.Document)).StartsWith(
            "<w:p><w:bookmarkStart w:name=\"Target\" w:id=\"0\" /><w:r><w:t xml:space=\"preserve\">Bookmarked words</w:t></w:r><w:bookmarkEnd w:id=\"0\" /><w:r><w:t xml:space=\"preserve\"> here</w:t></w:r></w:p>");
    }

    // At a caret: the two together, where the caret is.
    [Test]
    public async Task AtAPlace_ItsStartAndEnd_StandTogether()
    {
        var result = DocumentEditor.AddBookmark(Document(), 1, 0, null, null, "Spot", Plain);

        await Assert.That(Xml(result.Document)).Contains(
            "<w:p><w:bookmarkStart w:name=\"Spot\" w:id=\"0\" /><w:bookmarkEnd w:id=\"0\" /><w:r><w:t xml:space=\"preserve\">Second</w:t></w:r></w:p>");
    }

    // A name already used moves to the new place, as Word's dialog moves it; another name is the next id.
    [Test]
    public async Task AName_AlreadyUsed_Moves()
    {
        var first = DocumentEditor.AddBookmark(Document(), 0, 0, null, null, "Spot", Plain);
        var other = DocumentEditor.AddBookmark(first.Document, 0, 0, null, null, "Other", Plain);
        var moved = DocumentEditor.AddBookmark(other.Document, 1, 0, null, null, "Spot", Plain);

        var outline = DocumentOutline.Read(moved.Document);
        await Assert.That(outline.Bookmarks.Select(_ => (_.Name, _.Paragraph, _.Offset))).IsEquivalentTo([("Other", 0, 0), ("Spot", 1, 0)]);
        await Assert.That(Xml(moved.Document)).Contains("<w:bookmarkStart w:name=\"Spot\" w:id=\"2\" />");
    }

    // The outline says where each starts: the place in its paragraph's text, or the paragraph's end.
    [Test]
    public async Task TheOutline_ListsWhereEachStarts()
    {
        var inside = DocumentEditor.AddBookmark(Document(), 0, 11, null, null, "Words", Plain);
        var atEnd = DocumentEditor.AddBookmark(inside.Document, 1, 6, null, null, "End", Plain);

        var bookmarks = DocumentOutline.Read(atEnd.Document).Bookmarks;

        await Assert.That(bookmarks.Select(_ => (_.Name, _.Paragraph, _.Offset))).IsEquivalentTo([("Words", 0, 11), ("End", 1, 6)]);
        await Assert.That(bookmarks.Any(_ => _.Hidden)).IsFalse();
    }

    [Test]
    public async Task Deleting_LeavesTheText()
    {
        var made = DocumentEditor.AddBookmark(Document(), 0, 16, new(0, 0), new(0, 16), "Target", Plain);

        var deleted = DocumentEditor.DeleteBookmark(made.Document, "Target", Plain);

        await Assert.That(Xml(deleted)).DoesNotContain("bookmark");
        await Assert.That(DocumentOutline.Read(deleted).Paragraphs[0].Units[0].Text + DocumentOutline.Read(deleted).Paragraphs[0].Units[1].Text).IsEqualTo("Bookmarked words here");
    }

    // Word's dialog: a letter first, then letters, numbers and underscores, at most 40.
    [Test]
    [Arguments("Target", true)]
    [Arguments("Part_2", true)]
    [Arguments("Überblick", true)]
    [Arguments("2nd", false)]
    [Arguments("_Toc12", false)]
    [Arguments("two words", false)]
    [Arguments("a-b", false)]
    [Arguments("", false)]
    [Arguments("A123456789012345678901234567890123456789", true)]
    [Arguments("A1234567890123456789012345678901234567890", false)]
    public async Task Names_AreWords(string name, bool taken)
    {
        await Assert.That(DocumentEditor.IsBookmarkName(name)).IsEqualTo(taken);
        if (!taken)
        {
            await Assert.That(() => DocumentEditor.AddBookmark(Document(), 0, 0, null, null, name, Plain)).Throws<InvalidOperationException>();
        }
    }
}
