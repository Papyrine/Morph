/// <summary>
/// One wrapped line placed on a page at an absolute position, in points from the page's top-left. The
/// line box is <see cref="PlacedItem.X"/>/<see cref="PlacedItem.Y"/>/<see cref="PlacedItem.Width"/>/
/// <see cref="PlacedItem.Height"/>; <see cref="Baseline"/> is the absolute Y of the text baseline (where
/// a painter positions glyphs). <see cref="Runs"/> are the text runs to paint, left to right — one per
/// line in the first painter slice (the line's dominant font), several once mixed-format lines are split.
/// The line also carries its inline <see cref="Images"/> (bottom on the baseline), and is anchored back
/// to its source <see cref="Paragraph"/> and zero-based <see cref="LineIndex"/> for paragraph-level
/// painting (alignment, shading, borders). A paragraph split across a page boundary contributes lines to
/// more than one <see cref="LaidOutPage"/>.
/// </summary>
sealed record PlacedLine(
    float X,
    float Y,
    float Width,
    float Height,
    float Baseline,
    ParagraphElement Paragraph,
    int LineIndex,
    IReadOnlyList<PlacedRun> Runs,
    IReadOnlyList<PlacedImage> Images) : PlacedItem(X, Y, Width, Height)
{
    /// <summary>
    /// Where Word's change bar runs down the left margin beside the line, top and bottom in points from
    /// the page's top, when the line is revised; null when it is not. A line is revised when a run on
    /// it is inserted, deleted or reformatted, when it is the first line of a paragraph whose
    /// properties changed, or the last of one whose mark did. The bar spans the line's box, and on the
    /// paragraph's first line the space the paragraph keeps before it too, on its last its space after
    /// (Fragmenter.ChangeBar).
    /// </summary>
    public (float Top, float Bottom)? ChangeBar { get; init; }
}
