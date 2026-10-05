/// <summary>
/// The fully-paginated output of the layout pass (<c>docs/layout-engine.md</c>): a document
/// broken into pages once, backend-independently, that each backend then merely paints. Computing this
/// a single time — rather than re-deciding pagination inside three render loops — is what collapses
/// the page-count knife-edges in <c>src/page_counts.md</c>.
/// </summary>
sealed record LaidOutDocument(IReadOnlyList<LaidOutPage> Pages)
{
    /// <summary>
    /// The same document restricted to <paramref name="range"/> (1-based, inclusive), or unchanged
    /// when it is null.
    ///
    /// Safe to apply only because pagination is already fully resolved: the fragmenter has assembled
    /// each page's header and footer bands and baked its page-number fields against the true total,
    /// so dropping pages here changes which are painted and nothing else. A range extending past the
    /// last page simply keeps everything from its start.
    /// </summary>
    public LaidOutDocument Restrict(PageRange? range)
    {
        if (range is { } bounds)
        {
            return new(Pages.Where(_ => bounds.Contains(_.Number)).ToArray());
        }

        return this;
    }

    /// <summary>
    /// The same document with every page that asks for it
    /// (<see cref="PageSettings.FitHeightToContent"/>) ending under the last thing placed on it,
    /// and its bottom margin. Unchanged when no page asks, which is every document but a workbook
    /// drawn a sheet to an image.
    ///
    /// A step after layout rather than part of it, because nothing about where an item is placed
    /// depends on it: items are positioned from the top of the page, so a page cut short below them
    /// paints exactly what the tall one would have, on a canvas that is not mostly empty.
    /// </summary>
    /// <param name="dpi">
    /// The resolution the pages will be painted at, which is what says whether a fitted page is one
    /// an image can hold.
    /// </param>
    public LaidOutDocument FitToContent(int dpi)
    {
        if (!Pages.Any(_ => _.Settings.FitHeightToContent))
        {
            return this;
        }

        var pages = new LaidOutPage[Pages.Count];
        for (var index = 0; index < pages.Length; index++)
        {
            pages[index] = Fit(Pages[index], dpi);
        }

        return new(pages);
    }

    static LaidOutPage Fit(LaidOutPage page, int dpi)
    {
        var settings = page.Settings;
        if (!settings.FitHeightToContent)
        {
            return page;
        }

        var bottom = settings.MarginTop;
        foreach (var item in page.Items)
        {
            bottom = Math.Max(bottom, item.Y + item.Height);
        }

        // Never less than a pixel, which is what a page with nothing on it and no margins comes to
        var height = Math.Max(bottom + settings.MarginBottom, 72.0 / dpi);

        // A bitmap is addressed with an int, at four bytes a pixel. Said here, where the page is
        // known, rather than left to the allocation, which fails without saying which page or why
        var widthPixels = (long) (settings.WidthPoints * dpi / 72.0);
        var heightPixels = (long) (height * dpi / 72.0);
        if (widthPixels * heightPixels * 4 > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"Page {page.Number} is {widthPixels} x {heightPixels} pixels at {dpi} dpi, which is more than one image can hold. Lower the Dpi, or use SheetPagination.PrintLayout, which breaks a long sheet into pages.");
        }

        return page with
        {
            Settings = settings with
            {
                HeightPoints = height
            }
        };
    }

    /// <summary>
    /// The page a render context is created for. That is the document's own first page for every
    /// document but one whose pages are fitted to their content, where the first page's settings
    /// still hold the room it was laid out in rather than its size: the largest of the fitted pages
    /// each way stands in, so whatever a painter sizes from the context is large enough for any of
    /// them.
    /// </summary>
    public PageSettings ContextSettings(PageSettings document)
    {
        if (!document.FitHeightToContent ||
            Pages.Count == 0)
        {
            return document;
        }

        return document with
        {
            WidthPoints = Pages.Max(_ => _.Settings.WidthPoints),
            HeightPoints = Pages.Max(_ => _.Settings.HeightPoints)
        };
    }
}
