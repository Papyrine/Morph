using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// Covers <see cref="ImageCompressor"/>. Most fixtures are hand-built packages rather than corpus
/// documents, because the questions being asked are about geometry — an image is oversized only
/// relative to the space it is drawn in, and a corpus document states that size once, in one way.
/// A fixture can state it several ways at once.
/// <para>
/// The codec is left to be discovered rather than passed, so these also exercise
/// <c>ImageCodecFactory</c>. The test assembly references both backends and the factory prefers
/// ImageSharp, so that is what runs here; <see cref="ImageCodecTests"/> covers each directly.
/// </para>
/// </summary>
public class ImageCompressorTests
{
    [Test]
    public async Task ResamplesAnImageCarryingMorePixelsThanItIsDrawnAt()
    {
        // 600px across a two inch box is 300 DPI; at the 150 DPI default it needs 300
        var output = Package.With(Picture(TestImages.Photograph(600, 450), inches: 2)).Compress();

        var image = output.Images.Single();
        await Assert.That(image.RenderedDpi!.Value).IsEqualTo(300).Within(0.5);
        await Assert.That(image.Outcome).IsEqualTo(ImageOutcome.Resampled);
        await Assert.That(output.Saved).IsGreaterThan(0);
        await Assert.That(TestImages.Width(output.Part("word/media/image1.png"))).IsEqualTo(300);
    }

    [Test]
    public async Task LeavesAnImageThatIsAlreadyModestlySizedAtItsOwnDimensions()
    {
        // 200px across two inches is 100 DPI — already under the target
        var output = Package.With(Picture(TestImages.Photograph(200, 150), inches: 2)).Compress();

        await Assert.That(output.Images.Single().Outcome).IsNotEqualTo(ImageOutcome.Resampled);
        await Assert.That(TestImages.Width(output.Part("word/media/image1.png"))).IsEqualTo(200);
    }

    [Test]
    public async Task NeverResamplesAnImageNoDrawingStatesASizeFor()
    {
        // the package thumbnail is reached from _rels/.rels, not from a drawing, so nothing says
        // how many pixels it needs
        var output = Package.With().WithThumbnail(TestImages.Photograph(600, 450)).Compress();

        var image = output.Images.Single();
        await Assert.That(image.RenderedDpi).IsNull();
        await Assert.That(image.Outcome).IsNotEqualTo(ImageOutcome.Resampled);
        await Assert.That(TestImages.Width(output.Part("docProps/thumbnail.png"))).IsEqualTo(600);
    }

    [Test]
    public async Task CountsACropAgainstThePixelBudget()
    {
        // the middle half of the image fills a one inch box, so the whole image spans two inches
        // and needs twice the pixels the box alone would suggest
        var output = Package
            .With(Picture(TestImages.Photograph(600, 450), inches: 1, cropSides: 0.25))
            .Compress();

        await Assert.That(output.Images.Single().RenderedDpi!.Value).IsEqualTo(300).Within(0.5);
        await Assert.That(TestImages.Width(output.Part("word/media/image1.png"))).IsEqualTo(300);
    }

    [Test]
    public async Task SizesForTheLargestPlacementWhenAnImageIsUsedMoreThanOnce()
    {
        var photograph = TestImages.Photograph(600, 450);
        var output = Package
            .With(
                Picture(photograph, inches: 0.5),
                Picture(photograph, inches: 2))
            .Compress();

        // one part, two placements: the two inch one wins at 2 x 150, rather than the half inch
        // one that would have cut it to 75
        await Assert.That(output.Images.Count).IsEqualTo(1);
        await Assert.That(TestImages.Width(output.Part("word/media/image1.png"))).IsEqualTo(300);
    }

    [Test]
    public async Task ScalesByTheTransformOfAGroupThePictureSitsIn()
    {
        // the picture is an inch wide in the group's own coordinates, and the group is drawn at
        // twice that, so it is really two inches on the page and needs 300 pixels rather than the
        // 150 an inch would have called for
        var output = Package
            .With(Picture(TestImages.Photograph(600, 450), inches: 1, groupScale: 2))
            .Compress();

        await Assert.That(TestImages.Width(output.Part("word/media/image1.png"))).IsEqualTo(300);
    }

