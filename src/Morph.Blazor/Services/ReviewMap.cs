/// <summary>
/// Where the document's text is on its pages, for the review pane: which stretches of the pages' text a
/// comment or a tracked change covers, and which place in the document a stretch of selected text is.
/// Both ways go through the runs of the main document part — a change names the runs it covers
/// (<see cref="ReviewChange.Runs"/>), and each page's text layer says which runs it was drawn from
/// (<see cref="Morph.PageTextLayer.Sources"/>).
///
/// Offsets are into a page's <see cref="Morph.PageTextLayer.Text"/>, the same ones find results are
/// given in, so morph-viewer.js paints a comment's range exactly as it paints a find match.
/// </summary>
sealed class ReviewMap
{
    // Two stretches of one change are painted as one when all that parts them is what the layout put
    // between them — the space a justified line dropped, a wrap, a list marker — rather than text.
    const int bridge = 8;

    readonly IReadOnlyList<LayerSource>[] pages;

    // Where each run was drawn: the page, and its index among that page's sources.
    readonly Dictionary<int, List<(int Page, int Index)>> drawn = [];

    // The runs drawn, ascending.
    readonly int[] runs;

    public ReviewMap(IEnumerable<IReadOnlyList<LayerSource>> pageSources)
    {
        pages = pageSources.ToArray();
        for (var page = 0; page < pages.Length; page++)
        {
            var sources = pages[page];
            for (var index = 0; index < sources.Count; index++)
            {
                if (!drawn.TryGetValue(sources[index].Run, out var places))
                {
                    drawn[sources[index].Run] = places = [];
                }

                places.Add((page, index));
            }
        }

        runs = drawn.Keys.Order().ToArray();
    }

    public static ReviewMap Empty { get; } = new([]);

    /// <summary>Whether any of the pages' text is traced to the document: nothing can be placed otherwise.</summary>
    public bool HasSources => runs.Length > 0;

    /// <summary>The stretches of the pages' text the given runs were drawn as, in page order.</summary>
    public IReadOnlyList<TextMatch> Ranges(IReadOnlyList<int> runOrdinals)
    {
        var places = new List<(int Page, int Index)>();
        foreach (var run in runOrdinals)
        {
            if (drawn.TryGetValue(run, out var found))
            {
                places.AddRange(found);
            }
        }

        places.Sort();
        var ranges = new List<TextMatch>();
        var last = (Page: -1, Index: -1);
        foreach (var place in places)
        {
            if (place == last)
            {
                continue;
            }

            var source = pages[place.Page][place.Index];
            if (ranges.Count > 0 &&
                place.Page == last.Page &&
                place.Index == last.Index + 1 &&
                ranges[^1] is var previous &&
                source.Start - (previous.Start + previous.Length) <= bridge)
            {
                ranges[^1] = previous with {Length = source.End - previous.Start};
            }
            else
            {
                ranges.Add(new(place.Page, source.Start, source.Length));
            }

            last = place;
        }

        return ranges;
    }

    /// <summary>
    /// Where a run starts on the pages — or, for a run that draws nothing (a comment's reference mark,
    /// the end of a paragraph), where the next one that does starts. Null when there is none.
    /// </summary>
    public TextMatch? Place(int run)
    {
        var index = Array.BinarySearch(runs, run);
        if (index < 0)
        {
            index = ~index;
        }

        if (run < 0 ||
            index >= runs.Length)
        {
            return null;
        }

        var (page, source) = drawn[runs[index]].Min();
        return new(page, pages[page][source].Start, 0);
    }

    /// <summary>The run a character of a page's text was drawn from, if it is traced.</summary>
    public int? RunAt(int page, int offset)
    {
        if (Find(page, offset) is { } index &&
            pages[page][index] is var source &&
            offset < source.End)
        {
            return source.Run;
        }

        return null;
    }

    /// <summary>The runs a stretch of the pages' text was drawn from, from one page's offset to another's.</summary>
    public IReadOnlyList<int> Runs(int firstPage, int start, int lastPage, int end)
    {
        var found = new List<int>();
        for (var page = Math.Max(firstPage, 0); page <= lastPage && page < pages.Length; page++)
        {
            var from = page == firstPage ? start : 0;
            var to = page == lastPage ? end : int.MaxValue;
            foreach (var source in pages[page])
            {
                if (source.Start < to &&
                    source.End > from &&
                    !found.Contains(source.Run))
                {
                    found.Add(source.Run);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The place in the document just before a character of a page's text. An untraced character — a
    /// list marker, a header — stands for the next traced one, on this page or a later one.
    /// </summary>
    public SourcePosition? Before(int page, int offset)
    {
        for (var current = Math.Max(page, 0); current < pages.Length; current++)
        {
            var sources = pages[current];
            var from = current == page ? Math.Max(offset, 0) : 0;
            var index = Find(current, from) ?? -1;
            if (index >= 0 &&
                from < sources[index].End)
            {
                return At(sources[index], from, after: false);
            }

            if (index + 1 < sources.Count)
            {
                return At(sources[index + 1], sources[index + 1].Start, after: false);
            }
        }

        return null;
    }

    /// <summary>
    /// The place in the document just after a character of a page's text. An untraced character stands
    /// for the last traced one before it, on this page or an earlier one.
    /// </summary>
    public SourcePosition? After(int page, int offset)
    {
        for (var current = Math.Min(page, pages.Length - 1); current >= 0; current--)
        {
            var sources = pages[current];
            if (sources.Count == 0)
            {
                continue;
            }

            var from = current == page ? offset : int.MaxValue;
            if (Find(current, from) is not { } index)
            {
                continue;
            }

            var source = sources[index];
            return At(source, Math.Min(from, source.End - 1), after: true);
        }

        return null;
    }

    static SourcePosition At(LayerSource source, int offset, bool after)
    {
        var step = after ? 1 : 0;
        if (source.Atomic)
        {
            return new(source.Run, source.Offset + step);
        }

        return new(source.Run, source.Offset + offset - source.Start + step);
    }

    // The last source of a page that starts at or before the offset.
    int? Find(int page, int offset)
    {
        if (page < 0 ||
            page >= pages.Length)
        {
            return null;
        }

        var sources = pages[page];
        var low = 0;
        var high = sources.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (sources[middle].Start <= offset)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (found < 0)
        {
            return null;
        }

        return found;
    }
}
