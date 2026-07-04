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
    byte[] tocBytes = [];
    byte[] repeatedImageBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        smallBytes = File.ReadAllBytes(smallDoc);
        complexTablesBytes = File.ReadAllBytes(complexTablesDoc);
        watermarkBytes = File.ReadAllBytes(watermarkDoc);
        largeBytes = File.ReadAllBytes(largeDoc);
        tocBytes = BenchmarkDocs.Toc;
        repeatedImageBytes = BenchmarkDocs.RepeatedImage;
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

    // Hyperlink/style-heavy synthetic document: 300 hyperlinked runs carrying rStyle against a
    // 200-style styles.xml — exercises the per-run style lookup and per-link relationship
    // resolution paths.
    [Benchmark]
    [BenchmarkCategory("Parse")]
    public object Parse_Toc() => new DocumentParser().Parse(new MemoryStream(tocBytes));

    // 60 inline drawings all referencing the same image part — exercises per-reference image
    // part buffering.
    [Benchmark]
    [BenchmarkCategory("Parse")]
    public object Parse_RepeatedImage() => new DocumentParser().Parse(new MemoryStream(repeatedImageBytes));
}