    [Test]
    public async Task SizesAVmlShapeFromItsStyle()
    {
        var output = Package.With().WithVmlPicture(TestImages.Photograph(600, 450), points: 144).Compress();

        // 144pt is two inches
        await Assert.That(output.Images.Single().RenderedDpi!.Value).IsEqualTo(300).Within(0.5);
        await Assert.That(TestImages.Width(output.Part("word/media/image1.png"))).IsEqualTo(300);
    }

    [Test]
    public async Task ResamplingCanBeTurnedOff()
    {
        var output = Package
            .With(Picture(TestImages.Photograph(600, 450), inches: 2))
            .Compress(new()
            {
                TargetDpi = null
            });

        await Assert.That(output.Images.Single().Outcome).IsNotEqualTo(ImageOutcome.Resampled);
        await Assert.That(TestImages.Width(output.Part("word/media/image1.png"))).IsEqualTo(600);
    }

    [Test]
    public async Task KeepsTheOriginalBytesWhenReEncodingWouldNotShrinkThem()
    {
        var package = Package.With(Picture(TestImages.Photograph(200, 150), inches: 2));

        var output = package.Compress(new()
        {
            Codec = new StubCodec(200, 150)
        });

        await Assert.That(output.Images.Single().Outcome).IsEqualTo(ImageOutcome.NoGain);
        await Assert.That(output.Saved).IsEqualTo(0);
        await Assert.That(output.Part("word/media/image1.png").SequenceEqual(package.Part("word/media/image1.png"))).IsTrue();
    }

    [Test]
    public async Task LeavesAFileWithNothingToGainByteIdentical()
    {
        using var fixture = TempFile.Holding(Package.With(Picture(TestImages.Photograph(200, 150), inches: 2)).Bytes());
        var original = await File.ReadAllBytesAsync(fixture.Path);

        var result = ImageCompressor.Compress(fixture.Path, new()
        {
            Codec = new StubCodec(200, 150)
        });

        await Assert.That(result.Changed).IsFalse();
        await Assert.That((await File.ReadAllBytesAsync(fixture.Path)).SequenceEqual(original)).IsTrue();
    }

