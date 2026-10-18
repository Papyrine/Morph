/// <summary>
/// Where an editing command acts: the paragraphs it covers (<see cref="First"/> to <see cref="Last"/>,
/// by ordinal), the caret — a place <see cref="Offset"/> into paragraph <see cref="Paragraph"/> — and,
/// where text is selected, where it starts and ends. <see cref="Typing"/> says it came from a paragraph
/// open for typing, which the command then goes back to.
/// </summary>
sealed record EditTarget(int First, int Last, int Paragraph, int Offset, SourcePosition? Start, SourcePosition? End, bool Typing)
{
    /// <summary>The selected text, as a comment on it quotes it.</summary>
    public string Quote { get; init; } = "";

    public IReadOnlyList<int> Paragraphs =>
        Enumerable.Range(First, Last - First + 1).ToList();

    public bool HasSelection =>
        Start is { } start &&
        End is { } end &&
        start != end;
}
