/// <summary>
/// The note content the <see cref="Fragmenter"/> places: footnote and endnote bodies keyed by their
/// <c>w:id</c>, and the four separator paragraphs whose mark lines carry the rules. A footnote's body
/// stacks at the bottom of the page its citing line lands on; a cited endnote flows after the document
/// body under its own separator.
/// </summary>
sealed record DocumentNotes(
    IReadOnlyDictionary<string, IReadOnlyList<DocumentElement>> Footnotes,
    IReadOnlyDictionary<string, IReadOnlyList<DocumentElement>> Endnotes,
    ParagraphElement FootnoteSeparator,
    ParagraphElement FootnoteContinuationSeparator,
    ParagraphElement EndnoteSeparator,
    ParagraphElement EndnoteContinuationSeparator)
{
    /// <summary>
    /// The notes of a parsed document, or null when it carries none — the common case, which costs
    /// the flow nothing.
    /// </summary>
    public static DocumentNotes? From(ParsedDocument document)
    {
        if (document.Footnotes.Count == 0 && document.Endnotes.Count == 0)
        {
            return null;
        }

        var footnotes = new Dictionary<string, IReadOnlyList<DocumentElement>>();
        foreach (var footnote in document.Footnotes)
        {
            footnotes[footnote.Id] = footnote.Elements;
        }

        var endnotes = new Dictionary<string, IReadOnlyList<DocumentElement>>();
        foreach (var endnote in document.Endnotes)
        {
            endnotes[endnote.Id] = endnote.Elements;
        }

        return new(
            footnotes,
            endnotes,
            document.FootnoteSeparator ?? DefaultSeparator(),
            document.FootnoteContinuationSeparator ?? DefaultSeparator(),
            document.EndnoteSeparator ?? DefaultSeparator(),
            document.EndnoteContinuationSeparator ?? DefaultSeparator());
    }

    // Word's own separator paragraph: empty, single-spaced, no spacing after — the parser synthesises
    // this for a part without separator entries; a hand-built document reaches it here.
    public static ParagraphElement DefaultSeparator() => new()
    {
        Runs = [],
        Properties = new()
        {
            SpacingBeforePoints = 0,
            SpacingAfterPoints = 0,
            LineSpacingRule = LineSpacingRule.Auto,
            LineSpacingMultiplier = 1
        }
    };
}
