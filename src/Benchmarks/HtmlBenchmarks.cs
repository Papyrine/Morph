/// <summary>
/// HTML pipeline benchmarks. The HtmlConverter path was previously unmeasured;
/// these scenarios exercise the AngleSharp parser plus our HTML-to-DocumentElement
/// translation (rgb() colors, CSS border shorthand, inline styles).
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class HtmlBenchmarks
{
    SkiaHtmlConverter skia = new();
    ImageSharpHtmlConverter imageSharp = new();

    string smallHtml = "";
    string styledHtml = "";
    string largeHtml = "";

    [GlobalSetup]
    public void Setup()
    {
        smallHtml = BuildSmallHtml();
        styledHtml = BuildStyledHtml();
        largeHtml = BuildLargeHtml();
    }

    static string BuildSmallHtml() =>
        "<html><body>" +
        "<h1>Heading</h1>" +
        "<p>A short paragraph with <b>bold</b> and <i>italic</i> text.</p>" +
        "<ul><li>One</li><li>Two</li><li>Three</li></ul>" +
        "</body></html>";

    // Exercises the rgb()/hex/named color parsers and CSS border shorthand
    // — the paths the recent span-split refactor touched.
    static string BuildStyledHtml()
    {
        var builder = new StringBuilder();
        builder.Append("<html><body>");
        builder.Append("<h2 style=\"color: rgb(10, 20, 30);\">Styled section</h2>");
        builder.Append("<table style=\"border: 1px solid #336699;\">");
        for (var row = 0; row < 8; row++)
        {
            builder.Append("<tr>");
            for (var col = 0; col < 5; col++)
            {
                builder.Append("<td style=\"border: 0.75pt dashed rgb(200, 100, 50); padding: 4px;\">");
                builder.Append("Cell ").Append(row).Append(',').Append(col);
                builder.Append("</td>");
            }
            builder.Append("</tr>");
        }
        builder.Append("</table>");
        builder.Append("</body></html>");
        return builder.ToString();
    }

    static string BuildLargeHtml()
    {
        var builder = new StringBuilder();
        builder.Append("<html><body>");
        for (var section = 0; section < 20; section++)
        {
            builder.Append("<h2>Section ").Append(section).Append("</h2>");
            for (var paragraph = 0; paragraph < 6; paragraph++)
            {
                builder.Append("<p style=\"color: #444444;\">Paragraph ")
                    .Append(paragraph)
                    .Append(" of section ")
                    .Append(section)
                    .Append(" — lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>");
            }
            builder.Append("<ul>");
            for (var item = 0; item < 4; item++)
            {
                builder.Append("<li>Item ").Append(item).Append("</li>");
            }
            builder.Append("</ul>");
        }
        builder.Append("</body></html>");
        return builder.ToString();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("HtmlSmall")]
    public Task<IReadOnlyList<byte[]>> Skia_HtmlSmall() => skia.ConvertToImageData(smallHtml);

    [Benchmark]
    [BenchmarkCategory("HtmlSmall")]
    public Task<IReadOnlyList<byte[]>> ImageSharp_HtmlSmall() => imageSharp.ConvertToImageData(smallHtml);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("HtmlStyled")]
    public Task<IReadOnlyList<byte[]>> Skia_HtmlStyled() => skia.ConvertToImageData(styledHtml);

    [Benchmark]
    [BenchmarkCategory("HtmlStyled")]
    public Task<IReadOnlyList<byte[]>> ImageSharp_HtmlStyled() => imageSharp.ConvertToImageData(styledHtml);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("HtmlLarge")]
    public Task<IReadOnlyList<byte[]>> Skia_HtmlLarge() => skia.ConvertToImageData(largeHtml);

    [Benchmark]
    [BenchmarkCategory("HtmlLarge")]
    public Task<IReadOnlyList<byte[]>> ImageSharp_HtmlLarge() => imageSharp.ConvertToImageData(largeHtml);
}
