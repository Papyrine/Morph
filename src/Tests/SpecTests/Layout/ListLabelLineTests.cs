/// <summary>
/// A list label stands on its paragraph's first line in its own face and at its own size, and the
/// line rises to it: the label's ascent over the text's descent. Word-probed 2026-10-11
/// (<c>_probe_bullet</c> and <c>_probe_bulletcal</c>: one-line list paragraphs of 24pt Arial and of
/// 24pt Calibri with no spacing, the pitch read between their baselines).
/// </summary>
public class ListLabelLineTests
{
    static ParagraphElement Item(string textFamily, NumberingInfo? numbering, string text = "label r0") =>
        new()
        {
            Runs = [new() {Text = text, Properties = new() {FontFamily = textFamily, FontSizePoints = 24}}],
            Properties = new() {Numbering = numbering, LineSpacingMultiplier = 1}
        };

    static double FirstLine(string textFamily, NumberingInfo? numbering) =>
        LayoutTestFonts.Measurer.LayoutLines(Item(textFamily, numbering), 400)[0].Height;

    static NumberingInfo Label(string text, string? family, double? size = null) =>
        new() {Text = text, FontFamily = family, FontSizePoints = size};

    // Word's pitch, as the range its snapped baselines allow: three consecutive pitches of 29.42,
    // 29.43 and 28.82pt average 29.2 for a Symbol bullet on Arial.
    [Test]
    [Arguments("Arial", "Symbol", null, 29.0, 29.4)]
    [Arguments("Arial", "Symbol", 48.0, 53.04, 53.44)]
    [Arguments("Calibri", "Symbol", null, 30.2, 30.6)]
    [Arguments("Arial", "Microsoft YaHei", null, 34.8, 35.5)]
    public async Task The_first_line_rises_to_a_taller_label(string textFamily, string labelFamily, double? labelSize, double low, double high)
    {
        var height = FirstLine(textFamily, Label("•", labelFamily, labelSize));

        await Assert.That(height).IsGreaterThanOrEqualTo(low);
        await Assert.That(height).IsLessThanOrEqualTo(high);
    }

    // A 48pt "1." in the text's own face: 50.2pt on Arial, 52.2 on Calibri.
    [Test]
    [Arguments("Arial", 49.8, 50.4)]
    [Arguments("Calibri", 51.9, 52.5)]
    public async Task A_larger_label_in_the_texts_face_counts_by_its_ascent_alone(string family, double low, double high)
    {
        var height = FirstLine(family, Label("1.", null, 48));

        await Assert.That(height).IsGreaterThanOrEqualTo(low);
        await Assert.That(height).IsLessThanOrEqualTo(high);
    }

    // A smaller label, one in a face with less ascent than the text's, and one with nothing of its
    // own leave the line at the text's pitch.
    [Test]
    [Arguments("Symbol", 8.0)]
    [Arguments("Courier New", null)]
    [Arguments("Wingdings", null)]
    [Arguments(null, null)]
    public async Task A_label_no_taller_than_the_text_changes_nothing(string? labelFamily, double? labelSize) =>
        await Assert.That(FirstLine("Arial", Label("o", labelFamily, labelSize))).IsEqualTo(FirstLine("Arial", null)).Within(0.01);

    [Test]
    public async Task A_continuation_line_keeps_the_texts_pitch()
    {
        var lines = LayoutTestFonts.Measurer.LayoutLines(Item("Arial", Label("•", "Symbol"), "label r3\nsecond"), 400);

        await Assert.That((double) lines[0].Height).IsGreaterThan(29.0);
        await Assert.That((double) lines[1].Height).IsEqualTo(FirstLine("Arial", null)).Within(0.01);
    }

    // The label is drawn at the level's size when it declares one.
    [Test]
    public async Task The_marker_takes_the_levels_size()
    {
        var paragraph = Item("Arial", Label("•", "Symbol", 48));

        await Assert.That(CanonicalParagraphMeasurer.MarkerProperties(paragraph, paragraph.Properties.Numbering!).FontSizePoints).IsEqualTo(48);
        await Assert.That(CanonicalParagraphMeasurer.MarkerProperties(Item("Arial", Label("•", "Symbol")), Label("•", "Symbol")).FontSizePoints).IsEqualTo(24);
    }
}
