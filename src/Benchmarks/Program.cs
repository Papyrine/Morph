BenchmarkSwitcher.FromAssemblies([typeof(ConversionBenchmarks).Assembly]).Run(args);

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ConversionBenchmarks
{
    static string GetSourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
    static readonly string inputsDir = Path.GetFullPath(Path.Combine(GetSourceDir(), "..", "Tests", "Inputs"));

    // Small (~33KB) - simple resume
    static readonly string smallDoc = Path.Combine(inputsDir, "resumes", "01", "input.docx");
    // Medium (~92KB) - letter with images
    static readonly string mediumDoc = Path.Combine(inputsDir, "letters", "01", "input.docx");
    // Large (~5.5MB) - newsletter with many images
    static readonly string largeDoc = Path.Combine(inputsDir, "newsletters", "03", "input.docx");
    // Table-heavy: exercises bordered cells + ParseColor caching + HasVerticalMerge dedup
    static readonly string complexTablesDoc = Path.Combine(inputsDir, "complex_tables", "input.docx");
    // Long table that paginates row-by-row
    static readonly string tableMultipageDoc = Path.Combine(inputsDir, "table_multipage", "input.docx");
    // Vertical-merge table (RenderTableWithColumnTracking path)
    static readonly string tableVmergeDoc = Path.Combine(inputsDir, "table_vmerge_basic", "input.docx");
    // VML watermark in headers (exercises ParseTextWatermark style splitting)
    static readonly string watermarkDoc = Path.Combine(inputsDir, "business-plans", "04", "input.docx");

    SkiaDocumentConverter skia = new();
    ImageSharpDocumentConverter imageSharp = new();

    byte[] smallBytes = [];
    byte[] mediumBytes = [];
    byte[] largeBytes = [];
    byte[] complexTablesBytes = [];
    byte[] tableMultipageBytes = [];
    byte[] tableVmergeBytes = [];
    byte[] watermarkBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        smallBytes = File.ReadAllBytes(smallDoc);
        mediumBytes = File.ReadAllBytes(mediumDoc);
        largeBytes = File.ReadAllBytes(largeDoc);
        complexTablesBytes = File.ReadAllBytes(complexTablesDoc);
        tableMultipageBytes = File.ReadAllBytes(tableMultipageDoc);
        tableVmergeBytes = File.ReadAllBytes(tableVmergeDoc);
        watermarkBytes = File.ReadAllBytes(watermarkDoc);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Small")]
    public IReadOnlyList<byte[]> Skia_Small() => skia.ConvertToImageData(new MemoryStream(smallBytes));

    [Benchmark]
    [BenchmarkCategory("Small")]
    public IReadOnlyList<byte[]> ImageSharp_Small() => imageSharp.ConvertToImageData(new MemoryStream(smallBytes));

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Medium")]
    public IReadOnlyList<byte[]> Skia_Medium() => skia.ConvertToImageData(new MemoryStream(mediumBytes));

    [Benchmark]
    [BenchmarkCategory("Medium")]
    public IReadOnlyList<byte[]> ImageSharp_Medium() => imageSharp.ConvertToImageData(new MemoryStream(mediumBytes));

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Large")]
    public IReadOnlyList<byte[]> Skia_Large() => skia.ConvertToImageData(new MemoryStream(largeBytes));

    [Benchmark]
    [BenchmarkCategory("Large")]
    public IReadOnlyList<byte[]> ImageSharp_Large() => imageSharp.ConvertToImageData(new MemoryStream(largeBytes));

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ComplexTables")]
    public IReadOnlyList<byte[]> Skia_ComplexTables() => skia.ConvertToImageData(new MemoryStream(complexTablesBytes));

    [Benchmark]
    [BenchmarkCategory("ComplexTables")]
    public IReadOnlyList<byte[]> ImageSharp_ComplexTables() => imageSharp.ConvertToImageData(new MemoryStream(complexTablesBytes));

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("TableMultipage")]
    public IReadOnlyList<byte[]> Skia_TableMultipage() => skia.ConvertToImageData(new MemoryStream(tableMultipageBytes));

    [Benchmark]
    [BenchmarkCategory("TableMultipage")]
    public IReadOnlyList<byte[]> ImageSharp_TableMultipage() => imageSharp.ConvertToImageData(new MemoryStream(tableMultipageBytes));

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("TableVMerge")]
    public IReadOnlyList<byte[]> Skia_TableVMerge() => skia.ConvertToImageData(new MemoryStream(tableVmergeBytes));

    [Benchmark]
    [BenchmarkCategory("TableVMerge")]
    public IReadOnlyList<byte[]> ImageSharp_TableVMerge() => imageSharp.ConvertToImageData(new MemoryStream(tableVmergeBytes));

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Watermark")]
    public IReadOnlyList<byte[]> Skia_Watermark() => skia.ConvertToImageData(new MemoryStream(watermarkBytes));

    [Benchmark]
    [BenchmarkCategory("Watermark")]
    public IReadOnlyList<byte[]> ImageSharp_Watermark() => imageSharp.ConvertToImageData(new MemoryStream(watermarkBytes));
}

