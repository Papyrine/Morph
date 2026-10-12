/// <summary>
/// What <see cref="SvgReader"/> made of a document: the <see cref="Drawing"/>, or — when the
/// document uses something the reader does not draw — null and the first such thing it met, named
/// in <see cref="Unsupported"/>. The decision is for the whole document: a drawing is either all
/// there or not offered, so a picture is never painted with a part missing.
/// </summary>
sealed record SvgReadResult(SvgDrawing? Drawing, string? Unsupported);
