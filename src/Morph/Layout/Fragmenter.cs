using System.Globalization;

/// <summary>
/// Flows a document's block content into pages once, backend-independently — the heart of the layout
/// engine (<c>docs/layout-engine-proposal.md</c>, step 3). Handles multi-column block flow with
/// **line-level** page/column breaks (a paragraph too tall for the space left splits at a line boundary
/// and continues in the next column or page, which the raster backends cannot do today) and
/// **row-level** table breaks (a table taller than a column flows row by row, re-emitting
/// <c>w:tblHeader</c> rows after each break). Content fills column 0 to the bottom, then column 1, and so
/// on; the last column overflowing starts a new page. It applies the measured height-model rules from
/// <c>src/page_counts.md</c> — max-collapse paragraph spacing, space-before dropped at an automatically
/// broken region (column or page) top, and the empty-paragraph mark line.
///
/// <para>Paragraph flow places a line while its baseline clears the bottom margin, letting the last
/// line's descent and trailing line gap encroach the margin as Word does; table pagination mirrors the
/// raster backend's percentage tolerances so the two paginate a table identically until a canonical row
/// measurement retires the slack.</para>
///
/// <para>Columns are equal-width within a section. A NextPage or even/odd section break starts the next
/// section on a fresh page and adopts its geometry — page size, margins and column count — so each page
/// carries its own <see cref="PageSettings"/> (portrait/landscape switches, per-section margins), and
/// even/odd breaks insert a blank filler page for parity. A Continuous break switching column count flows
/// the new columns from the break point (the newsletter masthead → multi-column body case), each column on
/// that page topping out at the break and resetting to the page top on overflow. A multi-column section is
/// newspaper-flowed — column 0 fills to the bottom, then column 1, and so on — when it ends the document,
/// which matches Word (verified against three_columns, which lands its items 1-14 / 15-29 / 30 across the
/// columns, and two_columns); a multi-column section that a *section break* terminates has its last page's
/// columns balanced to equal heights instead, as Word does (validated against a Word-rendered fixture — six
/// items across three columns become two, two, two). Deferred to later slices, and noted so a document using
/// them is not yet expected to paginate: minimal-tallest-column balancing (the greedy fill targets the
/// average height, so uneven-height columns are approximate) and balancing a region that carries a table,
/// shading or a border; a margin-only continuous change; keep-next (widow/orphan and keep-lines are
/// handled); floats and their wrap exclusions; floating tables; and inline images inside a nested table
/// (nested tables themselves lay out). Other non-paragraph, non-table elements are skipped for now.</para>
/// </summary>
sealed class Fragmenter(CanonicalParagraphMeasurer measurer)
{
    public LaidOutDocument Layout(
        IReadOnlyList<DocumentElement> elements,
        PageSettings page,
        HeaderFooterContent? header = null,
        HeaderFooterContent? footer = null,
        HeaderFooterContent? firstPageHeader = null,
        HeaderFooterContent? firstPageFooter = null,
        HeaderFooterContent? evenPageHeader = null,
        HeaderFooterContent? evenPageFooter = null) =>
        new Flow(measurer, page, header, footer, firstPageHeader, firstPageFooter, evenPageHeader, evenPageFooter).Run(elements);

