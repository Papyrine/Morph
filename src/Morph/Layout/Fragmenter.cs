/// <summary>
/// Flows a document's block content into pages once, backend-independently — the heart of the layout
/// engine (<c>docs/layout-engine-proposal.md</c>, step 3). This first slice handles single-column block
/// flow with **line-level** page breaks: a paragraph too tall for the space left splits at a line
/// boundary and continues on the next page, which the raster backends cannot do today (each moves the
/// whole paragraph). It applies the measured height-model rules from <c>src/page_counts.md</c> —
/// max-collapse paragraph spacing, space-before dropped at an automatically broken page top, and the
/// empty-paragraph mark line — with an exact bottom-of-page fit (no slack: the canonical measurer does
/// not over-measure, so the compensating tolerance the raster path needs is unnecessary here).
///
/// <para>Deferred to later slices, and noted so a document using them is not yet expected to paginate:
/// multi-column sections and column breaks, widow/orphan and keep-next/keep-lines, floats and their wrap
/// exclusions, tables (a sub-layout with row-level splitting), header/footer band height, and even/odd
/// section-break parity. Non-paragraph elements are skipped for now.</para>
/// </summary>
sealed class Fragmenter(CanonicalParagraphMeasurer measurer)
{
    public LaidOutDocument Layout(IReadOnlyList<DocumentElement> elements, PageSettings page)
    {
        var contentTop = (float) page.MarginTop;
        var contentBottom = (float) (page.HeightPoints - page.MarginBottom);
        var contentLeft = (float) page.MarginLeft;
        var contentWidth = (float) page.ContentWidth;

        var pages = new List<LaidOutPage>();
        var lines = new List<PlacedLine>();
        var y = contentTop;
        var atPageTop = true;
        var lastAfter = 0f;
        var currentPageExplicit = false;

        // Emits the in-progress page and starts a fresh one. A page is kept when it has content, when
        // it is a deliberate blank left by an explicit break (Word does not absorb those — ledger
        // experiment 18), or when it is the only page; a natural trailing-overflow blank is dropped.
        void FinishPage(bool nextPageExplicit)
        {
            if (lines.Count > 0 || currentPageExplicit || pages.Count == 0)
            {
                pages.Add(new(pages.Count + 1, page, lines));
            }

            lines = [];
            y = contentTop;
            atPageTop = true;
            lastAfter = 0;
            currentPageExplicit = nextPageExplicit;
        }

        foreach (var element in elements)
        {
            switch (element)
            {
                case PageBreakElement:
                    // A page break starts a page even at the top of one, so N consecutive breaks yield
                    // N blank pages (experiment 18); the resulting page is marked explicit so it survives.
                    FinishPage(nextPageExplicit: true);
                    break;

                case SectionBreakElement { BreakType: not SectionBreakType.Continuous }:
                    // Even/odd parity is a later slice; treated as a plain page break here.
                    if (!atPageTop)
                    {
                        FinishPage(false);
                    }

                    break;

                case ParagraphElement paragraph:
                    var props = paragraph.Properties;
                    if (props.PageBreakBefore && !atPageTop)
                    {
                        FinishPage(false);
                    }

                    var paragraphLines = measurer.LayoutLines(paragraph, contentWidth);
                    var isEmpty = paragraphLines.Count == 1 && paragraphLines[0].Width <= 0;
                    var lineLeft = contentLeft + (float) props.LeftIndentPoints;

                    // Space-before, collapsed with the previous paragraph's after (max, not sum) and
                    // dropped entirely at the top of an automatically broken page. If the collapsed gap
                    // plus the first line overflows, the line-level break below resets the cursor, which
                    // is the same space-before drop applied to the paragraph that moved.
                    if (!atPageTop)
                    {
                        y += Math.Max(lastAfter, (float) props.SpacingBeforePoints);
                    }

                    for (var i = 0; i < paragraphLines.Count; i++)
                    {
                        var line = paragraphLines[i];
                        if (!atPageTop && y + line.Height > contentBottom)
                        {
                            FinishPage(false);
                        }

                        lines.Add(new(lineLeft, y, line.Width, line.Height, paragraph, i));
                        y += line.Height;
                        atPageTop = false;
                    }

                    lastAfter = isEmpty ? 0 : (float) props.SpacingAfterPoints;
                    break;

                // Tables, floats, images, column breaks and continuous sections are later slices.
            }
        }

        // Emit the trailing page. FinishPage keeps it only when it carries content, is an explicit-break
        // blank, or is the first page — so a natural trailing-overflow blank is dropped while a
        // deliberate one survives.
        FinishPage(false);

        return new(pages);
    }
}
