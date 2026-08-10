/// <summary>
/// Discovery for the scenario corpus, which is split by input format under <c>Inputs/</c>:
/// <c>word/</c> (<c>input.docx</c>), <c>excel/</c> (<c>input.xlsx</c>) and <c>powerpoint/</c>
/// (<c>input.pptx</c>). The split exists because the themed category names genuinely collide across
/// formats — <c>brochures</c>, <c>business</c> and <c>cards</c> occur in more than one corpus — and
/// because TUnit cannot filter on parameter values, so running one format's scenarios needs its own
/// test class over its own root.
///
/// Nesting the format level under <c>Inputs/</c> rather than using sibling <c>InputsWord/</c>
/// directories keeps every <c>src/Tests/Inputs/**</c> pattern working untouched — the CI
/// received-file upload, <c>regenerate-baselines.sh</c>, and the received-file sweeps all want the
/// whole corpus and do not care about format.
///
/// <see cref="ScenarioName"/> reports a path relative to the FORMAT root, not to <c>Inputs/</c>, so
/// a scenario stays named <c>resumes/01</c> rather than <c>word/resumes/01</c>. That keeps the
/// generated <c>compare.md</c> headings and the <c>BaselineHealthTests</c> allow-list keys stable
/// across the move.
/// </summary>
static class ScenarioInputs
{
    /// <summary>The corpus root holding the per-format roots.</summary>
    public static string InputsDirectory { get; } =
        Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs"));

    /// <summary>The root directory holding <paramref name="format"/>'s scenarios.</summary>
    public static string Root(ScenarioFormat format) =>
        Path.Combine(InputsDirectory, DirectoryName(format));

    /// <summary>
    /// How many pages of a scenario are rendered and baselined, or null for "all of them".
    ///
    /// The DOCX corpus is uncapped — its scenarios are mostly one or two pages, and capping would
    /// discard existing baselines for no gain. The deck corpus is capped because 40 decks hold 450
    /// slides, and each rendered page costs four PNGs (Skia, ImageSharp, the PDF page, and the
    /// PowerPoint reference). The first slides carry a deck's distinct layouts — title, then the
    /// first content layouts — while later ones are largely repeats of those.
    ///
    /// Whatever this returns, the reference-image generator must cap identically: a page-count
    /// mismatch makes the scenario tests record no per-page metric at all.
    /// </summary>
    public static PageRange? Pages(ScenarioFormat format) =>
        format == ScenarioFormat.Word ? null : new PageRange(1, 2);

    /// <summary>
    /// The DPI a scenario's pages are rendered and compared at.
    ///
    /// Documents stay at 150: their fidelity work is typographic — glyph positions, wrap points,
    /// hairline rules — and it needs the pixels. A slide is pictures and large-format layout, where
    /// 96 resolves a 16:9 canvas to 1280x720 and still shows every placement error worth catching,
    /// at 40% of the pixels.
    ///
    /// Every producer of a comparable image has to agree on this: the raster backends, the
    /// PowerPoint reference export in RenderHelper, and the PDF page rasterisation the metric uses.
    /// A mismatch does not fail loudly — it silently suppresses SSIM and skews the error metric,
    /// because the two images are no longer the same size.
    /// </summary>
    public static int Dpi(ScenarioFormat format) =>
        format == ScenarioFormat.Word ? 150 : 96;

    /// <summary>Every scenario directory for <paramref name="format"/>.</summary>
    public static IEnumerable<string> Directories(ScenarioFormat format) =>
        Directory.GetFiles(Root(format), $"input.{Extension(format)}", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)!;

    /// <summary>
    /// The single <c>input.*</c> file in a scenario directory. Deliberately format-agnostic so a
    /// test body never has to restate which format it is running, and deliberately
    /// <see cref="Enumerable.Single{T}(IEnumerable{T})"/> so a directory holding none or several
    /// fails loudly rather than silently picking one.
    /// </summary>
    public static string InputFile(string directory) =>
        Directory.EnumerateFiles(directory, "input.*").Single();

    /// <summary>Every scenario directory across every format.</summary>
    public static IEnumerable<string> AllDirectories() =>
        Enum.GetValues<ScenarioFormat>()
            .Where(_ => Directory.Exists(Root(_)))
            .SelectMany(Directories);

    /// <summary>The format a scenario directory holds, read from its input file's extension.</summary>
    public static ScenarioFormat FormatOf(string directory) =>
        Path.GetExtension(InputFile(directory)).ToLowerInvariant() switch
        {
            ".xlsx" => ScenarioFormat.Excel,
            ".pptx" => ScenarioFormat.PowerPoint,
            _ => ScenarioFormat.Word
        };

    /// <summary>
    /// The scenario's name: its path below the format root, with forward slashes. Falls back to the
    /// directory's own name when it sits outside the corpus (ad hoc probe directories).
    /// </summary>
    public static string ScenarioName(string directory)
    {
        var relative = Path.GetRelativePath(InputsDirectory, Path.GetFullPath(directory));
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return Path.GetFileName(directory);
        }

        // Drop the leading format segment so names stay stable across the corpus split.
        var segments = relative.Replace('\\', '/').Split('/');
        return segments.Length > 1 ? string.Join('/', segments.Skip(1)) : segments[0];
    }

    static string DirectoryName(ScenarioFormat format) =>
        format switch
        {
            ScenarioFormat.Word => "word",
            ScenarioFormat.Excel => "excel",
            ScenarioFormat.PowerPoint => "powerpoint",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };

    static string Extension(ScenarioFormat format) =>
        format switch
        {
            ScenarioFormat.Word => "docx",
            ScenarioFormat.Excel => "xlsx",
            ScenarioFormat.PowerPoint => "pptx",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
}
