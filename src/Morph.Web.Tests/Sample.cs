static class Sample
{
    // The bundled sample files — one per readable format — copied next to the test assembly by the
    // csproj. The DOCX (an agenda/minutes document) is two pages, so it exercises the multi-page PNG
    // (zip) path as well as the single-file formats; the XLSX (an invoice worksheet) is one page, which
    // covers the single-file PNG branch; the PPTX (a two-slide brochure) covers the slide path.
    public static byte[] DocxBytes { get; } = Read("sample.docx");

    public static byte[] XlsxBytes { get; } = Read("sample.xlsx");

    public static byte[] PptxBytes { get; } = Read("sample.pptx");

    /// <summary>The bytes of the bundled sample for a readable format.</summary>
    public static byte[] BytesFor(InputFormat format) =>
        format switch
        {
            InputFormat.Docx => DocxBytes,
            InputFormat.Xlsx => XlsxBytes,
            InputFormat.Pptx => PptxBytes,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "No sample for this format.")
        };

    /// <summary>Every readable format, for parameterising a test across all three inputs.</summary>
    public static IEnumerable<InputFormat> Formats() =>
        ConversionService.ReadableFormats.Select(_ => _.Format);

    // The Aptos faces, copied alongside the tests. The PDF export resolves fonts against a directory
    // (PdfSharp can't read Morph's embedded fonts), so the service tests point at this one.
    public static string FontDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "fonts");

    static byte[] Read(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name));
}
