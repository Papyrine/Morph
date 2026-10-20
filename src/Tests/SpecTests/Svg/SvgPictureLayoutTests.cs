/// <summary>
/// How a picture stored as SVG reaches a painter: the parsers keep the SVG as the picture and the
/// raster beside it as its fallback, and the layout engine hands the painters a
/// <see cref="PlacedImage.Vector"/> for every SVG the reader takes — and for the rest the
/// fallback, as it handed them for all before 2026-10-12.
/// </summary>
public class SvgPictureLayoutTests
{
    static readonly Fragmenter fragmenter = new(LayoutTestFonts.Measurer);

    static readonly byte[] redSquare = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\" fill=\"#FF0000\"/></svg>"u8.ToArray();

    // A document the reader gives up: its one shape is filled from a gradient.
    static readonly byte[] gradientSquare = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#F00\"/></linearGradient><rect width=\"10\" height=\"10\" fill=\"url(#g)\"/></svg>"u8.ToArray();

    static readonly byte[] fallback = "PNG"u8.ToArray();

    static PageSettings Page() =>
        new()
        {
            WidthPoints = 300,
            HeightPoints = 400,
            MarginTop = 20,
            MarginBottom = 20,
            MarginLeft = 20,
            MarginRight = 20
        };

    static ParagraphElement Inline(byte[] svg, byte[]? raster) =>
        new()
        {
            Runs =
            [
                new()
                {
                    Text = "",
                    Properties = new() {FontFamily = "Aptos", FontSizePoints = 11},
                    InlineImageData = svg,
                    InlineImageContentType = "image/svg+xml",
                    InlineImageRasterFallbackData = raster,
                    InlineImageWidthPoints = 24,
                    InlineImageHeightPoints = 24
                }
            ],
            Properties = new()
        };

    static FloatingImageElement Floating(byte[] svg, byte[]? raster) =>
        new()
        {
            ImageData = svg,
            ContentType = "image/svg+xml",
            RasterFallbackData = raster,
            WidthPoints = 100,
            HeightPoints = 60,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Margin,
            BehindText = true
        };

    static List<PlacedImage> Images(IEnumerable<PlacedItem> items)
    {
        var images = new List<PlacedImage>();
        foreach (var item in items)
        {
            switch (item)
            {
                case PlacedImage image:
                    images.Add(image);
                    break;
                case PlacedLine line:
                    images.AddRange(line.Images);
                    break;
                case PlacedTableRow row:
                    foreach (var cell in row.Cells)
                    {
                        images.AddRange(Images(cell.Floats));
                        images.AddRange(Images(cell.Content));
                    }

                    break;
                case PlacedClipGroup clip:
                    images.AddRange(Images(clip.Items));
                    break;
                case PlacedRotatedGroup group:
                    images.AddRange(Images(group.Items));
                    break;
            }
        }

        return images;
    }

    static List<PlacedImage> Images(LaidOutDocument document) =>
        document.Pages.SelectMany(_ => Images(_.Items)).ToList();

    static string Input(string scenario)
    {
        var directory = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", scenario.Replace('/', Path.DirectorySeparatorChar));
        return Directory.GetFiles(directory, "input.*").Single();
    }

    [Test]
    public async Task An_inline_SVG_picture_is_placed_with_its_drawing_and_its_fallback()
    {
        var placed = Images(fragmenter.Layout([Inline(redSquare, fallback)], Page())).Single();

        await Assert.That(placed.Vector!.Paths.Single().FillColorHex).IsEqualTo("FF0000");
        await Assert.That(ReferenceEquals(placed.Data, fallback)).IsTrue();
        await Assert.That(placed.Width).IsEqualTo(24f);
    }

    [Test]
    public async Task An_inline_SVG_the_reader_gives_up_is_placed_with_its_fallback_alone()
    {
        var placed = Images(fragmenter.Layout([Inline(gradientSquare, fallback)], Page())).Single();

        await Assert.That(placed.Vector).IsNull();
        await Assert.That(ReferenceEquals(placed.Data, fallback)).IsTrue();
    }

