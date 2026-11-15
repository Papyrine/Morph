using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Vml;
using DocumentFormat.OpenXml.Wordprocessing;
using static EditDocuments;
using static ReviewDocuments;

// A text box's text is written twice — the mc:Choice Word reads and the VML mc:Fallback older readers
// do — and an edit to the box leaves the two the same (TextBoxFallbacks). WordProbe's own text-box
// fixture, as Word wrote it: the box's paragraphs are 1 and 2 in the Choice, 3 and 4 in the Fallback.
public class TextBoxFallbackTests
{
    static byte[] Fixture() =>
        File.ReadAllBytes(System.IO.Path.Combine(ProjectFiles.ProjectDirectory, "..", "WordProbe", "Fixtures", "text-box.docx"));

    static string Reads(byte[] docx, int paragraph) =>
        string.Concat(DocumentOutline.Read(docx).Paragraphs[paragraph].Units.Where(_ => _.Editable).Select(_ => _.Text));

    [Test]
    public async Task TypingInTheBox_IsCopiedToItsFallback()
    {
        var edited = DocumentEditor.Rewrite(Fixture(), 1, [EditDocuments.Reads(T(0, "Box line one, typed"))], Plain).Document;

        await Assert.That(Reads(edited, 1)).IsEqualTo("Box line one, typed");
        await Assert.That(Reads(edited, 3)).IsEqualTo("Box line one, typed");
        await Assert.That(Reads(edited, 4)).IsEqualTo("Box line two");
    }

    // The fallback is still VML's: only what is in its text box is the Choice's.
    [Test]
    public async Task TheFallback_KeepsItsShape()
    {
        var edited = DocumentEditor.Rewrite(Fixture(), 2, [EditDocuments.Reads(T(0, "Box line 2"))], Plain).Document;

        var shapes = Read(edited, _ => _.MainDocumentPart!.Document!.Descendants<DocumentFormat.OpenXml.Vml.TextBox>().Count());
        await Assert.That(shapes).IsEqualTo(1);
        await Assert.That(Reads(edited, 4)).IsEqualTo("Box line 2");
    }

    // A paragraph Enter adds to the box is added to the fallback too, so the two keep one count.
    [Test]
    public async Task AParagraphAddedToTheBox_IsAddedToItsFallback()
    {
        var edited = DocumentEditor.Rewrite(Fixture(), 2, [EditDocuments.Reads(T(0, "Box line two")), EditDocuments.Reads(T(0, "Box line three"))], Plain).Document;

        var paragraphs = DocumentOutline.Read(edited).Paragraphs;
        await Assert.That(paragraphs.Count).IsEqualTo(9);
        await Assert.That(Reads(edited, 3)).IsEqualTo("Box line three");
        await Assert.That(Reads(edited, 6)).IsEqualTo("Box line three");
    }

    // Tracked, the fallback copies the revisions as they stand, and settling them settles both.
    [Test]
    public async Task ATrackedEdit_IsCopiedWithItsRevisions()
    {
        var edited = DocumentEditor.Rewrite(Fixture(), 1, [EditDocuments.Reads(T(0, "Box line 1"))], Tracked).Document;

        var accepted = ReviewEditor.ResolveAll(edited, accept: true);
        await Assert.That(Reads(accepted, 1)).IsEqualTo("Box line 1");
        await Assert.That(Reads(accepted, 3)).IsEqualTo("Box line 1");
        var rejected = ReviewEditor.ResolveAll(edited, accept: false);
        await Assert.That(Reads(rejected, 3)).IsEqualTo("Box line one");
    }

    // An edit elsewhere leaves a box that already agrees with its fallback as it was.
    [Test]
    public async Task AnEditElsewhere_LeavesTheBoxAlone()
    {
        var docx = Fixture();
        var before = Read(docx, _ => _.MainDocumentPart!.Document!.Descendants<AlternateContentFallback>().Single().OuterXml);

        var edited = DocumentEditor.Rewrite(docx, 5, [EditDocuments.Reads(T(0, "Second paragraph, edited."))], Plain).Document;

        var after = Read(edited, _ => _.MainDocumentPart!.Document!.Descendants<AlternateContentFallback>().Single().OuterXml);
        await Assert.That(after).IsEqualTo(before);
    }
}
