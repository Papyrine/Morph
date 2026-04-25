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
    /// </summary>
    public static void Handle(
        SectionBreakElement sectionBreak,
        RenderContextBase context,
        Action finishCurrentPage,
        Action startNewExplicitPage)
    {
        switch (sectionBreak.BreakType)
        {
            case SectionBreakType.NextPage:
                finishCurrentPage();
                ApplySectionSettings(sectionBreak.NewSectionSettings, context);
                startNewExplicitPage();
                break;

            case SectionBreakType.Continuous:
                // Continuous break — apply new settings but stay on the same page.
                ApplySectionSettings(sectionBreak.NewSectionSettings, context);
                // Reset to first column if column count changed.
                context.ResetColumn();
                break;

            case SectionBreakType.EvenPage:
                finishCurrentPage();
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
                finishCurrentPage();
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
