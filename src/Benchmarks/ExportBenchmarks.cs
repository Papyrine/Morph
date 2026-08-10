/// <summary>
/// Text-exporter benchmarks (HTML / Markdown) isolated from parsing: the document is parsed once
/// in setup via <see cref="WordDocument"/> and only the export is measured. Covers run
/// coalescing, per-run tag assembly, and the base64 data-URI path for embedded images.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ExportBenchmarks
{
    static string GetSourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
    static readonly string inputsDir = Path.GetFullPath(Path.Combine(GetSourceDir(), "..", "Tests", "Inputs", "word"));

    WordDocument medium = null!;
    WordDocument large = null!;
    WordDocument toc = null!;

    [GlobalSetup]
    public void Setup()
    {
        medium = new(Path.Combine(inputsDir, "letters", "01", "input.docx"));
        large = new(Path.Combine(inputsDir, "newsletters", "03", "input.docx"));
        toc = new(new MemoryStream(BenchmarkDocs.Toc));
    }

    [Benchmark]
    [BenchmarkCategory("Html")]
    public string Html_Medium() => medium.ExportToHtml();

    [Benchmark]
    [BenchmarkCategory("Html")]
    public string Html_Large() => large.ExportToHtml();

    [Benchmark]
    [BenchmarkCategory("Html")]
    public string Html_Toc() => toc.ExportToHtml();

    [Benchmark]
    [BenchmarkCategory("Markdown")]
    public string Markdown_Medium() => medium.ExportToMarkdown();

    [Benchmark]
    [BenchmarkCategory("Markdown")]
    public string Markdown_Large() => large.ExportToMarkdown();

    [Benchmark]
    [BenchmarkCategory("Markdown")]
    public string Markdown_Toc() => toc.ExportToMarkdown();
}
