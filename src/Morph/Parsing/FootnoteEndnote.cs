/// <summary>
/// A footnote definition from word/footnotes.xml.
/// </summary>
sealed record Footnote
{
    /// <summary>The footnote id (w:id).</summary>
    public required long Id { get; init; }

    /// <summary>Plain text of the footnote, runs flattened.</summary>
    public required string Text { get; init; }
}

/// <summary>
/// An endnote definition from word/endnotes.xml.
/// </summary>
sealed record Endnote
{
    /// <summary>The endnote id (w:id).</summary>
    public required long Id { get; init; }

    /// <summary>Plain text of the endnote, runs flattened.</summary>
    public required string Text { get; init; }
}