    // One document's flow state. A fresh instance per Layout call keeps the cursor, the in-progress page
    // and the emitted pages together without leaking between runs (the Fragmenter itself is reusable).
    sealed class Flow(
        CanonicalParagraphMeasurer measurer,
        PageSettings page,
        HeaderFooterContent? header,
        HeaderFooterContent? footer,
        HeaderFooterContent? firstPageHeader,
        HeaderFooterContent? firstPageFooter,
        HeaderFooterContent? evenPageHeader,
        HeaderFooterContent? evenPageFooter)
    {
        // The current section's geometry. Mutable: a section break with new section settings switches these
        // to the new page size, margins and column layout (ApplyGeometry). The initial values are the
        // document's first section, from the constructor's page.
        PageSettings current = page;
        float contentTop = (float) page.MarginTop;
        float contentBottom = (float) (page.HeightPoints - page.MarginBottom);
        float contentHeight = (float) (page.HeightPoints - page.MarginTop - page.MarginBottom);
        float fullContentLeft = (float) page.MarginLeft;
        float fullContentWidth = (float) (page.WidthPoints - page.MarginLeft - page.MarginRight);
        int columnCount = Math.Max(1, page.ColumnCount);
        float columnWidth = (float) page.ColumnWidth;
        float columnSpacing = (float) page.ColumnSpacing;
        readonly List<LaidOutPage> pages = [];

        List<PlacedItem> items = [];
        // Each finished page's body items with the section geometry it was laid out at, kept until the flow
        // ends so the bands can resolve NUMPAGES and each page renders at its own section's size and margins.
        readonly List<(IReadOnlyList<PlacedItem> Items, PageSettings Settings)> bodies = [];
        // Body floating shapes/images, each tagged with the page it was anchored on and whether it paints
        // behind the text; assembled into their page's items once the flow ends.
        readonly List<(int Page, PlacedItem Item, bool Behind)> bodyFloats = [];
        int currentColumn;
        float y;
        // Y where the current page's columns begin. Normally the content top, but a continuous section break
        // that switches column count starts the new columns at the break point (below a full-width masthead),
        // so each column on that page tops out there rather than at the page top. Reset to the content top on
        // every new page.
        float columnTop = (float) page.MarginTop;
        // Top of the current *region* — a fresh column or a fresh page. Space-before is dropped here and a
        // line/row is never pushed off it (nothing better to do), the same rule at a column or page top.
        bool atRegionTop = true;
        float lastAfter;
        // Style and contextual-spacing flag of the previous flow paragraph, for w:contextualSpacing: a
        // contextual paragraph that follows a same-style contextual one collapses the gap between them (its
        // own space-before and the previous paragraph's space-after) — Word's tight address blocks and list
        // runs. A table breaks the run and clears these.
        bool lastContextual;
        string? lastStyleId;
        bool currentPageExplicit;

        // Left edge of the current column, in points from the page's left.
        float ColumnLeft => fullContentLeft + currentColumn * (columnWidth + columnSpacing);

        // Top of a fresh *page* — the first column's region top. Page-level breaks skip when already here.
        bool AtPageTop => atRegionTop && currentColumn == 0;

        // Adopts a new section's page geometry — page size, margins and column layout — recomputing the
        // derived content box and column metrics so subsequent flow uses the new section's dimensions.
        void ApplyGeometry(PageSettings settings)
        {
            current = settings;
            contentTop = (float) settings.MarginTop;
            contentBottom = (float) (settings.HeightPoints - settings.MarginBottom);
            contentHeight = (float) (settings.HeightPoints - settings.MarginTop - settings.MarginBottom);
            fullContentLeft = (float) settings.MarginLeft;
            fullContentWidth = (float) (settings.WidthPoints - settings.MarginLeft - settings.MarginRight);
            columnCount = Math.Max(1, settings.ColumnCount);
            columnWidth = (float) settings.ColumnWidth;
            columnSpacing = (float) settings.ColumnSpacing;
        }

        // A section break. A continuous break keeps the same page; when it switches column count the new
        // columns begin at the break point (below a full-width masthead), so the section flows down from the
        // current cursor rather than the page top. Every other kind starts the new section on a fresh page —
        // finishing the current page unless it is already empty at its top — and adopts the new section's
        // geometry. Even/odd breaks additionally insert a blank filler page when the next page's parity is
        // wrong, as Word does.
        void ApplySectionBreak(SectionBreakElement sectionBreak)
        {
            // A multi-column section terminated by a break has its last page's columns balanced to equal
            // heights, as Word does — unlike a section that ends the document, which stays newspaper-flowed
            // (there is no break to trigger this). Runs before the geometry switch, on the columns just laid.
            if (columnCount > 1)
            {
                BalanceCurrentColumns();
            }

            if (sectionBreak.BreakType == SectionBreakType.Continuous)
            {
                // A same-column continuous break is a flow no-op. A new column count (the masthead → columns
                // case) adopts the new geometry and anchors the columns at the break point: column 0 flows
                // from here to the bottom, each later column tops out here too, and an overflow to the next
                // page resets the columns to its top (FinishPage). Page size stays — Word forces a page-size
                // change to be a next-page break, never continuous.
                if (sectionBreak.NewSectionSettings is { } continuous && Math.Max(1, continuous.ColumnCount) != columnCount)
                {
                    var breakY = y;
                    ApplyGeometry(continuous);
                    columnTop = breakY;
                    y = breakY;
                    currentColumn = 0;
                    atRegionTop = true;
                    lastAfter = 0;
                }

                return;
            }

            if (!AtPageTop)
            {
                FinishPage(false);
            }

            if (sectionBreak.BreakType is SectionBreakType.EvenPage or SectionBreakType.OddPage)
            {
                var wantEven = sectionBreak.BreakType == SectionBreakType.EvenPage;
                if (bodies.Count % 2 == 0 == wantEven)
                {
                    // The next page (bodies.Count + 1) would have the wrong parity, so emit the current empty
                    // page as a deliberate blank and move on — Word inserts that filler page.
                    currentPageExplicit = true;
                    FinishPage(false);
                }
            }

            if (sectionBreak.NewSectionSettings is { } settings)
            {
                ApplyGeometry(settings);
                y = contentTop;
                columnTop = contentTop;
            }
        }

        // Redistributes the current page's multi-column content into equal-height columns, the way Word
        // balances a multi-column section that a section break terminates. The column region is everything
        // placed at or below columnTop (a full-width masthead above it stays put); the lines flow in reading
        // order, so filling each column to the average height (total / columns) and advancing left to right
        // reproduces Word's even split — six items across three columns become two, two, two. Only plain
        // text lines are balanced; a region carrying a table, shading or a border box is left newspaper-
        // flowed (those move as coupled groups, a later slice). Leaves the cursor at the tallest column's
        // bottom so the following content clears the balanced block.
        void BalanceCurrentColumns()
        {
            var regionStart = items.FindIndex(_ => _.Y >= columnTop - 0.01f);
            if (regionStart < 0)
            {
                return;
            }

            var count = items.Count - regionStart;
            if (count == 0)
            {
                return;
            }

            var lines = new PlacedLine[count];
            for (var offset = 0; offset < count; offset++)
            {
                if (items[regionStart + offset] is not PlacedLine line)
                {
                    return;
                }

                lines[offset] = line;
            }

            var totalHeight = 0f;
            foreach (var line in lines)
            {
                totalHeight += line.Height;
            }

            var target = totalHeight / columnCount;
            var rebalanced = new List<PlacedItem>(count);
            var column = 0;
            var columnY = columnTop;
            var maxBottom = columnTop;
            var columnStride = columnWidth + columnSpacing;
            foreach (var line in lines)
            {
                var usedInColumn = columnY - columnTop;
                if (column < columnCount - 1 && usedInColumn > 0 && usedInColumn + line.Height > target + 0.01f)
                {
                    column++;
                    columnY = columnTop;
                }

                var oldColumn = (int) ((line.X - fullContentLeft) / columnStride);
                rebalanced.Add(ShiftLine(line, (column - oldColumn) * columnStride, columnY - line.Y));
                columnY += line.Height;
                maxBottom = Math.Max(maxBottom, columnY);
            }

            items.RemoveRange(regionStart, count);
            items.AddRange(rebalanced);
            y = maxBottom;
            currentColumn = column;
            atRegionTop = false;
        }

        public LaidOutDocument Run(IReadOnlyList<DocumentElement> elements)
        {
            y = contentTop;

            foreach (var element in elements)
            {
                switch (element)
                {
                    case PageBreakElement:
                        // A page break starts a page even at the top of one, so N consecutive breaks yield
                        // N blank pages (ledger experiment 18); the page is marked explicit so it survives
                        // FinishPage's keep test. It resets to the first column.
                        FinishPage(nextPageExplicit: true);
                        break;

                    case ColumnBreakElement:
                        // Move to the next column, or a new page from the last column.
                        AdvanceColumnOrPage();
                        break;

                    case SectionBreakElement sectionBreak:
                        // A continuous break keeps the same page — switching column count starts the new
                        // columns at the break point; every other kind starts the new section on a fresh page
                        // at the new geometry, inserting an even/odd parity filler page when needed.
                        ApplySectionBreak(sectionBreak);
                        break;

                    case ParagraphElement paragraph:
                        PlaceParagraph(paragraph);
                        break;

                    case TableElement table:
                        PlaceTable(table);
                        break;

                    case FloatingImageElement image when DecodableImageBytes(image) is { Length: > 0 } data:
                        bodyFloats.Add((bodies.Count, new PlacedImage(FloatX(image.HorizontalAnchor, image.HorizontalPositionPoints), FloatY(image.VerticalAnchor, image.VerticalPositionPoints), (float) image.WidthPoints, (float) image.HeightPoints, data, image.RotationDegrees, image.FlipHorizontal, image.FlipVertical, image.ClipToEllipse, image.ClipSubpaths, image.Crop), image.BehindText));
                        break;

                    // An image-filled shape (a full-bleed background photo) paints as a plain image — the shape
                    // painter skips image fills. It carries the shape's rotation and flip; a shape image has no
                    // source crop or clip geometry of its own.
                    case FloatingShapeElement shape when shape.ImageData is { Length: > 0 } shapeImage && shape.ImageContentType != "image/svg+xml":
                        bodyFloats.Add((bodies.Count, new PlacedImage(FloatX(shape.HorizontalAnchor, shape.HorizontalPositionPoints), FloatY(shape.VerticalAnchor, shape.VerticalPositionPoints), (float) shape.WidthPoints, (float) shape.HeightPoints, shapeImage, shape.RotationDegrees, shape.FlipHorizontal, shape.FlipVertical), shape.BehindText));
                        break;

                    case FloatingShapeElement shape when shape.ImageData == null && (shape.Gradient != null || shape.FillColorHex != null || shape.LineColorHex != null):
                        bodyFloats.Add((bodies.Count, new PlacedShape(FloatX(shape.HorizontalAnchor, shape.HorizontalPositionPoints), FloatY(shape.VerticalAnchor, shape.VerticalPositionPoints), (float) shape.WidthPoints, (float) shape.HeightPoints, shape), shape.BehindText));
                        break;

                    // Float wrap is a later slice.
                }
            }

            // Emit the trailing page. FinishPage keeps it only when it carries content, is an
            // explicit-break blank, or is the first page — so a natural trailing-overflow blank is dropped
            // while a deliberate one survives.
            FinishPage(false);

            return new(AssemblePages());
        }

        // Emits the in-progress page and starts a fresh one at its first column. A page is kept when it
        // has content, when it is a deliberate blank left by an explicit break (Word does not absorb
        // those), or when it is the only page; a natural trailing-overflow blank is dropped.
        void FinishPage(bool nextPageExplicit)
        {
            // A page is kept when it has visible content, when it is a deliberate blank left by an explicit
            // break, or when it is the only page. Only the body is stored here; the header/footer bands are
            // assembled once the flow finishes and the total page count is known, so a NUMPAGES field can
            // resolve. A page carrying only empty spacer lines — a document-final empty paragraph pushed off
            // the previous page — is a natural overflow blank Word does not render, so it drops.
            if (HasVisibleContent(items) || currentPageExplicit || bodies.Count == 0)
            {
                bodies.Add((items, current));
            }

            items = [];
            currentColumn = 0;
            y = contentTop;
            columnTop = contentTop;
            atRegionTop = true;
            lastAfter = 0;
            currentPageExplicit = nextPageExplicit;
        }

        // A page carries visible content if it has anything beyond empty spacer lines — a table row, an image,
        // a shape, or a line with real text or an inline image. A blank paragraph's whitespace-only line does
        // not count, so a trailing page left with only such lines is dropped as a natural overflow blank.
        static bool HasVisibleContent(IReadOnlyList<PlacedItem> pageItems)
        {
            foreach (var item in pageItems)
            {
                if (item is not PlacedLine line || line.Images.Count > 0 || line.Runs.Any(run => !string.IsNullOrWhiteSpace(run.Text)))
                {
                    return true;
                }
            }

            return false;
        }

        // Wraps each page body in its header background, header/footer text bands and — now the total is
        // known — its resolved page-number fields, producing the final pages.
        List<LaidOutPage> AssemblePages()
        {
            var total = bodies.Count;
            for (var index = 0; index < bodies.Count; index++)
            {
                var pageNumber = index + 1;
                var settings = bodies[index].Settings;
                // The behind-text header images follow the same first/even-page variant as the header text,
                // so a title page's decorative frame or illustration comes from its first-page header.
                var backgroundImages = ResolveHeaderImages(SelectVariant(pageNumber, firstPageHeader, evenPageHeader, header, settings), settings);
                var headerBand = HeaderBand(pageNumber, total, settings);
                var footerBand = FooterBand(pageNumber, total, settings);
                var body = bodies[index].Items;

                // Body floats: behind-text ones paint under the body (over the header background), in-front
                // ones over it. Anchored on the page they were reached on.
                var pageFloats = index;
                var behindFloats = bodyFloats.Where(_ => _.Page == pageFloats && _.Behind).Select(_ => _.Item).ToList();
                var frontFloats = bodyFloats.Where(_ => _.Page == pageFloats && !_.Behind).Select(_ => _.Item).ToList();

                var pageItems = backgroundImages.Count == 0 && headerBand.Count == 0 && footerBand.Count == 0 && behindFloats.Count == 0 && frontFloats.Count == 0
                    ? body
                    : (IReadOnlyList<PlacedItem>) [.. backgroundImages, .. headerBand, .. behindFloats, .. body, .. frontFloats, .. footerBand];
                pages.Add(new(pageNumber, settings, pageItems));
            }

            return pages;
        }

        // Absolute X of a body float: a page-anchored offset is from the page's left; anything else (margin,
        // column, character) is from the content's left. Nested-frame anchors are a later slice.
        float FloatX(HorizontalAnchor anchor, double offset) =>
            anchor == HorizontalAnchor.Page ? (float) offset : fullContentLeft + (float) offset;

        // Absolute Y of a body float: page-anchored from the top edge, margin-anchored from the top margin,
        // otherwise from the current flow cursor (a paragraph/character anchor, approximated by where the
        // float was reached).
        float FloatY(VerticalAnchor anchor, double offset) => anchor switch
        {
            VerticalAnchor.Page => (float) offset,
            VerticalAnchor.Margin => contentTop + (float) offset,
            _ => y + (float) offset
        };

        // The bytes a backend can actually decode: SVG artwork carries a raster equivalent (PdfSharp, like
        // ImageSharp, cannot rasterize SVG), so fall back to it when the primary data is SVG.
        static byte[] DecodableImageBytes(FloatingImageElement image) =>
            image.ContentType == "image/svg+xml" && image.RasterFallbackData is { Length: > 0 } fallback
                ? fallback
                : image.ImageData;

        // Content overflow or a column break: move to the next column, keeping the current page's items,
        // or start a new page when the last column is full.
        void AdvanceColumnOrPage()
        {
            if (currentColumn < columnCount - 1)
            {
                currentColumn++;
                y = columnTop;
                atRegionTop = true;
                lastAfter = 0;
                return;
            }

            FinishPage(false);
        }

        void PlaceParagraph(ParagraphElement paragraph)
        {
            var properties = paragraph.Properties;
            if (properties.PageBreakBefore && !AtPageTop)
            {
                FinishPage(false);
            }

            var paragraphLines = measurer.LayoutLineContents(paragraph, columnWidth);

            // Columns are equal width, so the available width for alignment is constant even as a line
            // spills to the next column; the left edge (ColumnLeft) is read per line, after any advance.
            var availableWidth = columnWidth - (float) properties.LeftIndentPoints - (float) properties.RightIndentPoints;

            // Space-before, collapsed with the previous paragraph's after (max, not sum) and dropped at a
            // region top. If the collapsed gap plus the first line overflows, the line-level break below
            // resets the cursor — the same space-before drop for the moved paragraph. w:contextualSpacing
            // removes the gap entirely between two same-style contextual paragraphs (Word's memo To/From/CC
            // block, list runs) — matching PageRendererBase's contextual collapse.
            var contextualCollapse = properties.ContextualSpacing && lastContextual && properties.StyleId == lastStyleId;
            if (!atRegionTop)
            {
                y += contextualCollapse ? 0f : Math.Max(lastAfter, (float) properties.SpacingBeforePoints);
            }

            // Paragraph-border box (w:pBdr): track the first line's top and last line's bottom so a single
            // border can be stroked around the whole paragraph. If it breaks across a column or page the
            // box would span the gap, so a break disables it (per-fragment borders are a later slice).
            float? borderTop = null;
            var borderBroke = false;

            var lineIndex = 0;
            while (lineIndex < paragraphLines.Count)
            {
                // How many of the remaining lines fit in the current region. A line fits if its baseline
                // clears the bottom margin — Word lets the last line's descent (and trailing gap) encroach
                // the margin rather than pushing it to the next page. The first line at a region top is
                // always taken (nothing better to do than overflow it).
                var fit = 0;
                var probeY = y;
                while (lineIndex + fit < paragraphLines.Count)
                {
                    var candidate = paragraphLines[lineIndex + fit];
                    if (!(atRegionTop && fit == 0) && probeY + candidate.Ascent > contentBottom)
                    {
                        break;
                    }

                    probeY += candidate.Height;
                    fit++;
                }

                // Keep-lines (w:keepLines) holds a whole paragraph together — if it will not all fit here it
                // moves to the next region intact. Otherwise widow/orphan control (Word's default) keeps at
                // least two lines together: one line alone at the bottom (orphan → move the pair) or at the
                // top of the next region (widow → carry one more line). Both apply only when a break actually
                // falls (fit < remaining) and there is somewhere better to move to (not already a region top).
                var remaining = paragraphLines.Count - lineIndex;
                if (!atRegionTop && fit < remaining)
                {
                    if (properties.KeepLines)
                    {
                        fit = 0;
                    }
                    else if (properties.WidowControl && remaining >= 2)
                    {
                        if (fit == 1)
                        {
                            fit = 0;
                        }
                        else if (fit == remaining - 1)
                        {
                            fit = remaining - 2;
                        }
                    }
                }

                if (fit == 0)
                {
                    AdvanceColumnOrPage();
                    continue;
                }

                for (var offset = 0; offset < fit; offset++)
                {
                    var placedIndex = lineIndex + offset;
                    var line = paragraphLines[placedIndex];
                    var indentLeft = ColumnLeft + (float) properties.LeftIndentPoints;
                    var lineLeft = indentLeft + AlignmentOffset(properties.Alignment, availableWidth, line.Width);
                    var baseline = y + line.Ascent;

                    // Paragraph shading (w:shd) fills the paragraph's column box behind the text, regardless
                    // of the text's own width or alignment — a centred title's band still spans the full
                    // column. Emitted before the line so it paints behind; one per line tiles into a band.
                    if (!string.IsNullOrEmpty(properties.BackgroundColorHex))
                    {
                        items.Add(new PlacedShading(indentLeft, y, availableWidth, line.Height, properties.BackgroundColorHex));
                    }

                    items.Add(new PlacedLine(lineLeft, y, line.Width, line.Height, baseline, paragraph, placedIndex, LineRuns(paragraph, line, placedIndex, lineLeft), MapImages(line, lineLeft, baseline)));
                    borderTop ??= y;
                    y += line.Height;
                    atRegionTop = false;
                }

                lineIndex += fit;
                if (lineIndex < paragraphLines.Count)
                {
                    // More lines remain — the paragraph breaks, so its border box would span the gap.
                    AdvanceColumnOrPage();
                    borderBroke = true;
                }
            }

            // Stroke the border around the paragraph's box, expanded by each edge's space (the gap Word
            // leaves between the text and the line). Emitted after the lines so it paints over any shading.
            if (!borderBroke && borderTop is { } boxTop && properties.Borders is { HasAnyBorder: true })
            {
                var left = ColumnLeft + (float) properties.LeftIndentPoints - (float) properties.BorderLeftSpacePoints;
                var top = boxTop - (float) properties.BorderTopSpacePoints;
                var width = availableWidth + (float) properties.BorderLeftSpacePoints + (float) properties.BorderRightSpacePoints;
                var height = y - boxTop + (float) properties.BorderTopSpacePoints + (float) properties.BorderBottomSpacePoints;
                items.Add(new PlacedBorder(left, top, width, height, properties.Borders!));
            }

            // An empty paragraph is a full-height spacer line AND carries its after-spacing into the
            // collapse with the next paragraph (measured against Word: two_columns' title/blank/body gap is
            // line + after + line + after, not line + after + line). It behaves like any other paragraph.
            lastAfter = (float) properties.SpacingAfterPoints;
            lastContextual = properties.ContextualSpacing;
            lastStyleId = properties.StyleId;
        }

        void PlaceTable(TableElement table)
        {
            // Floating tables take no flow space (their own slice); an empty table places nothing.
            if (table.Properties.IsFloating || table.Rows.Count == 0)
            {
                return;
            }

            var colCount = TableLayout.GetColumnCount(table);
            if (colCount == 0)
            {
                return;
            }

            var colWidths = TableLayout.CalculateColumnWidths(table, colCount, columnWidth, measurer);
            var hasVerticalMerge = TableLayout.HasVerticalMerge(table);
            var rowHeights = TableHeightCalculator.CalculateRowHeights(table, colWidths, measurer, hasVerticalMerge, addInteriorBorders: true);

            var tableWidth = 0f;
            foreach (var width in colWidths)
            {
                tableWidth += width;
            }

            var totalHeight = 0f;
            foreach (var height in rowHeights)
            {
                totalHeight += height;
            }

            var tableX = ComputeTableX(table, tableWidth);

            // The previous paragraph's after-spacing precedes the table (a table has no before-spacing to
            // collapse it with), and never applies at a region top.
            if (!atRegionTop)
            {
                y += lastAfter;
            }

            lastAfter = 0;
            // A table breaks the paragraph run, so a contextual paragraph after it does not collapse against
            // one before it.
            lastContextual = false;
            lastStyleId = null;

            // A table over 110% of a column's content height flows row by row (the raster backend's
            // needsRowByRowRendering); otherwise it stays whole and moves as a unit if needed.
            if (totalHeight > contentHeight * 1.10f)
            {
                PlaceTableRowByRow(table, colWidths, rowHeights, colCount, tableX, tableWidth);
                return;
            }

            // Narrow pre-advance for the fixed-height-row letter layouts: lift the whole table onto the
            // next region when it would otherwise clip its exact row, ahead of the softer whole-table move.
            if (!atRegionTop && totalHeight > 0)
            {
                var remaining = Math.Max(0f, contentBottom - y);
                if (totalHeight > remaining + 5f && HasExactRow(table))
                {
                    AdvanceColumnOrPage();
                }
            }

            // Whole-table move: mirrors EnsureSpaceFor(totalHeight − 2%) — a flow table may over-spill the
            // bottom by the shared rounding slack before it is pushed to the next region.
            EnsureSpaceFor(totalHeight - contentHeight * 0.02f);

            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                items.Add(BuildRow(y, table, rowIndex, colWidths, rowHeights, colCount, tableX, tableWidth, false));
                y += rowHeights[rowIndex];
                atRegionTop = false;
            }
        }

