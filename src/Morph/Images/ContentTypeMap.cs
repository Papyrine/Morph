using System.IO.Compression;
using System.Xml.Linq;

namespace Morph;

/// <summary>
/// The media type a package declares for each of its parts, read from <c>[Content_Types].xml</c>.
/// An <c>Override</c> names one part outright; otherwise a <c>Default</c> covers every part with
/// that file extension, which is how the media folders are almost always declared.
/// </summary>
sealed class ContentTypeMap
{
    readonly Dictionary<string, string> byExtension = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> byPartName = new(StringComparer.OrdinalIgnoreCase);

    public static ContentTypeMap Read(ZipArchive archive)
    {
        var map = new ContentTypeMap();

        var entry = archive.GetEntry(PackagePaths.ContentTypesPart);
        if (entry is null)
        {
            return map;
        }

        XDocument document;
        try
        {
            using var content = entry.Open();
            document = XDocument.Load(content);
        }
        catch
        {
            // without a readable content-types part nothing can be classified, which leaves every
            // part to be copied verbatim — the safe outcome
            return map;
        }

        foreach (var declaration in document.Root!.Elements())
        {
            var contentType = declaration.Attribute("ContentType")?.Value;
            if (contentType is null)
            {
                continue;
            }

            if (declaration.Name.LocalName == "Default" &&
                declaration.Attribute("Extension")?.Value is {} extension)
            {
                map.byExtension[extension] = contentType;
            }
            else if (declaration.Name.LocalName == "Override" &&
                     declaration.Attribute("PartName")?.Value is {} partName)
            {
                map.byPartName[PackagePaths.NormalizePartName(partName)] = contentType;
            }
        }

        return map;
    }

    /// <summary>The media type declared for <paramref name="partName"/>, or null when it has none.</summary>
    public string? For(string partName) =>
        byPartName.TryGetValue(partName, out var contentType)
            ? contentType
            : byExtension.GetValueOrDefault(PackagePaths.Extension(partName));
}
