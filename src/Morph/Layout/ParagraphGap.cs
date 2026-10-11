/// <summary>
/// The space Word leaves between two adjacent paragraphs, in the two parts it is made of: what is kept
/// of the first one's space-after, and what is kept of the second one's space-before.
///
/// <para>Word lays a paragraph's after down when the paragraph ends and gives the next one only the
/// EXCESS of its before over that after, which is why the two read as <c>max(after, before)</c>.
/// <c>w:contextualSpacing</c> acts on each paragraph's own half and on nothing else: a paragraph that
/// opts in drops its after against a following paragraph of the same style and its before against a
/// preceding one, whether or not the neighbour opts in. And the excess is still taken over the first
/// paragraph's DECLARED after, dropped or not, so a contextual paragraph with 30pt after followed by a
/// plain one with 20pt before leaves nothing, and by one with 40pt before leaves 10.</para>
///
/// <para>Word-probed 2026-10-11 (<c>_probe_ctx</c> and <c>_probe_ctx2</c>: nineteen pairs and chains
/// of single-line paragraphs at 10 to 50pt of spacing, baselines read off the XPS, four of them again
/// inside table cells): after 30 then before 20 leaves 30 with neither opting in, 0 with both, 0 with
/// the first only and 30 with the second only; after 10 then before 40 leaves 40, 0, 30 and 10. Two
/// paragraphs of different styles keep <c>max</c> whatever they opt into, an unstyled paragraph and
/// one that names the default style are the same style, and the flag is inherited from a style like
/// any other property. A rule that both paragraphs must opt in stood here until then, which is the
/// same thing only when they do.</para>
/// </summary>
static class ParagraphGap
{
    /// <summary>The style two paragraphs are compared by: the one named, or the document's default.</summary>
    public static string? StyleKey(ParagraphProperties properties) =>
        properties.ResolvedStyleId ?? properties.StyleId;

    /// <summary>
    /// What is kept of each half between a paragraph and the one before it.
    /// <paramref name="previousIsParagraph"/> is false when something else (a table, a drawing, the
    /// start of the container) stands directly before, and no style is shared with that.
    /// </summary>
    public static (float After, float Before) Between(
        float previousAfter,
        bool previousContextual,
        string? previousStyleKey,
        bool previousIsParagraph,
        ParagraphProperties current)
    {
        var sameStyle = previousIsParagraph && StyleKey(current) == previousStyleKey;
        var after = previousContextual && sameStyle ? 0f : previousAfter;
        return (after, Before(previousAfter, previousStyleKey, previousIsParagraph, current));
    }

    /// <summary>
    /// Whether the paragraph that ends a table cell drops its space-after. It does when it opts in to
    /// contextual spacing and the next paragraph in the document is of its style — and that next
    /// paragraph is the first of the following cell, or, for the row's last cell, the mark that ends
    /// the row, which counts as a paragraph of the default style.
    ///
    /// <para>Word-probed 2026-10-11 (<c>_probe_cellctx</c>: one-row tables of 10pt Arial with 24pt
    /// after, the row's bottom rule read against the last baseline): a contextual default-style pair
    /// in an only cell leaves the rule 2.3pt under the baseline, whatever follows the table; the same
    /// pair without the flag leaves it 26.3pt under; in a first cell it leaves 2.3pt when the second
    /// cell opens with the same style and 26.5pt when it opens with another; and a contextual pair in
    /// a style that is NOT the default keeps its 24pt in an only cell (26.4pt). letters/01's header
    /// table ends on such a paragraph, and the 8pt it kept put everything below it 8pt low.</para>
    /// </summary>
    public static bool DropsCellEndAfter(TableRow? row, TableCell cell)
    {
        if (row == null || CellParagraph(cell.Content, last: true) is not {Properties: {ContextualSpacing: true} ending})
        {
            return false;
        }

        for (var index = 0; index < row.Cells.Count - 1; index++)
        {
            if (ReferenceEquals(row.Cells[index], cell))
            {
                return CellParagraph(row.Cells[index + 1].Content, last: false) is { } next &&
                       StyleKey(next.Properties) == StyleKey(ending);
            }
        }

        return ending.InDefaultStyle;
    }

    /// <summary>
    /// What a paragraph keeps of its space-after ahead of a table. The next paragraph in the document
    /// is the table's first — its first cell's — so a contextual paragraph of that style drops its
    /// after there too. Same probe: 24pt after ahead of a table whose first cell opens in the same
    /// style left the table's top rule 2.6pt under the paragraph's baseline, for an ordinary table and
    /// a floating one alike (and for the floating one whatever the style of the paragraph it is
    /// anchored to, which comes after it). letters/01's header table sat 8pt low by that.
    /// </summary>
    public static float BeforeTable(float previousAfter, bool previousContextual, string? previousStyleKey, bool previousIsParagraph, TableElement table)
    {
        if (previousContextual &&
            previousIsParagraph &&
            table.Rows.Count > 0 &&
            table.Rows[0].Cells.Count > 0 &&
            CellParagraph(table.Rows[0].Cells[0].Content, last: false) is { } first &&
            StyleKey(first.Properties) == previousStyleKey)
        {
            return 0;
        }

        return previousAfter;
    }

    // The paragraph a cell's content opens or closes with, or null when something else stands there.
    static ParagraphElement? CellParagraph(IReadOnlyList<DocumentElement> content, bool last)
    {
        if (content.Count == 0)
        {
            return null;
        }

        var element = last ? content[^1] : content[0];
        return element as ParagraphElement ?? (element as ContentControlElement)?.CellParagraph;
    }

    /// <summary>
    /// What is kept of a paragraph's space-before against the after of the paragraph before it, where
    /// that after has already been laid down or has fallen away at a break.
    /// </summary>
    public static float Before(
        float previousAfter,
        string? previousStyleKey,
        bool previousIsParagraph,
        ParagraphProperties current)
    {
        if (current.ContextualSpacing && previousIsParagraph && StyleKey(current) == previousStyleKey)
        {
            return 0;
        }

        return Math.Max(0f, (float) current.SpacingBeforePoints - previousAfter);
    }
}
