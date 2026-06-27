extern alias Skia;
using SkiaSharp;
using SkiaTextRenderer = Skia::TextRenderer;

/// <summary>
/// Tests that document-level default paragraph indentation (from pPrDefault)
/// is applied to paragraphs through the style chain.
/// </summary>
public class DocDefaultIndentTests
{
    [Test]
    public async Task ParagraphDefaults_IndentAppliedToStylesWithoutExplicitIndent()
    {
        // The wedding/01 document has pPrDefault with w:ind w:left="720" w:right="720" (36pt each).
        // Styles that don't explicitly set indentation should inherit these defaults.
        var parser = new DocumentParser();
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "wedding", "01", "input.docx"));
        var doc = parser.Parse(stream);

        // Find paragraphs by style in the table cells
        var tableParagraphs = doc.Elements
            .OfType<TableElement>()
            .SelectMany(_ => _.Rows)
            .SelectMany(_ => _.Cells)
            .SelectMany(_ => _.Content)
            .OfType<ParagraphElement>()
            .Where(_ => _.Properties.StyleId != null &&
                        _.Runs.Count > 0)
            .ToList();

        // EventDate style (basedOn SecondaryText → Subtitle → Normal).
        // The Subtitle style's indent is skipped (orphaned numPr), so EventDate
        // inherits from Normal which gets 36pt from doc defaults.
        var eventDate = tableParagraphs.First(_ => _.Properties.StyleId == "EventDate");
        await Assert.That(eventDate.Properties.RightIndentPoints).IsEqualTo(36.0);
    }

    [Test]
    public async Task ParagraphDefaults_StyleOverridesDocDefault()
    {
        // When a style explicitly sets w:ind, it should override the document default.
        // The SecondName style has w:ind w:left="1728" which overrides the doc default 36pt.
        var parser = new DocumentParser();
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "wedding", "01", "input.docx"));
        var doc = parser.Parse(stream);

        var tableParagraphs = doc.Elements
            .OfType<TableElement>()
            .SelectMany(_ => _.Rows)
            .SelectMany(_ => _.Cells)
            .SelectMany(_ => _.Content)
            .OfType<ParagraphElement>()
            .Where(_ => _.Properties.StyleId != null &&
                        _.Runs.Count > 0)
            .ToList();

        // SecondName has explicit w:ind w:left="1728" (86.4pt), overriding doc default 36pt
        var secondName = tableParagraphs.First(_ => _.Properties.StyleId == "SecondName");
        await Assert.That(secondName.Properties.LeftIndentPoints).IsEqualTo(86.4);
    }

    [Test]
    public async Task ParagraphDefaults_NoDocDefaults_IndentIsZero()
    {
        // Documents without pPrDefault indentation should have 0 indent by default.
        // Use a simple document (e.g., bold_text) that likely has no pPrDefault indent.
        var parser = new DocumentParser();
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "bold_text", "input.docx"));
        var doc = parser.Parse(stream);

        var firstPara = doc.Elements.OfType<ParagraphElement>().First(_ => _.Runs.Count > 0);

        // No doc defaults indent → should be 0
        await Assert.That(firstPara.Properties.LeftIndentPoints).IsEqualTo(0.0);
        await Assert.That(firstPara.Properties.RightIndentPoints).IsEqualTo(0.0);
    }

    [Test]
    public async Task RightIndent_NarrowsAvailableWidthInTableCells()
    {
        // Verify that right indent reduces the available width for text layout
        // in table cell rendering (RenderParagraphInBounds).
        var pageSettings = new PageSettings
        {
            WidthPoints = 300,
            HeightPoints = 400,
            MarginTop = 20,
            MarginBottom = 20,
            MarginLeft = 20,
            MarginRight = 20
        };

        using var context1 = new SkiaRenderContext(pageSettings, 96, fontDirectory: ProjectFonts.Directory);
        var tr1 = new SkiaTextRenderer(context1);
        using var bmp1 = new SKBitmap(context1.PageWidthPixels, context1.PageHeightPixels);
        using var cvs1 = new SKCanvas(bmp1);

        using var context2 = new SkiaRenderContext(pageSettings, 96, fontDirectory: ProjectFonts.Directory);
        var tr2 = new SkiaTextRenderer(context2);
        using var bmp2 = new SKBitmap(context2.PageWidthPixels, context2.PageHeightPixels);
        using var cvs2 = new SKCanvas(bmp2);

        var text = "Sample text for width";
        const float cellWidth = 130;

        var noIndent = new ParagraphElement
        {
            Runs = [new() { Text = text, Properties = new() { FontSizePoints = 11 } }],
            Properties = new() { RightIndentPoints = 0 }
        };

        var withRightIndent = new ParagraphElement
        {
            Runs = [new() { Text = text, Properties = new() { FontSizePoints = 11 } }],
            Properties = new() { RightIndentPoints = 50 }
        };

        tr1.RenderParagraphInBounds(cvs1, noIndent, 20, cellWidth);
        var heightNoIndent = context1.CurrentY - context1.ContentTop;

        tr2.RenderParagraphInBounds(cvs2, withRightIndent, 20, cellWidth);
        var heightWithIndent = context2.CurrentY - context2.ContentTop;

        // Right indent narrows available width → text wraps to more lines → taller paragraph
        await Assert.That(heightWithIndent).IsGreaterThan(heightNoIndent);
    }
}
