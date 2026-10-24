/// <summary>
/// An affine transform as SVG writes one, <c>matrix(a b c d e f)</c>: a point (x, y) goes to
/// (a·x + c·y + e, b·x + d·y + f). The reader multiplies these down the tree and applies the
/// product to every point it emits, which is what lets an <see cref="SvgDrawing"/> be a flat list
/// with no transforms left in it.
/// </summary>
readonly record struct SvgMatrix(double A, double B, double C, double D, double E, double F)
{
    public static SvgMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public static SvgMatrix Translation(double x, double y) =>
        new(1, 0, 0, 1, x, y);

    public (double X, double Y) Apply(double x, double y) =>
        (A * x + C * y + E, B * x + D * y + F);

    /// <summary>
    /// This transform with <paramref name="inner"/> applied before it — the product a child's own
    /// <c>transform</c> makes with everything above it.
    /// </summary>
    public SvgMatrix Multiply(SvgMatrix inner) =>
        new(
            A * inner.A + C * inner.B,
            B * inner.A + D * inner.B,
            A * inner.C + C * inner.D,
            B * inner.C + D * inner.D,
            A * inner.E + C * inner.F + E,
            B * inner.E + D * inner.F + F);

    /// <summary>
    /// How much the transform scales a length, as the square root of the area it scales by. Exact
    /// for a transform that scales alike in every direction, which is the only kind a stroke width
    /// is put through (see <see cref="IsUniform"/>).
    /// </summary>
    public double Scale => Math.Sqrt(Math.Abs(A * D - B * C));

    /// <summary>
    /// True when the transform scales alike in every direction — any mix of translation, rotation,
    /// mirroring and one scale. Only then does a stroke stay a stroke of one width; a squash or a
    /// skew makes its width vary with direction, which a path and a width cannot say. One percent
    /// is allowed for, since Office writes a scale of <c>1.00001</c> beside a plain <c>1</c>.
    /// </summary>
    public bool IsUniform
    {
        get
        {
            var first = Math.Sqrt(A * A + B * B);
            var second = Math.Sqrt(C * C + D * D);
            var longest = Math.Max(first, second);
            if (longest == 0)
            {
                return true;
            }

            return Math.Abs(first - second) <= 0.01 * longest &&
                   Math.Abs(A * C + B * D) <= 0.01 * longest * longest;
        }
    }

    /// <summary>
    /// Reads a <c>transform</c> attribute: any run of <c>matrix</c>, <c>translate</c>,
    /// <c>scale</c>, <c>rotate</c>, <c>skewX</c> and <c>skewY</c>, the leftmost outermost. False for
    /// anything else, or for a function given the wrong number of values.
    /// </summary>
    public static bool TryParse(string text, out SvgMatrix matrix)
    {
        matrix = Identity;
        var span = text.AsSpan();
        var position = 0;
        SvgNumbers.SkipSeparator(span, ref position);
        while (position < span.Length)
        {
            var nameStart = position;
            while (position < span.Length &&
                   char.IsAsciiLetter(span[position]))
            {
                position++;
            }

            var name = span[nameStart..position];
            SvgNumbers.SkipSpace(span, ref position);
            if (position >= span.Length || span[position] != '(')
            {
                return false;
            }

            var close = span[position..].IndexOf(')');
            if (close < 0)
            {
                return false;
            }

            var values = SvgNumbers.ReadList(span.Slice(position + 1, close - 1));
            if (values == null || !TryBuild(name, values, out var step))
            {
                return false;
            }

            matrix = matrix.Multiply(step);
            position += close + 1;
            SvgNumbers.SkipSeparator(span, ref position);
        }

        return true;
    }

    static bool TryBuild(CharSpan name, List<double> values, out SvgMatrix matrix)
    {
        matrix = Identity;
        switch (name)
        {
            case "matrix" when values.Count == 6:
                matrix = new(values[0], values[1], values[2], values[3], values[4], values[5]);
                return true;
            case "translate" when values.Count is 1 or 2:
                matrix = Translation(values[0], values.Count == 2 ? values[1] : 0);
                return true;
            case "scale" when values.Count is 1 or 2:
                matrix = new(values[0], 0, 0, values.Count == 2 ? values[1] : values[0], 0, 0);
                return true;
            case "rotate" when values.Count is 1 or 3:
            {
                var radians = values[0] * Math.PI / 180;
                var turn = new SvgMatrix(Math.Cos(radians), Math.Sin(radians), -Math.Sin(radians), Math.Cos(radians), 0, 0);
                matrix = values.Count == 3
                    ? Translation(values[1], values[2]).Multiply(turn).Multiply(Translation(-values[1], -values[2]))
                    : turn;
                return true;
            }
            case "skewX" when values.Count == 1:
                matrix = new(1, 0, Math.Tan(values[0] * Math.PI / 180), 1, 0, 0);
                return true;
            case "skewY" when values.Count == 1:
                matrix = new(1, Math.Tan(values[0] * Math.PI / 180), 0, 1, 0, 0);
                return true;
            default:
                return false;
        }
    }
}
