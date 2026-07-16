extern alias Skia;
using SkiaSharp;
using SkiaTextRenderer = Skia::TextRenderer;

/// <summary>
/// Guards the measure/render contract for paragraphs inside table cells:
/// <see cref="TableHeightCalculator.MeasureCellHeight"/> decides the row height, and
/// <c>RenderParagraphInBounds</c> draws into it. If the render advances further than the
/// measurement predicted, the content overflows its row — and since the row only advances
/// CurrentY by the measured height, whatever follows the table draws on top of the overflow.
///
/// The scenario baselines do not catch this: they record whatever Morph renders, and the
/// ErrorMetric is nearly blind to it (business-plans/15 page 9 scored a normal 0.0996 while
/// rendering headings straight through the paragraphs above them).
/// </summary>
public class TableCellSpacingCollapseTests
{
    const float spacingBefore = 10;
    const float spacingAfter = 10;

    static (SkiaRenderContext Context, SkiaTextRenderer Renderer, SKBitmap Bitmap, SKCanvas Canvas) CreateRenderer()
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
        var context = new SkiaRenderContext(pageSettings, 96, fontDirectory: ProjectFonts.Directory);
        var renderer = new SkiaTextRenderer(context);
        var bitmap = new SKBitmap(context.PageWidthPixels, context.PageHeightPixels);
        return (context, renderer, bitmap, new(bitmap));
    }

    static ParagraphElement Paragraph(string text) =>
        new()
        {
            Runs = [new() {Text = text, Properties = new()}],
            Properties = new()
            {
                SpacingBeforePoints = spacingBefore,
                SpacingAfterPoints = spacingAfter
            }
        };

    static TableCell Cell(params string[] texts) =>
        new()
        {
            Properties = new(),
            Content = [..texts.Select(Paragraph)]
        };

    // No padding/margin so the measured height is purely the paragraph contributions and can be
    // compared directly against the render's CurrentY advance.
    static TableProperties TableProps() =>
        new()
        {
            DefaultCellPadding = new(top: 0, right: 0, bottom: 0, left: 0)
        };

    /// <summary>
    /// Renders the cell's paragraphs the way <c>RenderTableCell</c> does — resetting the
    /// cross-paragraph spacing state on entry, since Word never collapses across a cell boundary —
    /// and returns how far CurrentY advanced.
    /// </summary>
    static float RenderAdvance(SkiaRenderContext context, SkiaTextRenderer renderer, SKCanvas canvas, TableCell cell, float width)
    {
        context.LastParagraphSpacingAfterPoints = 0;
        context.LastParagraphHadContextualSpacing = false;
        context.LastParagraphStyleId = null;

        var startY = context.CurrentY;
        foreach (var para in cell.Content.OfType<ParagraphElement>())
        {
            renderer.RenderParagraphInBounds(canvas, para, 0, width);
        }

        return context.CurrentY - startY;
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(5)]
    public async Task RenderedCellContent_FitsTheMeasuredRowHeight(int paragraphCount)
    {
        var (context, renderer, bitmap, canvas) = CreateRenderer();
        using var _b = bitmap;
        using var _ca = canvas;
        using var _co = context;

        const float width = 300;
        var cell = Cell([..Enumerable.Range(0, paragraphCount).Select(_ => $"Paragraph {_}")]);

        var measured = TableHeightCalculator.MeasureCellHeight(cell, width, TableProps(), renderer);
        var rendered = RenderAdvance(context, renderer, canvas, cell, width);

        // The row advances CurrentY by the measured height. Rendering further than that overflows
        // the row and the next element draws over the spill.
        await Assert.That(rendered).IsLessThanOrEqualTo(measured + 0.01f);
    }

    [Test]
    public async Task MultiParagraphCell_CollapsesSpacingBetweenParagraphs_LikeTheMeasurer()
    {
        var (context, renderer, bitmap, canvas) = CreateRenderer();
        using var _b = bitmap;
        using var _ca = canvas;
        using var _co = context;

        const float width = 300;
        var one = Cell("Paragraph 0");
        var three = Cell("Paragraph 0", "Paragraph 1", "Paragraph 2");

        var renderedOne = RenderAdvance(context, renderer, canvas, one, width);
        var renderedThree = RenderAdvance(context, renderer, canvas, three, width);

        // Word charges max(after, before) between consecutive paragraphs, not the sum. With
        // before == after, each extra paragraph adds its lines plus exactly one gap. Summing both
        // would add spacingBefore extra per gap — the defect this guards.
        var perExtraParagraph = (renderedThree - renderedOne) / 2;
        var oneLineAndOneGap = (renderedOne - spacingBefore - spacingAfter) + spacingAfter;

        await Assert.That(perExtraParagraph).IsEqualTo(oneLineAndOneGap).Within(0.01f);
    }
}
