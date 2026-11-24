using static ProbeLayout;

/// <summary>
/// The body stops at the top of a footer that reaches above the bottom margin
/// (<c>Fragmenter.FooterReservedBottom</c>), and starts below a header that reaches below the top one.
/// Word-probed on <c>_probe_hdrspace</c> and <c>_probe_ftrstep</c> (2026-11-25): 60 numbered 12pt lines
/// under each band, a Letter page with 1in margins and the bands 0.5in from the edge, read off the XPS.
/// The margin alone takes lines 1 to 46.
/// </summary>
public class FooterReserveTests
{
    static string LastOnFirstPage(string footer, int bottomTwips = 1440)
    {
        var laidOut = Layout(Lines("line", 60), Section(bottomTwips: bottomTwips, bands: true), Text("header"), footer);
        return Lines(laidOut.Pages[0]).Last(_ => _.Text.StartsWith("line")).Text;
    }

    [Test]
    public async Task A_footer_inside_the_margin_leaves_the_body_alone() =>
        await Assert.That(LastOnFirstPage(Text("footer"))).IsEqualTo("line 46");

    [Test]
    public async Task A_tall_footer_lifts_the_body_foot() =>
        await Assert.That(LastOnFirstPage(Lines("footer", 8))).IsEqualTo("line 41");

    // The step that settles where the body stops: line 44's foot is 678.98pt down, which the footer's
    // top (756 - 13.8 - after) passes between 63pt and 64pt of space after.
    [Test]
    [Arguments(60, "line 44")]
    [Arguments(63, "line 44")]
    [Arguments(64, "line 43")]
    [Arguments(72, "line 43")]
    public async Task A_footers_space_after_counts(int after, string last) =>
        await Assert.That(LastOnFirstPage(Paragraph(Run("footer"), afterPoints: after))).IsEqualTo(last);

    [Test]
    public async Task Space_between_footer_paragraphs_counts() =>
        await Assert.That(LastOnFirstPage(Paragraph(Run("footer a"), afterPoints: 60) + Text("footer b"))).IsEqualTo("line 43");

    // ECMA-376 §17.6.11, and Word: under a negative w:bottom the eight-line footer stood over lines 42 to 46.
    [Test]
    public async Task A_negative_bottom_margin_pins_the_body_foot() =>
        await Assert.That(LastOnFirstPage(Lines("footer", 8), bottomTwips: -1440)).IsEqualTo("line 46");

    // The header's last paragraph's space after pushes the body down: Word's first body baseline 120.98pt,
    // 37.8pt under the 83.18pt a header inside the margin leaves it at.
    [Test]
    public async Task A_headers_last_space_after_pushes_the_body_down()
    {
        var plain = Layout(Lines("line", 2), Section(bands: true), Text("header"), Text("footer"));
        var spaced = Layout(Lines("line", 2), Section(bands: true), Paragraph(Run("header"), afterPoints: 60), Text("footer"));

        var plainFirst = Lines(plain.Pages[0]).First(_ => _.Text == "line 1").Baseline;
        var spacedFirst = Lines(spaced.Pages[0]).First(_ => _.Text == "line 1").Baseline;
        await Assert.That(spacedFirst - plainFirst).IsEqualTo(37.8f).Within(0.05f);
    }
}
