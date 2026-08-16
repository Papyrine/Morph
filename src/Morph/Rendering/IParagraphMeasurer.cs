/// <summary>
/// Backend-agnostic API for measuring paragraph layout. Used by shared table
/// height calculations so the math can live in <c>src/Morph/</c> while the actual
/// text shaping stays in the SkiaSharp / ImageSharp renderers.
/// </summary>
interface IParagraphMeasurer
{
    /// <summary>
    /// Returns per-line heights for the paragraph laid out at <paramref name="maxWidth"/>
    /// using table-cell line-height rules (no Word compatibility boost).
    /// </summary>
    List<float> LayoutParagraphForMeasurement(ParagraphElement paragraph, float maxWidth);

    /// <summary>
    /// Returns the widest line width when the paragraph is laid out at
    /// <paramref name="maxWidth"/>. Pass a very large width for the natural single-line width.
    /// </summary>
    float MeasureParagraphNaturalWidth(ParagraphElement paragraph, float maxWidth);

    /// <summary>
    /// Returns the total laid-out height of the paragraph (including spacing) at the given wrap width.
    /// </summary>
    float MeasureParagraphHeightWithWidth(ParagraphElement paragraph, float maxWidth);

    /// <summary>
    /// Returns the width of the paragraph's longest unbreakable token (e.g. "john@company.com") — the
    /// narrowest measure it can occupy without a word overflowing, which a table's autofit takes as a
    /// column's minimum width. Defined as the widest line left when the paragraph is laid out at a 1pt
    /// measure, which is what this default does; an implementation may answer the same question by a
    /// cheaper route.
    /// </summary>
    float MeasureLongestTokenWidth(ParagraphElement paragraph) =>
        MeasureParagraphNaturalWidth(paragraph, 1f);
}
