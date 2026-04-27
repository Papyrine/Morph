using DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Covers <c>w:mirrorIndents</c> parsing. The flag indicates that left/right indents
/// should swap on even pages for mirror-printing layouts. Morph tracks the flag on
/// <see cref="ParagraphProperties.MirrorIndents"/>; the renderer doesn't currently
/// swap indents at draw time (parsed-but-not-applied — same status as
/// <see cref="ParagraphProperties.IsRightToLeft"/>).
/// </summary>
public class MirrorIndentsTests
{
    [Test]
    public async Task ParagraphProperties_DefaultMirrorIndents_IsFalse()
    {
        var props = new ParagraphProperties();

        await Assert.That(props.MirrorIndents).IsFalse();
    }

    [Test]
    public async Task DocumentParser_ParsesMirrorIndents_WhenPresentInPPr()
    {
        // Use the corpus's complex_spacing document — it carries w:mirrorIndents on
        // at least one paragraph.
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "complex_spacing", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var anyMirror = doc.Elements
            .OfType<ParagraphElement>()
            .Any(_ => _.Properties.MirrorIndents);

        await Assert.That(anyMirror).IsTrue();
    }
}