    [Test]
    public async Task RewritesTheFileInPlaceWhenThereIsSomethingToGain()
    {
        using var fixture = TempFile.Holding(Package.With(Picture(TestImages.Photograph(600, 450), inches: 2)).Bytes());
        var original = new FileInfo(fixture.Path).Length;

        var result = ImageCompressor.Compress(fixture.Path);

        await Assert.That(result.Changed).IsTrue();
        await Assert.That(new FileInfo(fixture.Path).Length).IsLessThan(original);

        // the rewritten package is still a package
        using var document = WordprocessingDocument.Open(fixture.Path, false);
        await Assert.That(document.MainDocumentPart!.ImageParts.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task LeavesFormatsItCannotRewriteExactlyAsTheyWere()
    {
        var unsupported = new[] {"word/media/image2.svg", "word/media/hdphoto1.wdp", "word/media/image3.emf"};

        var package = Package.With(Picture(TestImages.Photograph(600, 450), inches: 2))
            .WithPart("word/media/image2.svg", "image/svg+xml", TestImages.Svg)
            .WithPart("word/media/hdphoto1.wdp", "image/vnd.ms-photo", [0x49, 0x49, 0xBC, 0x01])
            .WithPart("word/media/image3.emf", "image/x-emf", [0x01, 0x00, 0x00, 0x00]);

        var output = package.Compress();

        await Assert.That(output.Images
                .Where(_ => _.Outcome == ImageOutcome.UnsupportedFormat)
                .Select(_ => _.PartName))
            .IsEquivalentTo(unsupported);

        foreach (var part in unsupported)
        {
            await Assert.That(output.Part(part).SequenceEqual(package.Part(part))).IsTrue();
        }
    }

    [Test]
    public async Task ReportsWhatAPackageHoldsWithoutRewritingIt()
    {
        var package = Package.With(Picture(TestImages.Photograph(600, 450), inches: 2));
        var bytes = package.Bytes();

        var images = ImageCompressor.Inspect(new MemoryStream(bytes));

        var image = images.Single();
        await Assert.That(image.PartName).IsEqualTo("word/media/image1.png");
        await Assert.That(image.Width).IsEqualTo(600);
        await Assert.That(image.Height).IsEqualTo(450);
        await Assert.That(image.RenderedDpi!.Value).IsEqualTo(300).Within(0.5);
        await Assert.That(image.NewBytes).IsEqualTo(image.Bytes);
        await Assert.That(bytes.SequenceEqual(package.Bytes())).IsTrue();
    }

    [Test]
    public async Task RaisesAWarningForAnImageItCannotDecode()
    {
        var warnings = new List<ExportWarning>();
        var package = Package.With(Picture([1, 2, 3, 4, 5], inches: 2));

        var output = package.Compress(new()
        {
            OnWarning = warnings.Add
        });

        await Assert.That(output.Images.Single().Outcome).IsEqualTo(ImageOutcome.Unreadable);
        await Assert.That(warnings.Single().Kind).IsEqualTo(WarningKind.ImageRenderingFailed);
        await Assert.That(warnings.Single().Message).Contains("word/media/image1.png");
        await Assert.That(output.Part("word/media/image1.png").SequenceEqual(package.Part("word/media/image1.png"))).IsTrue();
    }

    // ---- format conversion ----

    [Test]
    public async Task ConvertsAnOpaquePngToJpegAndRetargetsEverythingThatReachedIt()
    {
        var output = Package
            .With(Picture(TestImages.Photograph(400, 300), inches: 2))
            .Compress(new()
            {
                ConvertOpaquePngToJpeg = true
            });

        var image = output.Images.Single();
        await Assert.That(image.Outcome).IsEqualTo(ImageOutcome.Converted);
        await Assert.That(image.NewPartName).IsEqualTo("word/media/image1.jpeg");

        await Assert.That(output.PartNames()).Contains("word/media/image1.jpeg");
        await Assert.That(output.PartNames()).DoesNotContain("word/media/image1.png");

        // the relationship follows the part, or the drawing points at nothing
        var relationships = output.Text("word/_rels/document.xml.rels");
        await Assert.That(relationships).Contains("media/image1.jpeg");
        await Assert.That(relationships).DoesNotContain("media/image1.png");

        // and the package has to declare what the new part is
        var contentTypes = output.Text("[Content_Types].xml");
        await Assert.That(contentTypes).Contains("\"jpeg\"");
        await Assert.That(contentTypes).DoesNotContain("\"png\"");
    }

    [Test]
    public async Task ConvertedPackagesStillOpen()
    {
        using var fixture = TempFile.Holding(Package.With(Picture(TestImages.Photograph(400, 300), inches: 2)).Bytes());

        ImageCompressor.Compress(fixture.Path, new()
        {
            ConvertOpaquePngToJpeg = true
        });

        using var document = WordprocessingDocument.Open(fixture.Path, false);
        var part = document.MainDocumentPart!.ImageParts.Single();

        await Assert.That(part.ContentType).IsEqualTo("image/jpeg");
        await Assert.That(part.Uri.OriginalString).EndsWith(".jpeg");
    }

    [Test]
    public async Task KeepsAPngThatCarriesTransparencyAsAPng()
    {
        var output = Package
            .With(Picture(TestImages.Photograph(400, 300, translucent: true), inches: 2))
            .Compress(new()
            {
                ConvertOpaquePngToJpeg = true
            });

        await Assert.That(output.Images.Single().Outcome).IsNotEqualTo(ImageOutcome.Converted);
        await Assert.That(output.PartNames()).Contains("word/media/image1.png");
    }

    [Test]
    public async Task DoesNotConvertByDefault()
    {
        var output = Package.With(Picture(TestImages.Photograph(400, 300), inches: 2)).Compress();

        await Assert.That(output.Images.Single().Outcome).IsNotEqualTo(ImageOutcome.Converted);
        await Assert.That(output.PartNames()).Contains("word/media/image1.png");
    }

    [Test]
    public async Task AvoidsANameThePackageIsAlreadyUsing()
    {
        var output = Package
            .With(Picture(TestImages.Photograph(400, 300), inches: 2))
            .WithPart("word/media/image1.jpeg", "image/jpeg", TestImages.Photograph(40, 30, jpeg: true))
            .Compress(new()
            {
                ConvertOpaquePngToJpeg = true
            });

        await Assert.That(output.Images.Single(_ => _.PartName.EndsWith(".png")).NewPartName)
            .IsEqualTo("word/media/image1-2.jpeg");
        await Assert.That(output.PartNames()).Contains("word/media/image1.jpeg");
        await Assert.That(output.PartNames()).Contains("word/media/image1-2.jpeg");
    }

    // ---- corpus ----

    /// <summary>
    /// <c>letters/02</c> carries both a PNG and a JPEG, so this exercises the two paths that
    /// actually rewrite bytes against a package Word wrote.
    /// </summary>
    [Test]
    public async Task PreservesWhatTheDocumentRenders()
    {
        using var fixture = TempFile.CopyOf(Corpus("letters/02"));

        var before = Anonymize(DocumentConverter.ConvertToHtml(fixture.Path));
        ImageCompressor.Compress(fixture.Path);
        var after = Anonymize(DocumentConverter.ConvertToHtml(fixture.Path));

        await Assert.That(after).IsEqualTo(before);
    }

    /// <summary>
    /// The whole corpus through the compressor, asserting only that nothing breaks and nothing
    /// grows. Explicit because it decodes and re-encodes several hundred images; the point is to
    /// have it available after a change to the walk, not to pay for it on every run.
    /// </summary>
    [Test]
    [Explicit]
    public async Task CompressesEveryCorpusDocumentWithoutGrowingAPart()
    {
        var inputs = Directory.EnumerateFiles(
            Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word"),
            "input.docx",
            SearchOption.AllDirectories);

        foreach (var input in inputs)
        {
            using var fixture = TempFile.CopyOf(input);

            var result = ImageCompressor.Compress(fixture.Path);

            await Assert.That(result.Images.All(_ => _.NewBytes <= _.Bytes))
                .IsTrue()
                .Because($"{input} grew a part");

            using var document = WordprocessingDocument.Open(fixture.Path, false);
            await Assert.That(document.MainDocumentPart).IsNotNull();
        }
    }

    static string Corpus(string scenario) =>
        Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", scenario, "input.docx");

    /// <summary>
    /// Strips the base64 payloads out of exported HTML. Recompressed images produce different
    /// bytes by design; what has to survive is everything else — the text, the structure, and one
    /// <c>img</c> per picture, still resolving.
    /// </summary>
    static string Anonymize(string html) =>
        Regex.Replace(html, "base64,[^\"']+", "base64,...");

    static Placement Picture(
        byte[] data,
        double inches,
        double cropSides = 0,
        double groupScale = 1) =>
        new(data, inches, cropSides, groupScale);

    /// <summary>One drawing of one image, and the geometry it is drawn with.</summary>
    /// <param name="Data">The image bytes. Placements sharing an array share a package part.</param>
    /// <param name="Inches">Width the picture states for itself, in its own coordinate space.</param>
    /// <param name="CropSides">Fraction cropped off each of the left and right edges.</param>
    /// <param name="GroupScale">When above 1, wraps the picture in a group drawn that many times its child coordinate space.</param>
    internal sealed record Placement(byte[] Data, double Inches, double CropSides, double GroupScale);

    /// <summary>An <see cref="ImageCodec"/> that reports fixed dimensions and, by default, only ever
    /// produces something one byte larger than it was given — so nothing is ever worth keeping.</summary>
    sealed class StubCodec(int width, int height, bool translucent = false) : ImageCodec
    {
        public Func<byte[], ImageEncodeRequest, byte[]?>? Encoder { get; init; }

        public override ImageProbe Probe(byte[] data) =>
            new(width, height, translucent);

        public override byte[]? Encode(byte[] data, ImageEncodeRequest request) =>
            Encoder is null ? new byte[data.Length + 1] : Encoder(data, request);
    }

    /// <summary>A file on disk that deletes itself.</summary>
    sealed class TempFile : IDisposable
    {
        public required string Path { get; init; }

        public void Dispose() =>
            File.Delete(Path);

        public static TempFile Holding(byte[] bytes)
        {
            var file = Create();
            File.WriteAllBytes(file.Path, bytes);
            return file;
        }

        public static TempFile CopyOf(string source)
        {
            var file = Create();
            File.Copy(source, file.Path, true);
            return file;
        }

        static TempFile Create() =>
            new()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"morph-images-{Guid.NewGuid():N}.docx")
            };
    }

