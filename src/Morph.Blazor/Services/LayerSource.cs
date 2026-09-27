/// <summary>
/// A stretch of one page's text layer text and the place in the document it was drawn from: characters
/// <see cref="Start"/> to <see cref="End"/> of <see cref="Morph.PageTextLayer.Text"/> are positions
/// <see cref="Offset"/> onwards of the main part's run <see cref="Run"/>, one position a character —
/// unless <see cref="Atomic"/>, where the whole stretch (a note's reference number) is the one position.
/// </summary>
readonly record struct LayerSource(int Start, int Length, int Run, int Offset, bool Atomic)
{
    public int End => Start + Length;
}
