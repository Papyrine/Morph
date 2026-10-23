/// <summary>
/// The rules an underline style and a double strike draw as (<see cref="TextDecorations"/>). Word-probed
/// from the XPS of <c>underline_styles</c> at 48pt and the same document at 24pt (see its notes.md).
/// </summary>
public class TextDecorationTests
{
    static string Fixture => Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "underline_styles", "input.docx");

    // 48pt, so the single band is p = 48 * 0.12 - 3 / 2 = 4.26 below the baseline and t = 3 thick.
    const float size = 48;
    const float baseline = 100;
    const float thickness = 3;
    const float offset = 4.26f;

    static (List<TextDecorations.Rule> Rules, List<TextDecorations.Wave> Waves) Collect(RunProperties properties, string text = "HHH  HH", Func<string, float>? measure = null)
    {
        var rules = new List<TextDecorations.Rule>();
        var waves = new List<TextDecorations.Wave>();
        TextDecorations.Collect(properties, text, 10, 200, baseline, 40, measure ?? (_ => _.Length * 10f), rules, waves);
        return (rules, waves);
    }

    static RunProperties Underlined(string style) =>
        Underlined(Enum.Parse<UnderlineStyle>(style));

    static RunProperties Underlined(UnderlineStyle style) =>
        new() { FontSizePoints = size, Underline = true, UnderlineStyle = style };

    [Test]
    public async Task The_parser_reads_every_underline_style_and_the_double_strike()
    {
        var runs = new DocumentParser().Parse(Fixture).Elements
            .OfType<ParagraphElement>()
            .SelectMany(_ => _.Runs)
            .ToList();
        var styles = runs.Where(_ => _.Properties.Underline).Select(_ => _.Properties.UnderlineStyle).Distinct().ToList();

        await Assert.That(styles.Count).IsEqualTo(Enum.GetValues<UnderlineStyle>().Length);
        await Assert.That(runs.Count(_ => _.Properties.DoubleStrikethrough)).IsEqualTo(2);
    }

    [Test]
    public async Task Double_is_two_half_rules_filling_from_half_the_offset_to_the_single_bands_bottom()
    {
        var rules = Collect(Underlined(UnderlineStyle.Double)).Rules;

        await Assert.That(rules.Select(_ => _.Thickness)).IsEquivalentTo([thickness / 2, thickness / 2]);
        await Assert.That(rules[0].Y - rules[0].Thickness / 2).IsEqualTo(baseline + offset / 2).Within(0.001f);
        await Assert.That(rules[1].Y + rules[1].Thickness / 2).IsEqualTo(baseline + offset + thickness).Within(0.001f);
    }

    [Test]
    [Arguments("Thick")]
    [Arguments("DashedHeavy")]
    public async Task Heavy_fills_from_half_the_offset_to_half_the_thickness_past_it(string style)
    {
        var rule = Collect(Underlined(style)).Rules[0];

        await Assert.That(rule.Y - rule.Thickness / 2).IsEqualTo(baseline + offset / 2).Within(0.001f);
        await Assert.That(rule.Y + rule.Thickness / 2).IsEqualTo(baseline + offset + thickness / 2).Within(0.001f);
    }

    [Test]
    [Arguments("Dotted", 1.44f, 1.44f)]
    [Arguments("Dash", 3.96f, 1.92f)]
    [Arguments("DashLong", 7.92f, 3.96f)]
    [Arguments("DashLongHeavy", 7.92f, 3.96f)]
    public async Task Dashes_are_fixed_lengths_from_the_runs_start(string style, float on, float off)
    {
        var rules = Collect(Underlined(style)).Rules;

        await Assert.That(rules[0].X0).IsEqualTo(10);
        await Assert.That(rules[0].X1 - rules[0].X0).IsEqualTo(on).Within(0.001f);
        await Assert.That(rules[1].X0 - rules[0].X1).IsEqualTo(off).Within(0.001f);
        await Assert.That(rules[^1].X1).IsLessThanOrEqualTo(210);
    }

    [Test]
    public async Task Dot_dot_dash_repeats_a_long_dash_and_two_dots()
    {
        var rules = Collect(Underlined(UnderlineStyle.DotDotDash)).Rules;

        await Assert.That(rules.Take(4).Select(_ => MathF.Round(_.X1 - _.X0, 2))).IsEquivalentTo([2.16f, 0.72f, 0.72f, 2.16f]);
    }

    [Test]
    public async Task Words_leaves_the_spaces_bare()
    {
        var rules = Collect(Underlined(UnderlineStyle.Words)).Rules;

        // "HHH  HH" at 10pt a character: HHH over 0-30, HH over 50-70.
        await Assert.That(rules.Select(_ => (_.X0, _.X1))).IsEquivalentTo([(10f, 40f), (60f, 80f)]);
    }

    [Test]
    public async Task Waves_stroke_at_a_fixed_width_from_half_the_offset()
    {
        var light = Collect(Underlined(UnderlineStyle.Wave)).Waves.Single();
        var heavy = Collect(Underlined(UnderlineStyle.WavyHeavy)).Waves.Single();
        var doubled = Collect(Underlined(UnderlineStyle.WavyDouble)).Waves;

        await Assert.That(light.Thickness).IsEqualTo(0.24f);
        await Assert.That(heavy.Thickness).IsEqualTo(0.96f);
        await Assert.That(doubled.Count).IsEqualTo(2);
        await Assert.That(light.Points.Min(_ => _.Y)).IsEqualTo(baseline + offset / 2).Within(0.001f);
        await Assert.That(light.Points.Max(_ => _.Y) - light.Points.Min(_ => _.Y)).IsEqualTo(size * 0.06f).Within(0.01f);
    }

    [Test]
    public async Task A_double_strike_is_two_rules_one_thickness_either_side_of_the_single()
    {
        var single = Collect(new() { FontSizePoints = size, Strikethrough = true }).Rules.Single();
        var doubled = Collect(new() { FontSizePoints = size, DoubleStrikethrough = true }).Rules;

        await Assert.That(doubled.Select(_ => _.Y)).IsEquivalentTo([single.Y - single.Thickness, single.Y + single.Thickness]);
        await Assert.That(doubled.All(_ => _.IsStrike &&
                                           _.Thickness == single.Thickness)).IsTrue();
    }
}
