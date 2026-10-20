/// <summary>
/// The compatibility mode 15 squeeze of a justified line, case for case as Word answered it
/// (<c>_probe_just15</c> and <c>_probe_just12</c>, 2026-10-11): a sentence of one repeated word, set
/// once per whole pixel of measure on Word's 120-dpi grid, and the narrowest measure at which the
/// first line still holds the word count given here. In mode 15 that measure lies below the line's
/// natural width, by a quarter of each space at most and by half the stretch the shorter line would
/// need at most; in mode 12 it is the natural width rounded up.
/// </summary>
public class JustifiedSqueezeTests
{
    static readonly CanonicalParagraphMeasurer squeezing = new(LayoutTestFonts.Resolve, squeezeJustifiedLines: true);
    static readonly CanonicalParagraphMeasurer plain = new(LayoutTestFonts.Resolve);

    // A line's last word is 14 letters where the kind is "longlast", so the line one word shorter is
    // very loose and only the quarter-space limit decides.
    static string[] Words(string kind, int count, int tail = 3)
    {
        if (kind == "longlast")
        {
            return [.. Enumerable.Repeat("nn", count - 1), new('n', 14), .. Enumerable.Repeat("nn", tail)];
        }

        return [.. Enumerable.Repeat(kind, count + tail)];
    }

    static ParagraphElement Justified(string family, double size, string[] words) =>
        new()
        {
            Properties = new() {Alignment = TextAlignment.Justify},
            Runs = [new() {Text = string.Join(' ', words), Properties = new() {FontFamily = family, FontSizePoints = size}}]
        };

    static int FirstLineLetters(CanonicalParagraphMeasurer measurer, ParagraphElement paragraph, int measurePixels)
    {
        var measure = (float) (measurePixels * 72.0 / CanonicalTextMeasurer.ReferenceDpi);
        var first = measurer.LayoutLineContents(paragraph, measure)[0];
        return first.Runs.Sum(_ => _.Text.Count(ch => ch != ' '));
    }

    [Test]
    [Arguments("Calibri", 12.0, "nn", 29, 728, 737)]
    [Arguments("Calibri", 12.0, "nnnn", 16, 725, 741)]
    [Arguments("Calibri", 12.0, "nnnnnnnn", 8, 697, 705)]
    [Arguments("Calibri", 12.0, "longlast", 25, 733, 760)]
    [Arguments("Calibri", 11.0, "nn", 32, 737, 745)]
    [Arguments("Calibri", 11.0, "nnnn", 17, 707, 722)]
    [Arguments("Calibri", 11.0, "nnnnnnnn", 9, 719, 727)]
    [Arguments("Calibri", 11.0, "longlast", 27, 717, 744)]
    [Arguments("Times New Roman", 12.0, "nn", 30, 737, 745)]
    [Arguments("Times New Roman", 12.0, "nnnn", 17, 745, 760)]
    [Arguments("Times New Roman", 12.0, "nnnnnnnn", 9, 750, 760)]
    [Arguments("Times New Roman", 12.0, "longlast", 25, 710, 740)]
    [Arguments("Arial", 10.0, "nn", 33, 753, 760)]
    [Arguments("Arial", 10.0, "nnnn", 18, 732, 747)]
    [Arguments("Arial", 10.0, "nnnnnnnn", 9, 696, 705)]
    [Arguments("Arial", 10.0, "longlast", 28, 725, 756)]
    public async Task The_last_word_wraps_at_the_measure_Word_wraps_it_at(string family, double size, string kind, int count, int squeezedThreshold, int plainThreshold)
    {
        var words = Words(kind, count);
        var paragraph = Justified(family, size, words);
        var whole = words.Take(count).Sum(_ => _.Length);
        var oneShort = words.Take(count - 1).Sum(_ => _.Length);

        // Mode 15: the squeezed line holds every word down to the threshold and loses one below it.
        await Assert.That(FirstLineLetters(squeezing, paragraph, squeezedThreshold)).IsEqualTo(whole);
        await Assert.That(FirstLineLetters(squeezing, paragraph, squeezedThreshold - 1)).IsEqualTo(oneShort);

        // Mode 12: nothing is squeezed, and the line breaks where a left-aligned one would.
        await Assert.That(FirstLineLetters(plain, paragraph, plainThreshold)).IsEqualTo(whole);
        await Assert.That(FirstLineLetters(plain, paragraph, plainThreshold - 1)).IsEqualTo(oneShort);
    }

    // Sixteen words that END the paragraph stay on one line down to the measure at which they stay on
    // the first line of a longer one - the last line is not justified, and is squeezed all the same.
    [Test]
    public async Task The_last_line_of_a_paragraph_is_squeezed_too()
    {
        var paragraph = Justified("Calibri", 12, Words("nnnn", 16, tail: 0));
        static float Measure(int pixels) => (float) (pixels * 72.0 / CanonicalTextMeasurer.ReferenceDpi);

        await Assert.That(squeezing.LayoutLineContents(paragraph, Measure(725)).Count).IsEqualTo(1);
        await Assert.That(squeezing.LayoutLineContents(paragraph, Measure(724)).Count).IsEqualTo(2);
    }

    // A squeezed line ends at the measure: its spaces give back exactly what it ran over.
    [Test]
    public async Task A_squeezed_line_ends_at_the_measure()
    {
        var paragraph = Justified("Calibri", 12, Words("nnnn", 16));
        var measure = (float) (730 * 72.0 / CanonicalTextMeasurer.ReferenceDpi);
        var first = squeezing.LayoutLineContents(paragraph, measure)[0];
        var end = first.Runs[^1].X + first.Runs[^1].Width;

        await Assert.That(first.Width).IsEqualTo(measure).Within(0.01f);
        await Assert.That(end).IsEqualTo(measure).Within(0.31f);
    }

    // Left-aligned text is never squeezed, whatever the mode.
    [Test]
    public async Task Text_that_is_not_justified_is_not_squeezed()
    {
        var words = Words("nnnn", 16);
        var paragraph = new ParagraphElement
        {
            Properties = new(),
            Runs = [new() {Text = string.Join(' ', words), Properties = new() {FontFamily = "Calibri", FontSizePoints = 12}}]
        };

        await Assert.That(FirstLineLetters(squeezing, paragraph, 741)).IsEqualTo(16 * 4);
        await Assert.That(FirstLineLetters(squeezing, paragraph, 740)).IsEqualTo(15 * 4);
    }
}