    /// <summary>
    /// A minimal but genuine Word package built around a set of placements. Hand-written rather
    /// than produced through the SDK because the point of most of these tests is the exact
    /// geometry the drawing states, and several state it in ways the SDK's helpers do not make
    /// convenient — a crop, a group transform, a VML shape.
    /// </summary>
    sealed class Package
    {
        const string relationshipTypes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        readonly List<Placement> placements = [];
        readonly List<(string PartName, string ContentType, byte[] Content)> extras = [];
        readonly List<string> bodies = [];
        readonly List<(string Id, string Target)> vmlRelationships = [];
        byte[]? built;

        public static Package With(params Placement[] placements)
        {
            var package = new Package();
            package.placements.AddRange(placements);
            return package;
        }

        public Package WithPart(string partName, string contentType, byte[] content)
        {
            extras.Add((partName, contentType, content));
            built = null;
            return this;
        }

        public Package WithThumbnail(byte[] png) =>
            WithPart("docProps/thumbnail.png", "image/png", png);

        /// <summary>A legacy VML picture, which states its size as CSS on the shape rather than in EMUs.</summary>
        public Package WithVmlPicture(byte[] png, double points)
        {
            var index = Media().Count + vmlRelationships.Count + 1;
            var relationship = $"rIdVml{index}";
            var size = points.ToString(CultureInfo.InvariantCulture);

            extras.Add(($"word/media/image{index}.png", "image/png", png));
            vmlRelationships.Add((relationship, $"media/image{index}.png"));
            bodies.Add(
                $"""
                 <w:p><w:r><w:pict><v:shape id="shape{index}" type="#_x0000_t75" style="width:{size}pt;height:{size}pt">
                 <v:imagedata r:id="{relationship}" o:title="" /></v:shape></w:pict></w:r></w:p>
                 """);

            built = null;
            return this;
        }

