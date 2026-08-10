/// <summary>
/// Tests for a:xfrm/@rot parsing.
/// </summary>
public class ImageRotationTests
{
    [Test]
    public async Task DocumentParser_ParsesInlineImageRotation()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "image_rotation", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var run = doc.Elements
            .OfType<ParagraphElement>()
            .SelectMany(_ => _.Runs)
            .Single(_ => _.InlineImageData is { Length: > 0 });

        await Assert.That(run.InlineImageRotationDegrees).IsEqualTo(45);
    }

    [Test]
    public async Task DocumentParser_NoRotation_DefaultsToZero()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "inline_image", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var run = doc.Elements
            .OfType<ParagraphElement>()
            .SelectMany(_ => _.Runs)
            .Single(_ => _.InlineImageData is { Length: > 0 });

        await Assert.That(run.InlineImageRotationDegrees).IsEqualTo(0);
    }
}
