/// <summary>
/// A footnote definition from word/footnotes.xml.
/// </summary>
sealed record Footnote
{
    /// <summary>The footnote id (w:id).</summary>
    public required string Id { get; init; }

    /// <summary>Plain text of the footnote, runs flattened — what the text exporters emit.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The note body as parsed paragraphs (and tables), its <c>w:footnoteRef</c> run carrying the
    /// citation number — what the layout engine stacks in the page-bottom footnote area.
    /// </summary>
    public IReadOnlyList<DocumentElement> Elements { get; init; } = [];
}

/// <summary>
/// An endnote definition from word/endnotes.xml.
/// </summary>
sealed record Endnote
{
    /// <summary>The endnote id (w:id).</summary>
    public required string Id { get; init; }

    /// <summary>Plain text of the endnote, runs flattened — what the text exporters emit.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The note body as parsed paragraphs (and tables), its <c>w:endnoteRef</c> run carrying the
    /// citation number — what the layout engine flows after the document body.
    /// </summary>
    public IReadOnlyList<DocumentElement> Elements { get; init; } = [];
}
