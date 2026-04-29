/// <summary>
/// One face inside a font file. A <c>.ttf</c>/<c>.otf</c> file contains a single
/// face (<see cref="Index"/> = 0); a <c>.ttc</c> collection contains several. The
/// metrics carried here are read once from the font's <c>name</c> and <c>OS/2</c>
/// tables when the file is indexed, so resolvers can score candidates by weight
/// and width without re-loading the file.
/// </summary>
sealed record FontFace
{
    /// <summary>Path to the font file on disk.</summary>
    public required string Path { get; init; }

    /// <summary>Face index inside a <c>.ttc</c> collection; 0 for single-face files.</summary>
    public int Index { get; init; }

    /// <summary>OS/2 <c>usWeightClass</c> (1–1000). Common values: 100=Thin, 300=Light, 350=Semilight, 400=Regular, 500=Medium, 600=Semibold, 700=Bold, 900=Black.</summary>
    public int Weight { get; init; }

    /// <summary>OS/2 <c>usWidthClass</c> (1–9). 5=Normal (default), 1–4=Condensed, 6–9=Expanded.</summary>
    public int Width { get; init; }

    /// <summary>True when the font's <c>fsSelection</c> italic bit is set.</summary>
    public bool Italic { get; init; }
}
