/// <summary>
/// Tests for w:documentProtection parsing.
/// </summary>
public class DocumentProtectionTests
{
    [Test]
    public async Task DocumentProtectionSettings_Defaults_AreNone()
    {
        var settings = new DocumentProtectionSettings();
        await Assert.That(settings.IsProtected).IsFalse();
        await Assert.That(settings.EditingMode).IsEqualTo(DocumentEditingMode.None);
    }

    [Test]
    public async Task DocumentParser_ParsesReadOnlyProtection()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "document_protection", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.Protection.IsProtected).IsTrue();
        await Assert.That(doc.Protection.EditingMode).IsEqualTo(DocumentEditingMode.ReadOnly);
    }

    [Test]
    public async Task DocumentParser_NoProtection_DefaultsToNone()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "all_caps", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.Protection.IsProtected).IsFalse();
    }
}
