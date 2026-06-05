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
        var docxPath = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "bold_text", "input.docx");
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
        var inputPath = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "agendas-minutes", "02", "input.docx");
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
}
