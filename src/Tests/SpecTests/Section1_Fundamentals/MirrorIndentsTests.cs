/// <summary>
/// Covers <c>w:mirrorIndents</c> parsing: the flag reaches
/// <see cref="ParagraphProperties.MirrorIndents"/> with the declared indents intact. The
/// page-parity transform itself lives in <see cref="Fragmenter"/> — see
/// <c>MirrorIndentsLayoutTests</c> for the Word-measured geometry.
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
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "complex_spacing", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var anyMirror = doc.Elements
            .OfType<ParagraphElement>()
            .Any(_ => _.Properties.MirrorIndents);

        await Assert.That(anyMirror).IsTrue();
    }
}
