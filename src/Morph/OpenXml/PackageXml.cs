using System.Xml.Linq;

namespace Morph;

/// <summary>
/// Reads an XML part out of a package, edits it, and writes it back in the shape Word writes it —
/// UTF-8 with a byte-order mark and no pretty-printing. Shared by the two rewriters that copy a
/// package entry by entry.
/// </summary>
static class PackageXml
{
    public static void Rewrite(Stream content, Stream destination, Action<XDocument> edit)
    {
        var document = XDocument.Load(content);
        edit(document);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            Indent = false,
        };

        using var writer = XmlWriter.Create(destination, settings);
        document.Save(writer);
    }
}
