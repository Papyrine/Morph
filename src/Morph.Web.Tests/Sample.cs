static class Sample
{
    // The bundled cover-letter DOCX, copied next to the test assembly by the csproj. Two pages, so it
    // exercises the multi-page PNG (zip) path as well as the single-file formats.
    public static byte[] DocxBytes { get; } = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "sample.docx"));

    // The Aptos faces, copied alongside the tests. The PDF export resolves fonts against a directory
    // (PdfSharp can't read Morph's embedded fonts), so the service tests point at this one.
    public static string FontDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "fonts");
}
