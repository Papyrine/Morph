/// <summary>
/// Makes an SVG picture fill whatever box it is drawn in, as an Office picture does.
///
/// An Office picture is stretched to its frame (<c>a:stretch</c>), but an <c>&lt;img&gt;</c> of an
/// SVG keeps the aspect of the SVG's viewBox and centres it: Basic.pptx's closing-tips frame is two
/// 361x1085 SVG bars stretched to 35x540pt and 35x929pt, and each drew as a short block in the middle
/// of where it belonged. <c>preserveAspectRatio="none"</c> on the root makes the SVG fill the box as
/// the painters draw it. An SVG with no viewBox has nothing to stretch, so its own width and height
/// become one.
/// </summary>
static partial class SvgStretch
{
    /// <summary>The SVG made to fill its box, or null when its root cannot be read.</summary>
    public static byte[]? Apply(byte[] data)
    {
        var svg = Encoding.UTF8.GetString(data);
        var root = RootPattern.Match(svg);
        if (!root.Success)
        {
            return null;
        }

        var tag = PreserveAspectPattern.Replace(root.Value, "");
        if (!ViewBoxPattern.IsMatch(tag))
        {
            if (Length(WidthPattern.Match(tag)) is not { } width ||
                Length(HeightPattern.Match(tag)) is not { } height)
            {
                return null;
            }

            tag = tag.Insert(4, $" viewBox=\"0 0 {Number(width)} {Number(height)}\"");
        }

        // Inserted straight after "<svg".
        tag = tag.Insert(4, " preserveAspectRatio=\"none\"");
        return Encoding.UTF8.GetBytes(string.Concat(svg.AsSpan(0, root.Index), tag, svg.AsSpan(root.Index + root.Length)));
    }

    static double? Length(Match match)
    {
        if (match.Success &&
            double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            value > 0)
        {
            return value;
        }

        return null;
    }

    static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"<svg\b[^>]*>")]
    private static partial Regex RootPattern { get; }

    [GeneratedRegex("""\s+preserveAspectRatio\s*=\s*("[^"]*"|'[^']*')""")]
    private static partial Regex PreserveAspectPattern { get; }

    [GeneratedRegex(@"\sviewBox\s*=")]
    private static partial Regex ViewBoxPattern { get; }

    [GeneratedRegex("""\swidth\s*=\s*["']\s*([\d.]+)(?:px)?\s*["']""")]
    private static partial Regex WidthPattern { get; }

    [GeneratedRegex("""\sheight\s*=\s*["']\s*([\d.]+)(?:px)?\s*["']""")]
    private static partial Regex HeightPattern { get; }
}
