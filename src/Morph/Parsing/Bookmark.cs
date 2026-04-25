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

    /// <summary>
    /// Zero-based ordinal of the enclosing body paragraph (counting w:p elements only).
    /// Null when the bookmark sits between paragraphs at body level. Useful for cross-reference
    /// lookups: locate the paragraph then resolve to a page in the rendered output.
    /// </summary>
    public int? ParagraphIndex { get; init; }
}
