/// <summary>
/// Whether the layout engine (<see cref="Fragmenter"/> → a backend painter) covers a parsed document — the
/// shared capability predicate gating BOTH the raster default path and the opt-in PDF cutover
/// (docs/layout-engine-proposal.md). A covered document paginates through the engine; an uncovered one falls
/// back to the production <c>PageRenderer</c> (raster) or <c>PdfTextEngine</c> (PDF). Successive emission
/// slices widened it from the original block/table/column subset to 321 of the 325 corpus documents; the four
/// left out are the two warped-WordArt test documents, one wrapping float (an exclusion the Fragmenter does
/// not emit), and one positioned frame.
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
                // PlacedImage — image fill).
                //
                // A wrapping floating image is held back, though the Fragmenter now DOES flow text beside one
                // (RegisterFloatExclusion / ResolveFlowBand, verified to lift the corpus's only such document
                // by +0.013 over ignoring the wrap). The blocker is no longer the wrap: image_wrap_square is
                // 11pt-dense, the size where the canonical measurer's integer-ppem grain under-measures by
                // ~1.8%, so it fits more words per line than Word and the page compresses — the engine lands
                // 0.037 below the production fallback whether or not the wrap is honoured. Admitting it would
                // trade real fidelity for a coverage count. Revisit when the Ppem grain closes
                // ("Remaining work" item 1 in docs/layout-engine-proposal.md).
                case FloatingShapeElement:
                case FloatingImageElement { WrapType: WrapType.None }:
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
