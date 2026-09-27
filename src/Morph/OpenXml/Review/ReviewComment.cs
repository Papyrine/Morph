/// <summary>
/// A reviewer's comment with where it is anchored, whether its thread is resolved, and the replies to it.
/// </summary>
sealed record ReviewComment
{
    /// <summary>The comment's <c>w:id</c>.</summary>
    public required string Id { get; init; }

    public string? Author { get; init; }

    public string? Initials { get; init; }

    /// <summary>The time as the file states it; Word writes the author's local clock.</summary>
    public DateTime? Date { get; init; }

    /// <summary>The comment's text, a line per paragraph.</summary>
    public required string Text { get; init; }

    /// <summary>Whether the thread is marked resolved (<c>w15:done</c>).</summary>
    public bool Resolved { get; init; }

    /// <summary>The replies, oldest first. Word's threads are flat, so a reply has none of its own.</summary>
    public IReadOnlyList<ReviewComment> Replies { get; init; } = [];

    /// <summary>The text the comment is attached to, a line per paragraph. Empty for a comment on a point.</summary>
    public string Quote { get; init; } = "";

    /// <summary>The ordinals of the main-part runs in the comment's range, ascending.</summary>
    public IReadOnlyList<int> Runs { get; init; } = [];

    /// <summary>
    /// The ordinal of the main-part run the comment sits at — the first in its range, or the one
    /// carrying its reference mark. -1 for a comment the document never refers to.
    /// </summary>
    public int Anchor { get; init; } = -1;
}
