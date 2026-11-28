/// <summary>
/// A line shape (<c>prst="line"</c> / <c>straightConnector1</c>) runs from one corner of its box to the
/// other, and draws its oval ends. wedding/04's checklist is the case: a spine 1.6pt wide and 914pt tall
/// that Word draws as one leaning rule, and 2pt section rules ending in 6pt dots.
/// </summary>
public class LineShapeTests
{
    static List<FloatingShapeElement> Lines()
    {
        var path = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "wedding", "04", "input.docx");
        using var stream = File.OpenRead(path);
        var document = new DocumentParser().Parse(stream);
        return document.Elements.OfType<FloatingShapeElement>().Where(_ => _.LineColorHex != null).ToList();
    }

    [Test]
    public async Task A_line_with_both_extents_is_one_segment_corner_to_corner()
    {
        var spine = Lines().Single(_ => _.HeightPoints > 900 && _.FillColorHex == null);

        await Assert.That(spine.OpenOutline).IsTrue();
        var segment = spine.Subpaths!.Single();
        await Assert.That(segment.Count).IsEqualTo(2);
        await Assert.That(Math.Abs(segment[1].X - segment[0].X)).IsEqualTo(1).Within(0.001);
        await Assert.That(Math.Abs(segment[1].Y - segment[0].Y)).IsEqualTo(1).Within(0.001);
    }

    [Test]
    public async Task An_oval_end_is_a_disc_three_line_widths_across()
    {
        var rule = Lines().First(_ => _.FillColorHex != null && _.WidthPoints > 200);

        // The 2pt rule's box grows by the 6pt dot's reach on every side; the dot is filled in the
        // line's colour and its polygon, stroked at the line's width, comes out 6pt across.
        await Assert.That(rule.FillColorHex).IsEqualTo(rule.LineColorHex);
        await Assert.That(rule.HeightPoints).IsEqualTo(6).Within(0.01);
        var disc = rule.Subpaths![1];
        var across = (disc.Max(_ => _.Y) - disc.Min(_ => _.Y)) * rule.HeightPoints;
        await Assert.That(across + rule.LineWidthPoints!.Value).IsEqualTo(6).Within(0.05);
    }
}
