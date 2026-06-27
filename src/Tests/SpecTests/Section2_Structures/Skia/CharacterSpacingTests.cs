extern alias Skia;
using SkiaSharp;
using SkiaTextRenderer = Skia::TextRenderer;

/// <summary>
/// Tests that character spacing (w:spacing in rPr) widens text during layout,
/// causing earlier line wrapping compared to the same text without spacing.
/// </summary>
public class CharacterSpacingTests
{
    static (SkiaRenderContext context, SkiaTextRenderer textRenderer, SKBitmap bitmap, SKCanvas canvas) CreateRenderer()
    {
        var pageSettings = new PageSettings
        {
            WidthPoints = 300,
            HeightPoints = 400,
            MarginTop = 20,
            MarginBottom = 20,
            MarginLeft = 20,
            MarginRight = 20
        };
        var context = new SkiaRenderContext(pageSettings, 96, fontDirectory: ProjectFonts.Directory);
        var textRenderer = new SkiaTextRenderer(context);
        var bitmap = new SKBitmap(context.PageWidthPixels, context.PageHeightPixels);
        var canvas = new SKCanvas(bitmap);
        return (context, textRenderer, bitmap, canvas);
    }

    [Test]
    public async Task CharacterSpacing_CausesEarlierWrapping()
    {
        // Use a narrow cell so that character spacing pushes text to an extra line.
        var (ctx1, tr1, bmp1, cvs1) = CreateRenderer();
        using var _b1 = bmp1;
        using var _c1 = cvs1;
        using var _x1 = ctx1;

        var (ctx2, tr2, bmp2, cvs2) = CreateRenderer();
        using var _b2 = bmp2;
        using var _c2 = cvs2;
        using var _x2 = ctx2;

        // Text carefully sized: fits on one line at 120pt without spacing,
        // but wraps with 3pt character spacing (~20 chars * 3pt = 60pt extra)
        var text = "Sample text for test";
        const float cellWidth = 120;

        var noSpacing = new ParagraphElement
        {
            Runs = [new() { Text = text, Properties = new() { FontSizePoints = 11 } }],
            Properties = new()
        };

        var withSpacing = new ParagraphElement
        {
            Runs = [new() { Text = text, Properties = new() { FontSizePoints = 11, CharacterSpacingPoints = 3.0 } }],
            Properties = new()
        };

        tr1.RenderParagraphInBounds(cvs1, noSpacing, 20, cellWidth);
        var heightNoSpacing = ctx1.CurrentY - ctx1.ContentTop;

        tr2.RenderParagraphInBounds(cvs2, withSpacing, 20, cellWidth);
        var heightWithSpacing = ctx2.CurrentY - ctx2.ContentTop;

        // Character spacing makes text wider → wraps to more lines → taller
        await Assert.That(heightWithSpacing).IsGreaterThan(heightNoSpacing);
    }

    [Test]
    public async Task CharacterSpacing_Zero_SameAsDefault()
    {
        var (ctx1, tr1, bmp1, cvs1) = CreateRenderer();
        using var _b1 = bmp1;
        using var _c1 = cvs1;
        using var _x1 = ctx1;

        var (ctx2, tr2, bmp2, cvs2) = CreateRenderer();
        using var _b2 = bmp2;
        using var _c2 = cvs2;
        using var _x2 = ctx2;

        var text = "Hello World";

        var defaultProps = new ParagraphElement
        {
            Runs = [new() { Text = text, Properties = new() { FontSizePoints = 14 } }],
            Properties = new()
        };

        var zeroSpacing = new ParagraphElement
        {
            Runs = [new() { Text = text, Properties = new() { FontSizePoints = 14, CharacterSpacingPoints = 0 } }],
            Properties = new()
        };

        tr1.RenderParagraph(cvs1, defaultProps);
        var height1 = ctx1.CurrentY - ctx1.ContentTop;

        tr2.RenderParagraph(cvs2, zeroSpacing);
        var height2 = ctx2.CurrentY - ctx2.ContentTop;

        await Assert.That(height1).IsEqualTo(height2).Within(0.01f);
    }

    [Test]
    public async Task CharacterSpacing_AffectsTableCellLayout()
    {
        // In table cell rendering (RenderParagraphInBounds), character spacing
        // should also widen text and cause earlier wrapping.
        var (ctx1, tr1, bmp1, cvs1) = CreateRenderer();
        using var _b1 = bmp1;
        using var _c1 = cvs1;
        using var _x1 = ctx1;

        var (ctx2, tr2, bmp2, cvs2) = CreateRenderer();
        using var _b2 = bmp2;
        using var _c2 = cvs2;
        using var _x2 = ctx2;

        var text = "This text should wrap differently with character spacing applied";
        const float cellWidth = 150;

        var noSpacing = new ParagraphElement
        {
            Runs = [new() { Text = text, Properties = new() { FontSizePoints = 11 } }],
            Properties = new()
        };

        var withSpacing = new ParagraphElement
        {
            Runs = [new() { Text = text, Properties = new() { FontSizePoints = 11, CharacterSpacingPoints = 1.5 } }],
            Properties = new()
        };

        tr1.RenderParagraphInBounds(cvs1, noSpacing, 20, cellWidth);
        var heightNoSpacing = ctx1.CurrentY - ctx1.ContentTop;

        tr2.RenderParagraphInBounds(cvs2, withSpacing, 20, cellWidth);
        var heightWithSpacing = ctx2.CurrentY - ctx2.ContentTop;

        await Assert.That(heightWithSpacing).IsGreaterThan(heightNoSpacing);
    }

    [Test]
    public async Task CharacterSpacing_ParsedFromDocx()
    {
        // The wedding/01 document has Subtitle style with w:spacing w:val="20" (1pt).
        // Verify the parser extracts it correctly.
        var parser = new DocumentParser();
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "wedding", "01", "input.docx"));
        var doc = parser.Parse(stream);

        // Find a paragraph with the Subtitle style (has character spacing from style rPr)
        var subtitleRun = doc.Elements
            .OfType<TableElement>()
            .SelectMany(_ => _.Rows)
            .SelectMany(_ => _.Cells)
            .SelectMany(_ => _.Content)
            .OfType<ParagraphElement>()
            .Where(_ => _.Properties.StyleId == "Subtitle")
            .SelectMany(_ => _.Runs)
            .First();

        // Subtitle style has w:spacing w:val="20" → 20 twips → 1pt
        await Assert.That(subtitleRun.Properties.CharacterSpacingPoints).IsEqualTo(1.0);
    }
}