        public byte[] Bytes() =>
            built ??= Build();

        public byte[] Part(string partName)
        {
            using var package = new ZipArchive(new MemoryStream(Bytes()), ZipArchiveMode.Read);
            using var content = package.GetEntry(partName)!.Open();
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            return buffer.ToArray();
        }

        public Output Compress(ImageCompressionOptions? options = null)
        {
            using var target = new MemoryStream();
            var result = ImageCompressor.Compress(new MemoryStream(Bytes()), target, options);
            return new(result, target.ToArray());
        }

        /// <summary>The media parts, one per distinct image, in the order they were first placed.</summary>
        List<byte[]> Media()
        {
            var media = new List<byte[]>();
            foreach (var placement in placements)
            {
                if (!media.Any(_ => ReferenceEquals(_, placement.Data)))
                {
                    media.Add(placement.Data);
                }
            }

            return media;
        }

        byte[] Build()
        {
            var media = Media();

            var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                Write(archive, "[Content_Types].xml", ContentTypes());
                Write(archive, "_rels/.rels", RootRelationships());
                Write(archive, "word/document.xml", Document(media));
                Write(archive, "word/_rels/document.xml.rels", DocumentRelationships(media));

                for (var index = 0; index < media.Count; index++)
                {
                    Write(archive, $"word/media/image{index + 1}.png", media[index]);
                }

                foreach (var (partName, _, content) in extras)
                {
                    Write(archive, partName, content);
                }
            }

            return buffer.ToArray();
        }

        string ContentTypes()
        {
            var extensions = extras
                .Select(_ => (Extension: _.PartName[(_.PartName.LastIndexOf('.') + 1)..], _.ContentType))
                .Concat([(Extension: "png", ContentType: "image/png")])
                .DistinctBy(_ => _.Extension, StringComparer.OrdinalIgnoreCase)
                .Select(_ => $"""<Default Extension="{_.Extension}" ContentType="{_.ContentType}" />""");

            return $"""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                    <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
                    <Default Extension="xml" ContentType="application/xml" />
                    {string.Join("\n", extensions)}
                    <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" />
                    </Types>
                    """;
        }

        string RootRelationships()
        {
            var thumbnail = extras.Any(_ => _.PartName == "docProps/thumbnail.png")
                ? """<Relationship Id="rIdThumb" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail" Target="docProps/thumbnail.png" />"""
                : "";

            return $"""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                    <Relationship Id="rIdDoc" Type="{relationshipTypes}/officeDocument" Target="word/document.xml" />
                    {thumbnail}
                    </Relationships>
                    """;
        }

