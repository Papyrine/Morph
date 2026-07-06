/// <summary>
/// Shared section-break dispatch and page-parity logic. Backends supply their own
/// page-finish / page-start primitives; this helper owns the rules that decide
/// when those run, including even/odd parity adjustments.
/// </summary>
static class SectionBreakHandler
{
    /// <summary>
    /// Applies a <see cref="SectionBreakElement"/> to <paramref name="context"/>.
    /// Calls <paramref name="finishCurrentPage"/> when the current page must end and
    /// <paramref name="startNewExplicitPage"/> when a new page must start. The latter
    /// is also responsible for marking the new page as resulting from an explicit break
    /// so blank-page discard logic doesn't drop it.
    /// <paramref name="isCurrentPageBlank"/> reports whether the current page has any
    /// significant content; when blank, <paramref name="discardCurrentPage"/> is called
    /// in place of <paramref name="finishCurrentPage"/> so trailing whitespace overflow
    /// from the previous section doesn't waste a sheet.
    /// </summary>
    public static void Handle(
        SectionBreakElement sectionBreak,
        RenderContextBase context,
        Action finishCurrentPage,
        Action startNewExplicitPage,
        Func<bool> isCurrentPageBlank,
        Action discardCurrentPage)
    {
        switch (sectionBreak.BreakType)
        {
            case SectionBreakType.NextPage:
                EndCurrentPage(isCurrentPageBlank, finishCurrentPage, discardCurrentPage);
                ApplySectionSettings(sectionBreak.NewSectionSettings, context);
                startNewExplicitPage();
                break;

            case SectionBreakType.Continuous:
                // A continuous break normally stays on the same page. But when the previous section
                // already filled the page — typically a page-height table that ran to (or past) the
                // bottom margin — Word flows the next section onto a new page rather than piling it
                // on top of the overflow. Without this the following section renders below the
                // bottom margin, off the sheet, and is silently lost.
                if (!isCurrentPageBlank() && context.CurrentY >= context.ContentBottom)
                {
                    finishCurrentPage();
                    ApplySectionSettings(sectionBreak.NewSectionSettings, context);
                    startNewExplicitPage();
                }
                else
                {
                    ApplySectionSettings(sectionBreak.NewSectionSettings, context);
                    // Reset to first column if column count changed.
                    context.ResetColumn();
                }

                break;

            case SectionBreakType.EvenPage:
                EndCurrentPage(isCurrentPageBlank, finishCurrentPage, discardCurrentPage);
                ApplySectionSettings(sectionBreak.NewSectionSettings, context);
                startNewExplicitPage();
                // If the new page is odd, advance once more so content lands on an even page.
                if (context.CurrentPageNumber % 2 != 0)
                {
                    finishCurrentPage();
                    startNewExplicitPage();
                }

                break;

            case SectionBreakType.OddPage:
                EndCurrentPage(isCurrentPageBlank, finishCurrentPage, discardCurrentPage);
                ApplySectionSettings(sectionBreak.NewSectionSettings, context);
                startNewExplicitPage();
                // If the new page is even, advance once more so content lands on an odd page.
                if (context.CurrentPageNumber % 2 == 0)
                {
                    finishCurrentPage();
                    startNewExplicitPage();
                }

                break;

            case SectionBreakType.NextColumn:
                // Move to next column, or to a new page if no more columns are available.
                ApplySectionSettings(sectionBreak.NewSectionSettings, context);
                if (!context.MoveToNextColumn())
                {
                    finishCurrentPage();
                    startNewExplicitPage();
                }

                break;
        }
    }

    static void EndCurrentPage(Func<bool> isCurrentPageBlank, Action finishCurrentPage, Action discardCurrentPage)
    {
        if (isCurrentPageBlank())
        {
            // The current page is just trailing-whitespace overflow from the previous
            // section. Word doesn't waste a sheet on this — drop it so the new section's
            // first page lands on what would otherwise have been a blank intermediate page.
            discardCurrentPage();
        }
        else
        {
            finishCurrentPage();
        }
    }

    /// <summary>
    /// Applies new <see cref="PageSettings"/> from a section break, refreshing line
    /// numbering as required.
    /// </summary>
    public static void ApplySectionSettings(PageSettings? settings, RenderContextBase context)
    {
        if (settings != null)
        {
            context.UpdatePageSettings(settings);
            context.ResetLineNumbersForSection();
        }
    }
}
