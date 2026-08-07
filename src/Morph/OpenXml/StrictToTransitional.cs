using System.IO.Compression;

namespace Morph;

/// <summary>
/// Adds ISO/IEC 29500 <em>Strict</em> DOCX support to the parser.
///
/// The Open XML SDK opens a Strict package fine — it maps the relationship-type namespaces to their
/// Transitional equivalents on open — but throws <see cref="InvalidDataException"/> the moment it
/// materialises the strongly-typed DOM (<c>mainPart.Document</c>), because the part <em>content</em>
/// is in the Strict namespaces (e.g. <c>http://purl.oclc.org/ooxml/wordprocessingml/main</c>) rather
/// than the Transitional namespaces the generated classes bind to. Word writes Strict when the author
/// picks "Strict Open XML Document"; Aspose.Words writes it for <c>OoxmlCompliance.Iso29500_2008_Strict</c>.
///
/// <see cref="Normalize"/> peeks the incoming stream and, only when it is Strict, buffers it and
/// rewrites the Strict namespaces to Transitional across every XML part in a single
/// <see cref="ZipArchive"/> pass. The rest of <see cref="DocumentParser"/> is entirely
/// Transitional-typed and works unchanged. Transitional documents are handed straight back — the same
/// stream instance, with no copy.
/// </summary>
static class StrictToTransitional
{
    const string strictMarker = "http://purl.oclc.org/ooxml/";
    const string strictWordprocessingMain = "http://purl.oclc.org/ooxml/wordprocessingml/main";

    static readonly UTF8Encoding utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Strict -> Transitional namespace map (ECMA-376 / ISO 29500), taken verbatim from the Open XML
    // SDK's OpenXmlNamespaceResolver. The relationship entry (officeDocument/relationships) also covers
    // the `Type` attributes in the .rels parts by prefix, so those are rewritten here rather than left
    // to the SDK's on-open remapping. Sorted longest-first so a shorter key can never partially rewrite
    // a longer URI.
    static readonly (string strict, string transitional)[] map =
        new (string strict, string transitional)[]
        {
            ("http://purl.oclc.org/ooxml/wordprocessingml/main", "http://schemas.openxmlformats.org/wordprocessingml/2006/main"),
            ("http://purl.oclc.org/ooxml/drawingml/main", "http://schemas.openxmlformats.org/drawingml/2006/main"),
            ("http://purl.oclc.org/ooxml/drawingml/picture", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
            ("http://purl.oclc.org/ooxml/drawingml/wordprocessingDrawing", "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"),
            ("http://purl.oclc.org/ooxml/drawingml/chart", "http://schemas.openxmlformats.org/drawingml/2006/chart"),
            ("http://purl.oclc.org/ooxml/drawingml/chartDrawing", "http://schemas.openxmlformats.org/drawingml/2006/chartDrawing"),
            ("http://purl.oclc.org/ooxml/drawingml/diagram", "http://schemas.openxmlformats.org/drawingml/2006/diagram"),
            ("http://purl.oclc.org/ooxml/drawingml/spreadsheetDrawing", "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"),
            ("http://purl.oclc.org/ooxml/drawingml/lockedCanvas", "http://schemas.openxmlformats.org/drawingml/2006/lockedCanvas"),
            ("http://purl.oclc.org/ooxml/drawingml/compatibility", "http://schemas.openxmlformats.org/drawingml/2006/compatibility"),
            ("http://purl.oclc.org/ooxml/officeDocument/math", "http://schemas.openxmlformats.org/officeDocument/2006/math"),
            ("http://purl.oclc.org/ooxml/officeDocument/bibliography", "http://schemas.openxmlformats.org/officeDocument/2006/bibliography"),
            ("http://purl.oclc.org/ooxml/officeDocument/customProperties", "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"),
            ("http://purl.oclc.org/ooxml/officeDocument/extendedProperties", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
            ("http://purl.oclc.org/ooxml/officeDocument/customXmlDataProps", "http://schemas.openxmlformats.org/officeDocument/2006/customXmlDataProps"),
            ("http://purl.oclc.org/ooxml/officeDocument/customXml", "http://schemas.openxmlformats.org/officeDocument/2006/customXml"),
            ("http://purl.oclc.org/ooxml/officeDocument/docPropsVTypes", "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"),
            ("http://purl.oclc.org/ooxml/officeDocument/sharedTypes", "http://schemas.openxmlformats.org/officeDocument/2006/sharedTypes"),
            ("http://purl.oclc.org/ooxml/officeDocument/relationships", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"),
            ("http://purl.oclc.org/ooxml/schemaLibrary/main", "http://schemas.openxmlformats.org/schemaLibrary/2006/main"),
            ("http://purl.oclc.org/ooxml/spreadsheetml/main", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"),
            ("http://purl.oclc.org/ooxml/presentationml/main", "http://schemas.openxmlformats.org/presentationml/2006/main"),
            ("http://purl.oclc.org/ooxml/descriptions/base", "http://descriptions.openxmlformats.org/description/base"),
            ("http://purl.oclc.org/ooxml/descriptions/full", "http://descriptions.openxmlformats.org/description/full"),
        }
        .OrderByDescending(_ => _.strict.Length)
        .ToArray();

    /// <summary>
    /// If <paramref name="input"/> is a Strict-format DOCX, returns a new seekable stream (positioned
    /// at 0) with its namespaces rewritten to Transitional; the caller owns and must dispose it.
    /// Otherwise returns <paramref name="input"/> unchanged — no buffer, no copy — leaving ownership
    /// with the caller. Compare the result against <paramref name="input"/> by reference to tell which
    /// happened. <paramref name="input"/> must be seekable and is left at its original position.
    /// </summary>
    public static Stream Normalize(Stream input)
    {
        if (!IsStrict(input))
        {
            return input;
        }

        var buffer = new MemoryStream();
        input.CopyTo(buffer);
        Rewrite(buffer);
        buffer.Position = 0;
        return buffer;
    }

    // Peeks word/document.xml's root namespace without materialising the DOM, restoring the stream
    // position afterwards. Only a positively-identified Strict root triggers the rewrite; anything else
    // (Transitional, or an unexpected layout) passes straight through to the SDK, which handles it or
    // reports the canonical error.
    static bool IsStrict(Stream input)
    {
        var start = input.Position;
        try
        {
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            var entry = zip.GetEntry("word/document.xml");
            if (entry is null)
            {
                return false;
            }

            using var stream = entry.Open();
            using var reader = XmlReader.Create(
                stream,
                new()
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    return reader.NamespaceURI == strictWordprocessingMain;
                }
            }

            return false;
        }
        finally
        {
            input.Position = start;
        }
    }

    // Rewrites every XML/relationship part in place, mutating the buffered package directly at the ZIP
    // layer — no second WordprocessingDocument open/save round trip. Parts carrying no Strict namespace
    // (media, and the shared content-types/packaging parts) are left byte-for-byte untouched.
    static void Rewrite(MemoryStream buffer)
    {
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var entry in zip.Entries)
        {
            if (!IsXml(entry.FullName))
            {
                continue;
            }

            string xml;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                xml = reader.ReadToEnd();
            }

            if (!xml.Contains(strictMarker, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (strict, transitional) in map)
            {
                xml = xml.Replace(strict, transitional, StringComparison.Ordinal);
            }

            using var target = entry.Open();
            target.SetLength(0);
            using var writer = new StreamWriter(target, utf8NoBom);
            writer.Write(xml);
        }
    }

    static bool IsXml(string name) =>
        name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);
}
