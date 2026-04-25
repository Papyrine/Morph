enum TrackedChangeType
{
    Insertion,
    Deletion
}

/// <summary>
/// A reviewer revision (insertion or deletion) recorded by Word's track changes feature.
/// </summary>
sealed record TrackedChange
{
    /// <summary>The revision id (w:id).</summary>
    public required string Id { get; init; }

    /// <summary>The author of the change (w:author).</summary>
    public string? Author { get; init; }

    /// <summary>Timestamp of the change if present (w:date).</summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>Whether this change inserts or deletes content.</summary>
    public required TrackedChangeType Type { get; init; }

    /// <summary>Plain text of the affected content.</summary>
    public required string Text { get; init; }
}
