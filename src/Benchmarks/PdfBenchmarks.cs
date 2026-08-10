/// <summary>
/// PDF exporter benchmarks over real corpus documents. The PDF backend was previously
/// unmeasured; these cover the paragraph-layout path (no layout cache as of writing —
/// table-heavy docs lay each cell out ~5×), image embedding, the deterministic-output
/// Normalize pass, and the Pages-range trimming behaviour.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class PdfBenchmarks
{
    static string GetSourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
    static readonly string inputsDir = Path.GetFullPath(Path.Combine(GetSourceDir(), "..", "Tests", "Inputs", "word"));
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(GetSourceDir(), "..", "Fonts"));

    static readonly PdfExportOptions options = new()
    {
        FontDirectory = fontsDirectory
    };

    static readonly PdfExportOptions firstPageOnly = new()
    {
        FontDirectory = fontsDirectory,
        Pages = PageRange.Single(1)
    };

    byte[] smallBytes = [];
    byte[] complexTablesBytes = [];
    byte[] tableMultipageBytes = [];
    byte[] largeBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        smallBytes = File.ReadAllBytes(Path.Combine(inputsDir, "resumes", "01", "input.docx"));
        complexTablesBytes = File.ReadAllBytes(Path.Combine(inputsDir, "complex_tables", "input.docx"));
        tableMultipageBytes = File.ReadAllBytes(Path.Combine(inputsDir, "table_multipage", "input.docx"));
        largeBytes = File.ReadAllBytes(Path.Combine(inputsDir, "newsletters", "03", "input.docx"));
    }

    [Benchmark]
    [BenchmarkCategory("Pdf")]
    public byte[] Pdf_Small() => PdfDocumentConverter.ConvertToPdf(new MemoryStream(smallBytes), options);

    [Benchmark]
    [BenchmarkCategory("Pdf")]
    public byte[] Pdf_ComplexTables() => PdfDocumentConverter.ConvertToPdf(new MemoryStream(complexTablesBytes), options);

    [Benchmark]
    [BenchmarkCategory("Pdf")]
    public byte[] Pdf_TableMultipage() => PdfDocumentConverter.ConvertToPdf(new MemoryStream(tableMultipageBytes), options);

    [Benchmark]
    [BenchmarkCategory("Pdf")]
    public byte[] Pdf_Large() => PdfDocumentConverter.ConvertToPdf(new MemoryStream(largeBytes), options);

    // Page-range export: measures how much work rendering page 1 of a multi-page document
    // avoids (today: none — the whole document renders, then pages are deleted).
    [Benchmark]
    [BenchmarkCategory("PdfPageRange")]
    public byte[] Pdf_TableMultipage_FirstPageOnly() => PdfDocumentConverter.ConvertToPdf(new MemoryStream(tableMultipageBytes), firstPageOnly);
}
