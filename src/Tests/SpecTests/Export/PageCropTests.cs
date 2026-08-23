using SkiaSharp;

/// <summary>
/// Covers <see cref="ImageExportOptions.Crop"/> — emitting the content box instead of the whole
/// sheet, so a caller wanting a thumbnail or a preview does not have to re-derive the document's
/// margins to crop it themselves.
///
/// The fixture is 300x400pt with 20pt margins and its header/footer bands 10pt in, rendered at
/// 144 DPI so a point is exactly two pixels and every expected number below is exact rather than
/// a truncation:
///
/// <code>
/// FullPage                    600 x 800  at (0, 0)
/// ContentBox                  520 x 720  at (40, 40)
/// ContentBoxWithHeaderFooter  520 x 760  at (40, 20)
/// </code>
///
/// Both tiers are tested, per the lesson of the UseLetterPageSize render guard: the rectangle is
/// pinned directly on <c>PageRect</c>, AND the emitted PNG is measured, because the option can be
/// computed correctly and still not reach a page.
/// </summary>
public class PageCropTests
{
    const int dpi = 144;
    const double pageWidthPoints = 300;
    const double pageHeightPoints = 400;
    const double marginPoints = 20;
    const double bandDistancePoints = 10;

    [Test]
    public async Task FullPageIsTheDefault() =>
        await Assert.That(new ImageExportOptions().Crop).IsEqualTo(PageCrop.FullPage);

    [Test]
    [Arguments(PageCrop.FullPage, 0, 0, 600, 800)]
    [Arguments(PageCrop.ContentBox, 40, 40, 520, 720)]
    [Arguments(PageCrop.ContentBoxWithHeaderFooter, 40, 20, 520, 760)]
    public async Task PageRectDropsTheRequestedMargins(PageCrop crop, int x, int y, int width, int height)
    {
        using var context = new SkiaRenderContext(Settings(), dpi);

        await Assert.That(context.PageRect(Settings(), crop)).IsEqualTo((x, y, width, height));
    }

    /// <summary>
    /// The gutter is added to the left margin at parse time (DocumentParser.ExtractPageSettings) and
    /// kept on PageSettings only for consumers, so a crop that reads both charges for it twice. Here
    /// a 20pt margin plus a 10pt gutter is the 30pt MarginLeft a parsed document would carry: the
    /// crop starts at 30pt (60px), not at 40.
    /// </summary>
    [Test]
    public async Task PageRectDoesNotChargeTheGutterTwice()
    {
        var gutter = Settings() with
        {
            MarginLeft = 30,
            GutterPoints = 10
        };
        using var context = new SkiaRenderContext(gutter, dpi);

        await Assert.That(context.PageRect(gutter, PageCrop.ContentBox)).IsEqualTo((60, 40, 500, 720));
    }

    /// <summary>
    /// A section break can change the margins as well as the paper, so the rectangle is resolved
    /// from the settings the page was laid at rather than from the context's own.
    /// </summary>
    [Test]
    public async Task PageRectFollowsThePagesOwnSettings()
    {
        using var context = new SkiaRenderContext(Settings(), dpi);
        var wideMargins = Settings() with
        {
            MarginLeft = 50,
            MarginRight = 50
        };

        await Assert.That(context.PageRect(wideMargins, PageCrop.ContentBox)).IsEqualTo((100, 40, 400, 720));
    }

    /// <summary>
    /// Margins are not validated against the paper upstream, so a document declaring more margin
    /// than it has page still has to produce something drawable rather than a negative rectangle.
    /// </summary>
    [Test]
    public async Task PageRectSurvivesMarginsWiderThanThePage()
    {
        var absurd = Settings() with
        {
            MarginLeft = 400,
            MarginRight = 400,
            MarginTop = 500,
            MarginBottom = 500
        };
        using var context = new SkiaRenderContext(absurd, dpi);

        var rect = context.PageRect(absurd, PageCrop.ContentBox);
        await Assert.That(rect.Width).IsGreaterThanOrEqualTo(1);
        await Assert.That(rect.Height).IsGreaterThanOrEqualTo(1);
        await Assert.That(rect.X + rect.Width).IsLessThanOrEqualTo(600);
        await Assert.That(rect.Y + rect.Height).IsLessThanOrEqualTo(800);
    }

