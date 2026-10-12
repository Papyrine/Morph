/// <summary>
/// One drawing operation of an <see cref="SvgDrawing"/>: a set of figures, filled and then stroked.
/// Everything the document said about the shape is already resolved into it — the element it came
/// from (a <c>rect</c> is four lines here), the transforms over it, the style it inherited or took
/// from a class, and its opacity, which is folded into <see cref="FillAlpha"/> and the stroke's
/// alpha. A null <see cref="FillColorHex"/> is no fill and a null <see cref="Stroke"/> no outline;
/// a path with neither is never emitted.
/// </summary>
sealed record SvgPath(IReadOnlyList<SvgFigure> Figures, string? FillColorHex, double FillAlpha, bool EvenOdd, SvgStroke? Stroke, SvgClip? Clip);
