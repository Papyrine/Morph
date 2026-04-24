using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(ConversionBenchmarks).Assembly).Run(args);

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ConversionBenchmarks
{
    static string GetSourceDir([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
    static readonly string inputsDir = Path.GetFullPath(Path.Combine(GetSourceDir(), "..", "Tests", "Inputs"));

    // Small (~33KB) - simple resume
    static readonly string smallDoc = Path.Combine(inputsDir, "resumes", "01", "input.docx");
    // Medium (~92KB) - letter with images
    static readonly string mediumDoc = Path.Combine(inputsDir, "letters", "01", "input.docx");
    // Large (~5.5MB) - newsletter with many images
    static readonly string largeDoc = Path.Combine(inputsDir, "newsletters", "03", "input.docx");

    readonly WordRender.Skia.ImageSharpDocumentConverter skia = new();
    readonly WordRender.ImageSharp.ImageSharpDocumentConverter imageSharp = new();

    byte[] smallBytes = [];
    byte[] mediumBytes = [];
    byte[] largeBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        smallBytes = File.ReadAllBytes(smallDoc);
        mediumBytes = File.ReadAllBytes(mediumDoc);
        largeBytes = File.ReadAllBytes(largeDoc);
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
}