    // The render guard: PageRect can be right and still never reach a page.
    [Test]
    [Arguments(Backend.Skia, PageCrop.FullPage, 600, 800)]
    [Arguments(Backend.Skia, PageCrop.ContentBox, 520, 720)]
    [Arguments(Backend.Skia, PageCrop.ContentBoxWithHeaderFooter, 520, 760)]
    [Arguments(Backend.ImageSharp, PageCrop.FullPage, 600, 800)]
    [Arguments(Backend.ImageSharp, PageCrop.ContentBox, 520, 720)]
    [Arguments(Backend.ImageSharp, PageCrop.ContentBoxWithHeaderFooter, 520, 760)]
    public async Task RenderedPageIsCroppedToTheRect(Backend backend, PageCrop crop, int width, int height)
    {
        var png = Render(backend, Document(), crop);

        await Assert.That(PngSize(png)).IsEqualTo((width, height));
    }

    /// <summary>
    /// The whole contract in one assertion: the emitted pixels ARE the full render's own, at the
    /// crop's offset. Nothing is rescaled to fill the smaller image, and the origin is a whole
    /// pixel — a fractional one would have to resample and this would fail.
    /// </summary>
    [Test]
    [Arguments(Backend.Skia)]
    [Arguments(Backend.ImageSharp)]
    public async Task CroppedPixelsAreTheFullRendersOwn(Backend backend)
    {
        using var full = SKBitmap.Decode(Render(backend, Document(), PageCrop.FullPage));
        using var cropped = SKBitmap.Decode(Render(backend, Document(), PageCrop.ContentBox));

        await Assert.That(cropped.ColorType).IsEqualTo(full.ColorType);
        await Assert.That(FirstRowDifferingFrom(cropped, full, 40, 40)).IsEqualTo(-1);
    }

    /// <summary>
    /// Word draws the header inside the top margin — 10pt down against this fixture's 20pt margin —
    /// so which crop is asked for decides whether it survives at all. The bar is the only red thing
    /// in the document, so scanning for red answers the question wherever it lands.
    /// </summary>
    [Test]
    [Arguments(Backend.Skia, PageCrop.FullPage, true)]
    [Arguments(Backend.Skia, PageCrop.ContentBox, false)]
    [Arguments(Backend.Skia, PageCrop.ContentBoxWithHeaderFooter, true)]
    [Arguments(Backend.ImageSharp, PageCrop.FullPage, true)]
    [Arguments(Backend.ImageSharp, PageCrop.ContentBox, false)]
    [Arguments(Backend.ImageSharp, PageCrop.ContentBoxWithHeaderFooter, true)]
    public async Task HeaderSurvivesOnlyWhenTheBandsAreKept(Backend backend, PageCrop crop, bool expected)
    {
        var png = Render(backend, Document(), crop);

        await Assert.That(ContainsRed(png)).IsEqualTo(expected);
    }

    /// <summary>
    /// HTML in reaches the painter through <c>HtmlConverter</c> rather than
    /// <c>DocumentConverter</c>, so it needs its own guard even though both end at the same
    /// <c>RenderPagesCounted</c>. Asserted against the full render rather than absolute numbers,
    /// since the paper an HTML source gets is the region default.
    /// </summary>
    [Test]
    [Arguments(Backend.Skia)]
    [Arguments(Backend.ImageSharp)]
    public async Task HtmlInputCarriesTheCrop(Backend backend)
    {
        const string html = "<p>Cropping leaves the layout alone.</p>";

        var (fullWidth, fullHeight) = PngSize(await RenderHtml(backend, html, PageCrop.FullPage));
        var cropped = PngSize(await RenderHtml(backend, html, PageCrop.ContentBox));

        // HtmlConverter.ParseHtml fixes the margins at 72pt whatever the source says, and 72pt at
        // 150 DPI is exactly 150px, so the content box is 300px smaller on each axis.
        await Assert.That(cropped).IsEqualTo((fullWidth - 300, fullHeight - 300));
    }

