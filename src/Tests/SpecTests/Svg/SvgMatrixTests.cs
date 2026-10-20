/// <summary>
/// The <c>transform</c> attribute as <see cref="SvgMatrix"/> reads it, and the two questions the
/// reader asks of a transform before it puts a stroke through one.
/// </summary>
public class SvgMatrixTests
{
    static SvgMatrix Parse(string text)
    {
        if (!SvgMatrix.TryParse(text, out var matrix))
        {
            throw new($"Transform did not parse: {text}");
        }

        return matrix;
    }

    static async Task Maps(string transform, double x, double y, double expectedX, double expectedY)
    {
        var (mappedX, mappedY) = Parse(transform).Apply(x, y);
        await Assert.That(mappedX).IsEqualTo(expectedX).Within(1e-9);
        await Assert.That(mappedY).IsEqualTo(expectedY).Within(1e-9);
    }

    [Test]
    public async Task Each_function_maps_a_point_as_SVG_defines_it()
    {
        await Maps("translate(10)", 1, 1, 11, 1);
        await Maps("translate(10, 20)", 1, 1, 11, 21);
        await Maps("scale(2)", 3, 4, 6, 8);
        await Maps("scale(2 3)", 3, 4, 6, 12);
        await Maps("rotate(90)", 1, 0, 0, 1);
        await Maps("rotate(90, 10, 10)", 10, 0, 20, 10);
        await Maps("skewX(45)", 0, 1, 1, 1);
        await Maps("skewY(45)", 1, 0, 1, 1);
        await Maps("matrix(1 2 3 4 5 6)", 1, 1, 9, 12);
    }

    [Test]
    public async Task A_list_applies_its_rightmost_function_first()
    {
        await Maps("translate(10,0) scale(2)", 1, 1, 12, 2);
        await Maps("scale(2) translate(10,0)", 1, 1, 22, 2);
        await Maps("translate(10,20)rotate(90)", 1, 0, 10, 21);
        await Maps("", 3, 4, 3, 4);
    }

    // The quarter turn minimalist-light-sales-pitch puts on a clip rectangle.
    [Test]
    public Task Office_writes_matrices_with_exponents_and_they_read() =>
        Maps("matrix(-1.83697e-16 1 -1 -1.83697e-16 993.878 235.92)", 100, 0, 993.878, 335.92);

    [Test]
    public async Task Anything_else_does_not_parse()
    {
        await Assert.That(SvgMatrix.TryParse("spin(3)", out _)).IsFalse();
        await Assert.That(SvgMatrix.TryParse("translate(a)", out _)).IsFalse();
        await Assert.That(SvgMatrix.TryParse("translate(1,2,3)", out _)).IsFalse();
        await Assert.That(SvgMatrix.TryParse("matrix(1 2 3)", out _)).IsFalse();
        await Assert.That(SvgMatrix.TryParse("scale(2", out _)).IsFalse();
    }

    [Test]
    public async Task A_stroke_keeps_one_width_only_under_a_transform_that_scales_alike_every_way()
    {
        await Assert.That(Parse("translate(5,5) rotate(30) scale(2)").IsUniform).IsTrue();
        await Assert.That(Parse("scale(-1,1)").IsUniform).IsTrue();

        // Office's own rounding noise beside a plain 1 still counts as alike.
        await Assert.That(Parse("matrix(1.00001 0 0 1 732.109 476.143)").IsUniform).IsTrue();

        await Assert.That(Parse("matrix(1 0 0 1.05042 110 840.19)").IsUniform).IsFalse();
        await Assert.That(Parse("skewX(10)").IsUniform).IsFalse();
    }

    [Test]
    public async Task The_scale_of_a_transform_is_what_it_does_to_a_length()
    {
        await Assert.That(Parse("scale(2)").Scale).IsEqualTo(2d).Within(1e-9);
        await Assert.That(Parse("rotate(40) scale(-3,3)").Scale).IsEqualTo(3d).Within(1e-9);
    }
}
