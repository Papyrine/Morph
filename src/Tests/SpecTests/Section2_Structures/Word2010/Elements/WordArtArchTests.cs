/// <summary>
/// Word's <c>textArchUp</c> / <c>textArchDown</c> geometry (<see cref="WordArtArch"/>), checked against the
/// numbers Word drew in the <c>_probe_arch</c> probes — read from the XPS glyph transforms — and the parse
/// of what the geometry needs from wordart's arch.
/// </summary>
public class WordArtArchTests
{
    // wordart's arch: a 432x108pt box at DrawingML's default insets, "Arc Text Up" in 36pt Impact. Word drew
    // it at 31.25pt; its ink runs 0.883 em, from the caps' tops to the p's descender.
    const float textWidth = 432 - 14.4f;
    const float textHeight = 108 - 7.2f;
    const float inkHeight = 0.883f * 36;

    [Test]
    public async Task The_text_is_drawn_smaller_by_twice_its_ink_height_over_the_rect_width()
    {
        await Assert.That(WordArtArch.Scale(textWidth, inkHeight) * 36).IsEqualTo(31.25f).Within(0.05f);

        // The 288pt box drew 29.225pt — narrower boxes shrink more; the height never enters.
        await Assert.That(WordArtArch.Scale(288 - 14.4f, inkHeight) * 36).IsEqualTo(29.225f).Within(0.05f);
    }

    [Test]
    public async Task The_baseline_depth_is_the_win_ascent_above_the_typo_ascender()
    {
        var impact = new FontMetrics
        {
            UnitsPerEm = 2048,
            Ascender = 2066,
            Descender = -432,
            LineGap = 0,
            WinAscent = 2066,
            TypoAscender = 1619
        };

        await Assert.That(WordArtArch.BaselineDepthEm(impact)).IsEqualTo(0.2183f).Within(0.0001f);
        await Assert.That(WordArtArch.BaselineDepthEm(impact with {WinAscent = 0, TypoAscender = 0})).IsEqualTo(0f);
    }

    [Test]
    public async Task Centred_text_on_an_arch_up_straddles_the_apex_inside_the_arch()
    {
        var scale = WordArtArch.Scale(textWidth, inkHeight);
        var depth = 0.2183f * 36 * scale;
        var advances = Enumerable.Repeat(15f, 5).ToList();
        var placements = WordArtArch.Place(advances, 0, 0, textWidth, textHeight, scale, depth, TextAlignment.Center, WordArtTransform.ArchUp);

        // The middle glyph stands level at the apex, its baseline the depth below the scaled ellipse's top.
        var middle = placements[2];
        await Assert.That(middle.AngleDegrees).IsEqualTo(0f).Within(0.01f);
        await Assert.That(middle.X + 7.5f).IsEqualTo(textWidth / 2).Within(0.01f);
        await Assert.That(middle.Y).IsEqualTo(textHeight / 2 - scale * textHeight / 2 + depth).Within(0.01f);

        // Either side mirrors it, turned up to the left and down to the right.
        await Assert.That(placements[0].AngleDegrees).IsLessThan(0f);
        await Assert.That(placements[4].AngleDegrees).IsEqualTo(-placements[0].AngleDegrees).Within(0.01f);
    }

    [Test]
    public async Task Left_aligned_text_starts_at_the_left_end_of_the_path()
    {
        var scale = WordArtArch.Scale(textWidth, inkHeight);
        var placements = WordArtArch.Place([0.2f, 0.2f], 0, 0, textWidth, textHeight, scale, 0, TextAlignment.Left, WordArtTransform.ArchUp);

        // Word's left-aligned "Arc Text Up" stood its A at -52 degrees, low on the arch's left flank.
        await Assert.That(placements[0].X).IsEqualTo(textWidth / 2 - scale * textWidth / 2).Within(0.5f);
        await Assert.That(placements[0].Y).IsEqualTo(textHeight / 2).Within(0.5f);
        await Assert.That(placements[0].AngleDegrees).IsLessThan(-60f);
    }

