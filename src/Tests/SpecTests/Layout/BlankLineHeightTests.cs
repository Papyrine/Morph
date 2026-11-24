using static ProbeLayout;

/// <summary>
/// Spaces and tabs set no line's height (<c>CanonicalParagraphMeasurer.SetsNoHeight</c>). Word-probed on
/// <c>_probe_blanks</c> (2026-11-25, compatibility modes 12 and 15 alike): a 12pt Times New Roman line
/// holding 48pt blanks, between two 12pt marker lines. Each expected step is the distance between the
/// markers' baselines Word drew, read off its XPS: 27.6pt is two plain 13.8pt lines.
/// </summary>
public class BlankLineHeightTests
{
    const int big = 96;

    static readonly Dictionary<string, string> cases = new()
    {
        ["trailing spaces"] = Paragraph(Run("trailing") + Run("   ", big)),
        ["spaces between words"] = Paragraph(Run("mid") + Run("   ", big) + Run("word")),
        ["a space between runs"] = Paragraph(Run("between runs") + Run(" ", big) + Run(" word")),
        ["a leading space"] = Paragraph(Run(" ", big) + Run("leading")),
        ["spaces where the line wraps"] = Paragraph(Run("wrap " + new string('x', 70)) + Run("   ", big) + Run(new string('y', 40))),
        ["a tab between words"] = Paragraph(Run("tab") + Tab(big) + Run("word")),
        ["a trailing tab"] = Paragraph(Run("tab trailing") + Tab(big)),
        ["a tab with a dot leader"] = Paragraph(Run("leader") + Tab(big) + Run("end"), properties: """<w:tabs><w:tab w:val="left" w:pos="5760" w:leader="dot"/></w:tabs>"""),
        ["spaces alone"] = Paragraph(Run("   ", big)),
        ["a tab alone"] = Paragraph(Tab(big)),
        ["no-break spaces between words"] = Paragraph(Run("nbsp") + Run("   ", big) + Run("word")),
        ["an empty paragraph with a large mark"] = Paragraph("", markHalfPoints: big)
    };

    [Test]
    [Arguments("trailing spaces", 27.6)]
    [Arguments("spaces between words", 27.6)]
    [Arguments("a space between runs", 27.6)]
    [Arguments("a leading space", 27.6)]
    [Arguments("spaces where the line wraps", 41.4)]
    [Arguments("a tab between words", 27.6)]
    [Arguments("a trailing tab", 27.6)]
    [Arguments("a tab with a dot leader", 27.6)]
    [Arguments("spaces alone", 27.6)]
    [Arguments("a tab alone", 27.6)]
    [Arguments("no-break spaces between words", 69.0)]
    [Arguments("an empty paragraph with a large mark", 69.0)]
    public async Task Blanks_set_no_line_height(string name, double step)
    {
        var laidOut = Layout(Text("before") + cases[name] + Text("after"), Section());
        var lines = Lines(laidOut.Pages[0]);

        var before = lines.Single(_ => _.Text == "before").Baseline;
        var after = lines.Single(_ => _.Text == "after").Baseline;
        await Assert.That(after - before).IsEqualTo((float) step).Within(0.05f);
    }
}
