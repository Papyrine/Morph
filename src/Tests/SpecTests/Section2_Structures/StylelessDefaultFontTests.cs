using DocumentFormat.OpenXml.Packaging;

// What a package with no styles part is set in, Word 16.0.20527 (2026-11-08, read off its XPS and
// Styles("Normal").Font): Aptos 12pt with nothing to say otherwise, and the theme's minor font at the
// same 12pt when the package has a theme. The build of October gave Calibri (docs/fidelity-audit.md).
public class StylelessDefaultFontTests
{
    const string wordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    const string drawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";

    static byte[] Package(string? minorFont)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            main.Document = [with($"<w:document xmlns:w=\"{wordNamespace}\"><w:body><w:p><w:r><w:t>Plain text.</w:t></w:r></w:p></w:body></w:document>")];
            if (minorFont != null)
            {
                main.AddNewPart<ThemePart>().Theme =
                [
                    with($"<a:theme xmlns:a=\"{drawingNamespace}\" name=\"Probe\"><a:themeElements><a:fontScheme name=\"Probe\">" +
                         $"<a:majorFont><a:latin typeface=\"{minorFont}\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
                         $"<a:minorFont><a:latin typeface=\"{minorFont}\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont>" +
                         "</a:fontScheme></a:themeElements></a:theme>")
                ];
            }
        }

        return stream.ToArray();
    }

    static RunProperties FirstRun(byte[] docx, string? defaultFont = null)
    {
        using var stream = new MemoryStream(docx);
        var document = new DocumentParser(defaultFont).Parse(stream);
        return document.Elements.OfType<ParagraphElement>().First().Runs.First(_ => _.Text.Length > 0).Properties;
    }

    [Test]
    public async Task NoStylesPart_IsSetInAptos12()
    {
        var properties = FirstRun(Package(null));

        await Assert.That(properties.FontFamily).IsEqualTo("Aptos");
        await Assert.That(properties.FontSizePoints).IsEqualTo(12.0);
    }

    [Test]
    public async Task NoStylesPart_WithATheme_IsSetInTheThemesMinorFont()
    {
        var properties = FirstRun(Package("Georgia"));

        await Assert.That(properties.FontFamily).IsEqualTo("Georgia");
        await Assert.That(properties.FontSizePoints).IsEqualTo(12.0);
    }

    // A caller's default font stands in for Word's built-in, not for what the document declares: the
    // theme is the document's own.
    [Test]
    public async Task ACallersDefaultFont_ReplacesTheBuiltIn_NotTheTheme()
    {
        await Assert.That(FirstRun(Package(null), "Arial").FontFamily).IsEqualTo("Arial");
        await Assert.That(FirstRun(Package("Georgia"), "Arial").FontFamily).IsEqualTo("Georgia");
    }
}
