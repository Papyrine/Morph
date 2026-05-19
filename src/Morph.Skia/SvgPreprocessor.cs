/// <summary>
/// Pre-processes raw SVG markup before handing it to Svg.Skia: strips embedded
/// CSS (style elements interfere with fill processing) and class attributes
/// (Svg.Skia ignores class-based styling).
/// </summary>
static partial class SvgPreprocessor
{
    [GeneratedRegex("<style[^>]*>.*?</style>", RegexOptions.Singleline)]
    private static partial Regex StyleElement();

    [GeneratedRegex("\\s+class=\"[^\"]*\"")]
    private static partial Regex ClassAttribute();

    public static byte[] StripStyleAndClass(byte[] svgData)
    {
        var svgContent = Encoding.UTF8.GetString(svgData);
        svgContent = StyleElement().Replace(svgContent, "");
        svgContent = ClassAttribute().Replace(svgContent, "");
        return Encoding.UTF8.GetBytes(svgContent);
    }
}
