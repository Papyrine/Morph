/// <summary>
/// The region an <see cref="SvgPath"/> is cut to (<c>clip-path</c>): the inside of
/// <see cref="Figures"/> under the nonzero rule, or the even-odd one. The figures are in the
/// drawing's own space like the path's. A clip set on a group inside another clipped group is cut
/// to both, which is <see cref="Parent"/>: a painter intersects the whole chain. Paths drawn under
/// the same <c>clip-path</c> share one instance, so a painter can keep a clip set for as long as the
/// reference stays the same.
/// </summary>
sealed class SvgClip(IReadOnlyList<SvgFigure> figures, bool evenOdd, SvgClip? parent)
{
    public IReadOnlyList<SvgFigure> Figures { get; } = figures;

    public bool EvenOdd { get; } = evenOdd;

    public SvgClip? Parent { get; } = parent;
}
