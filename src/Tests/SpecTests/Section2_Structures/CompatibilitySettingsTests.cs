/// <summary>
/// Tests for CompatibilitySettings parsing as specified in MS-DOCX.
/// Tests parsing of w:compat section from settings.xml.
/// </summary>
public class CompatibilitySettingsTests
{
    [Test]
    public async Task CompatibilitySettings_Defaults_AreCorrect()
    {
        var settings = new CompatibilitySettings();

        await Assert.That(settings.CompatibilityMode).IsEqualTo(15);
        await Assert.That(settings.UseLegacyTableLineSpacing).IsFalse();
        await Assert.That(settings.AddLineSpacingToTableCells).IsTrue();
    }

    [Test]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(14)]
    [Arguments(15)]
    public async Task CompatibilitySettings_CompatibilityMode_CanBeSet(int mode)
    {
        var settings = new CompatibilitySettings { CompatibilityMode = mode };
        await Assert.That(settings.CompatibilityMode).IsEqualTo(mode);
    }

    [Test]
    [Arguments(11, true)]
    [Arguments(12, true)]
    [Arguments(14, true)]
    [Arguments(15, false)]
    [Arguments(16, false)]
    public async Task CompatibilitySettings_UseLegacyTableLineSpacing_BasedOnMode(int mode, bool expectedLegacy)
    {
        var settings = new CompatibilitySettings { CompatibilityMode = mode };
        await Assert.That(settings.UseLegacyTableLineSpacing).IsEqualTo(expectedLegacy);
    }

    [Test]
    [Arguments(11, false)]
    [Arguments(12, false)]
    [Arguments(14, false)]
    [Arguments(15, true)]
    [Arguments(16, true)]
    public async Task CompatibilitySettings_AddLineSpacingToTableCells_BasedOnMode(int mode, bool expectedAdd)
    {
        var settings = new CompatibilitySettings { CompatibilityMode = mode };
        await Assert.That(settings.AddLineSpacingToTableCells).IsEqualTo(expectedAdd);
    }

    [Test]
    public async Task DocumentParser_ParsesCompatibilityMode15()
    {
        // Parse resumes/01 which has compatibility mode 15
        var inputsDir = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "resumes", "01");
        var inputFile = Path.Combine(inputsDir, "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.Compatibility).IsNotNull();
        await Assert.That(doc.Compatibility.CompatibilityMode).IsEqualTo(15);
        await Assert.That(doc.Compatibility.AddLineSpacingToTableCells).IsTrue();
        await Assert.That(doc.Compatibility.UseLegacyTableLineSpacing).IsFalse();
    }

    [Test]
    public async Task DocumentParser_DefaultsToMode12_WhenUndeclared()
    {
        // multiple_pages declares no compatibilityMode; Word treats such documents as
        // mode 12 (ECMA-376), not as modern documents.
        var inputsDir = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "multiple_pages");
        var inputFile = Path.Combine(inputsDir, "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.Compatibility.CompatibilityMode).IsEqualTo(12);
        await Assert.That(doc.Compatibility.UseLegacyTableLineSpacing).IsTrue();
    }

    [Test]
    public async Task DocumentParser_ParsesCompatibilityMode14()
    {
        // Parse compatibility_mode_14 which has compatibility mode 14 (Word 2010)
        var inputsDir = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "compatibility_mode_14");
        var inputFile = Path.Combine(inputsDir, "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.Compatibility).IsNotNull();
        await Assert.That(doc.Compatibility.CompatibilityMode).IsEqualTo(14);
        await Assert.That(doc.Compatibility.AddLineSpacingToTableCells).IsFalse();
        await Assert.That(doc.Compatibility.UseLegacyTableLineSpacing).IsTrue();
    }
}
