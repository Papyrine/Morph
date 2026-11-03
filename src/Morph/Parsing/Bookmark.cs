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

    /// <summary>
    /// The paragraph of the model the bookmark is in: the first one its w:p was read as. What finds
    /// the bookmark's page, since <see cref="ParagraphIndex"/> counts w:p elements and a w:p a break
    /// divides is several paragraphs of the model, or none. Null where
    /// <see cref="ParagraphIndex"/> is, and for a w:p holding nothing but a break.
    /// </summary>
    public ParagraphElement? Paragraph { get; init; }
}
