/// <summary>
/// Reviewer comment from word/comments.xml.
/// </summary>
sealed record Comment
{
    /// <summary>The comment id (w:id).</summary>
    public required string Id { get; init; }

    /// <summary>The comment author (w:author).</summary>
    public string? Author { get; init; }

    /// <summary>Plain text of the comment, runs flattened.</summary>
    public required string Text { get; init; }

    /// <summary>The comment timestamp if present (w:date).</summary>
    public DateTimeOffset? Date { get; init; }
}
