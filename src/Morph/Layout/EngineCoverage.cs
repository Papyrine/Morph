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
                // Inline images and inline shape groups (grouped drawings embedded in a run) are both
                // emitted and painted, so any plain paragraph is covered. A block-level content control
                // renders as its synthetic paragraph (its resolved value is in that paragraph's runs).
                case ParagraphElement:
                case ContentControlElement:
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
                // A non-wrapping floating text box lays out its content inside its box (the Fragmenter emits
                // the box chrome as a shape and the content as lines); wrapping text boxes need flow exclusions.
                case FloatingTextBoxElement { WrapType: WrapType.None }:
                    break;
                case TableElement table when IsSimpleTable(table):
                    break;
                // A floating table (w:tblpPr) lays out at its own anchored position with simple cells — the
                // Fragmenter positions it and reuses the nested-table layout; it takes no flow space.
                case TableElement { Properties.IsFloating: true } table when HasSimpleCells(table):
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    static bool IsSimpleTable(TableElement table) =>
        !table.Properties.IsFloating && HasSimpleCells(table);

    static bool HasSimpleCells(TableElement table)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                foreach (var element in cell.Content)
                {
                    // A cell holds paragraphs (which may carry inline images or shape groups), a content
                    // control (rendered as its synthetic paragraph), or a nested table that is itself simple
                    // — the Fragmenter lays a nested table out inline at the cell cursor. Other non-paragraph
                    // content is not covered yet.
                    if (element is TableElement nested)
                    {
                        if (!IsSimpleTable(nested))
                        {
                            return false;
                        }
                    }
                    else if (element is not (ParagraphElement or ContentControlElement))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
