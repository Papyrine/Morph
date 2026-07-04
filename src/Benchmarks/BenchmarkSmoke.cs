/// <summary>
/// Sanity harness for the synthetic benchmark documents (run with <c>dotnet run -- smoke</c>):
/// converts each through all three backends, prints page counts, and dumps first-page output
/// next to the generated docx under %TEMP%/morph-benchmark-smoke for visual inspection. Keeps
/// the benchmark inputs honest — a document that silently parses to an empty body would
/// otherwise still "benchmark" fine.
/// </summary>
static class BenchmarkSmoke
{
    public static void Run()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "morph-benchmark-smoke");
        Directory.CreateDirectory(outputDir);

        var fontsDirectory = Path.GetFullPath(Path.Combine(SourceDir(), "..", "Fonts"));
        var imageOptions = new ImageExportOptions
        {
            FontDirectory = fontsDirectory
        };
        var pdfOptions = new PdfExportOptions
        {
            FontDirectory = fontsDirectory
        };

        (string Name, byte[] Content)[] documents =
        [
            ("header-logo", BenchmarkDocs.HeaderLogoRaster),
            ("header-logo-svg", BenchmarkDocs.HeaderLogoSvg),
            ("picture-watermark", BenchmarkDocs.PictureWatermark),
            ("repeated-image", BenchmarkDocs.RepeatedImage),
            ("toc", BenchmarkDocs.Toc)
        ];

        var skia = new SkiaDocumentConverter();
        var imageSharp = new ImageSharpDocumentConverter();

        foreach (var (name, content) in documents)
        {
            File.WriteAllBytes(Path.Combine(outputDir, $"{name}.docx"), content);

            var parsed = new DocumentParser().Parse(new MemoryStream(content));
            Console.WriteLine($"{name}: watermarks={parsed.Watermarks.Count} header={parsed.Header != null} elements={parsed.Elements.Count}");


            var skiaPages = skia.ConvertToImageData(new MemoryStream(content), imageOptions);
            var imageSharpPages = imageSharp.ConvertToImageData(new MemoryStream(content), imageOptions);
            var pdf = PdfDocumentConverter.ConvertToPdf(new MemoryStream(content), pdfOptions);

            File.WriteAllBytes(Path.Combine(outputDir, $"{name}-skia-p1.png"), skiaPages[0]);
            File.WriteAllBytes(Path.Combine(outputDir, $"{name}-imagesharp-p1.png"), imageSharpPages[0]);
            File.WriteAllBytes(Path.Combine(outputDir, $"{name}.pdf"), pdf);

            Console.WriteLine($"{name}: skia={skiaPages.Count} pages, imageSharp={imageSharpPages.Count} pages, pdf={pdf.Length / 1024} KB, docx={content.Length / 1024} KB");
        }

        Console.WriteLine($"Output written to {outputDir}");
    }

    static string SourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
