using System.IO.Compression;
using System.Xml.Linq;

namespace Morph;

/// <summary>
/// Removes parts from a DOCX package that carry no rendering information — see
/// <see cref="DocumentParts"/> for what can be selected. Templates authored in Word routinely
/// ship megabytes of such payload, dominated by the Explorer preview picture.
/// </summary>
/// <remarks>
/// Parts that survive are copied across verbatim; only the package relationships and
/// <c>[Content_Types].xml</c> are rewritten, and only to drop entries that would otherwise dangle.
/// The document body is never touched, so the rendered output of every format is unchanged.
/// </remarks>
public static class DocumentCleaner
{
    const string thumbnailRelationship = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail";
    const string contentTypesPart = "[Content_Types].xml";

    /// <summary>Removes <paramref name="parts"/> from the DOCX file at <paramref name="docxPath"/>, in place.</summary>
    /// <returns>The parts actually removed, or <see cref="DocumentParts.None"/> if the package held none of them.</returns>
    /// <remarks>The file is left byte-for-byte untouched when there is nothing to remove.</remarks>
    public static DocumentParts Remove(string docxPath, DocumentParts parts = DocumentParts.All)
    {
        using var cleaned = new MemoryStream();

        DocumentParts removed;
        using (var source = File.OpenRead(docxPath))
        {
            removed = Remove(source, cleaned, parts);
        }

        if (removed == DocumentParts.None)
        {
            return DocumentParts.None;
        }

        File.WriteAllBytes(docxPath, cleaned.ToArray());
        return removed;
    }

    /// <summary>Copies the DOCX package in <paramref name="source"/> to <paramref name="target"/>, less <paramref name="parts"/>.</summary>
    /// <returns>The parts actually removed, or <see cref="DocumentParts.None"/> if the package held none of them.</returns>
    /// <remarks>A complete package is always written, even when nothing matched.</remarks>
    public static DocumentParts Remove(Stream source, Stream target, DocumentParts parts = DocumentParts.All)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

        var removals = MatchParts(archive, parts, out var removed);
        var survivors = archive.Entries
            .Select(_ => NormalizePartName(_.FullName))
            .Where(_ => !removals.Contains(_))
            .ToList();
        var orphaned = OrphanedExtensions(removals, survivors);

        using var output = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            if (removals.Contains(NormalizePartName(entry.FullName)))
            {
                continue;
            }

            // entry order is preserved so [Content_Types].xml stays wherever the authoring tool
            // put it — first, for every package Word writes
            var copy = output.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            if (entry.LastWriteTime.Year >= 1980)
            {
                copy.LastWriteTime = entry.LastWriteTime;
            }

            using var content = entry.Open();
            using var destination = copy.Open();

