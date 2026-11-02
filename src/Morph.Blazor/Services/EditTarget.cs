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

    /// <summary>Where the selection starts: its paragraph, and its place in it.</summary>
    public int StartParagraph { get; init; } = -1;

    /// <inheritdoc cref="StartParagraph"/>
    public int StartOffset { get; init; }

    /// <summary>
    /// The page (0-based) the reader is on there, or -1: a header's or a footer's paragraph is drawn on
    /// every page that shows it, and is opened again, or selected again, on this one.
    /// </summary>
    public int Page { get; init; } = -1;

    public IReadOnlyList<int> Paragraphs =>
        Enumerable.Range(First, Last - First + 1).ToList();

    public bool HasSelection =>
        Start is { } start &&
        End is { } end &&
        start != end;
}
