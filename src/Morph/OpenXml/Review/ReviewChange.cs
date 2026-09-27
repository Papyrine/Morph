/// <summary>
/// One tracked change as a reviewer sees it. Word writes a revision as many elements — an insertion that
/// runs across a bold word and into the next paragraph is three <c>w:ins</c> and a paragraph mark — so
/// <see cref="DocumentReview"/> gathers the adjacent elements of one kind by one author into one change,
/// which is accepted or rejected whole.
/// </summary>
sealed record ReviewChange
{
    /// <summary>
    /// Names the change's elements to <see cref="ReviewEditor"/>. Good for the bytes it was read from
    /// only: any edit renumbers the elements, and the document is read again.
    /// </summary>
    public required string Key { get; init; }

    public required ReviewChangeKind Kind { get; init; }

    public string? Author { get; init; }

    /// <summary>The time as the file states it; Word writes the author's local clock.</summary>
    public DateTime? Date { get; init; }

    /// <summary>
    /// The text the change covers — inserted, deleted, moved or reformatted — with a line break for
    /// each paragraph mark in it. Empty for a change that covers none, such as a page-setup change.
    /// </summary>
    public required string Text { get; init; }

    public required ReviewPart Part { get; init; }

    /// <summary>
    /// The ordinals of the main-part runs the change covers, ascending. Empty for a change outside the
    /// main part, and for one that covers no run.
    /// </summary>
    public IReadOnlyList<int> Runs { get; init; } = [];

    /// <summary>
    /// The ordinal of the main-part run the change sits at (its first run, or the run after a change
    /// that covers none), which places it on a page and among the other changes. -1 outside the main
    /// part.
    /// </summary>
    public int Anchor { get; init; } = -1;
}
