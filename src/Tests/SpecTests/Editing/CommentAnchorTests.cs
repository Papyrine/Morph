using static EditDocuments;
using static ReviewDocuments;

// A comment's anchor deleted (WordProbe H-17, R-H-4): the whole of it takes the comment with it, part of
// it shrinks the anchor and keeps the comment, and tracked, the deletion is struck and the comment stays.
// Runs 0 to 3 are "The ", "quick" (the anchor), the comment's reference and " brown fox.".
public class CommentAnchorTests
{
    static byte[] Commented(string anchor = "quick") =>
        Build(
            P(
                R("The "),
                "<w:commentRangeStart w:id=\"0\"/>",
                R(anchor),
                "<w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r>",
                R(" brown fox.")),
            comments: "<w:comment w:id=\"0\" w:author=\"Bob\"><w:p><w:r><w:t>Note</w:t></w:r></w:p></w:comment>");

    [Test]
    public async Task TheWholeAnchorDeleted_TakesTheComment()
    {
        var result = DocumentEditor.Delete(Commented(), new(1, 0), new(1, 5), Plain);

        await Assert.That(DocumentReview.Read(result.Document).Comments).IsEmpty();
        await Assert.That(Xml(result.Document)).DoesNotContain("w:comment");
        await Assert.That(Describe(result.Document)).IsEqualTo("The  brown fox.¶");
    }

    // The same, typed away in the open paragraph and handed in.
    [Test]
    public async Task TheWholeAnchorTypedAway_TakesTheComment()
    {
        var edited = Rewrite(Commented(), 0, Plain, Reads(T(0, "The "), T(2, " brown fox.")));

        await Assert.That(DocumentReview.Read(edited).Comments).IsEmpty();
        await Assert.That(Describe(edited)).IsEqualTo("The  brown fox.¶");
    }

    [Test]
    public async Task PartOfTheAnchorDeleted_KeepsTheComment()
    {
        var result = DocumentEditor.Delete(Commented(), new(1, 4), new(1, 5), Plain);

        await Assert.That(DocumentReview.Read(result.Document).Comments.Single().Quote).IsEqualTo("quic");
    }

    [Test]
    public async Task TheWholeAnchorDeleted_Tracked_KeepsTheComment()
    {
        var result = DocumentEditor.Delete(Commented(), new(1, 0), new(1, 5), Tracked);

        await Assert.That(DocumentReview.Read(result.Document).Comments.Count).IsEqualTo(1);
        await Assert.That(Xml(result.Document)).Contains("<w:delText xml:space=\"preserve\">quick</w:delText>");
    }

    // A comment that anchored to nothing before the edit is not the edit's to take.
    [Test]
    public async Task ACommentOnNothing_IsLeftAsItWas()
    {
        var docx = Build(
            P(
                R("The "),
                "<w:commentRangeStart w:id=\"0\"/><w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r>",
                R("quick brown fox.")),
            comments: "<w:comment w:id=\"0\" w:author=\"Bob\"><w:p><w:r><w:t>Note</w:t></w:r></w:p></w:comment>");

        var result = DocumentEditor.Delete(docx, new(2, 0), new(2, 6), Plain);

        await Assert.That(DocumentReview.Read(result.Document).Comments.Count).IsEqualTo(1);
    }
}
