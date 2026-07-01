using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// Covers <c>StrictToTransitional.Normalize</c>: ISO 29500 Strict packages are rewritten to
/// Transitional so the SDK's typed DOM binds, while Transitional packages pass through untouched
/// (same instance, no copy). Also exercises the ownership contract through <c>DocumentParser.Parse</c>.
/// </summary>
public class StrictToTransitionalTests
{
    const string strictWordNs = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    const string strictRelType = "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument";
    const string transitionalWordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    const string transitionalRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    [Test]
    public async Task RewritesStrictNamespacesToTransitional()
    {
        using var input = new MemoryStream(BuildDocx(strictWordNs, strictRelType, "Strict hello"));

        using var normalized = StrictToTransitional.Normalize(input);

        // A Strict document produces a fresh buffer, never the input stream.
        await Assert.That(ReferenceEquals(normalized, input)).IsFalse();
        await Assert.That(normalized.Position).IsEqualTo(0L);

        var bytes = ReadAll(normalized);

        // The generated classes bind only to Transitional namespaces, so the typed DOM materialising
        // at all proves the content namespaces were rewritten.
        using (var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false))
        {
            await Assert.That(doc.MainDocumentPart!.Document!.Body!.InnerText).IsEqualTo("Strict hello");
        }

        // No Strict namespace survives in any part — content or relationship.
        foreach (var (_, text) in ReadXmlEntries(bytes))
        {
            await Assert.That(text.Contains("purl.oclc.org")).IsFalse();
        }
    }

    [Test]
    public async Task LeavesTransitionalDocumentUntouched()
    {
        using var input = new MemoryStream(BuildDocx(transitionalWordNs, transitionalRelType, "Transitional hello"));

        var normalized = StrictToTransitional.Normalize(input);

        // Transitional is the common case: the input is handed straight back — no buffer, no copy.
        await Assert.That(ReferenceEquals(normalized, input)).IsTrue();
        await Assert.That(input.Position).IsEqualTo(0L);

        using var doc = WordprocessingDocument.Open(normalized, false);
        await Assert.That(doc.MainDocumentPart!.Document!.Body!.InnerText).IsEqualTo("Transitional hello");
    }

    [Test]
    public async Task ParsesStrictDocumentEndToEnd()
    {
        // Drives the whole Parse path, including the finally-branch that disposes the rewritten buffer.
        using var input = new MemoryStream(BuildDocx(strictWordNs, strictRelType, "Strict body"));

        var document = new DocumentParser("Arial").Parse(input);

        await Assert.That(document.Elements).IsNotEmpty();
    }

    [Test]
    public async Task ParsesTransitionalDocumentEndToEnd()
    {
        // Transitional path: Parse returns the input verbatim and must not dispose the caller's stream.
        using var input = new MemoryStream(BuildDocx(transitionalWordNs, transitionalRelType, "Transitional body"));

        var document = new DocumentParser("Arial").Parse(input);

        await Assert.That(document.Elements).IsNotEmpty();
        await Assert.That(input.CanRead).IsTrue();
    }

    // A minimal single-paragraph package. Content types and the packaging-relationship namespace are
    // shared between Strict and Transitional, so they carry no Strict marker and must round-trip
    // untouched; only the word namespace and the relationship type differ between the two flavours.
    static byte[] BuildDocx(string wordNs, string relType, string text) =>
        BuildZip(
            ("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """),
            ("_rels/.rels",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="{relType}" Target="word/document.xml"/>
                </Relationships>
                """),
            ("word/document.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="{wordNs}">
                  <w:body>
                    <w:p><w:r><w:t>{text}</w:t></w:r></w:p>
                  </w:body>
                </w:document>
                """));

    static byte[] BuildZip(params (string name, string content)[] entries)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                using var writer = new StreamWriter(stream);
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    static IEnumerable<(string name, string text)> ReadXmlEntries(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(entry.Open());
                yield return (entry.FullName, reader.ReadToEnd());
            }
        }
    }

    static byte[] ReadAll(Stream stream)
    {
        stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