    [Test]
    public async Task A_picture_stored_as_SVG_alone_is_placed_when_the_reader_takes_it_and_only_then()
    {
        // No raster to fall back on: before there was a reader such a picture took no place at all.
        var placed = Images(fragmenter.Layout([Inline(redSquare, null)], Page())).Single();
        await Assert.That(placed.Vector).IsNotNull();
        await Assert.That(placed.Data).IsNull();

        await Assert.That(Images(fragmenter.Layout([Inline(gradientSquare, null)], Page())).Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_floating_SVG_picture_is_placed_the_same_way()
    {
        var taken = Images(fragmenter.Layout([Floating(redSquare, fallback)], Page())).Single();
        await Assert.That(taken.Vector).IsNotNull();
        await Assert.That(ReferenceEquals(taken.Data, fallback)).IsTrue();

        var givenUp = Images(fragmenter.Layout([Floating(gradientSquare, fallback)], Page())).Single();
        await Assert.That(givenUp.Vector).IsNull();
        await Assert.That(ReferenceEquals(givenUp.Data, fallback)).IsTrue();

        var alone = Images(fragmenter.Layout([Floating(redSquare, null)], Page())).Single();
        await Assert.That(alone.Vector).IsNotNull();
        await Assert.That(Images(fragmenter.Layout([Floating(gradientSquare, null)], Page())).Count).IsEqualTo(0);
    }

    [Test]
    public async Task The_three_stars_of_icons_multiple_are_placed_blue_red_and_green()
    {
        // Their raster fallbacks are one blue PNG three times over; the colours are in the SVGs alone.
        using var stream = File.OpenRead(Input("word/icons_multiple"));
        var document = new DocumentParser().Parse(stream);

        var fills = Images(fragmenter.Layout(document.Elements, document.PageSettings))
            .Select(_ => _.Vector?.Paths.Single().FillColorHex);

        await Assert.That(string.Join(" ", fills)).IsEqualTo("4285F4 EA4335 34A853");
    }

    [Test]
    public async Task A_PowerPoint_picture_that_is_only_an_SVG_is_parsed_and_placed()
    {
        // education-infographics-poster's cap, microscope and figures: an a:blip with no r:embed,
        // only the asvg:svgBlip extension.
        using var stream = File.OpenRead(Input("powerpoint/education-infographics-poster"));
        var document = PowerPointConverter.Parse(stream, null);

        var pictures = Images(fragmenter.Layout(document.Elements, document.PageSettings))
            .Where(_ => _.Vector != null)
            .ToList();

        await Assert.That(pictures.Count).IsGreaterThanOrEqualTo(5);
        await Assert.That(pictures.All(_ => _.Data == null)).IsTrue();
    }

    [Test]
    public async Task An_Excel_picture_keeps_its_raster_as_the_fallback_beside_its_SVG()
    {
        using var stream = File.OpenRead(Input("excel/wedding-budget-template"));
        var document = ExcelConverter.Parse(stream, new ImageExportOptions {FontDirectory = ProjectFonts.Directory});

        var bells = Images(fragmenter.Layout(document.Elements, document.PageSettings)).First(_ => _.Vector != null);

        // The theme class's colour, which is the one Office drew, not the attribute beside it.
        await Assert.That(bells.Vector!.Paths[0].FillColorHex).IsEqualTo("997A28");
        await Assert.That(bells.Data).IsNotNull();
    }

    [Test]
    public async Task Every_SVG_part_in_the_corpus_is_read_or_given_up_for_a_reason_on_record()
    {
        // The survey the reader was built from, kept honest: a new scenario whose SVG uses
        // something the reader does not draw shows up here by name, rather than as a picture that
        // quietly stays a raster.
        var root = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
        var read = 0;
        var givenUp = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var directory in ScenarioInputs.AllDirectories())
        {
            using var archive = ZipFile.OpenRead(ScenarioInputs.InputFile(directory));
            foreach (var entry in archive.Entries.Where(_ => _.FullName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)))
            {
                using var part = entry.Open();
                using var buffer = new MemoryStream();
                await part.CopyToAsync(buffer);
                var result = SvgReader.Read(buffer.ToArray());
                if (result.Drawing != null)
                {
                    read++;
                }
                else
                {
                    givenUp.Add($"{Path.GetRelativePath(root, directory).Replace('\\', '/')}: {result.Unsupported}");
                }
            }
        }

        await Assert.That(read).IsGreaterThanOrEqualTo(110);
        await Assert.That(string.Join("\n", givenUp)).IsEqualTo(
            """
            excel/inventory-list-with-highlighting: a gradient fill
            word/cards/12: opacity on a group of shapes that overlap
            word/labels/06: a mask
            word/postcards/02: opacity on a group of shapes that overlap
            word/postcards/03: opacity on a group of shapes that overlap
            word/wedding/10: a gradient fill
            """.ReplaceLineEndings("\n"));
    }
}
