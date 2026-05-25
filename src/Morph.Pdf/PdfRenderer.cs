using System.Text;
using System.Text.RegularExpressions;

namespace Morph;

/// <summary>
/// Renders a parsed document to a PDF byte array using PdfSharp. Shared entry point for the
/// DOCX → PDF and HTML → PDF public converters. Output is made byte-reproducible (see
/// <see cref="MakeDeterministic"/> / <see cref="NormalizeFontSubsetTags"/>) so it can be snapshot-tested.
/// </summary>
static class PdfRenderer
{
    public static byte[] Render(ParsedDocument document, ConversionOptions? options)
    {
        options ??= new();
        var context = new PdfRenderContext(
            document.PageSettings,
            document.Compatibility,
            options.FontWidthScale,
            options.FontFallback,
            options.FontDirectory);

        var renderer = new PdfPageRenderer(context);
        renderer.RenderDocument(document);

        MakeDeterministic(context.Document);

        using var stream = new MemoryStream();
        context.Document.Save(stream, closeStream: false);
        return Normalize(stream.ToArray());
    }

    // A PDF's CreationDate/ModDate (stamped with DateTime.Now) and trailer /ID (a fresh GUID) vary
    // per save, so identical input produces different bytes. Pin them to fixed values.
    static readonly DateTime fixedTimestamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    const string fixedDocumentId = "MorphDeterminist";

    static void MakeDeterministic(PdfSharp.Pdf.PdfDocument document)
    {
        document.Info.CreationDate = fixedTimestamp;
        document.Info.ModificationDate = fixedTimestamp;
        document.Internals.FirstDocumentID = fixedDocumentId;
        document.Internals.SecondDocumentID = fixedDocumentId;
    }

    // PdfSharp prefixes each embedded font subset with a random 6-uppercase-letter tag (e.g.
    // "YZGTLG+Aptos") generated from a GUID, and writes random XMP DocumentID/InstanceID UUIDs —
    // neither has an override hook, and they're the last sources of per-save variance. Remap subset
    // tags to deterministic ones (AAAAAA, AAAAAB, … by first appearance) and pin the UUIDs. Both
    // patterns are tightly anchored so the binary (FlateDecode) streams are never touched; Latin1
    // round-trips every byte losslessly.
    static readonly Regex subsetTagPattern = new(@"(/BaseFont|/FontName)/([A-Z]{6})\+", RegexOptions.Compiled);
    static readonly Regex xmpUuidPattern = new(@"uuid:[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
    const string fixedUuid = "uuid:00000000-0000-0000-0000-000000000000";

    static byte[] Normalize(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var map = new Dictionary<string, string>();

        text = subsetTagPattern.Replace(text, match =>
        {
            var original = match.Groups[2].Value;
            if (!map.TryGetValue(original, out var replacement))
            {
                replacement = DeterministicTag(map.Count);
                map[original] = replacement;
            }

            return $"{match.Groups[1].Value}/{replacement}+";
        });

        text = xmpUuidPattern.Replace(text, fixedUuid);

        return Encoding.Latin1.GetBytes(text);
    }

    static string DeterministicTag(int index)
    {
        var tag = new char[6];
        for (var position = 5; position >= 0; position--)
        {
            tag[position] = (char) ('A' + index % 26);
            index /= 26;
        }

        return new(tag);
    }
}
