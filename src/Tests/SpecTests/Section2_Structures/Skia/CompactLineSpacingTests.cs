extern alias Skia;
using SkiaSharp;
using SkiaRenderContext = Skia::RenderContext;
using SkiaTextRenderer = Skia::TextRenderer;

/// <summary>
/// Tests that compact auto line spacing (multiplier &lt; 1.0) does not cause
/// inter-paragraph overlap. The last line of a paragraph with compact spacing
/// should advance CurrentY by at least the natural font height, not the
/// compressed height. Compression only applies between lines within a paragraph.
/// </summary>
public class CompactLineSpacingTests
{
    static (SkiaRenderContext context, SkiaTextRenderer textRenderer, SKBitmap bitmap, SKCanvas canvas) CreateRenderer()
    {
        var pageSettings = new PageSettings
        {
            WidthPoints = 612,
            HeightPoints = 792,
            MarginTop = 72,
            MarginBottom = 72,
            MarginLeft = 72,
            MarginRight = 72
        };
        var context = new SkiaRenderContext(pageSettings, 96);
        var textRenderer = new SkiaTextRenderer(context);
        var bitmap = new SKBitmap(context.PageWidthPixels, context.PageHeightPixels);
        var canvas = new SKCanvas(bitmap);
        return (context, textRenderer, bitmap, canvas);
    }

    [Test]
    public async Task CompactAutoSpacing_SingleLine_AdvancesAtLeastFontSize()
    {
        var (context, textRenderer, bitmap, canvas) = CreateRenderer();
        using var _b = bitmap;
        using var _ca = canvas;
        using var _co = context;

        var startY = context.CurrentY;
        const float fontSize = 48;

        var paragraph = new ParagraphElement
        {
            Runs = [new() { Text = "Hello", Properties = new() { FontSizePoints = fontSize } }],
            Properties = new()
            {
                LineSpacingRule = LineSpacingRule.Auto,
                LineSpacingMultiplier = 0.7
            }
        };

        textRenderer.RenderParagraph(canvas, paragraph);

        var advancement = context.CurrentY - startY;

        // With the fix, the last line uses natural height (ascent + descent >= fontSize).
        // Without the fix, advancement would be ~fontSize * 1.15 * 0.7 ≈ 0.8 * fontSize,
        // which is less than fontSize.
        await Assert.That(advancement).IsGreaterThanOrEqualTo(fontSize);
    }

    [Test]
    public async Task CompactAutoSpacing_TwoParagraphs_NoOverlap()
    {
        var (context, textRenderer, bitmap, canvas) = CreateRenderer();
        using var _b = bitmap;
        using var _ca = canvas;
        using var _co = context;

        const float fontSize = 48;

        var first = new ParagraphElement
        {
            Runs = [new() { Text = "First", Properties = new() { FontSizePoints = fontSize } }],
            Properties = new()
            {
                LineSpacingRule = LineSpacingRule.Auto,
                LineSpacingMultiplier = 0.7,
                SpacingAfterPoints = 0
            }
        };

        var second = new ParagraphElement
        {
            Runs = [new() { Text = "Second", Properties = new() { FontSizePoints = 12 } }],
            Properties = new()
            {
                SpacingBeforePoints = 0
            }
        };

        textRenderer.RenderParagraph(canvas, first);
        var yAfterFirst = context.CurrentY;
        textRenderer.RenderParagraph(canvas, second);

        // The second paragraph should start at or below the first paragraph's
        // natural text height (not the compressed 0.7x height).
        // yAfterFirst should be >= startY + fontSize (natural height >= font size).
        var startY = (float)context.PageSettings.MarginTop;
        var firstTextBottom = startY + fontSize; // conservative: ascent alone > fontSize * 0.8
        await Assert.That(yAfterFirst).IsGreaterThanOrEqualTo(firstTextBottom);
    }

