using System.Xml.Linq;

namespace Morph;

/// <summary>
/// Part-name arithmetic for an OPC package: resolving relationship targets, normalizing the names
/// <c>ZipArchive</c> hands back, and classifying the parts that describe the package rather than
/// belong to it. Shared by <see cref="DocumentCleaner"/> and <see cref="ImageCompressor"/>, which
/// both rewrite a package by copying it entry by entry.
/// </summary>
static class PackagePaths
{
    public const string ContentTypesPart = "[Content_Types].xml";

    public static bool IsExternal(XElement relationship) =>
        string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase);

    public static bool IsRelationshipPart(string partName) =>
        partName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) &&
        partName.Contains("_rels/", StringComparison.OrdinalIgnoreCase);

    public static bool IsContentTypesPart(string partName) =>
        partName.Equals(ContentTypesPart, StringComparison.OrdinalIgnoreCase);

    public static string Extension(string partName)
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
    public static string ResolvePartName(string relsPartName, string target)
    {
        if (target.StartsWith('/'))
        {
            return NormalizePartName(target);
        }

        return NormalizePartName(OwningDirectory(relsPartName) + target);
    }

    /// <summary>
    /// The folder a <c>_rels</c> part's relative targets resolve against — <c>word/</c> for
    /// <c>word/_rels/document.xml.rels</c>, and the package root for <c>_rels/.rels</c>. Includes
    /// the trailing slash, or is empty at the root.
    /// </summary>
    public static string OwningDirectory(string relsPartName)
    {
        var marker = relsPartName.LastIndexOf("_rels/", StringComparison.OrdinalIgnoreCase);
        return marker < 0 ? "" : relsPartName[..marker];
    }

    public static string NormalizePartName(string partName)
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
