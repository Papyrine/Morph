/// <summary>
/// Tests for word/comments.xml parsing.
/// </summary>
public class CommentsTests
{
    [Test]
    public async Task Comment_RequiredFields()
    {
        var comment = new Comment { Id = "1", Text = "hi" };
        await Assert.That(comment.Id).IsEqualTo("1");
        await Assert.That(comment.Text).IsEqualTo("hi");
        await Assert.That(comment.Author).IsNull();
    }

    [Test]
    public async Task DocumentParser_ParsesComments()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "comments", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.Comments.Count).IsEqualTo(1);
        var comment = doc.Comments[0];
        await Assert.That(comment.Id).IsEqualTo("1");
        await Assert.That(comment.Author).IsEqualTo("Reviewer");
        await Assert.That(comment.Text).IsEqualTo("Looks good to me.");
        await Assert.That(comment.Date).IsNotNull();
    }

    [Test]
    public async Task DocumentParser_NoComments_EmptyList()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "all_caps", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.Comments).IsEmpty();
    }
}
