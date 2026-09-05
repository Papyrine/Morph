/// <summary>
/// Where a superscript or subscript sits and how big it draws (<see cref="VerticalRunPosition"/>) —
/// XPS-read on <c>_probe_subsup</c> / <c>_probe_subsup2</c>: 65% of the run size, raised by a third
/// of it, or lowered so the reduced descent bottom stays on the full one.
/// </summary>
public class VerticalRunPositionTests
{
    [Test]
    public async Task A_superscript_or_subscript_measures_at_sixty_five_percent()
    {
        var plain = new RunProperties { FontFamily = "Aptos", FontSizePoints = 96 };
        await Assert.That(VerticalRunPosition.RenderSizePoints(plain)).IsEqualTo(96d);
        await Assert.That(VerticalRunPosition.RenderSizePoints(plain with { VerticalAlignment = VerticalRunAlignment.Superscript })).IsEqualTo(62.4).Within(0.001);
        await Assert.That(VerticalRunPosition.RenderSizePoints(plain with { VerticalAlignment = VerticalRunAlignment.Subscript })).IsEqualTo(62.4).Within(0.001);
    }

    [Test]
    public async Task A_superscript_rises_a_third_and_a_subscript_drops_a_third_of_the_descent_it_gave_up()
    {
        var plain = new RunProperties { FontFamily = "Aptos", FontSizePoints = 96 };

        // Calibri's 0.269 descent at 96pt is 25.8pt; Word lowered the subscript 9.0 (_probe_subsup).
        await Assert.That(VerticalRunPosition.BaselineShiftPoints(plain, 25.8)).IsEqualTo(0f);
        await Assert.That(VerticalRunPosition.BaselineShiftPoints(plain with { VerticalAlignment = VerticalRunAlignment.Superscript }, 25.8)).IsEqualTo(32f).Within(0.01f);
        await Assert.That(VerticalRunPosition.BaselineShiftPoints(plain with { VerticalAlignment = VerticalRunAlignment.Subscript }, 25.8)).IsEqualTo(-9.03f).Within(0.01f);
    }

    [Test]
    public async Task The_measurer_lays_a_superscript_out_narrower_and_carries_its_shift()
    {
        static ParagraphElement Paragraph(VerticalRunAlignment alignment) => new()
        {
            Runs =
            [
                new()
                {
                    Text = "Hx",
                    Properties = new() { FontFamily = "Aptos", FontSizePoints = 48 }
                },
                new()
                {
                    Text = "Hs",
                    Properties = new() { FontFamily = "Aptos", FontSizePoints = 48, VerticalAlignment = alignment }
                }
            ],
            Properties = new()
        };

        var baseline = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(VerticalRunAlignment.Baseline), 1000);
        var raised = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(VerticalRunAlignment.Superscript), 1000);
        var lowered = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(VerticalRunAlignment.Subscript), 1000);

        var fullRun = baseline[0].Runs[1];
        var superRun = raised[0].Runs[1];
        var subRun = lowered[0].Runs[1];

        await Assert.That(superRun.Width).IsEqualTo(fullRun.Width * 0.65f).Within(0.5f);
        await Assert.That(subRun.Width).IsEqualTo(fullRun.Width * 0.65f).Within(0.5f);
        await Assert.That(fullRun.BaselineShift).IsEqualTo(0f);
        await Assert.That(superRun.BaselineShift).IsEqualTo(16f).Within(0.01f);
        await Assert.That(subRun.BaselineShift).IsLessThan(0f);

        // The line itself keeps the full-size pitch: the reduced glyphs sit inside the run's own box.
        await Assert.That(raised[0].Height).IsEqualTo(baseline[0].Height).Within(0.01f);
    }
}