    // Unset must move nobody: every corpus baseline is a full-page render.
    [Test]
    [Arguments(Backend.Skia)]
    [Arguments(Backend.ImageSharp)]
    public async Task UnsetRendersExactlyAsFullPage(Backend backend)
    {
        var unset = Render(backend, Document(), null);
        var fullPage = Render(backend, Document(), PageCrop.FullPage);

        await Assert.That(unset).IsEquivalentTo(fullPage);
    }

    public enum Backend
    {
        Skia,
        ImageSharp
    }

    static PageSettings Settings() =>
        new()
        {
            WidthPoints = pageWidthPoints,
            HeightPoints = pageHeightPoints,
            MarginTop = marginPoints,
            MarginBottom = marginPoints,
            MarginLeft = marginPoints,
            MarginRight = marginPoints,
            HeaderDistance = bandDistancePoints,
            FooterDistance = bandDistancePoints
        };

    // A page-anchored bar sitting wholly inside the header band — above the content box, below the
    // paper edge — plus body text, so the content area carries ink for the pure-crop comparison.
    static ParsedDocument Document() =>
        new()
        {
            PageSettings = Settings(),
            Elements =
            [
                Para(TextRun("Cropping leaves the layout alone."))
            ],
            Header = new()
            {
                Elements =
                [
                    new FloatingShapeElement
                    {
                        FillColorHex = "FF0000",
                        WidthPoints = 100,
                        HeightPoints = 6,
                        HorizontalAnchor = HorizontalAnchor.Page,
                        VerticalAnchor = VerticalAnchor.Page,
                        HorizontalPositionPoints = 100,
                        VerticalPositionPoints = 12,
                        BehindText = true
                    }
                ]
            }
        };

    static byte[] Render(Backend backend, ParsedDocument document, PageCrop? crop)
    {
        var options = new ImageExportOptions
        {
            Dpi = dpi,
            FontDirectory = ProjectFonts.Directory,
            DeterministicRendering = true
        };

        if (crop is { } value)
        {
            options = options with
            {
                Crop = value
            };
        }

        byte[]? result = null;
        void Sink(Action<Stream> writePng)
        {
            using var stream = new MemoryStream();
            writePng(stream);
            result ??= stream.ToArray();
        }

        if (backend == Backend.Skia)
        {
            SkiaDocumentConverter.RenderPagesCounted(document, options, Sink);
        }
        else
        {
            ImageSharpDocumentConverter.RenderPagesCounted(document, options, Sink);
        }

        return result!;
    }

    static async Task<byte[]> RenderHtml(Backend backend, string html, PageCrop crop)
    {
        var options = new ImageExportOptions
        {
            Dpi = 150,
            FontDirectory = ProjectFonts.Directory,
            DeterministicRendering = true,
            Crop = crop
        };

        HtmlConverter converter = backend == Backend.Skia
            ? new SkiaHtmlConverter()
            : new ImageSharpHtmlConverter();

        return (await converter.ConvertToImageData(html, options))[0];
    }

    // IHDR is the first chunk: an 8-byte signature, then length/type, then width and height as
    // big-endian. Read by hand rather than through a decoder, so the assertion is on the file the
    // caller receives. Same helper as DefaultPaperSizeTests.
    static (int Width, int Height) PngSize(byte[] png) =>
        ((png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19],
            (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);

    // The index of the first row of `cropped` that is not `full`'s row at the same offset, or -1
    // when every row matches. Returning the row rather than a bool makes a failure locatable.
    static int FirstRowDifferingFrom(SKBitmap cropped, SKBitmap full, int offsetX, int offsetY)
    {
        var croppedBytes = cropped.Bytes;
        var fullBytes = full.Bytes;
        var rowLength = cropped.Width * cropped.BytesPerPixel;

        for (var y = 0; y < cropped.Height; y++)
        {
            var left = croppedBytes.AsSpan(y * cropped.RowBytes, rowLength);
            var right = fullBytes.AsSpan((y + offsetY) * full.RowBytes + offsetX * full.BytesPerPixel, rowLength);
            if (!left.SequenceEqual(right))
            {
                return y;
            }
        }

        return -1;
    }

    static bool ContainsRed(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel is {Red: > 200, Green: < 50, Blue: < 50})
                {
                    return true;
                }
            }
        }

        return false;
    }
}
