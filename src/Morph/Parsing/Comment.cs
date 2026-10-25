/// <summary>
/// Reviewer comment from word/comments.xml.
/// </summary>
sealed record Comment
{
    /// <summary>The comment id (w:id).</summary>
    public required string Id { get; init; }

    /// <summary>The comment author (w:author).</summary>
    public string? Author { get; init; }

    /// <summary>The author's initials (w:initials), which Word's balloon labels the comment with.</summary>
    public string? Initials { get; init; }

    /// <summary>Plain text of the comment, runs flattened.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The face the comment's first run resolves to through its styles, which its balloon is set in.
    /// Null when the comment has no run.
    /// </summary>
    public string? FontFamily { get; init; }

    /// <summary>The comment's paragraphs as plain text, one entry each.</summary>
    public IReadOnlyList<string> Paragraphs { get; init; } = [];

    /// <summary>
    /// The 1-based number Word labels the comment with ("Commented [R1]"): where its range starts among
    /// the body's comment ranges. Null when the comment isn't anchored in the body.
    /// </summary>
    public int? Number { get; init; }

    /// <summary>The comment timestamp if present (w:date).</summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>
    /// Zero-based ordinal of the body paragraph that contains the matching w:commentRangeStart.
    /// Null when the comment isn't anchored to a paragraph in the body. Useful to surface a
    /// comment indicator next to the right paragraph in the rendered output later on.
    /// </summary>
    public int? AnchorParagraphIndex { get; init; }
}
