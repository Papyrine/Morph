using System.IO.Compression;
using System.Xml.Linq;

namespace Morph;

/// <summary>
/// Works out how large each image part is actually drawn, which is the only thing that makes
/// downsampling safe: an image is oversized only relative to the space it occupies.
/// </summary>
/// <remarks>
/// <para>
/// One rule covers all three OOXML formats, because all three size pictures with DrawingML. From
/// each <c>a:blip</c> the walk goes up the ancestor chain looking for the nearest stated size —
/// a direct <c>wp:extent</c> / <c>xdr:ext</c> child (Word inline drawings, spreadsheet one-cell and
/// absolute anchors) or an <c>spPr/xfrm/ext</c> (the shape properties every <c>pic</c> carries, in
/// documents, worksheets and slides alike). VML is handled separately off the owning shape's CSS
/// <c>width</c>.
/// </para>
/// <para>
/// Sizes are returned as the width in inches that the <em>whole source image</em> spans, so a
/// cropped picture reports the larger notional width its visible slice implies. A part reached
/// from several places reports the largest. A part reached from none is absent, and its caller
/// must not resample it — a spreadsheet <c>twoCellAnchor</c> sized by row and column spans lands
/// here, as does any image whose relationship this walk cannot follow.
/// </para>
/// </remarks>
static class DrawingExtents
{
    const double emusPerInch = 914400d;
    const string relationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    const string officeNamespace = "urn:schemas-microsoft-com:office:office";

    static readonly XName embed = XName.Get("embed", relationshipsNamespace);
    static readonly XName id = XName.Get("id", relationshipsNamespace);
    static readonly XName relid = XName.Get("relid", officeNamespace);

    /// <summary>
    /// Maps part name to the widest the whole source image is drawn, in inches.
    /// </summary>
    public static Dictionary<string, double> Measure(ZipArchive archive)
    {
        var widths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            var partName = PackagePaths.NormalizePartName(entry.FullName);
            if (!partName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                PackagePaths.IsContentTypesPart(partName))
            {
                continue;
            }

            var targets = RelationshipTargets(archive, partName);
            if (targets.Count == 0)
            {
                continue;
            }

            XDocument document;
            try
            {
                using var content = entry.Open();
                document = XDocument.Load(content);
            }
            catch
            {
                // a part that is not well-formed XML tells us nothing about image sizes; the
                // package is being copied verbatim regardless
                continue;
            }

            foreach (var (target, inches) in Measure(document, targets))
            {
                if (!widths.TryGetValue(target, out var existing) ||
                    inches > existing)
                {
                    widths[target] = inches;
                }
            }
        }

