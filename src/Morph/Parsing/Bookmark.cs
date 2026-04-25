/// <summary>
/// Named anchor in a document (w:bookmarkStart). Bookmarks are invisible — they exist
/// to be cross-reference targets (PAGEREF/REF fields) and navigation anchors.
/// </summary>
sealed record Bookmark
{
    /// <summary>The bookmark identifier (w:id), unique within the document.</summary>
    public required string Id { get; init; }

    /// <summary>The bookmark name (w:name) used by hyperlinks and field references.</summary>
    public required string Name { get; init; }
}
