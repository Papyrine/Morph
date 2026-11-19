/// <summary>
/// The floating tables in one table cell that the cell's text wraps round: the box each takes out of the
/// cell, where a paragraph that meets one goes, and how far down the cell has to reach to hold them. The
/// placement (<c>Fragmenter</c>'s cell arm) and the row height (<c>TableHeightCalculator.MeasureCellHeight</c>)
/// both ask it, each in its own coordinates, so a row is sized for exactly what is placed in it.
///
/// <para>Word-probed (<c>_probe_cellfloat</c>, 2026-11-19: a one-row 72pt table floated at <c>tblpY</c> 1 twip
/// in a 300pt cell, 20pt Arial on exact 24pt lines, read off the XPS):</para>
/// <list type="bullet">
/// <item>A line that meets a table's box sets beside it, in the strip between the table and the cell's edge,
/// and comes back to the full width once past the box. <c>rightFromText</c> and <c>bottomFromText</c> widen
/// the box: at 720 twips the text starts 36pt right of a 100pt table, and one more line sets beside a
/// full-width one.</item>
/// <item>With no strip, the paragraph goes under the box (the table's bottom border included) and keeps
/// the space in front of it: 24pt before puts its line 24pt under the table's foot, none puts it at the
/// foot.</item>
/// <item>A line that ends above a table (<c>tblpY</c> 36pt, under one 24pt line) stays full width above it.</item>
/// <item>The cell reaches the box's foot, <c>bottomFromText</c> included, whatever stands beside it: a lone
/// empty end mark (which collapses, <c>ParagraphElement.IsCollapsedCellMark</c>) or one short line beside a
/// narrow table, so the row grows to hold the table.</item>
/// </list>
/// <para>Not modelled: Word decides each LINE against the box, where a paragraph is given one strip for all
/// its lines here, as <c>Fragmenter.ResolveFlowBand</c> does in the page flow; and a strip under 36pt is not
/// used (the page flow's limit) where Word sets even an 8.6pt one, a letter a line ("A / F / T / E", then
/// "RA" at the full width under the table).</para>
/// </summary>
sealed class CellFloatWrap
{
    // Below this a strip is not used and the paragraph goes under the table instead, as in the page flow
    // (Fragmenter.ResolveFlowBand's minUsableWidth).
    const float minStripWidth = 36f;

    readonly List<(float Top, float Bottom, float Left, float Right)> boxes = [];

    /// <summary>The lowest foot of any box, its <c>bottomFromText</c> included: how far down the cell reaches.</summary>
    public float Extent { get; private set; } = float.MinValue;

    /// <summary>
    /// Whether the cell's text wraps round this floating table. Only one anchored to the text both ways:
    /// the row height knows where a cell's text is but not where on the page the cell will stand, so a
    /// table anchored to the margin or the page keeps standing outside the cell's flow.
    /// </summary>
    public static bool Wraps(TableElement table) =>
        table.Properties is
        {
            IsFloating: true,
            FloatingVerticalAnchor: FloatingTableVerticalAnchor.Text,
            FloatingHorizontalAnchor: FloatingTableHorizontalAnchor.Text
        };

    /// <summary>Adds a floating table that stands at (left, top), width by height, before its distances.</summary>
    public void Add(TableElement table, float left, float top, float width, float height)
    {
        var distance = table.Properties.FloatingDistance;
        var bottom = top + height + (float)distance.Bottom;
        boxes.Add((top - (float)distance.Top, bottom, left - (float)distance.Left, left + width + (float)distance.Right));
        Extent = Math.Max(Extent, bottom);
    }

    /// <summary>
    /// Where a paragraph lays out whose first line, <paramref name="lineHeight"/> tall, would start at
    /// <paramref name="lineTop"/>, the space in front of it having begun at <paramref name="spaceTop"/>, in a
    /// cell whose text runs from <paramref name="left"/> to <paramref name="right"/>. Constrained is false
    /// when the paragraph keeps the cell's full width.
    /// </summary>
    public (float LineTop, float Left, float Width, bool Constrained) Place(float spaceTop, float lineTop, float lineHeight, float left, float right)
    {
        for (var guard = 0; guard < 8; guard++)
        {
            float? clearTo = null;
            List<(float Start, float End)> segments = [(left, right)];
            foreach (var box in boxes)
            {
                if (lineTop + lineHeight <= box.Top ||
                    lineTop >= box.Bottom)
                {
                    continue;
                }

                clearTo = Math.Max(clearTo ?? box.Bottom, box.Bottom);
                var remaining = new List<(float Start, float End)>();
                foreach (var (start, end) in segments)
                {
                    if (box.Right <= start ||
                        box.Left >= end)
                    {
                        remaining.Add((start, end));
                        continue;
                    }

                    if (box.Left > start)
                    {
                        remaining.Add((start, box.Left));
                    }

                    if (box.Right < end)
                    {
                        remaining.Add((box.Right, end));
                    }
                }

                segments = remaining;
            }

            if (clearTo is not { } clear)
            {
                return (lineTop, left, right - left, false);
            }

            var bestStart = 0f;
            var bestWidth = 0f;
            foreach (var (start, end) in segments)
            {
                if (end - start > bestWidth)
                {
                    bestStart = start;
                    bestWidth = end - start;
                }
            }

            if (bestWidth >= minStripWidth)
            {
                return (lineTop, bestStart, bestWidth, true);
            }

            // Under the box, with the space that stood in front of the paragraph still in front of it.
            var space = lineTop - spaceTop;
            spaceTop = clear;
            lineTop = clear + space;
        }

        return (lineTop, left, right - left, false);
    }
}