        return widths;
    }

    static IEnumerable<(string Target, double Inches)> Measure(XDocument document, Dictionary<string, string> targets)
    {
        foreach (var element in document.Descendants())
        {
            var local = element.Name.LocalName;

            if (local == "blip")
            {
                var relationship = element.Attribute(embed)?.Value;
                if (relationship is not null &&
                    targets.TryGetValue(relationship, out var target) &&
                    DrawnWidth(element) is { } inches)
                {
                    yield return (target, inches / VisibleWidthFraction(element.Parent));
                }
            }
            else if (local == "imagedata")
            {
                var relationship = element.Attribute(id)?.Value ?? element.Attribute(relid)?.Value;
                if (relationship is not null &&
                    targets.TryGetValue(relationship, out var target) &&
                    VmlWidth(element.Parent) is { } inches)
                {
                    yield return (target, inches);
                }
            }
        }
    }

    /// <summary>
    /// Walks out from a <c>blip</c> to the nearest ancestor that states a size, scaling by the
    /// transform of every group passed through on the way — a picture inside a group is sized in
    /// the group's child coordinate space, and a group that scales its children up needs
    /// proportionally more pixels, not fewer.
    /// </summary>
    static double? DrawnWidth(XElement blip)
    {
        double? emus = null;
        var scale = 1d;

        for (var ancestor = blip.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (emus is null)
            {
                // the innermost stated size wins, but the walk carries on past it: inside a group
                // that size is in the group's child coordinates, not the page's
                emus = StatedWidth(ancestor);
                continue;
            }

            if (ancestor.Name.LocalName is "grpSp" or "wgp" &&
                GroupScale(ancestor) is { } groupScale)
            {
                scale *= groupScale;
            }
        }

        return emus * scale / emusPerInch;
    }

    /// <summary>
    /// The width in EMUs an element states for its content, either directly (<c>wp:inline</c> and
    /// the spreadsheet anchors put an <c>extent</c> / <c>ext</c> straight in) or through the shape
    /// properties a <c>pic</c> carries.
    /// </summary>
    static double? StatedWidth(XElement element)
    {
        foreach (var child in element.Elements())
        {
            var local = child.Name.LocalName;

            if (local is "extent" or "ext" &&
                Emus(child, "cx") is { } direct)
            {
                return direct;
            }

            if (local is "spPr" or "grpSpPr" &&
                Transform(child) is { } transformed)
            {
                return transformed;
            }
        }

        return null;
    }

    static double? Transform(XElement shapeProperties)
    {
        var extent = shapeProperties
            .Elements()
            .FirstOrDefault(_ => _.Name.LocalName == "xfrm")
            ?.Elements()
            .FirstOrDefault(_ => _.Name.LocalName == "ext");

        return extent is null ? null : Emus(extent, "cx");
    }

    /// <summary>
    /// How much a group stretches its children horizontally: the width it occupies over the width
    /// of the coordinate space its children are laid out in.
    /// </summary>
    static double? GroupScale(XElement group)
    {
        var transform = group
            .Elements()
            .FirstOrDefault(_ => _.Name.LocalName is "grpSpPr")
            ?.Elements()
            .FirstOrDefault(_ => _.Name.LocalName == "xfrm");

        if (transform is null)
        {
            return null;
        }

        var extent = transform.Elements().FirstOrDefault(_ => _.Name.LocalName == "ext");
        var childExtent = transform.Elements().FirstOrDefault(_ => _.Name.LocalName == "chExt");

        if (extent is null ||
            childExtent is null ||
            Emus(extent, "cx") is not { } outer ||
            Emus(childExtent, "cx") is not { } inner ||
            inner <= 0)
        {
            return null;
        }

        return outer / inner;
    }

    /// <summary>
    /// The fraction of the source image's width that a <c>srcRect</c> crop leaves visible. A
    /// picture cropped to half its width and drawn an inch wide is really being asked for two
    /// inches of source pixels.
    /// </summary>
    static double VisibleWidthFraction(XElement? blipFill)
    {
        var crop = blipFill?.Elements().FirstOrDefault(_ => _.Name.LocalName == "srcRect");
        if (crop is null)
        {
            return 1;
        }

        // srcRect edges are thousandths of a percent, and may be negative (padding rather than crop)
        var left = Percentage(crop, "l");
        var right = Percentage(crop, "r");
        var visible = 1 - left - right;

        return visible <= 0 ? 1 : visible;
    }

    static double Percentage(XElement element, string name) =>
        double.TryParse(element.Attribute(name)?.Value, CultureInfo.InvariantCulture, out var value)
            ? value / 100000d
            : 0;

    static double? Emus(XElement element, string name) =>
        double.TryParse(element.Attribute(name)?.Value, CultureInfo.InvariantCulture, out var value) &&
        value > 0
            ? value
            : null;

    /// <summary>The width in inches from a VML shape's CSS <c>style</c>, e.g. <c>width:120.5pt</c>.</summary>
    static double? VmlWidth(XElement? shape)
    {
        var style = shape?.Attribute("style")?.Value;
        if (style is null)
        {
            return null;
        }

        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = declaration.IndexOf(':');
            if (separator < 0 ||
                !declaration.AsSpan(0, separator).Trim().Equals("width", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Length(declaration.AsSpan(separator + 1).Trim());
        }

        return null;
    }

    static double? Length(CharSpan value)
    {
        // a bare number in a VML style is points
        var unit = 0;
        var perInch = 72d;

        foreach (var (suffix, divisor) in units)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                unit = suffix.Length;
                perInch = divisor;
                break;
            }
        }

        return double.TryParse(value[..^unit], CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed / perInch
            : null;
    }

    static readonly (string Suffix, double PerInch)[] units =
    [
        ("pt", 72d),
        ("px", 96d),
        ("in", 1d),
        ("cm", 2.54d),
        ("mm", 25.4d),
        ("pc", 6d)
    ];

    /// <summary>Maps each relationship id declared for <paramref name="partName"/> to the part it reaches.</summary>
    static Dictionary<string, string> RelationshipTargets(ZipArchive archive, string partName)
    {
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);

        var separator = partName.LastIndexOf('/');
        var relsPartName = separator < 0
            ? $"_rels/{partName}.rels"
            : $"{partName[..separator]}/_rels/{partName[(separator + 1)..]}.rels";

        var entry = archive.GetEntry(relsPartName);
        if (entry is null)
        {
            return targets;
        }

        try
        {
            using var content = entry.Open();
            foreach (var relationship in XDocument.Load(content).Root!.Elements())
            {
                var relationshipId = relationship.Attribute("Id")?.Value;
                var target = relationship.Attribute("Target")?.Value;

                if (relationshipId is not null &&
                    target is not null &&
                    !PackagePaths.IsExternal(relationship))
                {
                    targets[relationshipId] = PackagePaths.ResolvePartName(relsPartName, target);
                }
            }
        }
        catch
        {
            // malformed relationships part — no sizes can be read from it
        }

        return targets;
    }
}
