/// <summary>
/// Builds the layout engine's font-metrics resolver and paragraph measurer from a conversion's font
/// settings — the production analogue of the tests' <c>LayoutTestFonts</c>. The measurer reads OpenType
/// metrics (<see cref="FontMetrics"/>) so the <see cref="Fragmenter"/> wraps and paginates without a
/// backend font library; the same <c>FontDirectory</c> then feeds the painter's PdfSharp resolver, so the
/// measure pass and the paint pass pick the same face. Directory + bundled seed + the user
/// <c>FontFallback</c> alias + DefaultFont last resort mirror the shared <see cref="FontResolver{TFont}"/>
/// the raster backends use. An OS-level system fallback for a metrics face is not wired yet — a directory
/// or bundled miss falls through to DefaultFont.
/// </summary>
static class LayoutFonts
{
    public static FontResolver<FontMetrics> CreateResolver(string? fontDirectory, Func<string, string?>? fontFallback) =>
        new(
            loadFace: (face, _) => FontMetricsReader.Read(face.Path, face.Index),
            systemFallback: null,
            releaseFont: null,
            fontDirectory: fontDirectory,
            fontFallback: fontFallback,
            seed: FontResolver<FontMetrics>.BuildBundledSeed(bytes => FontMetricsReader.Read(new MemoryStream(bytes))!));

    /// <summary>
    /// Adapts <see cref="FontResolver{TFont}.Resolve"/> to the measurer's contract. The measurer reads a
    /// null metric as an unresolved font (zero width); the resolver instead throws once even DefaultFont
    /// fails, so that terminal case is translated into the null the measurer expects.
    /// </summary>
    public static Func<string, bool, bool, FontMetrics?> ToDelegate(FontResolver<FontMetrics> resolver) =>
        (family, bold, italic) =>
        {
            try
            {
                return resolver.Resolve(family, bold, italic);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        };
}
