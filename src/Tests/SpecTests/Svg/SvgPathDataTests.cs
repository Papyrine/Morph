using static SvgTestSupport;

/// <summary>
/// Path data (the <c>d</c> attribute) as <see cref="SvgPathData"/> reads it: every command in both
/// cases, the grammar's shorthands, and the rewriting of quadratics and arcs as cubics.
/// </summary>
public class SvgPathDataTests
{
    static bool Parses(string data) =>
        SvgPathData.TryParse(data, new());

    [Test]
    public async Task Lines_are_read_in_absolute_and_relative_form()
    {
        var figures = Path("M10 10 L20 10 l0 10 H5 h-5 V0 v5 Z");

        await Assert.That(Describe(figures)).IsEqualTo("M10,10 L20,10 L20,20 L5,20 L0,20 L0,0 L0,5 Z");
    }

    [Test]
    public async Task Pairs_after_a_move_are_lines_relative_when_the_move_was()
    {
        await Assert.That(Describe(Path("M0 0 10 0 10 10"))).IsEqualTo("M0,0 L10,0 L10,10");
        await Assert.That(Describe(Path("M5 5 m5 5 10 0 0 10"))).IsEqualTo("M10,10 L20,10 L20,20");
    }

    [Test]
    public async Task A_second_move_starts_a_second_figure_and_a_bare_move_draws_nothing() =>
        await Assert.That(Describe(Path("M0 0 L5 0 M9 9 M20 20 L25 20"))).IsEqualTo("M0,0 L5,0 M20,20 L25,20");

    [Test]
    public async Task Drawing_after_a_close_starts_where_the_closed_figure_began() =>
        await Assert.That(Describe(Path("M10 10 L20 10 L20 20 Z l-5 -5"))).IsEqualTo("M10,10 L20,10 L20,20 Z M10,10 L5,5");

    [Test]
    public async Task Cubics_are_read_in_absolute_and_relative_form() =>
        await Assert.That(Describe(Path("M0 0 C1 2 3 4 5 6 c1 1 2 2 3 3"))).IsEqualTo("M0,0 C1,2 3,4 5,6 C6,7 7,8 8,9");

    [Test]
    public async Task A_smooth_cubic_mirrors_the_curve_before_it()
    {
        // After a C the first control point is the last one reflected in the current point.
        await Assert.That(Describe(Path("M0 0 C0 10 10 10 10 0 S20 -10 20 0"))).IsEqualTo("M0,0 C0,10 10,10 10,0 C10,-10 20,-10 20,0");

        // With no curve before it there is nothing to mirror, and it starts from the current point.
        await Assert.That(Describe(Path("M0 0 L10 0 s10 -10 10 0"))).IsEqualTo("M0,0 L10,0 C10,0 20,-10 20,0");
    }

    [Test]
    public async Task A_quadratic_is_raised_to_the_cubic_of_the_same_curve()
    {
        // Control points two thirds of the way from each end to the quadratic's one.
        await Assert.That(Describe(Path("M0 0 Q30 30 60 0"))).IsEqualTo("M0,0 C20,20 40,20 60,0");
        await Assert.That(Describe(Path("M0 0 q30 30 60 0 T120 0"))).IsEqualTo("M0,0 C20,20 40,20 60,0 C80,-20 100,-20 120,0");
    }

    [Test]
    public async Task Numbers_need_no_separator_where_a_sign_or_a_point_ends_the_last()
    {
        await Assert.That(Describe(Path("M.5.5L1-2 3e1 4E-1"))).IsEqualTo("M0.5,0.5 L1,-2 L30,0.4");
        await Assert.That(Describe(Path("M0,0,10,0\n10,10z"))).IsEqualTo("M0,0 L10,0 L10,10 Z");
    }