            if (IsRelationshipPart(entry.FullName))
            {
                Rewrite(content, destination, document => DropRelationships(document, entry.FullName, removals));
            }
            else if (entry.FullName.Equals(contentTypesPart, StringComparison.OrdinalIgnoreCase))
            {
                Rewrite(content, destination, document => DropContentTypes(document, removals, orphaned));
            }
            else
            {
                content.CopyTo(destination);
            }
        }

        return removed;
    }

    /// <summary>Reports which of <paramref name="parts"/> the package at <paramref name="docxPath"/> currently carries.</summary>
    public static DocumentParts Find(string docxPath, DocumentParts parts = DocumentParts.All)
    {
        using var source = File.OpenRead(docxPath);
        return Find(source, parts);
    }

    /// <summary>Reports which of <paramref name="parts"/> the package in <paramref name="docxStream"/> currently carries.</summary>
    public static DocumentParts Find(Stream docxStream, DocumentParts parts = DocumentParts.All)
    {
        using var archive = new ZipArchive(docxStream, ZipArchiveMode.Read, leaveOpen: true);
        MatchParts(archive, parts, out var found);
        return found;
    }

    static HashSet<string> MatchParts(ZipArchive archive, DocumentParts parts, out DocumentParts matched)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        matched = DocumentParts.None;

        foreach (var entry in archive.Entries)
        {
            var part = Classify(entry.FullName);
            if (part == DocumentParts.None ||
                !parts.HasFlag(part))
            {
                continue;
            }

            names.Add(NormalizePartName(entry.FullName));
            matched |= part;
        }

        if (!parts.HasFlag(DocumentParts.Thumbnail))
        {
            return names;
        }

        // the preview picture is identified by its relationship type rather than its name, so a
        // package that stores it outside docProps/ is still matched. A target with no part behind
        // it is kept in the set too, so the dangling relationship still gets dropped.
        foreach (var target in ThumbnailTargets(archive))
        {
            names.Add(target);
            matched |= DocumentParts.Thumbnail;
        }

        return names;
    }

    static DocumentParts Classify(string partName)
    {
        var normalized = NormalizePartName(partName);

        if (normalized.StartsWith("docProps/thumbnail", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentParts.Thumbnail;
        }

        if (normalized.StartsWith("word/glossary/", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentParts.Glossary;
        }

        if (normalized.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentParts.CustomXml;
        }

        if (normalized.Equals("word/people.xml", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentParts.RevisionAuthors;
        }

        return DocumentParts.None;
    }

    static IEnumerable<string> ThumbnailTargets(ZipArchive archive)
    {
        var rels = archive.GetEntry("_rels/.rels");
        if (rels is null)
        {
            yield break;
        }

        using var content = rels.Open();
        foreach (var relationship in XDocument.Load(content).Root!.Elements())
        {
            if (relationship.Name.LocalName != "Relationship" ||
                relationship.Attribute("Type")?.Value != thumbnailRelationship)
            {
                continue;
            }

            var target = relationship.Attribute("Target")?.Value;
            if (target is not null &&
                !IsExternal(relationship))
            {
                yield return ResolvePartName("_rels/.rels", target);
            }
        }
    }

    static void DropRelationships(XDocument document, string relsPartName, HashSet<string> removals)
    {
        foreach (var relationship in document.Root!.Elements().ToList())
        {
            if (relationship.Name.LocalName != "Relationship" ||
                IsExternal(relationship))
            {
                continue;
            }

            var target = relationship.Attribute("Target")?.Value;
            if (target is not null &&
                removals.Contains(ResolvePartName(relsPartName, target)))
            {
                relationship.Remove();
            }
        }
    }

    static void DropContentTypes(XDocument document, HashSet<string> removals, HashSet<string> orphaned)
    {
        foreach (var declaration in document.Root!.Elements().ToList())
        {
            if (declaration.Name.LocalName == "Override")
            {
                var partName = declaration.Attribute("PartName")?.Value;
                if (partName is not null &&
                    removals.Contains(NormalizePartName(partName)))
                {
                    declaration.Remove();
                }
            }
            else if (declaration.Name.LocalName == "Default")
            {
                var extension = declaration.Attribute("Extension")?.Value;
                if (extension is not null &&
                    orphaned.Contains(extension))
                {
                    declaration.Remove();
                }
            }
        }
    }

    /// <summary>
    /// Extensions that only the removed parts were using, and so whose <c>Default</c> content-type
    /// declaration is now dead. Extensions that were already unused before the removal are left
    /// alone — tidying those is not this method's business.
    /// </summary>
    static HashSet<string> OrphanedExtensions(HashSet<string> removals, IEnumerable<string> survivors)
    {
        var extensions = removals
            .Select(Extension)
            .Where(_ => _.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        extensions.ExceptWith(survivors.Select(Extension));
        return extensions;
    }

    static void Rewrite(Stream content, Stream destination, Action<XDocument> edit)
    {
        var document = XDocument.Load(content);
        edit(document);

        var settings = new XmlWriterSettings
        {
            // Word writes these parts as UTF-8 with a BOM and no pretty-printing
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            Indent = false,
        };

        using var writer = XmlWriter.Create(destination, settings);
        document.Save(writer);
    }

    static bool IsExternal(XElement relationship) =>
        string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase);

    static bool IsRelationshipPart(string partName) =>
        partName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) &&
        partName.Contains("_rels/", StringComparison.OrdinalIgnoreCase);

    static string Extension(string partName)
    {
        var dot = partName.LastIndexOf('.');
        return dot < 0 ? "" : partName[(dot + 1)..];
    }

    /// <summary>
    /// Resolves a relationship <c>Target</c> to a package-root-relative part name. Targets are
    /// either absolute (<c>/docProps/thumbnail.emf</c>) or relative to the folder owning the
    /// <c>_rels</c> directory, so <c>word/_rels/document.xml.rels</c> resolves
    /// <c>../customXml/item1.xml</c> against <c>word/</c>.
    /// </summary>
    static string ResolvePartName(string relsPartName, string target)
    {
        if (target.StartsWith('/'))
        {
            return NormalizePartName(target);
        }

        var marker = relsPartName.LastIndexOf("_rels/", StringComparison.OrdinalIgnoreCase);
        var directory = marker < 0 ? "" : relsPartName[..marker];
        return NormalizePartName(directory + target);
    }

    static string NormalizePartName(string partName)
    {
        var segments = new List<string>();
        foreach (var segment in partName.Replace('\\', '/').Split('/'))
        {
            if (segment.Length == 0 ||
                segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }
}
