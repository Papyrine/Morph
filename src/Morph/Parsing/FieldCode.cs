/// <summary>
/// A field code captured from the document body (w:fldChar/w:instrText).
/// Common instructions: PAGE, NUMPAGES, DATE, AUTHOR, TOC, PAGEREF, REF, HYPERLINK.
/// </summary>
sealed record FieldCode
{
    /// <summary>The raw instruction text (e.g. <c>PAGE \* MERGEFORMAT</c>, <c>TOC \o "1-3"</c>).</summary>
    public required string Instruction { get; init; }

    /// <summary>The cached result text Word last computed for this field. Empty when no result was found.</summary>
    public required string Result { get; init; }

    /// <summary>The field's primary keyword (first space-delimited token of the instruction, uppercased) — e.g. <c>PAGE</c>, <c>TOC</c>.</summary>
    public string Keyword
    {
        get
        {
            var trimmed = Instruction.AsSpan().Trim();
            if (trimmed.IsEmpty)
            {
                return string.Empty;
            }

            var space = trimmed.IndexOfAny(' ', '\t');
            var token = space < 0 ? trimmed : trimmed[..space];
            return token.ToString().ToUpperInvariant();
        }
    }
}
