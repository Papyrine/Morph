/// <summary>
/// One card of the viewer's review pane: a comment thread or a tracked change. <see cref="Key"/> names it
/// to the page script (the card's <c>data-review-key</c>) and survives an edit to the document only as
/// far as the thing it names does — a comment keeps its id, a change is renumbered.
/// </summary>
sealed record ReviewEntry
{
    public ReviewEntry(ReviewComment comment)
    {
        Comment = comment;
        Key = KeyOf(comment);
    }

    public ReviewEntry(ReviewChange change)
    {
        Change = change;
        Key = $"change:{change.Key}";
    }

    public string Key { get; }

    public ReviewComment? Comment { get; }

    public ReviewChange? Change { get; }

    /// <summary>The main-part runs the entry covers.</summary>
    public IReadOnlyList<int> Runs => Comment?.Runs ?? Change!.Runs;

    /// <summary>The main-part run the entry sits at, or -1 for one that has no place on a page.</summary>
    public int Anchor => Comment?.Anchor ?? Change!.Anchor;

    public static string KeyOf(ReviewComment comment) =>
        $"comment:{comment.Id}";
}

/// <summary>What the review pane lists.</summary>
enum ReviewFilter
{
    All,
    Comments,
    Changes
}

/// <summary>What a draft in the review pane will become when posted.</summary>
enum ReviewDraftKind
{
    /// <summary>A new comment on the text that was selected.</summary>
    Comment,

    /// <summary>A reply in a thread.</summary>
    Reply,

    /// <summary>New text for an existing comment.</summary>
    Edit
}

/// <summary>A comment being written in the review pane.</summary>
sealed class ReviewDraft(ReviewDraftKind kind)
{
    public ReviewDraftKind Kind { get; } = kind;

    public string Text { get; set; } = "";

    /// <summary>The comment replied to or edited.</summary>
    public string? CommentId { get; init; }

    /// <summary>The thread the draft shows in: the card it belongs to.</summary>
    public string? ThreadId { get; init; }

    /// <summary>Where a new comment's range starts and ends.</summary>
    public SourcePosition Start { get; init; }

    public SourcePosition End { get; init; }

    /// <summary>The text a new comment is on.</summary>
    public string Quote { get; init; } = "";

    /// <summary>That text on the pages, as (page, start, length) triples: shown while the comment is written.</summary>
    public int[] Ranges { get; init; } = [];
}