    [Test]
    public async Task Centred_text_on_an_arch_down_sits_below_the_dip()
    {
        var scale = WordArtArch.Scale(textWidth, inkHeight);
        var depth = 5f;
        var placements = WordArtArch.Place([10f, 10f, 10f], 0, 0, textWidth, textHeight, scale, depth, TextAlignment.Center, WordArtTransform.ArchDown);

        var middle = placements[1];
        await Assert.That(middle.AngleDegrees).IsEqualTo(0f).Within(0.01f);
        await Assert.That(middle.Y).IsEqualTo(textHeight / 2 + scale * textHeight / 2 + depth).Within(0.01f);

        // The left glyph slopes down toward the dip.
        await Assert.That(placements[0].AngleDegrees).IsGreaterThan(0f);
    }

    // _probe_circle_*: 36pt text in boxes with no insets, each glyph's em, origin and turn read off the XPS.
    [Test]
    public async Task A_circle_scales_on_the_longer_side_of_its_rect()
    {
        // 36pt Arial capitals (0.716 em of ink) drew at 30.73pt in a 300x300, a 300x150 and a 200x300 box.
        var ink = 0.716f * 36;
        await Assert.That(WordArtArch.Scale(WordArtArch.ScaleSide(WordArtTransform.Circle, 300, 300), ink) * 36).IsEqualTo(30.73f).Within(0.05f);
        await Assert.That(WordArtArch.Scale(WordArtArch.ScaleSide(WordArtTransform.Circle, 300, 150), ink) * 36).IsEqualTo(30.73f).Within(0.05f);
        await Assert.That(WordArtArch.Scale(WordArtArch.ScaleSide(WordArtTransform.Circle, 200, 300), ink) * 36).IsEqualTo(30.73f).Within(0.05f);

        // An arch takes the width whatever the height.
        await Assert.That(WordArtArch.ScaleSide(WordArtTransform.ArchUp, 200, 300)).IsEqualTo(200f);
    }

    [Test]
    public async Task Centred_text_on_a_circle_sits_about_its_right_end_reading_downward()
    {
        // Eight 36pt Impact capitals in a 300x300 box at (72, 72): Word drew them at 30.275pt, the fifth
        // with its origin at (342.02, 221.68) turned 93.70 degrees, the first at (325.47, 161.35) turned 63.43.
        var scale = 30.275f / 36;
        var em = 30.275f;
        var advances = Enumerable.Repeat(1137f / 2048 * em, 8).ToList();
        var placements = WordArtArch.Place(advances, 72, 72, 300, 300, scale, 0.2183f * em, TextAlignment.Center, WordArtTransform.Circle);

        await Assert.That(placements[4].X).IsEqualTo(342.02f).Within(0.75f);
        await Assert.That(placements[4].Y).IsEqualTo(221.68f).Within(0.75f);
        await Assert.That(placements[4].AngleDegrees).IsEqualTo(93.70f).Within(0.3f);
        await Assert.That(placements[0].X).IsEqualTo(325.47f).Within(0.75f);
        await Assert.That(placements[0].Y).IsEqualTo(161.35f).Within(0.75f);
        await Assert.That(placements[0].AngleDegrees).IsEqualTo(63.43f).Within(0.3f);
    }

    [Test]
    public async Task Left_aligned_text_on_a_circle_climbs_from_its_left_end()
    {
        var placements = WordArtArch.Place([0.2f, 0.2f], 0, 0, 300, 300, 1, 0, TextAlignment.Left, WordArtTransform.Circle);

        // The path opens at the left end of the ellipse and runs up over the top.
        await Assert.That(placements[0].X).IsEqualTo(0f).Within(0.5f);
        await Assert.That(placements[0].Y).IsEqualTo(150f).Within(0.5f);
        await Assert.That(placements[0].AngleDegrees).IsEqualTo(-90f).Within(1f);
    }

    [Test]
    public async Task The_parse_carries_the_insets_the_inner_alignment_and_the_paragraph_spacing()
    {
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "wordart", "input.docx"));
        var arch = new DocumentParser()
            .Parse(stream)
            .Elements
            .OfType<WordArtElement>()
            .First(_ => _.Transform == WordArtTransform.ArchUp);

        await Assert.That(arch.Insets).IsEqualTo(WordArtInsets.Default);
        await Assert.That(arch.TextAlignment).IsEqualTo(TextAlignment.Center);
        await Assert.That(arch.SpacingAfterPoints).IsEqualTo(10d);
    }
}
