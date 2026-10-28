/// <summary>
/// A built-up fraction (<c>m:f</c>) set inline: its numerator stacked over its denominator, each a list of
/// runs already at the reduced size Word sets the levels in, about the math axis of the text around it.
/// <see cref="SizePoints"/> is the size of the math the fraction stands in, which places the levels and
/// the bar (<c>CanonicalParagraphMeasurer</c>); <see cref="HasBar"/> is false for <c>m:type="noBar"</c>.
/// </summary>
sealed record MathFraction(IReadOnlyList<Run> Numerator, IReadOnlyList<Run> Denominator, double SizePoints, bool HasBar);
