/// <summary>
/// Whether the layout engine (<see cref="Fragmenter"/> → a backend painter) covers a parsed document — the
/// shared capability predicate gating BOTH the raster default path and the opt-in PDF cutover
/// (docs/layout-engine-proposal.md). A covered document paginates through the engine; an uncovered one falls
/// back to the production <c>PageRenderer</c> (raster) or <c>PdfTextEngine</c> (PDF). Successive emission
/// slices widened it from the original block/table/column subset to every one of the 325 corpus documents,
/// so the fallback is now cold for the raster path (PDF still routes through it until that flip).
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
                // PlacedImage — image fill). A wrapping floating image is admitted too: the Fragmenter flows
                // text beside it (RegisterFloatExclusion / ResolveFlowBand). This was the last hold-out, held
                // back not by the wrap but by two measurer defects that made the engine the worse renderer
                // for the corpus's only wrapping-float document — the Ppem grain and the baseline ascent,
                // both since fixed, which took it from 0.037 below the production fallback to 0.016 above.
                case FloatingShapeElement:
                case FloatingImageElement:
                // A non-wrapping floating text box lays out its content inside its box (the Fragmenter emits
                // the box chrome as a shape and the content as lines); wrapping text boxes need flow exclusions.
                case FloatingTextBoxElement { WrapType: WrapType.None }:
                // Unwarped WordArt is Word's inline text box — box chrome plus centred text. A warped one
                // (arch/wave/envelope/…) stays a single figure the painter rasterizes through its backend's
                // IWordArtRasterizer, reusing the warp geometry rather than reimplementing it.
                case WordArtElement:
                case FloatingWordArtElement:
                // A positioned text frame (w:framePr, lifted out of the flow by FrameGrouper) auto-sizes to its
                // paragraphs and paints at its resolved anchor position, taking no flow space.
                case PositionedFrameElement:
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
                    else if (element is WordArtElement)
                    {
                        // WordArt in a cell renders as its box plus centred text, or — warped — as one
                        // rasterized figure.
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
