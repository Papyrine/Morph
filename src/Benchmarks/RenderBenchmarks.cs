/// <summary>
/// End-to-end render benchmarks over the synthetic multi-page documents in
/// <see cref="BenchmarkDocs"/>, covering all three backends. These exist to make per-page /
/// per-occurrence render costs visible: header logo decode (raster and SVG), picture watermark
/// processing, repeated inline images, and TOC-style tab + hyperlink layout. The parse cost is
/// included (public-API shape), but it is small relative to the multi-page render work these
/// documents generate.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class RenderBenchmarks
{
    static string GetSourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(GetSourceDir(), "..", "Fonts"));

    static readonly ImageExportOptions imageOptions = new()
    {
        FontDirectory = fontsDirectory
    };

    static readonly PdfExportOptions pdfOptions = new()
    {
        FontDirectory = fontsDirectory
    };

    SkiaDocumentConverter skia = new();
    ImageSharpDocumentConverter imageSharp = new();

    byte[] headerLogo = [];
    byte[] headerLogoSvg = [];
    byte[] watermark = [];
    byte[] repeatedImage = [];
    byte[] toc = [];

    [GlobalSetup]
    public void Setup()
    {
        headerLogo = BenchmarkDocs.HeaderLogoRaster;
        headerLogoSvg = BenchmarkDocs.HeaderLogoSvg;
        watermark = BenchmarkDocs.PictureWatermark;
        repeatedImage = BenchmarkDocs.RepeatedImage;
        toc = BenchmarkDocs.Toc;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("HeaderLogo")]
    public IReadOnlyList<byte[]> Skia_HeaderLogo() => skia.ConvertToImageData(new MemoryStream(headerLogo), imageOptions);

    [Benchmark]
    [BenchmarkCategory("HeaderLogo")]
    public IReadOnlyList<byte[]> ImageSharp_HeaderLogo() => imageSharp.ConvertToImageData(new MemoryStream(headerLogo), imageOptions);

    [Benchmark]
    [BenchmarkCategory("HeaderLogo")]
    public byte[] Pdf_HeaderLogo() => PdfDocumentConverter.ConvertToPdf(new MemoryStream(headerLogo), pdfOptions);

    // SVG is a Skia-only feature (ImageSharp and PDF fall back to the PNG representation).
    [Benchmark]
    [BenchmarkCategory("HeaderLogoSvg")]
    public IReadOnlyList<byte[]> Skia_HeaderLogoSvg() => skia.ConvertToImageData(new MemoryStream(headerLogoSvg), imageOptions);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PictureWatermark")]
    public IReadOnlyList<byte[]> Skia_PictureWatermark() => skia.ConvertToImageData(new MemoryStream(watermark), imageOptions);

    [Benchmark]
    [BenchmarkCategory("PictureWatermark")]
    public IReadOnlyList<byte[]> ImageSharp_PictureWatermark() => imageSharp.ConvertToImageData(new MemoryStream(watermark), imageOptions);

    [Benchmark]
    [BenchmarkCategory("PictureWatermark")]
    public byte[] Pdf_PictureWatermark() => PdfDocumentConverter.ConvertToPdf(new MemoryStream(watermark), pdfOptions);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("RepeatedImage")]
    public IReadOnlyList<byte[]> Skia_RepeatedImage() => skia.ConvertToImageData(new MemoryStream(repeatedImage), imageOptions);

    [Benchmark]
    [BenchmarkCategory("RepeatedImage")]
    public IReadOnlyList<byte[]> ImageSharp_RepeatedImage() => imageSharp.ConvertToImageData(new MemoryStream(repeatedImage), imageOptions);

    [Benchmark]
    [BenchmarkCategory("RepeatedImage")]
    public byte[] Pdf_RepeatedImage() => PdfDocumentConverter.ConvertToPdf(new MemoryStream(repeatedImage), pdfOptions);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Toc")]
    public IReadOnlyList<byte[]> Skia_Toc() => skia.ConvertToImageData(new MemoryStream(toc), imageOptions);

    [Benchmark]
    [BenchmarkCategory("Toc")]
    public IReadOnlyList<byte[]> ImageSharp_Toc() => imageSharp.ConvertToImageData(new MemoryStream(toc), imageOptions);

    [Benchmark]
    [BenchmarkCategory("Toc")]
    public byte[] Pdf_Toc() => PdfDocumentConverter.ConvertToPdf(new MemoryStream(toc), pdfOptions);
}
