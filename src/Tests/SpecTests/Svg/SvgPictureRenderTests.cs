using Morph.PDFium;

/// <summary>
/// Each backend's painter drawing a picture from its SVG, checked by the colour of a few pixels
/// rather than against a baseline, so the tests hold on any machine. One picture floats on an
/// otherwise empty page, and the same assertions run through Skia, ImageSharp and the PDF painter
/// (rasterised by PDFium): the three draw the one <see cref="SvgDrawing"/> with their own path
/// calls, and what they must agree on is where its colours land.
/// </summary>
public class SvgPictureRenderTests
{
    const int dpi = 96;

    static readonly byte[] blueFallback = SolidPng(new(0, 0, 255));

    // Red all over.
    const string red = "<rect width=\"30\" height=\"10\" fill=\"#FF0000\"/>";

    // Green in its left third, red in the rest — enough to tell which way round it was drawn.
    const string greenThenRed = "<rect width=\"10\" height=\"10\" fill=\"#00C000\"/><rect x=\"10\" width=\"20\" height=\"10\" fill=\"#FF0000\"/>";

    static byte[] Svg(string body) =>
        Encoding.UTF8.GetBytes($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 30 10\">{body}</svg>");

    static byte[] SolidPng(Rgba32 color)
    {
        using var image = new Image<Rgba32>(30, 10, color);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    // The picture's box: 120 by 40 points with its corner at (60, 150), on a 300 by 400 page.
    static FloatingImageElement Picture(byte[] svg, byte[]? fallback, ImageCrop? crop = null, double rotationDegrees = 0, bool flipHorizontal = false, BlipColorEffect colorEffect = BlipColorEffect.None) =>
        new()
        {
            ImageData = svg,
            ContentType = "image/svg+xml",
            RasterFallbackData = fallback,
            WidthPoints = 120,
            HeightPoints = 40,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Page,
            HorizontalPositionPoints = 60,
            VerticalPositionPoints = 150,
            BehindText = true,
            Crop = crop,
            RotationDegrees = rotationDegrees,
            FlipHorizontal = flipHorizontal,
            ColorEffect = colorEffect
        };

    static Image<Rgba32> Render(string backend, FloatingImageElement picture) =>
        Image.Load<Rgba32>(RenderBytes(backend, picture, out _));

    static byte[] RenderBytes(string backend, FloatingImageElement picture, out byte[]? pdf)
    {
        var document = new ParsedDocument
        {
            PageSettings = new()
            {
                WidthPoints = 300,
                HeightPoints = 400,
                MarginTop = 20,
                MarginBottom = 20,
                MarginLeft = 20,
                MarginRight = 20,
                HeaderDistance = 0,
                FooterDistance = 0
            },
            Elements =
            [
                picture,
                new ParagraphElement
                {
                    Runs = [new() {Text = ".", Properties = new() {FontFamily = "Aptos", FontSizePoints = 11}}],
                    Properties = new()
                }
            ]
        };

        pdf = null;
        if (backend == "pdf")
        {
            pdf = PdfRenderer.Render(document, new() {FontDirectory = ProjectFonts.Directory});
            using var pages = PdfiumDocument.Load(pdf);
            return pages.RenderPage(0, dpi);
        }

        var options = new ImageExportOptions {Dpi = dpi, FontDirectory = ProjectFonts.Directory};
        byte[]? page = null;
        void Keep(Action<Stream> write)
        {
            using var stream = new MemoryStream();
            write(stream);
            page ??= stream.ToArray();
        }

        if (backend == "skia")
        {
            SkiaDocumentConverter.RenderPagesCounted(document, options, Keep);
        }
        else
        {
            ImageSharpDocumentConverter.RenderPagesCounted(document, options, Keep);
        }

        return page!;
    }

    // The pixel under a point of the page.
    static Rgba32 At(Image<Rgba32> page, double xPoints, double yPoints) =>
        page[(int) (xPoints * dpi / 72), (int) (yPoints * dpi / 72)];

    static bool IsRed(Rgba32 pixel) => pixel is {R: > 200, G: < 60, B: < 60};

    static bool IsGreen(Rgba32 pixel) => pixel is {R: < 60, G: > 150, B: < 60};

    static bool IsBlue(Rgba32 pixel) => pixel is {R: < 60, G: < 60, B: > 200};

    static bool IsWhite(Rgba32 pixel) => pixel is {R: > 240, G: > 240, B: > 240};

    [Test]
    [Arguments("skia")]
    [Arguments("imagesharp")]
    [Arguments("pdf")]
    public async Task A_picture_is_drawn_from_its_SVG_not_from_the_raster_beside_it(string backend)
    {
        // icons_multiple in small: the fallback is blue, the SVG is not.
        using var page = Render(backend, Picture(Svg(red), blueFallback));

        await Assert.That(IsRed(At(page, 120, 170))).IsTrue();
        await Assert.That(IsRed(At(page, 62, 152))).IsTrue();
        await Assert.That(IsRed(At(page, 178, 188))).IsTrue();
        await Assert.That(IsWhite(At(page, 56, 170))).IsTrue();
        await Assert.That(IsWhite(At(page, 120, 194))).IsTrue();
    }

    [Test]
    [Arguments("skia")]
    [Arguments("imagesharp")]
    [Arguments("pdf")]
    public async Task A_picture_whose_SVG_the_reader_gives_up_is_drawn_from_its_raster_as_before(string backend)
    {
        var gradient = Svg("<linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#F00\"/></linearGradient><rect width=\"30\" height=\"10\" fill=\"url(#g)\"/>");
        using var page = Render(backend, Picture(gradient, blueFallback));

        await Assert.That(IsBlue(At(page, 120, 170))).IsTrue();
    }

    [Test]
    [Arguments("skia")]
    [Arguments("imagesharp")]
    [Arguments("pdf")]
    public async Task A_picture_stored_as_SVG_alone_is_drawn(string backend)
    {
        using var page = Render(backend, Picture(Svg(red), null));

        await Assert.That(IsRed(At(page, 120, 170))).IsTrue();
    }

    [Test]
    [Arguments("skia")]
    [Arguments("imagesharp")]
    [Arguments("pdf")]
    public async Task A_crop_shows_the_cropped_part_of_the_drawing_over_the_whole_box(string backend)
    {
        // The left third cropped away: the box is all red, where uncropped its left end is green.
        using var page = Render(backend, Picture(Svg(greenThenRed), blueFallback, crop: new() {Left = 1d / 3}));

        await Assert.That(IsRed(At(page, 64, 170))).IsTrue();
        await Assert.That(IsRed(At(page, 176, 170))).IsTrue();
        await Assert.That(IsWhite(At(page, 56, 170))).IsTrue();
    }

    [Test]
    [Arguments("skia")]
    [Arguments("imagesharp")]
    [Arguments("pdf")]
    public async Task A_clip_path_cuts_by_its_own_rule(string backend)
    {
        // An even-odd ring: the frame's outline with a hole through its middle. Read as nonzero —
        // which is how ImageSharp's canvas reads any clip path — the hole would fill.
        var ring = Svg("<clipPath id=\"ring\"><path d=\"M0 0H30V10H0Z M6 2H24V8H6Z\" clip-rule=\"evenodd\"/></clipPath><g clip-path=\"url(#ring)\">" + red + "</g>");
        using var page = Render(backend, Picture(ring, blueFallback));

        await Assert.That(IsRed(At(page, 68, 170))).IsTrue();
        await Assert.That(IsRed(At(page, 172, 170))).IsTrue();
        await Assert.That(IsRed(At(page, 120, 153))).IsTrue();
        await Assert.That(IsWhite(At(page, 120, 170))).IsTrue();
    }

    [Test]
    [Arguments("skia")]
    [Arguments("imagesharp")]
    [Arguments("pdf")]
    public async Task A_turned_picture_turns_about_the_middle_of_its_box(string backend)
    {
        // A quarter turn clockwise about (120, 170): the box now stands 40 wide and 120 tall, its
        // green end at the top.
        using var page = Render(backend, Picture(Svg(greenThenRed), blueFallback, rotationDegrees: 90));

        await Assert.That(IsGreen(At(page, 120, 120))).IsTrue();
        await Assert.That(IsRed(At(page, 120, 220))).IsTrue();
        await Assert.That(IsWhite(At(page, 70, 170))).IsTrue();
        await Assert.That(IsWhite(At(page, 170, 170))).IsTrue();
    }

    [Test]
    [Arguments("skia")]
    [Arguments("imagesharp")]
    [Arguments("pdf")]
    public async Task A_mirrored_picture_is_drawn_mirrored(string backend)
    {
        using var page = Render(backend, Picture(Svg(greenThenRed), blueFallback, flipHorizontal: true));

        await Assert.That(IsRed(At(page, 70, 170))).IsTrue();
        await Assert.That(IsGreen(At(page, 170, 170))).IsTrue();
    }

    [Test]
    [Arguments("skia")]
    [Arguments("imagesharp")]
    [Arguments("pdf")]
    public async Task A_recolour_is_applied_to_the_drawing(string backend)
    {
        using var page = Render(backend, Picture(Svg(red), blueFallback, colorEffect: BlipColorEffect.Grayscale));

        // Pure red in greyscale is 21% of white.
        var pixel = At(page, 120, 170);
        await Assert.That(Math.Abs(pixel.R - 54)).IsLessThanOrEqualTo(3);
        await Assert.That(pixel.G).IsEqualTo(pixel.R);
        await Assert.That(pixel.B).IsEqualTo(pixel.R);
    }

    [Test]
    public async Task The_PDF_holds_the_picture_as_paths_and_does_not_embed_its_raster()
    {
        RenderBytes("pdf", Picture(Svg(red), blueFallback), out var pdf);
        var text = Encoding.Latin1.GetString(pdf!);

        await Assert.That(text).Contains("/Subtype/Form");
        await Assert.That(text).DoesNotContain("/Subtype/Image");
    }
}
