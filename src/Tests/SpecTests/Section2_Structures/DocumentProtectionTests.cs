using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// Tests for w:documentProtection parsing.
/// </summary>
/// <remarks>
/// Protection changes nothing on the page: the <c>document_protection/01</c> scenario that preceded
/// these tests rendered byte-identically to <c>all_caps</c>, the same document unprotected, in every
/// output and in Word's own reference.
/// </remarks>
public class DocumentProtectionTests
{
    const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    static ParsedDocument Parse(string settings)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = [with($"<w:document {wNs}><w:body><w:p><w:r><w:t>Protected</w:t></w:r></w:p></w:body></w:document>")];
            main.AddNewPart<DocumentSettingsPart>().Settings = [with($"<w:settings {wNs}>{settings}</w:settings>")];
        }

        stream.Position = 0;
        return new DocumentParser().Parse(stream);
    }

    [Test]
    public async Task DocumentProtectionSettings_Defaults_AreNone()
    {
        var settings = new DocumentProtectionSettings();
        await Assert.That(settings.IsProtected).IsFalse();
        await Assert.That(settings.EditingMode).IsEqualTo(DocumentEditingMode.None);
    }

    [Test]
    [Arguments("readOnly", nameof(DocumentEditingMode.ReadOnly))]
    [Arguments("comments", nameof(DocumentEditingMode.Comments))]
    [Arguments("trackedChanges", nameof(DocumentEditingMode.TrackedChanges))]
    [Arguments("forms", nameof(DocumentEditingMode.Forms))]
    public async Task DocumentParser_ParsesEditingMode(string edit, string expected)
    {
        var doc = Parse($"""<w:documentProtection w:edit="{edit}" w:enforcement="true" />""");

        await Assert.That(doc.Protection.IsProtected).IsTrue();
        await Assert.That(doc.Protection.EditingMode.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task DocumentParser_NoProtection_DefaultsToNone()
    {
        var doc = Parse("");

        await Assert.That(doc.Protection.IsProtected).IsFalse();
    }
}
