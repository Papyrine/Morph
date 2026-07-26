/// <summary>
/// One wrapped line placed on a page at an absolute position, in points from the page's top-left. A
/// line is anchored back to its source <see cref="Paragraph"/> and its zero-based
/// <see cref="LineIndex"/> within that paragraph, so a painter can resolve the glyphs to draw (the
/// glyph-run breakdown, <c>PlacedGlyphRun</c> in the proposal, is a later slice). A paragraph split
/// across a page boundary contributes lines to more than one <see cref="LaidOutPage"/>.
/// </summary>
sealed record PlacedLine(
    float X,
    float Y,
    float Width,
    float Height,
    ParagraphElement Paragraph,
    int LineIndex);
