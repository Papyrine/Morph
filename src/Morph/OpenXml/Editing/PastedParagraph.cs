/// <summary>
/// A pasted paragraph: its text, the style it was (a heading, by the id Word gives it), the list it was
/// in and at what level, and its alignment when that was not the left.
/// </summary>
sealed record PastedParagraph(IReadOnlyList<PastedRun> Runs)
{
    public string? Style { get; init; }

    public ListKind List { get; init; }

    public int Level { get; init; }

    public TextAlignment? Alignment { get; init; }

    public string Text => string.Concat(Runs.Select(_ => _.Text));
}
