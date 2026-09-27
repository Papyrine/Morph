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

    static int Span(EditUnit unit)
    {
        if (unit.Editable)
        {
            return unit.Text.Length;
        }

        return 1;
    }
}
