if (args is ["smoke"])
{
    BenchmarkSmoke.Run();
    return;
}

BenchmarkSwitcher.FromAssemblies([typeof(ConversionBenchmarks).Assembly]).Run(args);

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ConversionBenchmarks
{
    static string GetSourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
    static readonly string inputsDir = Path.GetFullPath(Path.Combine(GetSourceDir(), "..", "Tests", "Inputs", "word"));

    // Small (~33KB) - simple resume
    static readonly string smallDoc = Path.Combine(inputsDir, "resumes", "01", "input.docx");
    // Medium (~92KB) - letter with images
    static readonly string mediumDoc = Path.Combine(inputsDir, "letters", "01", "input.docx");
    // Large (~5.5MB) - newsletter with many images
    static readonly string largeDoc = Path.Combine(inputsDir, "newsletters", "03", "input.docx");
    // Table-heavy: exercises bordered cells + ParseColor caching
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