    [Test]
    public async Task A_quarter_circle_arc_is_one_cubic_that_stays_on_the_circle()
    {
        var figure = Path("M10 0 A10 10 0 0 1 0 10").Single();
        var curve = figure.Segments.Single();

        await Assert.That(curve.IsCurve).IsTrue();
        await Assert.That(curve.X).IsEqualTo(0);
        await Assert.That(curve.Y).IsEqualTo(10);
        await Assert.That(curve.Control1X).IsEqualTo(10).Within(1e-9);
        await Assert.That(curve.Control1Y).IsEqualTo(5.5228).Within(1e-3);
        await Assert.That(curve.Control2X).IsEqualTo(5.5228).Within(1e-3);
        await Assert.That(curve.Control2Y).IsEqualTo(10).Within(1e-9);

        // Halfway along, the cubic is within three parts in ten thousand of the radius.
        var midX = (10 + 3 * curve.Control1X + 3 * curve.Control2X + curve.X) / 8;
        var midY = (0 + 3 * curve.Control1Y + 3 * curve.Control2Y + curve.Y) / 8;
        await Assert.That(Math.Sqrt(midX * midX + midY * midY)).IsEqualTo(10).Within(0.003);
    }

    [Test]
    public async Task The_sweep_flag_picks_the_side_and_the_large_arc_flag_the_long_way_round()
    {
        // The same two points and radius, the other way round: its circle is centred on (10, 10).
        var other = Path("M10 0 A10 10 0 0 0 0 10").Single().Segments.Single();
        var midX = (10 + 3 * other.Control1X + 3 * other.Control2X + other.X) / 8;
        var midY = (0 + 3 * other.Control1Y + 3 * other.Control2Y + other.Y) / 8;
        await Assert.That(Math.Sqrt((midX - 10) * (midX - 10) + (midY - 10) * (midY - 10))).IsEqualTo(10).Within(0.003);

        // Three quarters of a turn is three cubics, each a quarter.
        var large = Path("M10 0 A10 10 0 1 0 0 10").Single();
        await Assert.That(large.Segments.Count).IsEqualTo(3);
        await Assert.That(large.Segments[^1].X).IsEqualTo(0);
        await Assert.That(large.Segments[^1].Y).IsEqualTo(10);
    }

    [Test]
    public async Task Arc_flags_may_run_into_each_other_and_the_number_after() =>
        await Assert.That(Describe(Path("M10 0 a10 10 0 01-10 10"))).IsEqualTo(Describe(Path("M10 0 A10 10 0 0 1 0 10")));

    [Test]
    public async Task Radii_too_small_to_span_the_arc_are_scaled_up_until_they_do()
    {
        // Radius 1 cannot join points 10 apart; the arc becomes the half circle of radius 5, drawn
        // clockwise on a y-down page, so over the top.
        var figure = Path("M0 0 A1 1 0 0 1 10 0").Single();

        await Assert.That(figure.Segments.Count).IsEqualTo(2);
        await Assert.That(figure.Segments[0].X).IsEqualTo(5).Within(1e-9);
        await Assert.That(figure.Segments[0].Y).IsEqualTo(-5).Within(1e-9);
    }

    [Test]
    public async Task An_arc_with_a_zero_radius_is_a_line_and_one_to_its_own_start_is_nothing()
    {
        await Assert.That(Describe(Path("M0 0 A0 5 0 0 1 10 10"))).IsEqualTo("M0,0 L10,10");
        await Assert.That(Path("M3 3 A5 5 0 0 1 3 3").Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_rotated_elliptical_arc_ends_on_the_point_the_data_names()
    {
        var figure = Path("M0 0 a20 10 30 1 1 15 -5").Single();

        await Assert.That(figure.Segments.All(_ => _.IsCurve)).IsTrue();
        await Assert.That(figure.Segments[^1].X).IsEqualTo(15);
        await Assert.That(figure.Segments[^1].Y).IsEqualTo(-5);
    }

    [Test]
    public async Task Data_that_is_not_path_data_all_the_way_through_does_not_parse()
    {
        await Assert.That(Parses("")).IsTrue();
        await Assert.That(Parses("L10 10")).IsFalse();
        await Assert.That(Parses("M10")).IsFalse();
        await Assert.That(Parses("M0 0 X5 5")).IsFalse();
        await Assert.That(Parses("M0 0 L5 5 Z 7 7")).IsFalse();
        await Assert.That(Parses("M0 0 A5 5 0 2 1 10 10")).IsFalse();
    }
}
