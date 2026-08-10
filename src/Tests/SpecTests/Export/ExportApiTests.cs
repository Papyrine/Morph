/// <summary>
/// Behavioural tests for the new per-format options (<see cref="HtmlExportOptions"/>,
/// <see cref="MarkdownExportOptions"/>, <see cref="PdfExportOptions"/>) — image handlers,
/// warning callbacks, pretty-print toggle, page range — and the parse-once
/// <see cref="WordDocument"/> wrapper.
/// </summary>
public class ExportApiTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    [Test]
    public async Task HtmlPrettyFormatFalse_DropsIndentation()
    {
        // Use a list — nested <li> are indented under <ul> in pretty mode.
        var document = Doc(ListItem("•", 18, "item1"), ListItem("•", 18, "item2"));

        var pretty = HtmlExporter.Export(document, new() {PrettyFormat = true});
        var compact = HtmlExporter.Export(document, new() {PrettyFormat = false});

        await Assert.That(pretty).Contains("  <li>");
        await Assert.That(compact).DoesNotContain("  <li>");
        // Content unchanged
        await Assert.That(compact).Contains("<li>item1</li>");
    }

    [Test]
    public async Task HtmlEmbedImagesFalse_NoHandler_OmitsImageTag()
    {
        var image = new ImageElement
        {
            ImageData = [0, 1, 2, 3],
            WidthPoints = 100,
            HeightPoints = 50
        };
        var document = Doc(image);

        var output = HtmlExporter.Export(document, new() {EmbedImagesAsBase64 = false});

        await Assert.That(output).DoesNotContain("<img");
        await Assert.That(output).DoesNotContain("data:");
    }

    [Test]
    public async Task HtmlImageHandler_ProvidesSrc()
    {
        var image = new ImageElement
        {
            ImageData = [9, 8, 7, 6],
            ContentType = "image/png",
            WidthPoints = 100,
            HeightPoints = 50
        };
        var document = Doc(image);

        var received = new List<EmbeddedImage>();
        var output = HtmlExporter.Export(document, new()
        {
            ImageHandler = info =>
            {
                received.Add(info);
                return $"images/image-{info.Index}.png";
            }
        });

        await Assert.That(received.Count).IsEqualTo(1);
        await Assert.That(received[0].ContentType).IsEqualTo("image/png");
        await Assert.That(received[0].Data.Length).IsEqualTo(4);
        await Assert.That(output).Contains("src=\"images/image-0.png\"");
        await Assert.That(output).DoesNotContain("base64,");
    }

    [Test]
    public async Task MarkdownImageHandler_ProvidesSrc()
    {
        var image = new ImageElement {ImageData = [1, 2], WidthPoints = 10, HeightPoints = 10};
        var document = Doc(image);

        var output = MarkdownExporter.Export(document, new()
        {
            ImageHandler = info => $"img/{info.Index}.png"
        });

        await Assert.That(output).Contains("![](img/0.png)");
        await Assert.That(output).DoesNotContain("base64,");
    }

    [Test]
    public async Task HtmlOnWarning_FiresForUnsupportedElement()
    {
        var document = Doc(new InkElement
        {
            WidthPoints = 100,
            HeightPoints = 100,
            Strokes = []
        });

        var warnings = new List<ExportWarning>();
        HtmlExporter.Export(document, new() {OnWarning = warnings.Add});

        await Assert.That(warnings.Count).IsEqualTo(1);
        await Assert.That(warnings[0].Kind).IsEqualTo(WarningKind.UnsupportedElement);
        await Assert.That(warnings[0].Message).Contains("InkElement");
    }

    [Test]
    public async Task MarkdownOnWarning_FiresForUnsupportedElement()
    {
        var document = Doc(new InkElement
        {
            WidthPoints = 100,
            HeightPoints = 100,
            Strokes = []
        });

        var warnings = new List<ExportWarning>();
        MarkdownExporter.Export(document, new() {OnWarning = warnings.Add});

        await Assert.That(warnings.Count).IsEqualTo(1);
        await Assert.That(warnings[0].Kind).IsEqualTo(WarningKind.UnsupportedElement);
    }

    [Test]
    public async Task WordDocument_ParseOnceExportMany()
    {
        var docxPath = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "bold_text", "input.docx");
        var document = new WordDocument(docxPath);

        // Same source produces consistent output across calls — the point of parse-once.
        var html1 = document.ExportToHtml();
        var html2 = document.ExportToHtml();
        var markdown = document.ExportToMarkdown();

        await Assert.That(html1).IsEqualTo(html2);
        await Assert.That(html1).Contains("<strong>");
        await Assert.That(markdown).Contains("**");
    }

    [Test]
    public async Task PdfPageRange_LimitsOutputToRequestedPages()
    {
        var inputPath = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "agendas-minutes", "02", "input.docx");
        if (!File.Exists(inputPath))
        {
            return;
        }

        var firstPageOnly = PdfDocumentConverter.ConvertToPdf(inputPath, new()
        {
            FontDirectory = fontsDirectory,
            Pages = PageRange.Single(1)
        });
        var fullDocument = PdfDocumentConverter.ConvertToPdf(inputPath, new()
        {
            FontDirectory = fontsDirectory
        });

        using var trimmed = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(firstPageOnly), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        using var full = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(fullDocument), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);

        await Assert.That(trimmed.PageCount).IsEqualTo(1);
        await Assert.That(full.PageCount).IsGreaterThanOrEqualTo(2);
    }

    // A family that is neither bundled in src/Fonts nor plausibly installed on any host, so
    // PdfFontResolver.CanResolve must miss on both the indexed faces and the platform resolver.
    const string unresolvableFamily = "Morph Test Nonexistent Family";

    static PdfRenderContext FallbackContext(Func<string, string?> fontFallback) =>
        new(
            new()
            {
                WidthPoints = 612,
                HeightPoints = 792,
                MarginTop = 72,
                MarginBottom = 72,
                MarginLeft = 72,
                MarginRight = 72
            },
            compatibility: null,
            fontWidthScale: 1.0,
            fontFallback: fontFallback,
            fontDirectory: fontsDirectory);

    /// <summary>
    /// PdfSharp resolves faces through a process-global <c>IFontResolver</c> that cannot see
    /// per-conversion state, so <see cref="PdfExportOptions.FontFallback"/> is applied in
    /// <c>PdfRenderContext</c> before the family reaches <c>XFont</c>. Guards that wiring.
    /// </summary>
    [Test]
    public async Task PdfFontFallback_SubstitutesUnresolvableFamily()
    {
        var requested = new List<string>();
        var context = FallbackContext(name =>
        {
            requested.Add(name);
            return "Arial";
        });

        var font = context.GetFont(unresolvableFamily, bold: false, italic: false, sizePoints: 12);

        await Assert.That(requested).Contains(unresolvableFamily);
        await Assert.That(font.FontFamily.Name).IsEqualTo("Arial");
    }

    /// <summary>
    /// The delegate is a fallback, not an interceptor: a family the resolver can already serve must
    /// never reach it. Without this the delegate would fire for every common family whenever no
    /// FontDirectory narrowed the index.
    /// </summary>
    [Test]
    public async Task PdfFontFallback_NotInvokedForResolvableFamily()
    {
        var requested = new List<string>();
        var context = FallbackContext(name =>
        {
            requested.Add(name);
            return "Aptos";
        });

        var font = context.GetFont("Arial", bold: false, italic: false, sizePoints: 12);

        await Assert.That(requested).IsEmpty();
        await Assert.That(font.FontFamily.Name).IsEqualTo("Arial");
    }

    /// <summary>A null return falls through to PdfFontResolver's own platform / default chain.</summary>
    [Test]
    public async Task PdfFontFallback_NullReturn_LeavesFamilyUnchanged()
    {
        var context = FallbackContext(_ => null);

        var font = context.GetFont(unresolvableFamily, bold: false, italic: false, sizePoints: 12);

        await Assert.That(font.FontFamily.Name).IsEqualTo(unresolvableFamily);
    }

    // PdfExportOptions.FontWidthScale reaches PDF wrapping through the shared canonical measurer
    // (LayoutFonts builds exactly this pairing in PdfRenderer), so the option's effect is asserted
    // where it is applied.
    static CanonicalParagraphMeasurer EngineAtScale(double scale) =>
        new(LayoutTestFonts.Resolve, scale);

    // Plain text, no character spacing — so every measured width term scales linearly and the
    // scaled/unscaled split inside the measurer can't hide behind a tracking constant.
    static ParagraphElement ScaleProbe() =>
        Para(TextRun("The quick brown fox jumps over the lazy dog"));

    /// <summary>
    /// <see cref="PdfExportOptions.FontWidthScale"/> widens the measured glyph advances that drive
    /// PDF line wrapping. At the 1.0 default it is an exact no-op (guarded by the PDF snapshot
    /// baselines); this exercises the non-unit path the baselines cannot, since 1.0 changes nothing.
    /// </summary>
    [Test]
    public async Task PdfFontWidthScale_ScalesNaturalWidthProportionally()
    {
        var paragraph = ScaleProbe();

        // A width large enough that the probe stays on one line, so natural width is the full
        // sum of scaled word + space advances.
        var natural10 = EngineAtScale(1.0).MeasureParagraphNaturalWidth(paragraph, 100_000f);
        var natural15 = EngineAtScale(1.5).MeasureParagraphNaturalWidth(paragraph, 100_000f);

        // No character spacing, so the ratio is the scale factor itself (within float rounding).
        await Assert.That(natural15).IsGreaterThan(natural10 * 1.45f);
        await Assert.That(natural15).IsLessThan(natural10 * 1.55f);
    }

    /// <summary>The corollary users actually care about: a larger scale wraps sooner.</summary>
    [Test]
    public async Task PdfFontWidthScale_WrapsEarlier()
    {
        var paragraph = ScaleProbe();

        // Derive a width that fits the probe on one line at 1.0 — then a 1.5 scale (~50% wider)
        // must overflow it. No magic constant: the threshold comes from the actual metrics.
        var oneLineWidth = EngineAtScale(1.0).MeasureParagraphNaturalWidth(paragraph, 100_000f);
        var maxWidth = oneLineWidth * 1.1f;

        var lines10 = EngineAtScale(1.0).LayoutParagraphForMeasurement(paragraph, maxWidth).Count;
        var lines15 = EngineAtScale(1.5).LayoutParagraphForMeasurement(paragraph, maxWidth).Count;

        await Assert.That(lines10).IsEqualTo(1);
        await Assert.That(lines15).IsGreaterThan(1);
    }
}
