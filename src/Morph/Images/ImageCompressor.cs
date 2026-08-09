using System.IO.Compression;
using System.Xml.Linq;

namespace Morph;

/// <summary>
/// Recompresses the images inside an OOXML package. Where <see cref="DocumentCleaner"/> removes
/// parts that hold no rendering information, this rewrites the parts that hold the most bytes:
/// pictures routinely account for more of a document than everything else combined.
/// </summary>
/// <remarks>
/// <para>
/// Three things are done to each image, all of them skipped when they would not help. It is
/// resampled down to <see cref="ImageCompressionOptions.TargetDpi"/> relative to the size the
/// document actually draws it at; it is re-encoded, which also discards EXIF, XMP and ICC
/// metadata; and, when
/// <see cref="ImageCompressionOptions.ConvertOpaquePngToJpeg"/> is set, a PNG with no translucent
/// pixel may be written out as a JPEG. A part is only ever replaced by a <em>smaller</em> one, so
/// no image can be made worse and larger at once.
/// </para>
/// <para>
/// Nothing here is specific to Word. Image parts are found through <c>[Content_Types].xml</c> and
/// sized through DrawingML, so <c>.docx</c>, <c>.xlsx</c> and <c>.pptx</c> are all handled by the
/// same walk.
/// </para>
/// <para>
/// The encoding is done by an <see cref="ImageCodec"/>, because core <c>Morph</c> has no imaging
/// dependency. Referencing <c>Morph.ImageSharp</c> or <c>Morph.Skia</c> is enough for one to be
/// found; otherwise supply <see cref="ImageCompressionOptions.Codec"/>.
/// </para>
/// </remarks>
public static class ImageCompressor
{
    /// <summary>Recompresses the images in the package at <paramref name="packagePath"/>, in place.</summary>
    /// <returns>What was achieved, per image and in total.</returns>
    /// <remarks>The file is left byte-for-byte untouched when no image got smaller.</remarks>
    public static ImageCompressionResult Compress(string packagePath, ImageCompressionOptions? options = null)
    {
        using var compressed = new MemoryStream();

        ImageCompressionResult result;
        using (var source = File.OpenRead(packagePath))
        {
            result = Compress(source, compressed, options);
        }

        if (!result.Changed)
        {
            return result;
        }

        File.WriteAllBytes(packagePath, compressed.ToArray());
        return result;
    }

    /// <summary>Copies the package in <paramref name="source"/> to <paramref name="target"/> with its images recompressed.</summary>
    /// <returns>What was achieved, per image and in total.</returns>
    /// <remarks>A complete package is always written, even when no image got smaller.</remarks>
    public static ImageCompressionResult Compress(Stream source, Stream target, ImageCompressionOptions? options = null)
    {
        options ??= new();

        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var planned = Plan(archive, options, encode: true);

        Write(archive, target, planned);

        return new(
            planned.Sum(_ => _.Report.Bytes),
            planned.Sum(_ => _.Report.NewBytes),
            planned.Select(_ => _.Report).ToList());
    }

    /// <summary>
    /// Reports what the images in the package at <paramref name="packagePath"/> are, without
    /// rewriting anything.
    /// </summary>
    /// <remarks>
    /// Nothing is encoded, so the sizes reported are the current ones and every
    /// <see cref="ImageReport.Outcome"/> describes what <em>would</em> be attempted. The useful
    /// column is <see cref="ImageReport.RenderedDpi"/>: an image far above
    /// <see cref="ImageCompressionOptions.TargetDpi"/> is carrying pixels the document cannot show.
    /// </remarks>
    public static IReadOnlyList<ImageReport> Inspect(string packagePath, ImageCompressionOptions? options = null)
    {
        using var source = File.OpenRead(packagePath);
        return Inspect(source, options);
    }

    /// <summary>Reports what the images in <paramref name="package"/> are, without rewriting anything.</summary>
    public static IReadOnlyList<ImageReport> Inspect(Stream package, ImageCompressionOptions? options = null)
    {
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        return Plan(archive, options ?? new(), encode: false)
            .Select(_ => _.Report)
            .ToList();
    }

