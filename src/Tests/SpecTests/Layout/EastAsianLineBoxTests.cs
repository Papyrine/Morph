/// <summary>
/// The line box of an East Asian face: 1.3 times the face's own, the extra shared evenly above and
/// below the text. Word-probed 2026-10-11 (<c>_probe_eaheight</c>, three explicit lines of Latin text
/// at the top of a page per face, size and spacing, on two base documents): the pitches and first
/// baselines below are Word's, the baselines given from the top of the text area.
/// </summary>
public class EastAsianLineBoxTests
{
    static ParagraphElement ThreeLines(string family, double size, double multiple = 1) =>
        new()
        {
            Runs = [new() {Text = "Hxg\nHxg\nHxg", Properties = new() {FontFamily = family, FontSizePoints = size}}],
            Properties = new() {LineSpacingMultiplier = multiple}
        };

    [Test]
    [Arguments("Microsoft YaHei", true)]
    [Arguments("Microsoft YaHei UI", true)]
    [Arguments("MS Gothic", true)]
    [Arguments("Batang", true)]
    [Arguments("Microsoft GothicNeo", true)]
    [Arguments("Arial", false)]
    [Arguments("Calibri", false)]
    [Arguments("Segoe UI", false)]
    public async Task A_face_is_East_Asian_by_its_code_pages(string family, bool expected) =>
        await Assert.That(LayoutTestFonts.Resolve(family, false, false)!.EastAsian).IsEqualTo(expected);

    // Word's single-spaced pitch at 48pt, in points.
    [Test]
    [Arguments("Microsoft YaHei", 82.26)]
    [Arguments("Microsoft YaHei UI", 79.26)]
    [Arguments("Batang", 62.43)]
    [Arguments("Microsoft GothicNeo", 74.75)]
    [Arguments("Arial", 55.25)]
    public async Task The_single_pitch_is_Words(string family, double expected)
    {
        var lines = LayoutTestFonts.Measurer.LayoutLines(ThreeLines(family, 48), 400);

        await Assert.That(lines.Count).IsEqualTo(3);
        await Assert.That((double) lines[1].Height).IsEqualTo(expected).Within(0.25);
    }

    // The first baseline under the top of the text area: Microsoft YaHei 30.6pt at 24pt and 60.0pt
    // at 48pt (51 and 100 pixels on the 120-dpi grid), against Arial's 22.8 and 45.0.
    [Test]
    [Arguments("Microsoft YaHei", 24, 30.6)]
    [Arguments("Microsoft YaHei", 48, 60.0)]
    [Arguments("MS Gothic", 24, 24.0)]
    [Arguments("Arial", 24, 22.8)]
    [Arguments("Arial", 48, 45.0)]
    public async Task Half_the_extra_stands_above_the_text(string family, int size, double expected)
    {
        var lines = LayoutTestFonts.Measurer.LayoutLineContents(ThreeLines(family, size), 400);

        await Assert.That((double) lines[0].Ascent).IsEqualTo(expected).Within(0.35);
    }

    // A multiple stacks on the 1.3 box and leaves the baseline where it was: Word's 1.5 lines of 48pt
    // YaHei are 123.7pt apart, and the first baseline is the single-spaced one.
    [Test]
    public async Task A_multiple_applies_over_the_wider_box()
    {
        var single = LayoutTestFonts.Measurer.LayoutLineContents(ThreeLines("Microsoft YaHei", 48), 400);
        var oneAndHalf = LayoutTestFonts.Measurer.LayoutLineContents(ThreeLines("Microsoft YaHei", 48, 1.5), 400);

        await Assert.That((double) oneAndHalf[1].Height).IsEqualTo(123.7).Within(0.4);
        await Assert.That(oneAndHalf[0].Ascent).IsEqualTo(single[0].Ascent).Within(0.01f);
    }
}
