/// <summary>
/// Covers the space-compression wedge in <see cref="CanonicalParagraphMeasurer"/>: Word shrinks a
/// line's inter-word spaces — by at most one 120-dpi layout pixel (0.6pt) each — rather than wrap,
/// when that lets the overflowing word fit. Word-probed twice: resumes/16's own XPS (full lines
/// whose 10pt Calibri spaces advance 1.8pt against 2.4pt everywhere else), then the
/// <c>_probe_wedge</c> measure sweep (one sentence over twelve right-indents), which showed the
/// compression is per space and just-enough — mixes like 9 narrowed + 8 natural, 16 + 1 — and that
/// paragraph-level compat flags do not gate it.
/// </summary>
public class SpaceCompressionTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static CanonicalParagraphMeasurer Measurer()
    {
        // The wedge only fires on faces measuring with Word's own advances (the sidecar gate), so
        // the metrics carry an EMPTY WordAdvances: the gate arms, and every lookup still falls back
        // to the linear model, keeping this file's Courier arithmetic exact.
        var metrics = FontMetricsReader.Read(Path.Combine(fontsDirectory, "Courier_New_400.ttf"))!
            with {WordAdvances = new Dictionary<int, IReadOnlyDictionary<int, float>>()};
        return new((_, _, _) => metrics, fontWidthScale: 1.0);
    }

    static ParagraphElement Paragraph(string text) =>
        new()
        {
            Runs = [new() {Text = text, Properties = new() {FontFamily = "Courier New", FontSizePoints = 12}}],
            Properties = new()
        };

    // Courier New at 12pt advances 7.2pt (12 pen pixels) per character, spaces included, so the
    // arithmetic is exact: "wwww wwww wwww" is 14 chars = 168 pen pixels = 100.8pt natural, with
    // 2 spaces = 2 one-pixel quanta = 1.2pt of give.

    [Test]
    public async Task Word_overflowing_by_less_than_the_spaces_give_is_wedged()
    {
        // At a 100pt measure the last word overflows by 0.8pt = two quanta with two spaces on
        // the line, so Word keeps it on one line.
        var lines = Measurer().LayoutLines(Paragraph("wwww wwww wwww"), 100f);

        await Assert.That(lines.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_justified_paragraph_never_wedges()
    {
        // The same 0.8pt overhang that the first test wedges: under jc=both Word wraps the word
        // instead (letters/04's XPS — "and" overhangs the 780px column by 6.9px across fifteen
        // spaces and still starts the second line), since justification stretches every gap after
        // the break and a compressed line would show nothing for it.
        var paragraph = new ParagraphElement
        {
            Runs = [new() {Text = "wwww wwww wwww", Properties = new() {FontFamily = "Courier New", FontSizePoints = 12}}],
            Properties = new() {Alignment = TextAlignment.Justify}
        };
        var lines = Measurer().LayoutLines(paragraph, 100f);

        await Assert.That(lines.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Word_overflowing_by_more_than_the_spaces_give_wraps()
    {
        // The same text at a 99.5pt measure overflows by 1.3pt = three quanta — past what two
        // spaces can give — so the word wraps exactly as it did before the wedge existed.
        var lines = Measurer().LayoutLines(Paragraph("wwww wwww wwww"), 99.5f);

        await Assert.That(lines.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Wedged_line_reclaims_whole_pixels_and_fits_the_measure()
    {
        var line = Measurer().LayoutLines(Paragraph("wwww wwww wwww"), 100f).Single();

        // The 0.8pt overhang needs two one-pixel quanta, so both spaces narrow and the line lands
        // at 166 pen pixels = 99.6pt — inside the measure, on the pixel grid, like Word's own
        // quantised mixes.
        await Assert.That(line.Width).IsLessThanOrEqualTo(100f);
        await Assert.That(line.Width).IsEqualTo(99.6f).Within(0.01f);
    }

    [Test]
    public async Task A_line_with_no_spaces_cannot_wedge()
    {
        // One unbroken 14-char word at the same measures: no spaces to compress, so it stays a
        // single overflowing line (the wrapper never breaks inside a word).
        var lines = Measurer().LayoutLines(Paragraph("wwwwwwwwwwwwww"), 100f);

        await Assert.That(lines.Count).IsEqualTo(1);
    }
}
