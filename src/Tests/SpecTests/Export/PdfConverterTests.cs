/// <summary>
/// Smoke tests for the DOCX → PDF backend: real documents must convert to a valid, non-empty PDF
/// without throwing. (PDF bytes are not byte-stable, so these assert structure rather than snapshot.)
/// </summary>
public class PdfConverterTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    public static IEnumerable<string> SampleInputs()
    {
        var inputs = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word");
        foreach (var name in new[] {"bold_text", "align_center", "hyperlinks"})
        {
            var path = Path.Combine(inputs, name, "input.docx");
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(SampleInputs))]
    public async Task ProducesValidPdf(string inputPath)
    {
        var pdf = PdfDocumentConverter.ConvertToPdf(
            inputPath,
            new()
            {
                FontDirectory = fontsDirectory
            });

        await Assert.That(pdf.Length).IsGreaterThan(1000);
        // Every PDF starts with the "%PDF-" header.
        var header = Encoding.ASCII.GetString(pdf, 0, 5);
        await Assert.That(header).IsEqualTo("%PDF-");
    }

    [Test]
    public async Task OutputIsDeterministic()
    {
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "bullet_list", "input.docx");
        var first = PdfDocumentConverter.ConvertToPdf(
            input,
            new()
            {
                FontDirectory = fontsDirectory
            });
        var second = PdfDocumentConverter.ConvertToPdf(
            input,
            new()
            {
                FontDirectory = fontsDirectory
            });

        await Assert.That(second).IsEquivalentTo(first);
    }

    /// <summary>
    /// <see cref="PdfExportOptions.RasterizeWordArt"/> reaches the painter, not just the measurer: with it
    /// off the warped figures are dropped, so the page carries less ink. The scenario snapshots cannot
    /// guard this — they all render at the default — and the painter once hardcoded its settings, honouring
    /// neither this nor the font width scale / fallback / compatibility the measurer was given (a painter
    /// resolving a different face than the measurer draws text off its measured line).
    /// </summary>
    [Test]
    public async Task RasterizeWordArtOffDropsTheWarpedFigures()
    {
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "wordart-envelope", "input.docx");

        var withArt = PdfDocumentConverter.ConvertToPdf(
            input,
            new()
            {
                FontDirectory = fontsDirectory,
                RasterizeWordArt = true
            });
        var withoutArt = PdfDocumentConverter.ConvertToPdf(
            input,
            new()
            {
                FontDirectory = fontsDirectory,
                RasterizeWordArt = false
            });

        // The warps reserve their height either way, so only the drawing differs — dropping four
        // rasterized figures makes for a markedly smaller file.
        await Assert.That(withoutArt.Length).IsLessThan(withArt.Length);
        await Assert.That(Encoding.ASCII.GetString(withoutArt, 0, 5)).IsEqualTo("%PDF-");
    }

    /// <summary>
    /// When an image's primary content type is one PdfSharp can't decode (SVG) <em>and</em> its
    /// raster fallback is equally undecodable, <c>PdfPainter</c> must skip the image
    /// rather than hand the bytes to <c>XImage.FromStream</c> — which throws. Regression guard for
    /// the fallback check; without it this render throws instead of producing a PDF.
    /// </summary>
    [Test]
    public async Task ImageWithUndecodableFallbackIsSkippedNotThrown()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" /></svg>"u8.ToArray();

        var document = new ParsedDocument
        {
            PageSettings = new(),
            Elements =
            [
                new ParagraphElement
                {
                    Runs =
                    [
                        new()
                        {
                            Text = "anchor"
                        }
                    ],
                    Properties = new()
                },
                new FloatingImageElement
                {
                    ImageData = svg,
                    ContentType = "image/svg+xml",
                    RasterFallbackData = svg,
                    RasterFallbackContentType = "image/svg+xml",
                    WidthPoints = 50,
                    HeightPoints = 50
                }
            ]
        };

        var pdf = PdfRenderer.Render(
            document,
            new()
            {
                FontDirectory = fontsDirectory
            });

        await Assert.That(pdf.Length).IsGreaterThan(1000);
        await Assert.That(Encoding.ASCII.GetString(pdf, 0, 5)).IsEqualTo("%PDF-");
    }

    [Test]
    public async Task MultiPageDocumentPaginates()
    {
        var inputPath = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "agendas-minutes", "02", "input.docx");
        if (!File.Exists(inputPath))
        {
            return;
        }

        var pdf = PdfDocumentConverter.ConvertToPdf(
            inputPath,
            new()
            {
                FontDirectory = fontsDirectory
            });

        using var document = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        await Assert.That(document.PageCount).IsGreaterThanOrEqualTo(2);
    }
}
