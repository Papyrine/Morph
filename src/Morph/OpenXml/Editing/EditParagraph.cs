/// <summary>
/// One <c>w:p</c> of the main document part, by its ordinal among them in document order, with what
/// it holds in the order it holds it.
/// </summary>
sealed record EditParagraph(int Ordinal, IReadOnlyList<EditUnit> Units)
{
    /// <summary>Whether the block after this paragraph is a paragraph it can be joined to.</summary>
    public bool HasNext { get; init; }

    /// <summary>Whether the block before this paragraph is a paragraph that can be joined to it.</summary>
    public bool HasPrevious { get; init; }

    /// <summary>The part the paragraph is in: the body, a header or footer, the footnotes or endnotes.</summary>
    public ReviewPart Part { get; init; }

    /// <summary>Whether the paragraph is in a table's cell, which is what the table commands act on.</summary>
    public bool InTable { get; init; }

    /// <summary>
    /// The cell the paragraph is in — of the innermost table, when tables nest — or null outside one:
    /// what Word's Tab, Alt+Home and Alt+PgUp move between.
    /// </summary>
    public EditCell? Cell { get; init; }

    /// <summary>The paragraph's level in the list it is in — by its own numbering or its style's — or null.</summary>
    public int? ListLevel { get; init; }

    /// <summary>
    /// Whether Backspace at the paragraph's start takes off its number or a step of its indent
    /// (<see cref="DocumentEditor.Unindent"/>) rather than joining it to the paragraph before.
    /// </summary>
    public bool Indented { get; init; }

    /// <summary>
    /// The style a paragraph begun by Enter at this one's end is in, when it is not this one's own: the
    /// style's <c>w:next</c>, as a heading names body text.
    /// </summary>
    public string? NextStyle { get; init; }

    /// <summary>
    /// How many places there are in the paragraph: one for each character of its text, and one for
    /// each thing in it that is not text.
    /// </summary>
    public int Length
    {
        get
        {
            var length = 0;
            foreach (var unit in Units)
            {
                length += Span(unit);
            }

            return length;
        }
    }

    /// <summary>
    /// The place of a position in one of the paragraph's runs, counted as <see cref="Length"/> counts;
    /// null when the run is not one of this paragraph's.
    /// </summary>
    public int? OffsetOf(SourcePosition position)
    {
        var offset = 0;
        int? nearest = null;
        foreach (var unit in Units)
        {
            if (unit.Run == position.Run)
            {
                if (unit.Editable &&
                    position.Offset >= unit.Start &&
                    position.Offset <= unit.Start + unit.Text.Length)
                {
                    return offset + position.Offset - unit.Start;
                }

                // A position on something that is not text: before it, or after.
                nearest ??= position.Offset <= unit.Start ? offset : offset + Span(unit);
            }

            offset += Span(unit);
        }

        return nearest;
    }

    /// <summary>
    /// The position in one of the paragraph's runs of a place, counted as <see cref="Length"/> counts —
    /// what <see cref="OffsetOf"/> turns back into the place. A selection starts at the first
    /// character of text at or after its place, and ends (<paramref name="ending"/>) after the last
    /// one before it; null where there is no such character.
    /// </summary>
    public SourcePosition? PositionAt(int place, bool ending)
    {
        var offset = 0;
        SourcePosition? before = null;
        foreach (var unit in Units)
        {
            var span = Span(unit);
            if (unit.Editable &&
                span > 0)
            {
                if (ending)
                {
                    if (place > offset &&
                        place <= offset + span)
                    {
                        return new(unit.Run, unit.Start + place - offset);
                    }

                    if (offset + span < place)
                    {
                        before = new(unit.Run, unit.Start + span);
                    }
                }
                else
                {
                    if (place < offset + span)
                    {
                        return new(unit.Run, unit.Start + Math.Max(place - offset, 0));
                    }
                }
            }

            offset += span;
        }

        return before;
    }

    static int Span(EditUnit unit)
    {
        if (unit.Editable)
        {
            return unit.Text.Length;
        }

        return 1;
    }
}