        string DocumentRelationships(List<byte[]> media)
        {
            var relationships = media
                .Select((_, index) =>
                    $"""<Relationship Id="rId{index + 1}" Type="{relationshipTypes}/image" Target="media/image{index + 1}.png" />""")
                .Concat(vmlRelationships.Select(_ =>
                    $"""<Relationship Id="{_.Id}" Type="{relationshipTypes}/image" Target="{_.Target}" />"""));

            return $"""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                    {string.Join("\n", relationships)}
                    </Relationships>
                    """;
        }

        string Document(List<byte[]> media)
        {
            var paragraphs = placements
                .Select((placement, index) => Paragraph(placement, index, media))
                .Concat(bodies);

            return $"""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                                xmlns:r="{relationshipTypes}"
                                xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                                xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"
                                xmlns:wpg="http://schemas.microsoft.com/office/word/2010/wordprocessingGroup"
                                xmlns:v="urn:schemas-microsoft-com:vml"
                                xmlns:o="urn:schemas-microsoft-com:office:office">
                    <w:body>
                    {string.Join("\n", paragraphs)}
                    <w:sectPr><w:pgSz w:w="11906" w:h="16838" /></w:sectPr>
                    </w:body>
                    </w:document>
                    """;
        }

        static string Paragraph(Placement placement, int index, List<byte[]> media)
        {
            var relationship = media.FindIndex(_ => ReferenceEquals(_, placement.Data)) + 1;

            var width = (long) (placement.Inches * 914400);
            var height = width * 3 / 4;
            var outerWidth = (long) (width * placement.GroupScale);
            var outerHeight = (long) (height * placement.GroupScale);

            var crop = placement.CropSides > 0
                ? $"""<a:srcRect l="{(int) (placement.CropSides * 100000)}" r="{(int) (placement.CropSides * 100000)}" />"""
                : "";

            var picture =
                $"""
                 <pic:pic>
                 <pic:nvPicPr><pic:cNvPr id="{index + 1}" name="image{relationship}.png" /><pic:cNvPicPr /></pic:nvPicPr>
                 <pic:blipFill><a:blip r:embed="rId{relationship}" />{crop}<a:stretch><a:fillRect /></a:stretch></pic:blipFill>
                 <pic:spPr><a:xfrm><a:off x="0" y="0" /><a:ext cx="{width}" cy="{height}" /></a:xfrm>
                 <a:prstGeom prst="rect"><a:avLst /></a:prstGeom></pic:spPr>
                 </pic:pic>
                 """;

            var content = placement.GroupScale > 1
                ? $"""
                   <a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingGroup">
                   <wpg:wgp><wpg:cNvGrpSpPr />
                   <wpg:grpSpPr><a:xfrm><a:off x="0" y="0" /><a:ext cx="{outerWidth}" cy="{outerHeight}" />
                   <a:chOff x="0" y="0" /><a:chExt cx="{width}" cy="{height}" /></a:xfrm></wpg:grpSpPr>
                   {picture}
                   </wpg:wgp>
                   </a:graphicData>
                   """
                : $"""
                   <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                   {picture}
                   </a:graphicData>
                   """;

            return $"""
                    <w:p><w:r><w:drawing>
                    <wp:inline distT="0" distB="0" distL="0" distR="0">
                    <wp:extent cx="{outerWidth}" cy="{outerHeight}" />
                    <wp:docPr id="{index + 1}" name="Picture {index + 1}" />
                    <a:graphic>
                    {content}
                    </a:graphic>
                    </wp:inline>
                    </w:drawing></w:r></w:p>
                    """;
        }

        static void Write(ZipArchive archive, string partName, string content) =>
            Write(archive, partName, Encoding.UTF8.GetBytes(content));

        static void Write(ZipArchive archive, string partName, byte[] content)
        {
            using var stream = archive.CreateEntry(partName, CompressionLevel.Optimal).Open();
            stream.Write(content);
        }
    }

    /// <summary>The package a compression produced, alongside what the compressor said about it.</summary>
    sealed class Output(ImageCompressionResult result, byte[] bytes)
    {
        public IReadOnlyList<ImageReport> Images => result.Images;
        public long Saved => result.Saved;

        public byte[] Part(string partName)
        {
            using var package = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            using var content = package.GetEntry(partName)!.Open();
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            return buffer.ToArray();
        }

        public IReadOnlyList<string> PartNames()
        {
            using var package = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            return package.Entries.Select(_ => _.FullName).ToList();
        }

        public string Text(string partName) =>
            Encoding.UTF8.GetString(Part(partName)).TrimStart('﻿');
    }
}
