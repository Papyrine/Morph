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
            seed: FontResolver<FontMetrics>.BuildBundledSeed(LoadEmbeddedFace));

    // An embedded face has no path to find its .wordadvances sidecars beside, so they come from the
    // assembly too. The seed answers before the FontDirectory does, so without this every document
    // set in Aptos — the modern Word default — measured on the linear fallback even with the
    // directory copy of the face carrying Word's advances.
    static FontMetrics LoadEmbeddedFace(string faceName, byte[] bytes) =>
        FontMetricsReader.WithWordAdvances(
            FontMetricsReader.Read(new MemoryStream(bytes))!,
            EmbeddedFonts.WordAdvances(faceName, ".wordadvances"),
            EmbeddedFonts.WordAdvances(faceName, ".wordadvances15"),
            faceName);

    /// <summary>
    /// Adapts <see cref="FontResolver{TFont}.Resolve"/> to the measurer's contract. A family the resolver
    /// cannot serve (no bundled face, no FontFallbacks alias) retries as
    /// <see cref="DefaultFontSettings.DefaultFont"/> — the same last resort
    /// <c>PdfFontResolver.ResolveTypeface</c> and the raster backends apply, so the measurer sizes the
    /// line the painter will draw. Without it an unresolvable mark font measured a ZERO-height line:
    /// brochures/03's first paragraph (mark font Biome, unbundled) collapsed and started the full-page
    /// table 16pt high. The measurer reads a null metric as an unresolved font (zero width), so only the
    /// terminal case — even DefaultFont fails — is translated into null.
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
            }

            try
            {
                return resolver.Resolve(DefaultFontSettings.DefaultFont, bold, italic);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        };
}
