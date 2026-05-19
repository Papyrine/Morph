/// <summary>
/// Tests for w:ins / w:del parsing.
/// </summary>
public class TrackedChangesTests
{
    [Test]
    public async Task TrackedChange_RequiredFields()
    {
        var change = new TrackedChange { Id = "1", Type = TrackedChangeType.Insertion, Text = "x" };
        await Assert.That(change.Type).IsEqualTo(TrackedChangeType.Insertion);
        await Assert.That(change.Text).IsEqualTo("x");
        await Assert.That(change.Author).IsNull();
    }

    [Test]
    public async Task DocumentParser_CapturesInsertionsAndDeletions()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "tracked_changes", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.TrackedChanges.Count).IsEqualTo(2);

        var insertion = doc.TrackedChanges.Single(_ => _.Type == TrackedChangeType.Insertion);
        await Assert.That(insertion.Author).IsEqualTo("Reviewer");
        await Assert.That(insertion.Text).IsEqualTo("inserted ");

        var deletion = doc.TrackedChanges.Single(_ => _.Type == TrackedChangeType.Deletion);
        await Assert.That(deletion.Text).IsEqualTo("removed.");
    }

    [Test]
    public async Task DocumentParser_NoTrackedChanges_EmptyList()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "all_caps", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.TrackedChanges).IsEmpty();
    }
}
