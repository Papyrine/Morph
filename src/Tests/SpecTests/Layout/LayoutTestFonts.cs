/// <summary>
/// Shared font resolution for the layout-engine tests: indexes the bundled <c>src/Fonts</c> directory
/// and resolves a family + style to <see cref="FontMetrics"/> the same way the renderers pick a face,
/// plus a ready <see cref="CanonicalParagraphMeasurer"/> over it.
/// </summary>
static class LayoutTestFonts
{
    static readonly string directory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static readonly FontFileCache cache = new(
        FontCacheLoader.EnumerateFontFilesInDirectory(directory, recursive: true),
        OpenTypeReader.ReadFaces);

    public static FontMetrics? Resolve(string family, bool bold, bool italic)
    {
        var candidates = FontHelpers.GetCandidateNames(family, bold);
        if (!cache.TryGet(candidates, out var faces) || faces.Length == 0)
        {
            return null;
        }

        var weight = FontHelpers.ResolveTargetWeight(family, bold);
        var best = faces.OrderBy(_ => FontHelpers.ScoreFace(_, weight, italic)).First();
        return FontMetricsReader.Read(best.Path, best.Index);
    }

    public static readonly CanonicalParagraphMeasurer Measurer = new(Resolve);
}
