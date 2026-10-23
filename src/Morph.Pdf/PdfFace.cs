/// <summary>
/// A face as the PDF painter draws it: the Krilla font that is embedded, and the OpenType metrics of the
/// same file, which give each character its glyph and advance. The two are read from the same bytes, so
/// a glyph is drawn at exactly the advance the layout engine measured it at.
/// </summary>
sealed class PdfFace(Font font, FontMetrics metrics, bool italic, string? family, string? source = null) :
    IDisposable
{
    public Font Font { get; } = font;

    public FontMetrics Metrics { get; } = metrics;

    /// <summary>Whether the face is itself an italic one. An italic run in a face that is not is sheared.</summary>
    public bool Italic { get; } = italic;

    /// <summary>The family the face declares, for diagnostics and tests.</summary>
    public string? Family { get; } = family;

    /// <summary>
    /// The file the face was read from — <c>path|index</c> for a member of a collection — or null for a
    /// bundled face, which is read from the assembly.
    /// </summary>
    public string? Source { get; } = source;

    public static PdfFace? Load(FontFace face)
    {
        var metrics = FontMetricsReader.Read(face.Path, face.Index);
        if (metrics == null)
        {
            return null;
        }

        var source = face.Path.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)
            ? $"{face.Path}|{face.Index}"
            : face.Path;
        return new(Font.LoadFile(face.Path, (uint) face.Index), metrics, face.Italic, face.Family, source);
    }

    public static PdfFace Load(byte[] bytes)
    {
        var metrics = FontMetricsReader.Read(new MemoryStream(bytes))!;
        // A subset (the bullets face) may carry too little for the reader to describe; it is upright.
        var face = OpenTypeReader.ReadFaces(new MemoryStream(bytes), "").Select(_ => _.Face).FirstOrDefault();
        return new(Font.Load(bytes), metrics, face?.Italic ?? false, face?.Family);
    }

    public void Dispose() =>
        Font.Dispose();
}