        // A table taller than a column, placed row by row. Rows do not split: one that will not fit moves
        // whole to the next region. w:tblHeader rows are re-emitted after each break, and a trailing run of
        // empty rows is absorbed rather than starting a region for them.
        void PlaceTableRowByRow(TableElement table, float[] colWidths, float[] rowHeights, int colCount, float tableX, float tableWidth)
        {
            var headerCount = 0;
            while (headerCount < table.Rows.Count && table.Rows[headerCount].IsHeader)
            {
                headerCount++;
            }

            var lastVisibleRow = table.Rows.Count - 1;
            while (lastVisibleRow >= 0 && !TableLayout.RowHasVisibleContent(table.Rows[lastVisibleRow]))
            {
                lastVisibleRow--;
            }

            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var rowHeight = rowHeights[rowIndex];
                var broke = rowIndex <= lastVisibleRow && EnsureSpaceFor(rowHeight);

                if (broke && headerCount > 0 && rowIndex >= headerCount)
                {
                    for (var headerIndex = 0; headerIndex < headerCount; headerIndex++)
                    {
                        items.Add(BuildRow(y, table, headerIndex, colWidths, rowHeights, colCount, tableX, tableWidth, true));
                        y += rowHeights[headerIndex];
                        atRegionTop = false;
                    }
                }

                items.Add(BuildRow(y, table, rowIndex, colWidths, rowHeights, colCount, tableX, tableWidth, false));
                y += rowHeight;
                atRegionTop = false;
            }
        }

        // Builds a placed row at the current cursor: the row box plus each cell's box, shading, borders and
        // laid-out content. Cell geometry mirrors PageRendererBase.RenderTableRow so the tree matches the
        // raster backend's grid — merge-continuation cells contribute nothing (the originating cell covers
        // them), and a merge-restart cell's box spans the merged rows' heights.
        PlacedTableRow BuildRow(float rowY, TableElement table, int rowIndex, float[] colWidths, float[] rowHeights, int colCount, float tableX, float tableWidth, bool isRepeatedHeader)
        {
            var row = table.Rows[rowIndex];
            var rowHeight = rowHeights[rowIndex];
            var cells = new List<PlacedCell>();
            var cellX = tableX;
            var gridColIndex = 0;

            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = cell.Properties.GridSpan;

                var cellWidth = 0f;
                for (var offset = 0; offset < span && gridColIndex + offset < colCount; offset++)
                {
                    cellWidth += colWidths[gridColIndex + offset];
                }

                if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
                {
                    cellX += cellWidth;
                    gridColIndex += span;
                    continue;
                }

                var cellHeight = cell.Properties.VerticalMerge == VerticalMergeType.Restart
                    ? TableLayout.CalculateVerticalMergeHeight(table, rowIndex, gridColIndex, rowHeights)
                    : rowHeight;

                var padding = TableLayout.GetEffectivePadding(cell.Properties, table.Properties, row);
                var content = LayoutCellContent(cell, cellX + (float) padding.Left, rowY + (float) padding.Top, cellWidth - (float) padding.Horizontal, cellHeight - (float) padding.Vertical, cell.Properties.VerticalAlignment);
                var borders = TableLayout.ResolveCellBorders(cell.Properties, table.Properties, rowIndex, gridColIndex, table.Rows.Count, colCount, row);

                // Behind-text floats (a label template's coloured cell background and freeform blobs) paint
                // before the cell's paragraphs, so prepend them to the content.
                var floatShapes = ResolveCellFloatShapes(cell, cellX, rowY);
                if (floatShapes.Count > 0)
                {
                    content = [.. floatShapes, .. content];
                }

                cells.Add(new PlacedCell(cellX, rowY, cellWidth, cellHeight, cell.Properties.BackgroundColorHex, borders, content));

                cellX += cellWidth;
                gridColIndex += span;
            }

            return new PlacedTableRow(tableX, rowY, tableWidth, rowHeight, table, rowIndex, isRepeatedHeader, cells);
        }

        // Cell-anchored behind-text shapes resolved to absolute boxes: the offset is measured from the
        // cell's top-left (Word anchors these to the cell frame). Only solid-fill / outline shapes render
        // for now; gradient and image fills, in-front-of-text floats, and the paragraph-anchor walk that
        // positions non-cell-top floats are later slices (PdfPainter.PaintShape skips what it can't draw).
        static IReadOnlyList<PlacedItem> ResolveCellFloatShapes(TableCell cell, float cellX, float cellY)
        {
            if (cell.Floats.Count == 0)
            {
                return [];
            }

            var shapes = new List<PlacedItem>();
            foreach (var element in cell.Floats)
            {
                if (element is not FloatingShapeElement shape || !shape.BehindText)
                {
                    continue;
                }

                var shapeX = cellX + (float) shape.HorizontalPositionPoints;
                var shapeY = cellY + (float) shape.VerticalPositionPoints;
                shapes.Add(new PlacedShape(shapeX, shapeY, (float) shape.WidthPoints, (float) shape.HeightPoints, shape));
            }

            return shapes;
        }

        // Lays out a nested table (a table inside a cell) at a fixed position with no page breaks: its
        // columns fit the cell width, its row heights come from the shared calculator (which already
        // measures nested tables when sizing the outer cell), and each row is built at the running Y.
        // Returns the placed rows and the table's total height.
        (IReadOnlyList<PlacedItem> Items, float Height) LayoutNestedTable(TableElement table, float left, float top, float width)
        {
            var colCount = TableLayout.GetColumnCount(table);
            if (colCount == 0 || table.Rows.Count == 0)
            {
                return ([], 0f);
            }

            var colWidths = TableLayout.CalculateColumnWidths(table, colCount, width, measurer);
            var rowHeights = TableHeightCalculator.CalculateRowHeights(table, colWidths, measurer, TableLayout.HasVerticalMerge(table), addInteriorBorders: true);

            var tableWidth = 0f;
            foreach (var colWidth in colWidths)
            {
                tableWidth += colWidth;
            }

            var rows = new List<PlacedItem>();
            var rowY = top;
            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                rows.Add(BuildRow(rowY, table, rowIndex, colWidths, rowHeights, colCount, left, tableWidth, false));
                rowY += rowHeights[rowIndex];
            }

            return (rows, rowY - top);
        }

        // The total height a table would occupy at the given width — the sum of its row heights. Used to
        // anchor a footer band (whose bottom sits a fixed distance above the page edge) before laying it out.
        float NestedTableHeight(TableElement table, float width)
        {
            var colCount = TableLayout.GetColumnCount(table);
            if (colCount == 0 || table.Rows.Count == 0)
            {
                return 0f;
            }

            var colWidths = TableLayout.CalculateColumnWidths(table, colCount, width, measurer);
            var rowHeights = TableHeightCalculator.CalculateRowHeights(table, colWidths, measurer, TableLayout.HasVerticalMerge(table), addInteriorBorders: true);
            var total = 0f;
            foreach (var rowHeight in rowHeights)
            {
                total += rowHeight;
            }

            return total;
        }

        // Stacks a cell's paragraphs from the top of its padded interior, wrapping each to the cell width,
        // then shifts them down for centre/bottom vertical alignment within the available height. No page
        // breaks — the row height already accommodates the content. A nested table lays out inline too.
        IReadOnlyList<PlacedItem> LayoutCellContent(TableCell cell, float contentLeft, float contentTop, float contentWidth, float availableHeight, CellVerticalAlignment verticalAlignment)
        {
            var lines = new List<PlacedItem>();
            var cellY = contentTop;
            var lastCellAfter = 0f;
            var first = true;
            var hasNestedTable = false;

            foreach (var element in cell.Content)
            {
                // A nested table lays out at the cell cursor with no page breaks — the outer row height
                // already accommodates it (TableHeightCalculator measures nested tables). Its rows are
                // PlacedTableRows, which the vertical-alignment shift below cannot move, so a cell holding one
                // stays top-aligned.
                if (element is TableElement nestedTable)
                {
                    cellY += first ? 0 : lastCellAfter;
                    first = false;
                    hasNestedTable = true;
                    var (nestedItems, nestedHeight) = LayoutNestedTable(nestedTable, contentLeft, cellY, contentWidth);
                    lines.AddRange(nestedItems);
                    cellY += nestedHeight;
                    lastCellAfter = 0;
                    continue;
                }

                if (element is not ParagraphElement paragraph)
                {
                    continue;
                }

                var properties = paragraph.Properties;
                // Space-before, collapsed with the previous paragraph's after (max, not sum). Unlike page
                // flow, a cell's FIRST paragraph keeps its space-before — TableHeightCalculator sizes the
                // cell with it, so the content must be positioned with it too or it floats to the top and
                // leaves the gap at the bottom.
                cellY += first
                    ? (float) properties.SpacingBeforePoints
                    : Math.Max(lastCellAfter, (float) properties.SpacingBeforePoints);
                first = false;

                var paragraphLines = measurer.LayoutLineContents(paragraph, contentWidth);
                var isEmpty = paragraphLines.Count == 1 && paragraphLines[0].Width <= 0;
                var textLeft = contentLeft + (float) properties.LeftIndentPoints;
                var availableWidth = contentWidth - (float) properties.LeftIndentPoints - (float) properties.RightIndentPoints;

                for (var lineIndex = 0; lineIndex < paragraphLines.Count; lineIndex++)
                {
                    var line = paragraphLines[lineIndex];
                    var lineLeft = textLeft + AlignmentOffset(properties.Alignment, availableWidth, line.Width);
                    var baseline = cellY + line.Ascent;
                    lines.Add(new PlacedLine(lineLeft, cellY, line.Width, line.Height, baseline, paragraph, lineIndex, LineRuns(paragraph, line, lineIndex, lineLeft), MapImages(line, lineLeft, baseline)));
                    cellY += line.Height;
                }

                lastCellAfter = isEmpty ? 0 : (float) properties.SpacingAfterPoints;
            }

            // Centre/bottom alignment shifts the whole content down within the cell's available height
            // (top alignment leaves it at the padded top). Mirrors PageRendererBase's cell content offset.
            // Skipped when a nested table is present, since ShiftDown only moves text lines.
            var offset = hasNestedTable
                ? 0f
                : verticalAlignment switch
                {
                    CellVerticalAlignment.Center => Math.Max(0f, (availableHeight - (cellY - contentTop)) / 2),
                    CellVerticalAlignment.Bottom => Math.Max(0f, availableHeight - (cellY - contentTop)),
                    _ => 0f
                };

            if (offset > 0.01f)
            {
                for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    lines[lineIndex] = ShiftDown(lines[lineIndex], offset);
                }
            }

            return lines;
        }

        // Moves a placed line (and its inline images) down the page by an offset, for cell vertical
        // alignment. Runs carry no Y of their own — the painter draws them at the line's baseline — so
        // shifting Y and Baseline moves the text with the box.
        static PlacedItem ShiftDown(PlacedItem item, float offset) =>
            item is PlacedLine line ? ShiftLine(line, 0, offset) : item;

        // Shifts a placed line and everything it carries — its runs and inline images — by (dx, dy). Moving a
        // line into another column (dx) when balancing and dropping it for cell vertical alignment (dy only)
        // both go through here.
        static PlacedLine ShiftLine(PlacedLine line, float dx, float dy)
        {
            var runs = new PlacedRun[line.Runs.Count];
            for (var runIndex = 0; runIndex < line.Runs.Count; runIndex++)
            {
                runs[runIndex] = line.Runs[runIndex] with { X = line.Runs[runIndex].X + dx };
            }

            var images = line.Images;
            if (images.Count > 0)
            {
                var shifted = new PlacedImage[images.Count];
                for (var imageIndex = 0; imageIndex < images.Count; imageIndex++)
                {
                    shifted[imageIndex] = images[imageIndex] with { X = images[imageIndex].X + dx, Y = images[imageIndex].Y + dy };
                }

                images = shifted;
            }

            return line with { X = line.X + dx, Y = line.Y + dy, Baseline = line.Baseline + dy, Runs = runs, Images = images };
        }

        // Resolves a header's behind-text floating images to absolute page positions. The full-page
        // decorative frames of letter/label templates are anchored here — page/margin/column horizontally,
        // at the header paragraph vertically — and a header-band-top estimate suffices since they span the
        // whole page. Front-text header art, header text/tables and footers are later slices.
        // Lays out a header or footer's paragraphs as a self-contained band from (left, top), wrapping each
        // to the band width and stacking it with its own spacing — no page breaks (a band fits its margin
        // area). Reuses the body's line mapping, alignment and shading. Tables in a band, borders, and
        // per-page page-number fields are later slices; an empty paragraph adds only its invisible mark line.
        IReadOnlyList<PlacedItem> LayoutBand(IReadOnlyList<DocumentElement> elements, float left, float top, float width)
        {
            var result = new List<PlacedItem>();
            var bandY = top;
            foreach (var element in elements)
            {
                // A header/footer table (a page-number bar, an agenda's footer grid) lays out inline in the
                // band, reusing the nested-table layout.
                if (element is TableElement bandTable)
                {
                    var (tableItems, tableHeight) = LayoutNestedTable(bandTable, left, bandY, width);
                    result.AddRange(tableItems);
                    bandY += tableHeight;
                    continue;
                }

                if (element is not ParagraphElement paragraph)
                {
                    continue;
                }

                var properties = paragraph.Properties;
                bandY += (float) properties.SpacingBeforePoints;
                var paragraphLines = measurer.LayoutLineContents(paragraph, width);
                var availableWidth = width - (float) properties.LeftIndentPoints - (float) properties.RightIndentPoints;
                for (var lineIndex = 0; lineIndex < paragraphLines.Count; lineIndex++)
                {
                    var line = paragraphLines[lineIndex];
                    var indentLeft = left + (float) properties.LeftIndentPoints;
                    var lineLeft = indentLeft + AlignmentOffset(properties.Alignment, availableWidth, line.Width);
                    var baseline = bandY + line.Ascent;
                    if (!string.IsNullOrEmpty(properties.BackgroundColorHex))
                    {
                        result.Add(new PlacedShading(indentLeft, bandY, availableWidth, line.Height, properties.BackgroundColorHex));
                    }

                    result.Add(new PlacedLine(lineLeft, bandY, line.Width, line.Height, baseline, paragraph, lineIndex, LineRuns(paragraph, line, lineIndex, lineLeft), MapImages(line, lineLeft, baseline)));
                    bandY += line.Height;
                }

                bandY += (float) properties.SpacingAfterPoints;
            }

            return result;
        }

        // The header's text band for one page, at the header distance across the full content width. Page 1
        // takes the first-page header when the document has one (a "different first page" title header, which
        // may be empty); other pages take the default. Even-page headers are a later slice.
        IReadOnlyList<PlacedItem> HeaderBand(int pageNumber, int totalPages, PageSettings settings)
        {
            var content = SelectVariant(pageNumber, firstPageHeader, evenPageHeader, header, settings);
            var bandLeft = (float) settings.MarginLeft;
            var bandWidth = (float) (settings.WidthPoints - settings.MarginLeft - settings.MarginRight);
            return content == null
                ? []
                : LayoutBand(SubstitutePageFields(content.Elements, pageNumber, totalPages), bandLeft, (float) settings.HeaderDistance, bandWidth);
        }

        // Picks the header/footer for a page: page 1 of a title-page document takes the first-page variant
        // (which may be null — Word shows none on a title page, so no fall-back to the default); an
        // even-numbered page takes the even variant when the document opts into even/odd, else the default.
        static HeaderFooterContent? SelectVariant(int pageNumber, HeaderFooterContent? first, HeaderFooterContent? even, HeaderFooterContent? standard, PageSettings settings) =>
            pageNumber == 1 && settings.DifferentFirstPage ? first
            : pageNumber % 2 == 0 ? even ?? standard
            : standard;

        // The footer's text band for one page, anchored so its bottom sits the footer distance above the
        // page's bottom edge, with PAGE fields resolved to this page's number. Page 1 takes the first-page
        // footer when present (often empty — Word shows no footer on a title page). NUMPAGES (needs the final
        // total), even-page footers, footer tables and 3-way tab alignment are later slices.
        IReadOnlyList<PlacedItem> FooterBand(int pageNumber, int totalPages, PageSettings settings)
        {
            var content = SelectVariant(pageNumber, firstPageFooter, evenPageFooter, footer, settings);
            if (content == null)
            {
                return [];
            }

            var bandLeft = (float) settings.MarginLeft;
            var bandWidth = (float) (settings.WidthPoints - settings.MarginLeft - settings.MarginRight);
            var elements = SubstitutePageFields(content.Elements, pageNumber, totalPages);
            var height = 0f;
            foreach (var element in elements)
            {
                if (element is ParagraphElement paragraph)
                {
                    height += measurer.MeasureParagraphHeightWithWidth(paragraph, bandWidth);
                }
                else if (element is TableElement table)
                {
                    height += NestedTableHeight(table, bandWidth);
                }
            }

            var footerTop = (float) settings.HeightPoints - (float) settings.FooterDistance - height;
            return LayoutBand(elements, bandLeft, footerTop, bandWidth);
        }

        // Replaces each page-field run's cached text with this page's number (PAGE) or the document total
        // (NUMPAGES / SECTIONPAGES — no section support yet, so both resolve to the whole-document total),
        // cloning only the paragraphs that carry a field. A "Page N of M" footer now reads correctly.
        static IReadOnlyList<DocumentElement> SubstitutePageFields(IReadOnlyList<DocumentElement> elements, int pageNumber, int totalPages)
        {
            var result = new List<DocumentElement>(elements.Count);
            foreach (var element in elements)
            {
                if (element is ParagraphElement paragraph && paragraph.Runs.Any(_ => _.PageField != PageFieldKind.None))
                {
                    var runs = paragraph.Runs
                        .Select(_ => _.PageField switch
                        {
                            PageFieldKind.Page => _.WithText(pageNumber.ToString(CultureInfo.InvariantCulture)),
                            PageFieldKind.NumberOfPages or PageFieldKind.SectionPages => _.WithText(totalPages.ToString(CultureInfo.InvariantCulture)),
                            _ => _
                        })
                        .ToList();
                    result.Add(new ParagraphElement
                    {
                        Runs = runs,
                        Properties = paragraph.Properties,
                        IsAnchorOnlyMark = paragraph.IsAnchorOnlyMark,
                        IsCollapsedCellMark = paragraph.IsCollapsedCellMark
                    });
                }
                else
                {
                    result.Add(element);
                }
            }

            return result;
        }

        static IReadOnlyList<PlacedImage> ResolveHeaderImages(HeaderFooterContent? header, PageSettings page)
        {
            if (header == null)
            {
                return [];
            }

            var images = new List<PlacedImage>();
            var marginLeft = (float) page.MarginLeft;
            var headerTop = (float) page.HeaderDistance;

            foreach (var element in header.Elements)
            {
                if (element is not FloatingImageElement image || !image.BehindText || DecodableImageBytes(image) is not { Length: > 0 } data)
                {
                    continue;
                }

                var imageX = image.HorizontalAnchor == HorizontalAnchor.Page
                    ? (float) image.HorizontalPositionPoints
                    : marginLeft + (float) image.HorizontalPositionPoints;
                var imageY = image.VerticalAnchor switch
                {
                    VerticalAnchor.Page => (float) image.VerticalPositionPoints,
                    VerticalAnchor.Margin => (float) page.MarginTop + (float) image.VerticalPositionPoints,
                    _ => headerTop + (float) image.VerticalPositionPoints
                };

                images.Add(new PlacedImage(imageX, imageY, (float) image.WidthPoints, (float) image.HeightPoints, data));
            }

            return images;
        }

        // Table X within the current column, by w:jc alignment: centred and right collapse the indent into
        // the slack, left applies w:tblInd — matching PageRendererBase.ComputeTableX (non-floating).
        float ComputeTableX(TableElement table, float tableWidth)
        {
            var slack = columnWidth - tableWidth;
            return table.Properties.Alignment switch
            {
                TextAlignment.Center => ColumnLeft + Math.Max(0, slack / 2),
                TextAlignment.Right => ColumnLeft + Math.Max(0, slack),
                _ => ColumnLeft + (float) table.Properties.IndentPoints
            };
        }

        // Mirrors PageRendererBase.EnsureSpaceFor: advances to the next column or page when the height will
        // not fit in the space left (a 2% rounding slack via HasSpaceFor), unless already at a region top
        // or the height exceeds a whole column. Returns whether it broke.
        bool EnsureSpaceFor(float height)
        {
            if (height > contentHeight || atRegionTop || HasSpaceFor(height))
            {
                return false;
            }

            AdvanceColumnOrPage();
            return true;
        }

        bool HasSpaceFor(float height) => y + height <= contentBottom + contentHeight * 0.02f;

        static bool HasExactRow(TableElement table)
        {
            foreach (var row in table.Rows)
            {
                if (row.IsExactHeight)
                {
                    return true;
                }
            }

            return false;
        }

        // The X offset that aligns a line of the given width within the available width. Centre and right
        // shift the whole line; left and justify sit at the left edge (justify's inter-word slack is a
        // later slice). A line wider than the available width (rare — an unbreakable word) is not shifted.
        static float AlignmentOffset(TextAlignment alignment, float availableWidth, float lineWidth)
        {
            var slack = availableWidth - lineWidth;
            if (slack <= 0)
            {
                return 0;
            }

            return alignment switch
            {
                TextAlignment.Center => slack / 2,
                TextAlignment.Right => slack,
                _ => 0
            };
        }

        // The runs to paint for a line: its text run segments, plus — on the first line of a list
        // paragraph — the list marker positioned in the hanging-indent gutter to the left of the text.
        IReadOnlyList<PlacedRun> LineRuns(ParagraphElement paragraph, LaidOutLine line, int lineIndex, float lineLeft)
        {
            var runs = MapRuns(line, lineLeft);
            if (lineIndex == 0 && paragraph.Properties.Numbering is { Text.Length: > 0 } numbering)
            {
                return [MarkerRun(paragraph, numbering, lineLeft), .. runs];
            }

            return runs;
        }

        // The list marker as a placed run: its text in the bullet or number font, a hanging indent to the
        // left of the text (or right-aligned just before it when there is no hanging indent). Font, colour
        // and position mirror PdfTextEngine's marker placement; the paragraph's LeftIndent already sets the
        // text edge, so the marker offsets back from there.
        PlacedRun MarkerRun(ParagraphElement paragraph, NumberingInfo numbering, float lineLeft)
        {
            var firstProperties = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Properties : new RunProperties();
            var useBulletFont = FontHelpers.UseBulletFont(numbering.Text, numbering.FontFamily);
            var markerProperties = new RunProperties
            {
                FontFamily = useBulletFont ? "Morph Bullets" : firstProperties.FontFamily,
                FontSizePoints = firstProperties.FontSizePoints,
                Bold = !useBulletFont && firstProperties.Bold,
                ColorHex = numbering.ColorHex ?? firstProperties.ColorHex
            };

            var markerWidth = measurer.MeasureRunWidth(numbering.Text, markerProperties);
            var hanging = (float) paragraph.Properties.HangingIndentPoints;
            var markerX = hanging > 0.01f
                ? lineLeft - hanging
                : lineLeft - markerWidth - 3f;

            return new PlacedRun(markerX, markerWidth, numbering.Text, markerProperties);
        }

        // Projects a laid-out line's run segments to placed runs at absolute X (the line's left edge plus
        // each segment's canonical offset). Shared by page-flow and cell-flow line placement.
        static PlacedRun[] MapRuns(LaidOutLine line, float lineLeft)
        {
            var runs = new PlacedRun[line.Runs.Count];
            for (var runIndex = 0; runIndex < line.Runs.Count; runIndex++)
            {
                var run = line.Runs[runIndex];
                runs[runIndex] = new PlacedRun(lineLeft + run.X, run.Width, run.Text, run.Properties, run.Leader);
            }

            return runs;
        }

        // Projects a laid-out line's inline images to placed images: the line's left edge plus each
        // image's offset, with its bottom sitting on the text baseline.
        static IReadOnlyList<PlacedImage> MapImages(LaidOutLine line, float lineLeft, float baseline)
        {
            if (line.Images.Count == 0)
            {
                return [];
            }

            var images = new PlacedImage[line.Images.Count];
            for (var imageIndex = 0; imageIndex < line.Images.Count; imageIndex++)
            {
                var image = line.Images[imageIndex];
                images[imageIndex] = new PlacedImage(lineLeft + image.X, baseline - image.Height, image.Width, image.Height, image.Data, image.RotationDegrees, image.FlipHorizontal, image.FlipVertical, Crop: image.Crop);
            }

            return images;
        }
    }
}
