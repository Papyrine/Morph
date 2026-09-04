/// <summary>
/// Covers the <c>w:mirrorIndents</c> transform in <see cref="Fragmenter"/>. Word-measured
/// (<c>_probe_mirror</c>, <c>_probe_mirror2</c> — left/right/hanging/firstLine swept on both page
/// parities; complex_spacing's Combination 7 confirms on a real document): an even page keeps the
/// declared indents, an odd page mirrors the box — left' = right + hanging,
/// right' = left + firstLine − hanging, with the first-line delta itself unchanged.
/// </summary>
public class MirrorIndentsLayoutTests
{
    static readonly Fragmenter fragmenter = new(LayoutTestFonts.Measurer);

    static readonly string wrapping = string.Join(' ', Enumerable.Repeat("lorem", 40));

    // 300pt wide, 20pt margins → 260pt measure, matching CanonicalFragmenterTests' geometry.
    static readonly PageSettings page = new()
    {
        WidthPoints = 300,
        HeightPoints = 400,
        MarginTop = 20,
        MarginBottom = 20,
        MarginLeft = 20,
        MarginRight = 20
    };

    static ParagraphElement P(ParagraphProperties properties) =>
        new()
        {
            Runs =
            [
                new()
                {
                    Text = wrapping,
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                }
            ],
            Properties = properties
        };

    static (PlacedLine First, PlacedLine Continuation) Lines(LaidOutDocument document, int pageIndex)
    {
        var lines = document.Pages[pageIndex].Items.OfType<PlacedLine>()
            .Where(_ => _.Paragraph.Properties.MirrorIndents)
            .OrderBy(_ => _.LineIndex)
            .ToList();
        return (lines[0], lines[1]);
    }

    [Test]
    public async Task Hanging_mirror_on_an_odd_page_builds_lefts_from_the_right_indent()
    {
        // Declared left 60, hanging 30 (right 0). Mirrored: left' = 0 + 30 = 30, so the first line
        // outdents to the margin (20) and continuations sit at margin + 30 = 50 — Word's A/C blocks
        // in _probe_mirror2.
        var document = fragmenter.Layout([P(new()
        {
            MirrorIndents = true,
            LeftIndentPoints = 60,
            HangingIndentPoints = 30
        })], page);

        var (first, continuation) = Lines(document, 0);
        await Assert.That(first.X).IsEqualTo(20f);
        await Assert.That(continuation.X).IsEqualTo(50f);
    }

    [Test]
    public async Task FirstLine_mirror_on_an_odd_page_keeps_the_first_line_delta()
    {
        // Declared left 60, firstLine 30 (right 0). Mirrored: left' = 0, first = 0 + 30 — Word's D
        // block in _probe_mirror2.
        var document = fragmenter.Layout([P(new()
        {
            MirrorIndents = true,
            LeftIndentPoints = 60,
            FirstLineIndentPoints = 30
        })], page);

        var (first, continuation) = Lines(document, 0);
        await Assert.That(first.X).IsEqualTo(50f);
        await Assert.That(continuation.X).IsEqualTo(20f);
    }

    [Test]
    public async Task Mirror_on_an_even_page_keeps_the_declared_indents()
    {
        var filler = new ParagraphElement
        {
            Runs = [new() {Text = "filler", Properties = new() {FontFamily = "Aptos", FontSizePoints = 11}}],
            Properties = new()
        };
        var mirrored = P(new()
        {
            MirrorIndents = true,
            LeftIndentPoints = 60,
            HangingIndentPoints = 30,
            PageBreakBefore = true
        });

        var document = fragmenter.Layout([filler, mirrored], page);

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        var (first, continuation) = Lines(document, 1);
        await Assert.That(first.X).IsEqualTo(50f);
        await Assert.That(continuation.X).IsEqualTo(80f);
    }
}
