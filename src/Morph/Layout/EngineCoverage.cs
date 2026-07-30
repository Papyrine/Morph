/// <summary>
/// Whether the layout engine (<see cref="Fragmenter"/> → <c>PdfPainter</c>) covers a parsed document —
/// the capability predicate that routes the PDF cutover (docs/layout-engine-proposal.md, "The PDF cutover
/// (step 5)"). Conservative by design: it admits the block/table/column subset the painter renders at
/// parity with Word (the <c>PdfPainterFidelityTests</c> 0.942-SSIM set), and <c>PdfRenderer</c> falls back
/// to <c>PdfTextEngine</c> for everything else. Each Phase C emission slice (WordArt, floats, floating
/// tables, form fields, ...) widens what is admitted.
/// </summary>
static class EngineCoverage
{
    public static bool Covers(ParsedDocument document)
    {
        foreach (var element in document.Elements)
        {
            switch (element)
            {
                // Inline images are emitted and painted, so a plain paragraph is covered; an inline shape
                // group (WordArt, grouped drawing) is not yet, and disqualifies the document.
                case ParagraphElement paragraph when !HasInlineArt(paragraph):
                case PageBreakElement:
                case ColumnBreakElement:
                // The Fragmenter paginates every section-break kind (NextPage/Even/Odd advance and re-lay at
                // the new geometry, Continuous switches columns at the break point); each page records the
                // settings it was laid at, so a painter that sizes per page renders a mid-document page-size
                // or column change.
                case SectionBreakElement:
                    break;
                // Behind/in-front floating shapes carry no text wrap; the Fragmenter lays them out by anchor
                // into the page's float items and every painter draws them (solid, gradient, or — routed to
                // PlacedImage — image fill). A floating image is admitted only when it does not wrap text:
                // Square/Tight/Through/TopAndBottom need flow exclusions the engine does not emit yet.
                case FloatingShapeElement:
                case FloatingImageElement { WrapType: WrapType.None }:
                    break;
                case TableElement table when IsSimpleTable(table):
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    static bool IsSimpleTable(TableElement table)
    {
        if (table.Properties.IsFloating)
        {
            return false;
        }

        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                foreach (var element in cell.Content)
                {
                    if (element is not ParagraphElement paragraph || HasInlineArt(paragraph))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    static bool HasInlineArt(ParagraphElement paragraph) =>
        paragraph.Runs.Any(_ => _.InlineShapeGroup != null);
}