/// <summary>
/// Parse-only DOCX benchmarks. Isolates the OOXML parser from rendering so
/// allocation-sensitive parser changes (e.g. span-based splitting) are visible
/// in MemoryDiagnoser output instead of being dwarfed by render cost.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ParseBenchmarks
{
    static string GetSourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
    static readonly string inputsDir = Path.GetFullPath(Path.Combine(GetSourceDir(), "..", "Tests", "Inputs"));

    static readonly string smallDoc = Path.Combine(inputsDir, "resumes", "01", "input.docx");
    static readonly string complexTablesDoc = Path.Combine(inputsDir, "complex_tables", "input.docx");
    static readonly string watermarkDoc = Path.Combine(inputsDir, "business-plans", "04", "input.docx");
    static readonly string largeDoc = Path.Combine(inputsDir, "newsletters", "03", "input.docx");

    byte[] smallBytes = [];
    byte[] complexTablesBytes = [];
    byte[] watermarkBytes = [];
    byte[] largeBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        smallBytes = File.ReadAllBytes(smallDoc);
        complexTablesBytes = File.ReadAllBytes(complexTablesDoc);
        watermarkBytes = File.ReadAllBytes(watermarkDoc);
        largeBytes = File.ReadAllBytes(largeDoc);
    }

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public object Parse_Small() => new DocumentParser().Parse(new MemoryStream(smallBytes));

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public object Parse_ComplexTables() => new DocumentParser().Parse(new MemoryStream(complexTablesBytes));

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public object Parse_Watermark() => new DocumentParser().Parse(new MemoryStream(watermarkBytes));

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public object Parse_Large() => new DocumentParser().Parse(new MemoryStream(largeBytes));
}

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

/// <summary>
/// InkML trace-point parser benchmarks. No DOCX input in the corpus contains
/// ink, so we feed synthetic trace strings of representative shapes directly to
/// <see cref="InkParser.ParseTracePoints"/> — the method whose alloc footprint
/// the recent span-split refactor reduced.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class InkBenchmarks
{
    string shortStroke = "";
    string longStroke = "";
    string mixedStrokes = "";

    [GlobalSetup]
    public void Setup()
    {
        shortStroke = BuildTrace(pointCount: 20, includeRelativePrefix: false);
        longStroke = BuildTrace(pointCount: 5_000, includeRelativePrefix: false);
        mixedStrokes = BuildTrace(pointCount: 5_000, includeRelativePrefix: true);
    }

    static string BuildTrace(int pointCount, bool includeRelativePrefix)
    {
        var builder = new StringBuilder(pointCount * 12);
        for (var i = 0; i < pointCount; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            if (includeRelativePrefix && i > 0 && i % 4 == 0)
            {
                builder.Append('\'');
            }

            builder.Append(i * 7).Append(' ').Append(i * 11);
        }
        return builder.ToString();
    }

    [Benchmark]
    [BenchmarkCategory("Ink")]
    public object Parse_ShortStroke() => InkParser.ParseTracePoints(shortStroke);

    [Benchmark]
    [BenchmarkCategory("Ink")]
    public object Parse_LongStroke() => InkParser.ParseTracePoints(longStroke);

    [Benchmark]
    [BenchmarkCategory("Ink")]
    public object Parse_MixedStrokes() => InkParser.ParseTracePoints(mixedStrokes);
}