    [Test]
    public async Task NormalAutoSpacing_SingleLine_NotAffectedByFix()
    {
        var (context, textRenderer, bitmap, canvas) = CreateRenderer();
        using var _b = bitmap;
        using var _ca = canvas;
        using var _co = context;

        var startY = context.CurrentY;
        const float fontSize = 24;

        var paragraph = new ParagraphElement
        {
            Runs = [new() { Text = "Normal spacing", Properties = new() { FontSizePoints = fontSize } }],
            Properties = new()
            {
                LineSpacingRule = LineSpacingRule.Auto,
                LineSpacingMultiplier = 1.0
            }
        };

        textRenderer.RenderParagraph(canvas, paragraph);

        var advancement = context.CurrentY - startY;

        // Normal spacing should advance by roughly fontSize * 1.0-1.2 (natural height + boost).
        // The fix (which only applies to multiplier < 1.0) should not change this.
        await Assert.That(advancement).IsGreaterThanOrEqualTo(fontSize);
        await Assert.That(advancement).IsLessThan(fontSize * 2);
    }

    [Test]
    public async Task ExactSpacing_NotAffectedByFix()
    {
        var (context, textRenderer, bitmap, canvas) = CreateRenderer();
        using var _b = bitmap;
        using var _ca = canvas;
        using var _co = context;

        var startY = context.CurrentY;
        const float exactHeight = 20;

        var paragraph = new ParagraphElement
        {
            Runs = [new() { Text = "Exact spacing", Properties = new() { FontSizePoints = 24 } }],
            Properties = new()
            {
                LineSpacingRule = LineSpacingRule.Exactly,
                LineSpacingPoints = exactHeight
            }
        };

        textRenderer.RenderParagraph(canvas, paragraph);

        var advancement = context.CurrentY - startY;

        // Exact spacing should advance by exactly the specified height,
        // regardless of font size. The fix should not affect this.
        await Assert.That(advancement).IsEqualTo(exactHeight).Within(0.01f);
    }

    [Test]
    public async Task CompactAutoSpacing_MatchesNaturalHeightForSingleLine()
    {
        // Render the same paragraph with 0.7x and 1.0x spacing.
        // For a single-line paragraph, the fix should make the 0.7x advancement
        // equal to the natural height (same as what 1.0x would give without the boost).

        var (ctx1, tr1, bmp1, cvs1) = CreateRenderer();
        using var _b1 = bmp1;
        using var _c1 = cvs1;
        using var _x1 = ctx1;

        var (ctx2, tr2, bmp2, cvs2) = CreateRenderer();
        using var _b2 = bmp2;
        using var _c2 = cvs2;
        using var _x2 = ctx2;

        const float fontSize = 36;

        var compactParagraph = new ParagraphElement
        {
            Runs = [new() { Text = "Test", Properties = new() { FontSizePoints = fontSize } }],
            Properties = new()
            {
                LineSpacingRule = LineSpacingRule.Auto,
                LineSpacingMultiplier = 0.7
            }
        };

        var normalParagraph = new ParagraphElement
        {
            Runs = [new() { Text = "Test", Properties = new() { FontSizePoints = fontSize } }],
            Properties = new()
            {
                LineSpacingRule = LineSpacingRule.Auto,
                LineSpacingMultiplier = 1.0
            }
        };

        tr1.RenderParagraph(cvs1, compactParagraph);
        var compactAdvancement = ctx1.CurrentY - ctx1.ContentTop;

        tr2.RenderParagraph(cvs2, normalParagraph);
        var normalAdvancement = ctx2.CurrentY - ctx2.ContentTop;

        // The compact advancement should be <= normal advancement (since normal gets a boost).
        // But it should be close — not 70% of normal.
        // Without the fix, compact would be ~70% of (normal / boost).
        // With the fix, compact should be >= 85% of normal (natural height vs boosted height).
        await Assert.That(compactAdvancement).IsGreaterThan(normalAdvancement * 0.85f);
    }
}