    static List<PlannedImage> Plan(ZipArchive archive, ImageCompressionOptions options, bool encode)
    {
        var codec = options.Codec ??
                    ImageCodecFactory.TryGet() ??
                    throw new InvalidOperationException(
                        "No image codec is available. Add a reference to Morph.ImageSharp or Morph.Skia, " +
                        $"or set {nameof(ImageCompressionOptions)}.{nameof(ImageCompressionOptions.Codec)}.");

        var contentTypes = ContentTypeMap.Read(archive);
        var widths = DrawingExtents.Measure(archive);
        var taken = archive.Entries
            .Select(_ => PackagePaths.NormalizePartName(_.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var planned = new List<PlannedImage>();

        foreach (var entry in archive.Entries)
        {
            var partName = PackagePaths.NormalizePartName(entry.FullName);
            var contentType = contentTypes.For(partName);

            if (contentType is null ||
                !ImageMediaTypes.IsImage(contentType))
            {
                continue;
            }

            planned.Add(Plan(entry, partName, contentType, codec, options, widths, taken, encode));
        }

        return planned;
    }

    static PlannedImage Plan(
        ZipArchiveEntry entry,
        string partName,
        string contentType,
        ImageCodec codec,
        ImageCompressionOptions options,
        Dictionary<string, double> widths,
        HashSet<string> taken,
        bool encode)
    {
        var bytes = entry.Length;

        if (!ImageMediaTypes.IsRewritable(contentType))
        {
            return Untouched(partName, contentType, bytes, ImageOutcome.UnsupportedFormat);
        }

        var data = Read(entry);
        var probe = codec.Probe(data);
        if (probe is null ||
            probe.Width <= 0 ||
            probe.Height <= 0)
        {
            options.OnWarning?.Invoke(
                new(WarningKind.ImageRenderingFailed, $"'{partName}' could not be decoded, so it was left as it is."));
            return Untouched(partName, contentType, bytes, ImageOutcome.Unreadable);
        }

        // absent from widths means no drawing states a size for it, so there is nothing to say how
        // many pixels it needs and it must not be resampled
        var drawnInches = widths.TryGetValue(partName, out var inches) && inches > 0 ? inches : (double?) null;
        var renderedDpi = drawnInches is null ? null : probe.Width / drawnInches;

        var targetWidth = TargetWidth(probe, options, drawnInches, renderedDpi);
        var targetContentType = TargetContentType(contentType, probe, options);

        var outcome = targetContentType != contentType
            ? ImageOutcome.Converted
            : targetWidth < probe.Width
                ? ImageOutcome.Resampled
                : ImageOutcome.Recompressed;

        var report = new ImageReport(
            partName, contentType, bytes, bytes, probe.Width, probe.Height, renderedDpi, outcome);

        if (!encode)
        {
            return new(report, null, null, null);
        }

        var targetHeight = Math.Max(1, (int) Math.Round(probe.Height * (double) targetWidth / probe.Width));
        var replacement = codec.Encode(
            data, new(targetWidth, targetHeight, targetContentType, options.JpegQuality));

        if (replacement is null)
        {
            options.OnWarning?.Invoke(
                new(WarningKind.ImageRenderingFailed, $"'{partName}' could not be re-encoded, so it was left as it is."));
            return new(report with {Outcome = ImageOutcome.Unreadable}, null, null, null);
        }

        if (replacement.Length >= bytes)
        {
            return new(report with {Outcome = ImageOutcome.NoGain}, null, null, null);
        }

        var newPartName = targetContentType == contentType
            ? null
            : Rename(partName, ImageMediaTypes.ExtensionFor(targetContentType), taken);

        return new(
            report with
            {
                NewBytes = replacement.Length,
                NewPartName = newPartName
            },
            replacement,
            newPartName,
            newPartName is null ? null : targetContentType);
    }

    /// <summary>
    /// The pixel width the image should end up at: enough to serve the space it occupies at the
    /// target resolution, and never more than it already has.
    /// </summary>
    static int TargetWidth(ImageProbe probe, ImageCompressionOptions options, double? drawnInches, double? renderedDpi)
    {
        if (options.TargetDpi is not {} targetDpi ||
            targetDpi <= 0 ||
            drawnInches is not {} inches ||
            renderedDpi <= targetDpi)
        {
            return probe.Width;
        }

        return Math.Clamp((int) Math.Round(inches * targetDpi), 1, probe.Width);
    }

    static string TargetContentType(string contentType, ImageProbe probe, ImageCompressionOptions options) =>
        options.ConvertOpaquePngToJpeg &&
        !probe.HasTranslucency &&
        ImageMediaTypes.Matches(contentType, ImageMediaTypes.Png)
            ? ImageMediaTypes.Jpeg
            : contentType;

    /// <summary>
    /// The part's name under its new extension, avoiding any name the package already uses.
    /// </summary>
    static string Rename(string partName, string extension, HashSet<string> taken)
    {
        var dot = partName.LastIndexOf('.');
        var stem = dot < 0 ? partName : partName[..dot];

        var candidate = $"{stem}.{extension}";
        for (var suffix = 2; !taken.Add(candidate); suffix++)
        {
            candidate = $"{stem}-{suffix}.{extension}";
        }

        return candidate;
    }

    static PlannedImage Untouched(string partName, string contentType, long bytes, ImageOutcome outcome) =>
        new(new(partName, contentType, bytes, bytes, null, null, null, outcome), null, null, null);

    static void Write(ZipArchive archive, Stream target, List<PlannedImage> planned)
    {
        var replacements = planned
            .Where(_ => _.Replacement is not null)
            .ToDictionary(_ => _.Report.PartName, _ => _, StringComparer.OrdinalIgnoreCase);

        var renames = replacements.Values
            .Where(_ => _.NewPartName is not null)
            .ToDictionary(_ => _.Report.PartName, _ => _.NewPartName!, StringComparer.OrdinalIgnoreCase);

        using var output = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var entry in archive.Entries)
        {
            var partName = PackagePaths.NormalizePartName(entry.FullName);
            var replacement = replacements.GetValueOrDefault(partName);

            // entry order is preserved so [Content_Types].xml stays wherever the authoring tool
            // put it — first, for every package Word writes
            var name = replacement?.NewPartName ?? entry.FullName;
            var copy = output.CreateEntry(name, CompressionLevel.Optimal);
            if (entry.LastWriteTime.Year >= 1980)
            {
                copy.LastWriteTime = entry.LastWriteTime;
            }

            using var destination = copy.Open();

            if (replacement?.Replacement is {} bytes)
            {
                destination.Write(bytes);
                continue;
            }

            using var content = entry.Open();

            if (renames.Count > 0 &&
                PackagePaths.IsRelationshipPart(entry.FullName))
            {
                PackageXml.Rewrite(content, destination, document => Retarget(document, entry.FullName, renames));
            }
            else if (renames.Count > 0 &&
                     PackagePaths.IsContentTypesPart(entry.FullName))
            {
                PackageXml.Rewrite(content, destination, document => Redeclare(document, archive, replacements, renames));
            }
            else
            {
                content.CopyTo(destination);
            }
        }
    }

    /// <summary>Points every relationship that reached a renamed part at its new name.</summary>
    static void Retarget(XDocument document, string relsPartName, Dictionary<string, string> renames)
    {
        foreach (var relationship in document.Root!.Elements())
        {
            if (relationship.Name.LocalName != "Relationship" ||
                PackagePaths.IsExternal(relationship))
            {
                continue;
            }

            var target = relationship.Attribute("Target");
            if (target is null ||
                !renames.TryGetValue(PackagePaths.ResolvePartName(relsPartName, target.Value), out var renamed))
            {
                continue;
            }

            // keep the target relative when it started that way and still can be, since that is
            // how Word writes them; otherwise fall back to the always-valid absolute form
            var owning = PackagePaths.OwningDirectory(relsPartName);
            target.Value = !target.Value.StartsWith('/') && renamed.StartsWith(owning, StringComparison.OrdinalIgnoreCase)
                ? renamed[owning.Length..]
                : $"/{renamed}";
        }
    }

    /// <summary>
    /// Brings <c>[Content_Types].xml</c> in line with the renames: any <c>Override</c> follows its
    /// part, a <c>Default</c> is added for each extension now in use, and one is dropped when the
    /// last part using it has gone.
    /// </summary>
    static void Redeclare(
        XDocument document,
        ZipArchive archive,
        Dictionary<string, PlannedImage> replacements,
        Dictionary<string, string> renames)
    {
        var root = document.Root!;

        foreach (var declaration in root.Elements().ToList())
        {
            if (declaration.Name.LocalName != "Override")
            {
                continue;
            }

            var partName = declaration.Attribute("PartName");
            if (partName is null)
            {
                continue;
            }

            var declared = PackagePaths.NormalizePartName(partName.Value);
            if (!renames.TryGetValue(declared, out var renamed))
            {
                continue;
            }

            partName.Value = $"/{renamed}";

            if (replacements[declared].NewContentType is {} contentType)
            {
                declaration.SetAttributeValue("ContentType", contentType);
            }
        }

        var survivingExtensions = archive.Entries
            .Select(_ => PackagePaths.NormalizePartName(_.FullName))
            .Select(_ => renames.GetValueOrDefault(_, _))
            .Select(PackagePaths.Extension)
            .Where(_ => _.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var declaration in root.Elements().Where(_ => _.Name.LocalName == "Default").ToList())
        {
            var extension = declaration.Attribute("Extension")?.Value;
            if (extension is not null &&
                !survivingExtensions.Contains(extension))
            {
                declaration.Remove();
            }
        }

        var present = root
            .Elements()
            .Where(_ => _.Name.LocalName == "Default")
            .Select(_ => _.Attribute("Extension")?.Value)
            .Where(_ => _ is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        foreach (var plan in replacements.Values.Where(_ => _.NewContentType is not null))
        {
            var extension = PackagePaths.Extension(plan.NewPartName!);
            if (present.Add(extension))
            {
                root.AddFirst(
                    new XElement(
                        root.Name.Namespace + "Default",
                        new XAttribute("Extension", extension),
                        new XAttribute("ContentType", plan.NewContentType!)));
            }
        }
    }

    static byte[] Read(ZipArchiveEntry entry)
    {
        using var content = entry.Open();
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <param name="Report">What to tell the caller about this part.</param>
    /// <param name="Replacement">The bytes to write instead, or null to copy the part across as it is.</param>
    /// <param name="NewPartName">The name to write it under, when the format changed.</param>
    /// <param name="NewContentType">The media type to declare for it, when the format changed.</param>
    sealed record PlannedImage(ImageReport Report, byte[]? Replacement, string? NewPartName, string? NewContentType);
}
