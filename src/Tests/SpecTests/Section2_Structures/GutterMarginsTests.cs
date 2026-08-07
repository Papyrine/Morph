/// <summary>
/// Tests for w:pgMar/@w:gutter parsing.
/// </summary>
public class GutterMarginsTests
{
    [Test]
    public async Task PageSettings_DefaultGutter_IsZero()
    {
        var settings = new PageSettings();
        await Assert.That(settings.GutterPoints).IsEqualTo(0);
        await Assert.That(settings.GutterAtTop).IsFalse();
    }

    [Test]
    public async Task DocumentParser_ParsesGutter_FoldsIntoLeftMargin()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "gutter_margins", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        // 720 twips = 36 pt
        await Assert.That(doc.PageSettings.GutterPoints).IsEqualTo(36);
        await Assert.That(doc.PageSettings.GutterAtTop).IsFalse();
        // Left margin (72 pt) plus gutter (36 pt) = 108 pt effective.
        await Assert.That(doc.PageSettings.MarginLeft).IsEqualTo(108);
    }

    [Test]
    public async Task DocumentParser_NoGutter_LeftMarginUnchanged()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "all_caps", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.PageSettings.GutterPoints).IsEqualTo(0);
        await Assert.That(doc.PageSettings.MarginLeft).IsEqualTo(72);
    }
}
