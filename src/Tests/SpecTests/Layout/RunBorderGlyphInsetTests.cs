/// <summary>
/// A run border (<c>w:bdr</c>) pushes the run's glyphs right by its drawn stack plus the floored
/// <c>w:space</c>, and holds the next run off by the same on the right — XPS-read on
/// <c>_probe_runbdr</c> (<see cref="BorderStroke.RunBorderGlyphInset"/>).
/// </summary>
public class RunBorderGlyphInsetTests
{
    [Test]
    [Arguments(0.75, 1.0, 1.2)]
    [Arguments(3.0, 4.0, 6.6)]
    [Arguments(6.0, 0.0, 6.0)]
    public async Task The_inset_is_the_drawn_stack_plus_the_floored_space(double width, double space, double expected)
    {
        var edge = new BorderEdge { IsVisible = true, WidthPoints = width, SpacePoints = space };

        await Assert.That(BorderStroke.RunBorderGlyphInset(edge)).IsEqualTo(expected).Within(0.001);
    }

    [Test]
    public async Task An_invisible_edge_reserves_nothing() =>
        await Assert.That(BorderStroke.RunBorderGlyphInset(BorderEdge.None)).IsEqualTo(0d);

    [Test]
    public async Task A_run_beginning_with_a_space_draws_its_box_inside_the_line_and_reserves_nothing()
    {
        static ParagraphElement Paragraph(string boxed, BorderEdge? border) => new()
        {
            Runs =
            [
                new() { Text = "before ", Properties = new() { FontFamily = "Aptos", FontSizePoints = 10 } },
                new() { Text = boxed, Properties = new() { FontFamily = "Aptos", FontSizePoints = 10, Border = border } },
                new() { Text = " after", Properties = new() { FontFamily = "Aptos", FontSizePoints = 10 } }
            ],
            Properties = new()
        };

        var border = new BorderEdge { IsVisible = true, WidthPoints = 2.25, SpacePoints = 1 };
        var plain = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph("boxed", null), 1000)[0];
        var reserving = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph("boxed", border), 1000)[0];
        var inside = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(" boxed ", border), 1000)[0];

        await Assert.That(reserving.Height).IsGreaterThan(plain.Height + 6f);
        await Assert.That(inside.Height).IsEqualTo(plain.Height).Within(0.01f);
        await Assert.That(BorderStroke.RunBorderReserves("boxed")).IsTrue();
        await Assert.That(BorderStroke.RunBorderReserves(" boxed ")).IsFalse();

        // The inside box: outer faces on the line box, so the inner rect starts one drawn stack (1.8) in.
        var box = BorderStroke.RunBorderBox(border, 100, 50, 200, 12.2, 0, reserves: false);
        await Assert.That(box.Y).IsEqualTo(201.8).Within(0.001);
        await Assert.That(box.Height).IsEqualTo(12.2 - 3.6).Within(0.001);
    }

    [Test]
    public async Task The_measurer_starts_a_bordered_run_after_its_inset_and_ends_the_next_run_after_it_too()
    {
        static ParagraphElement Paragraph(BorderEdge? border) => new()
        {
            Runs =
            [
                new()
                {
                    Text = "boxed",
                    Properties = new() { FontFamily = "Aptos", FontSizePoints = 12, Border = border }
                },
                new()
                {
                    Text = "after",
                    Properties = new() { FontFamily = "Aptos", FontSizePoints = 12 }
                }
            ],
            Properties = new()
        };

        var border = new BorderEdge { IsVisible = true, WidthPoints = 3, SpacePoints = 4 };
        var plain = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(null), 1000)[0];
        var bordered = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph(border), 1000)[0];

        // 6.6pt before the boxed glyphs, 6.6pt after them, the glyph run itself unchanged.
        await Assert.That(bordered.Runs[0].X).IsEqualTo(plain.Runs[0].X + 6.6f).Within(0.05f);
        await Assert.That(bordered.Runs[0].Width).IsEqualTo(plain.Runs[0].Width).Within(0.05f);
        await Assert.That(bordered.Runs[1].X).IsEqualTo(plain.Runs[1].X + 13.2f).Within(0.05f);
        await Assert.That(bordered.Width).IsEqualTo(plain.Width + 13.2f).Within(0.05f);
    }
}
