/// <summary>
/// Tests for what the HTML export hangs off an authored page boundary. It reflows and never
/// paginates, but a page break the author placed is content: the page before it stays as tall as
/// its floating art reaches, a page that opens with art in its top margin gets that margin back,
/// and a picture the text wraps round stops wrapping at the end of its page.
/// </summary>
public class HtmlPageExportTests
{
    // Letter, with an inch of margin all round: the content box is 468 x 648.
    static readonly PageSettings page = new()
    {
        WidthPoints = 612,
        HeightPoints = 792,
        MarginTop = 72,
        MarginRight = 72,
        MarginBottom = 72,
        MarginLeft = 72
    };

    static string Export(params DocumentElement[] elements) =>
        HtmlExporter.Export(
            new()
            {
                PageSettings = page,
                Elements = elements
            },
            new() {EmitDocument = false});

    static FloatingShapeElement Band(double top, double height, VerticalAnchor anchor = VerticalAnchor.Paragraph) =>
        new()
        {
            WidthPoints = 612,
            HeightPoints = height,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalPositionPoints = top,
            VerticalAnchor = anchor,
            BehindText = true,
            FillColorHex = "3281A2"
        };

    /// <summary>A document with no floating art and no break is written exactly as before.</summary>
    [Test]
    public async Task PlainDocument_IsWrittenWithoutAPageBlock()
    {
        var html = Export(Para(TextRun("one")), Para(TextRun("two")));

        await Assert.That(html).IsEqualTo("<p>one</p>\n<p>two</p>\n");
    }

    /// <summary>
    /// agendas-minutes/08 hangs a footer band 706pt under its first paragraph. The page's text is
    /// shorter than that, so the next page's content started under the band. The page is held open
    /// as far as its art reaches: 706 + 36.
    /// </summary>
    [Test]
    public async Task Page_IsAtLeastAsTallAsItsArtReaches()
    {
        var html = Export(
            Band(706, 36),
            Para(TextRun("page one")),
            new PageBreakElement(),
            Para(TextRun("page two")));

        await Assert.That(html).Contains("<div style=\"min-height: 742pt\">");
        // The second page holds no art and is written bare, after the first page's block closes.
        await Assert.That(html).EndsWith("</div>\n<p>page two</p>\n");
    }

    /// <summary>
    /// A later page that opens with art reaching above its first paragraph — into what was the top
    /// margin on paper — gets that room back, where the first page already has the body's top
    /// padding for it. The reach is capped at the margin: nothing was drawn above the paper.
    /// </summary>
    [Test]
    public async Task LaterPage_OpeningWithArtAboveItsContent_ReservesTheMarginItReachesInto()
    {
        var html = Export(
            Band(-50, 36),
            Para(TextRun("page one")),
            new SectionBreakElement {BreakType = SectionBreakType.NextPage},
            Band(-50, 36),
            Para(TextRun("page two")),
            new PageBreakElement(),
            Band(-500, 36),
            Para(TextRun("page three")));

        // The first page is written bare: its band draws in the body's own top padding.
        await Assert.That(html).StartsWith("<div style=\"position: relative\"><svg");
        await Assert.That(html).Contains("<div style=\"padding-top: 50pt\">\n  <div style=\"position: relative\"><svg style=\"position: absolute; left: -72pt; top: -50pt;");
        await Assert.That(html).Contains("<div style=\"padding-top: 72pt\">");
        await Assert.That(CountOf(html, "padding-top")).IsEqualTo(2);
    }

    /// <summary>Art anchored below the page's first content reaches up into its own page, which needs no room.</summary>
    [Test]
    public async Task ArtAnchoredBelowThePagesFirstContent_ReservesNothingAbove()
    {
        var html = Export(
            Para(TextRun("page one")),
            new PageBreakElement(),
            Para(TextRun("page two")),
            Band(-50, 36),
            Para(TextRun("more")));

        await Assert.That(html).DoesNotContain("padding-top");
    }

    /// <summary>
    /// The last page has the body's bottom padding for art that reaches into the bottom margin, so
    /// it is held open no further than the content box: 792 - 72 - 72.
    /// </summary>
    [Test]
    public async Task LastPage_IsHeldOpenNoFurtherThanTheContentBox()
    {
        var html = Export(Band(-72, 792, VerticalAnchor.Margin), Para(TextRun("only page")));

        await Assert.That(html).Contains("<div style=\"min-height: 648pt\">");
    }

    /// <summary>
    /// A continuous section break and a column break end no page. A paragraph that breaks before
    /// itself starts one, and takes the art declared directly ahead of it — its own, since a
    /// drawing is written just before the paragraph it is anchored to — onto the new page with it.
    /// </summary>
    [Test]
    public async Task OnlyABreakThatStartsAPage_EndsOne()
    {
        var continuous = Export(
            Band(0, 300),
            Para(TextRun("one")),
            new SectionBreakElement {BreakType = SectionBreakType.Continuous},
            new ColumnBreakElement(),
            Band(0, 100),
            Para(TextRun("two")));
        var breakBefore = Export(
            Band(0, 300),
            Para(TextRun("one")),
            Band(0, 100),
            new ParagraphElement
            {
                Runs = [TextRun("two")],
                Properties = new() {PageBreakBefore = true, SpacingAfterPoints = 8}
            });

        await Assert.That(CountOf(continuous, "min-height")).IsEqualTo(1);
        await Assert.That(continuous).Contains("<div style=\"min-height: 300pt\">");
        await Assert.That(breakBefore).Contains("<div style=\"min-height: 300pt\">");
        await Assert.That(breakBefore).Contains("</div>\n<div style=\"min-height: 100pt\">");
    }

    /// <summary>
    /// A picture the text wraps round is a CSS float, which would go on being wrapped by whatever
    /// follows. Word starts the next page clear of it: image_wrap_square's paragraph after the
    /// break ran on beside the first page's picture.
    /// </summary>
    [Test]
    public async Task WrappedPicture_StopsBeingWrappedAtTheEndOfItsPage()
    {
        var html = Export(
            new FloatingImageElement
            {
                ImageData = [1, 2, 3],
                ContentType = "image/png",
                WidthPoints = 100,
                HeightPoints = 100,
                WrapType = WrapType.Square
            },
            Para(TextRun("beside the picture")),
            new PageBreakElement(),
            Para(TextRun("next page")));

        await Assert.That(html).Contains("<div style=\"clear: both\">\n  <p>next page</p>\n</div>\n");
    }

    /// <summary>
    /// A page-relative drawing is placed from where its page's content starts, which is one top
    /// margin down the page. It was placed from the content as though that were the paper's edge,
    /// a margin too low: business/02's shaded panel began under the heading it backs.
    /// </summary>
    [Test]
    public async Task PageRelativeArt_IsPlacedATopMarginAboveItsOffset()
    {
        var html = Export(Band(100, 36, VerticalAnchor.Page), Para(TextRun("text")));

        await Assert.That(html).Contains("left: -72pt; top: 28pt;");
    }

    static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
