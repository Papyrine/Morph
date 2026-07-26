/// <summary>
/// One wrapped line placed on a page at an absolute position, in points from the page's top-left. The
/// line box is <see cref="PlacedItem.X"/>/<see cref="PlacedItem.Y"/>/<see cref="PlacedItem.Width"/>/
/// <see cref="PlacedItem.Height"/>; <see cref="Baseline"/> is the absolute Y of the text baseline (where
/// a painter positions glyphs). <see cref="Runs"/> are the text runs to paint, left to right — one per
/// line in the first painter slice (the line's dominant font), several once mixed-format lines are split.
/// The line is also anchored back to its source <see cref="Paragraph"/> and zero-based
/// <see cref="LineIndex"/> for paragraph-level painting (alignment, shading, borders). A paragraph split
/// across a page boundary contributes lines to more than one <see cref="LaidOutPage"/>.
/// </summary>
sealed record PlacedLine(
    float X,
    float Y,
    float Width,
    float Height,
    float Baseline,
    ParagraphElement Paragraph,
    int LineIndex,
    IReadOnlyList<PlacedRun> Runs) : PlacedItem(X, Y, Width, Height);
