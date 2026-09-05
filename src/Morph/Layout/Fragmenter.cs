/// <summary>
/// Flows a document's block content into pages once, backend-independently — the heart of the layout
/// engine (<c>docs/layout-engine.md</c>, step 3). Handles multi-column block flow with
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
/// handled); float wrap exclusions (square/tight — floats themselves and floating tables lay out); and
/// inline images inside a nested table (nested tables themselves lay out). Other non-paragraph,
/// non-table elements are skipped for now.</para>
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
        // Set in Run from the first page's reserved top, which cannot be a field initializer: HeaderReservedTop
        // measures the header band, so it reads the caches this class owns. Three initializers used to compute
        // that one value three times over.
        float contentTop;
        float contentBottom = (float) (page.HeightPoints - page.MarginBottom);
        float contentHeight;
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
        // Absolutely-positioned body floats (page/margin anchor) awaiting page resolution: their element is
        // reached before the content they belong to has flowed, so the page is only known once the next
        // visible line or row commits (a page-2 background is declared before page-1 finishes). Resolved into
        // bodyFloats there; a flow-anchored float never lands here — its page is the emit-time cursor page.
        readonly List<(PlacedItem Item, bool Behind)> pendingFloats = [];
        // Boxes a wrapping float (wp:wrapSquare / wrapTight / wrapThrough / wrapTopAndBottom) carves out of
        // the text measure, in absolute page points. Page-scoped: cleared when a page starts, since a float
        // never carries its exclusion across a break. A wrapNone or behind-text float registers nothing —
        // overlap is its design. Mirrors RenderContextBase's list, which the production renderers consume.
        readonly List<FloatExclusion> floatExclusions = [];
        int currentColumn;
        float y;
        // Y where the current page's columns begin. Normally the content top, but a continuous section break
        // that switches column count starts the new columns at the break point (below a full-width masthead),
        // so each column on that page tops out there rather than at the page top. Reset to the content top on
        // every new page. Set in Run alongside contentTop.
        float columnTop;
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
        // True while the current page was started by a non-continuous section break — Word KEEPS a
        // paragraph's spacing-before at the top of such a page (a new page setup), unlike a page
        // reached by automatic overflow (business-plans/08's page 2 ran 22pt high without this).
        // Mirrors the deleted production ShouldSuppressPageTopSpacingBefore's section-break carve-out.
        bool currentPageSectionStart;
        // The trailing after-spacing of the page that just finished. Word's flow adds a paragraph's
        // after when it completes and the NEXT paragraph adds only the excess of its before — and that
        // carry survives a section-break page boundary: business-plans/08's page-2 Heading1
        // (before=30pt) sits 22pt below the margin because the previous page's 8pt after already
        // "spent" part of it. Captured in FinishPage before lastAfter resets.
        float pageCarriedAfter;

        // Lines counted so far for w:lnNumType numbering — body flow lines of non-suppressed
        // paragraphs. Reset per page (restart="newPage") in FinishPage and per section
        // (restart="newSection") in ApplySectionBreak; "continuous" never resets.
        int lineNumberCount;

        // The open w:pBdr border run: consecutive paragraphs whose borders, border spaces and indents all
        // match get ONE box around the lot, not one box each (see ParagraphProperties.SharesBorderGroupWith).
        // Held open across paragraphs because the box's bottom is not known until the run ends — the run is
        // emitted by FlushBorderRun. Null when no bordered run is open.
        //
        // Top/Bottom are the first member's first line top and the last member's last line bottom, so every
        // gap BETWEEN members — including their spacing-after — falls inside the box, which is what Word
        // draws (probe group E: two paragraphs 12pt apart share one box spanning the gap).
        ParagraphProperties? borderRunProperties;
        float borderRunTop;
        float borderRunBottom;
        // The run's content width, taken from its first member. Members share indents by construction, so
        // this only differs if a wrapping float narrows a later member's measure — rare enough that Word's
        // behaviour there is unprobed, and the first member's measure is the honest default.
        float borderRunWidth;
        // Y of each internal member boundary that a visible w:between edge rules.
        readonly List<float> borderRunBetweens = [];
        // Where in `items` the run's first member began, so a shaded box's fill can be INSERTED
        // there — behind the member lines already placed — rather than appended over them.
        int borderRunItemsIndex;

        // Left edge of the current column, in points from the page's left.
        float ColumnLeft => fullContentLeft + currentColumn * (columnWidth + columnSpacing);

        // Top of a fresh *page* — the first column's region top. Page-level breaks skip when already here.
        bool AtPageTop => atRegionTop && currentColumn == 0;

        // Adopts a new section's page geometry — page size, margins and column layout — recomputing the
        // derived content box and column metrics so subsequent flow uses the new section's dimensions.
        void ApplyGeometry(PageSettings settings)
        {
            current = settings;
            contentTop = HeaderReservedTop(settings, header);
            contentBottom = (float) (settings.HeightPoints - settings.MarginBottom);
            contentHeight = contentBottom - contentTop;
            fullContentLeft = (float) settings.MarginLeft;
            fullContentWidth = (float) (settings.WidthPoints - settings.MarginLeft - settings.MarginRight);
            columnCount = Math.Max(1, settings.ColumnCount);
            columnWidth = (float) settings.ColumnWidth;
            columnSpacing = (float) settings.ColumnSpacing;
        }

        // Where the body starts. A positive w:top margin is a MINIMUM (ECMA-376 §17.6.11): a header whose
        // content, laid out from the header distance, reaches past the margin pushes the body down to clear
        // it — so a two-line header no longer overlaps the first body line. A negative w:top
        // (TopMarginIsAbsolute) pins the body at the margin and lets the header overlap instead. Reserves for
        // the default header (per-page first/even variants are a later refinement), mirroring how
        // The deleted production render parked the body cursor below the rendered header.
        //
        // A header TABLE counts toward that height exactly as a paragraph does — LayoutBand stacks the two
        // the same way, and FooterBand has always measured both. Skipping tables here left the body reserving
        // only the header's text: a banner header whose masthead is a shaded one-column table (a protective
        // marking over a colour bar) reserved two lines and let the first body heading land inside the bar.
        float HeaderReservedTop(PageSettings settings, HeaderFooterContent? content)
        {
            var marginTop = (float) settings.MarginTop;
            if (settings.TopMarginIsAbsolute || content == null)
            {
                return marginTop;
            }

            var bandWidth = (float) (settings.WidthPoints - settings.MarginLeft - settings.MarginRight);
            var headerHeight = 0f;
            foreach (var element in content.Elements)
            {
                if (element is ParagraphElement paragraph)
                {
                    headerHeight += measurer.MeasureParagraphHeightWithWidth(paragraph, bandWidth);
                }
                else if (element is TableElement table)
                {
                    headerHeight += NestedTableHeight(table, bandWidth);
                }
            }

            return Math.Max(marginTop, (float) settings.HeaderDistance + headerHeight);
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

            // w:lnNumType restart="newSection" numbers each section from Start again; "continuous"
            // carries the count across, and "newPage" resets in FinishPage anyway.
            if (current.LineNumbers is { Restart: LineNumberRestart.NewSection })
            {
                lineNumberCount = 0;
            }

            // The page this break started keeps a leading paragraph's spacing-before (see the field).
            currentPageSectionStart = true;
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
            // The body starts below whatever the FIRST page's header variant reserves — a different question
            // from ApplyGeometry's, which adopts a later section's plain header. Read off `current` rather
            // than the primary-constructor `page` (the same settings at this point): taking the parameter
            // captures it into the type while a field initializer also uses it, which is CS9124.
            contentTop = HeaderReservedTop(current, SelectVariant(1, firstPageHeader, evenPageHeader, header, current));
            contentHeight = contentBottom - contentTop;
            columnTop = contentTop;
            y = contentTop;

            foreach (var element in elements)
            {
                // Anything but another flow paragraph ends an open border run — a table, a rule, a break.
                // Out-of-flow floats are exempt: they take no flow space, so they cannot come between two
                // members of a run the way a table can.
                if (element is not (ParagraphElement or
                    ContentControlElement or
                    FloatingImageElement or
                    FloatingShapeElement or
                    FloatingWordArtElement))
                {
                    FlushBorderRun();
                }

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

                    // A block-level content control renders as its synthetic paragraph (the parser resolved
                    // its value — checkbox glyph, dropdown selection, formatted date, plain text — into runs).
                    case ContentControlElement {CellParagraph: { } controlParagraph}:
                        PlaceParagraph(controlParagraph);
                        break;

                    case TableElement {Properties.IsFloating: true} table:
                        PlaceFloatingTable(table);
                        break;

                    case TableElement table:
                        PlaceTable(table);
                        break;

                    case FloatingImageElement image when DecodableImageBytes(image) is {Length: > 0}:
                        EmitBodyFloat(image, image.VerticalAnchor, image.AnchorParagraph);
                        break;

                    // An image-filled shape (a full-bleed background photo) paints as a plain image — the shape
                    // painter skips image fills. It carries the shape's rotation and flip; a shape image has no
                    // source crop or clip geometry of its own.
                    case FloatingShapeElement {ImageData.Length: > 0} shape when shape.ImageContentType != "image/svg+xml":
                        EmitBodyFloat(shape, shape.VerticalAnchor, shape.AnchorParagraph);
                        break;

                    case FloatingShapeElement {ImageData: null} shape when shape.Gradient != null || shape.FillColorHex != null || shape.LineColorHex != null:
                        EmitBodyFloat(shape, shape.VerticalAnchor, shape.AnchorParagraph);
                        break;

                    case FloatingTextBoxElement {WrapType: WrapType.None} textBox:
                        PlaceTextBox(textBox);
                        break;

                    case WordArtElement wordArt:
                        PlaceWordArt(wordArt);
                        break;

                    // Floating WordArt is absolutely positioned (no box chrome), like the other body floats —
                    // it takes no flow space.
                    case FloatingWordArtElement floatingWordArt:
                        PlaceFloatingWordArt(floatingWordArt);
                        break;

                    case PositionedFrameElement frame:
                        PlaceFrame(frame);
                        break;

                    // An HTML <hr>: a 0.75pt gray line across the content width in a 6pt slot,
                    // mirroring the production RenderHorizontalRule geometry (line at slot middle).
                    case HorizontalRuleElement:
                        EnsureSpaceFor(6);
                        items.Add(
                            new PlacedBorder(
                                ColumnLeft,
                                y + 3,
                                columnWidth,
                                0,
                                new()
                                {
                                    Top = new()
                                    {
                                        IsVisible = true,
                                        WidthPoints = 0.75,
                                        ColorHex = "A0A0A0"
                                    }
                                }));
                        y += 6;
                        atRegionTop = false;
                        lastAfter = 0;
                        break;

                    // Float wrap is a later slice.
                }
            }

            // The body ended, so close the last border run before the trailing floats append — the box
            // belongs under them, not over them.
            FlushBorderRun();

            // A deferred anchored float whose anchor paragraph never reached PlaceParagraph (lifted into
            // a positioned frame, or suppressed) settles at the cursor — the pre-deferral behaviour.
            foreach (var orphanedFloats in anchorDeferredFloats.Values)
            {
                foreach (var orphaned in orphanedFloats)
                {
                    EmitBodyFloatAt(orphaned, y);
                }
            }

            anchorDeferredFloats.Clear();

            // Any float still deferred had no visible content after it — settle it on the in-progress final
            // page before that page is emitted.
            ResolvePendingFloats();

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
            // The page ends the open border run — and the box has to reach items before they are handed to
            // the page below.
            FlushBorderRun();

            // A page is kept when it has visible content, when it is a deliberate blank left by an explicit
            // break, or when it is the only page. Only the body is stored here; the header/footer bands are
            // assembled once the flow finishes and the total page count is known, so a NUMPAGES field can
            // resolve. A page carrying only empty spacer lines — a document-final empty paragraph pushed off
            // the previous page — is a natural overflow blank Word does not render, so it drops.
            CentreVertically();

            if (HasVisibleContent(items) || currentPageExplicit || bodies.Count == 0)
            {
                bodies.Add((items, current));
            }

            items = [];
            currentColumn = 0;
            y = contentTop;
            columnTop = contentTop;
            atRegionTop = true;
            pageCarriedAfter = lastAfter;
            lastAfter = 0;
            currentPageExplicit = nextPageExplicit;
            currentPageSectionStart = false;
            // A float's exclusion belongs to the page it was anchored on; the new page starts clear.
            floatExclusions.Clear();

            // w:lnNumType restart="newPage" (the OOXML default) numbers each page from Start again.
            if (current.LineNumbers is { Restart: LineNumberRestart.NewPage })
            {
                lineNumberCount = 0;
            }
        }

        // Drops the page's content into the middle of the margin band, for a sheet that asked to be centred
        // down the page (printOptions/@verticalCentered — PageSettings.VerticallyCentered). It runs at page
        // CLOSE because that is the first moment the slack is known: nothing can say how much room is left
        // over until everything that fits has been placed.
        //
        // The extent measured is the BODY's — the grid — and the page's floats are then moved by the same
        // amount rather than measured themselves, exactly as the horizontal half works: Excel centres the
        // print area, and a sheet's drawings anchor to its cells, so art that does not travel with the grid
        // detaches from it. Word-measured on org-charts-visual, whose centred sheet declares a 0.5in top
        // margin against a 1in bottom: Excel's gaps come out 305px and 351px at 96 DPI, the 46px difference
        // being exactly the margin difference — so the content is centred between the MARGINS, not on the
        // paper.
        //
        // A page too full to have slack keeps its content where it is, so on a multi-page sheet the rule
        // degenerates to nothing on the full pages and centres only the last, short one. That IS Excel's
        // behaviour — it centres each page independently rather than stopping once the print area spans
        // pages. Probed directly (_probe_vcenter_*, three 30pt-row sheets differing only in how empty the
        // last page is, since no corpus workbook overflows a centred sheet vertically): Excel puts 6 rows at
        // gaps 446/430 on a single page, 24 rows at 136/120, and — the case in question — the 7 rows left on
        // page 2 of a 33-row sheet at 429/413 on a 1056px page, dead centre rather than at the ~72px top
        // margin. The engine reproduces all three.
        void CentreVertically()
        {
            if (!current.VerticallyCentered || items.Count == 0)
            {
                return;
            }

            var top = float.MaxValue;
            var bottom = float.MinValue;
            foreach (var item in items)
            {
                top = Math.Min(top, item.Y);
                bottom = Math.Max(bottom, item.Y + item.Height);
            }

            var offset = (contentBottom - bottom - (top - contentTop)) / 2;
            if (offset <= 0.01f)
            {
                return;
            }

            for (var index = 0; index < items.Count; index++)
            {
                items[index] = ShiftItem(items[index], offset);
            }

            var pageIndex = bodies.Count;
            for (var index = 0; index < bodyFloats.Count; index++)
            {
                if (bodyFloats[index].Page == pageIndex)
                {
                    bodyFloats[index] = bodyFloats[index] with {Item = ShiftItem(bodyFloats[index].Item, offset)};
                }
            }
        }

        // Moves any placed item down the page, recursing into the two kinds that carry absolute coordinates
        // of their own: a line's inline images and baseline (ShiftLine), and a table row's cells and the
        // content inside them. Everything else is a plain box, so shifting Y on the record is the whole job —
        // a run carries no Y at all, being drawn at its line's baseline.
        static PlacedItem ShiftItem(PlacedItem item, float dy) =>
            item switch
            {
                PlacedLine line => ShiftLine(line, 0, dy),
                PlacedTableRow row => row with
                {
                    Y = row.Y + dy,
                    Cells = [.. row.Cells.Select(cell => cell with
                    {
                        Y = cell.Y + dy,
                        Content = [.. cell.Content.Select(inner => ShiftItem(inner, dy))]
                    })]
                },
                _ => item with {Y = item.Y + dy}
            };

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
                // The behind-text header/footer images follow the same first/even-page variant as the band
                // text, so a title page's decorative frame or illustration comes from its first-page header.
                var backgroundImages = ResolveBandImages(SelectVariant(pageNumber, firstPageHeader, evenPageHeader, header, settings), settings, isFooter: false);
                var footerImages = ResolveBandImages(SelectVariant(pageNumber, firstPageFooter, evenPageFooter, footer, settings), settings, isFooter: true);
                var headerBand = HeaderBand(pageNumber, total, settings);
                var footerBand = FooterBand(pageNumber, total, settings);
                var body = bodies[index].Items;

                // Body floats: behind-text ones paint under the body (over the header background), in-front
                // ones over it. Anchored on the page they were reached on.
                var pageFloats = index;
                var behindFloats = bodyFloats.Where(_ => _.Page == pageFloats && _.Behind).Select(_ => _.Item).ToList();
                var frontFloats = bodyFloats.Where(_ => _.Page == pageFloats && !_.Behind).Select(_ => _.Item).ToList();

                // The whole header/footer story paints BELOW every body item, floats included — w:behindDoc
                // orders a float against the body TEXT and says nothing about the bands, which are under it
                // either way. Word-probed (_probe_footerz: two opaque page-anchored rectangles over a
                // three-word footer, one behindDoc="1" and one behindDoc="0"; both bury their word outright
                // while the uncovered third word proves the footer rendered, and the same pair over a line
                // of body text shows the ordinary behind/in-front split, so the fixture is sound).
                //
                // The footer band used to paint LAST, over everything. That drew business-plans/13's cover
                // footer on top of the grey title rectangle — a behind-text body float spanning 575.4pt to
                // the page bottom — where Word has it buried; business-plans/15's TOC pages are the same
                // shape. The header band was already ordered correctly.
                var pageItems = backgroundImages.Count == 0 && footerImages.Count == 0 && headerBand.Count == 0 && footerBand.Count == 0 && behindFloats.Count == 0 && frontFloats.Count == 0
                    ? body
                    : (IReadOnlyList<PlacedItem>) [.. backgroundImages, .. footerImages, .. headerBand, .. footerBand, .. behindFloats, .. body, .. frontFloats];
                pages.Add(new(pageNumber, settings, pageItems));
            }

            return pages;
        }

        // Each flow paragraph's PRE-SPACING top — where the cursor sat before its space-before was
        // applied, which is Word's paragraph-anchor reference (probed: an offset-0 anchored shape on a
        // 60pt-before paragraph sits at the previous paragraph's bottom edge). A float whose anchor
        // paragraph laid out before the float resolves from here.
        readonly Dictionary<ParagraphElement, float> paragraphPreSpacingTops = [];

        // Non-wrapping floats waiting for their anchor paragraph's first line to place. Emitting at the
        // cursor is usually equivalent (nothing places between a float and its own paragraph), but when
        // the anchor's first line page-breaks away the cursor value strands the float on the old page
        // (letters/05 page 2 ran 34-54px of band error from a stranded float). Word never splits a
        // float from its anchor paragraph — probed (_probe_float_push): a 76pt shape anchored at a
        // page's last paragraph stays with it and CLIPS at the page edge rather than pushing the
        // paragraph over, so the anchor's own placement is the single source of the float's page.
        readonly Dictionary<ParagraphElement, List<DocumentElement>> anchorDeferredFloats = [];

        // w:mirrorIndents substitutions made this run, keyed by the parse instance. Anchor-keyed
        // float lookups translate through this map so a float that names the original paragraph
        // still finds its mirrored stand-in.
        readonly Dictionary<ParagraphElement, ParagraphElement> mirrorSubstitutes = [];

        /// <summary>
        /// Word-measured mirror rule (<c>_probe_mirror</c>, <c>_probe_mirror2</c> — hanging and
        /// firstLine swept on both parities; complex_spacing's Combination 7 confirms on a real
        /// document): an even page keeps the declared indents; an odd page mirrors the box. With
        /// F = firstLine − hanging, every line's right inset becomes left + F, the continuation
        /// left becomes right − min(F, 0), and the first-line delta F itself is unchanged — in
        /// field terms left' = right + hanging, right' = left + firstLine − hanging. Parity is
        /// decided greedily from the page the paragraph starts on, like Word's own layout; a
        /// mirror paragraph that splits across the page boundary keeps its starting parity.
        /// </summary>
        ParagraphElement MirrorForPage(ParagraphElement paragraph)
        {
            var properties = paragraph.Properties;
            if (!properties.MirrorIndents ||
                bodies.Count % 2 != 0 ||
                (properties.LeftIndentPoints == 0 &&
                 properties.RightIndentPoints == 0 &&
                 properties.HangingIndentPoints == 0 &&
                 properties.FirstLineIndentPoints == 0))
            {
                return paragraph;
            }

            if (mirrorSubstitutes.TryGetValue(paragraph, out var existing))
            {
                return existing;
            }

            var mirrored = new ParagraphElement
            {
                Runs = paragraph.Runs,
                Properties = properties with
                {
                    LeftIndentPoints = properties.RightIndentPoints + properties.HangingIndentPoints,
                    RightIndentPoints = properties.LeftIndentPoints + properties.FirstLineIndentPoints - properties.HangingIndentPoints,
                },
                IsAnchorOnlyMark = paragraph.IsAnchorOnlyMark,
                IsSectionBreakMark = paragraph.IsSectionBreakMark,
                IsCollapsedCellMark = paragraph.IsCollapsedCellMark,
            };
            mirrorSubstitutes[paragraph] = mirrored;
            if (anchorDeferredFloats.Remove(paragraph, out var deferred))
            {
                anchorDeferredFloats[mirrored] = deferred;
            }

            return mirrored;
        }

        // True when EmitBodyFloatAt would register a wrap exclusion for this element — such a float
        // cannot defer past its anchor paragraph, whose flow band must see the exclusion.
        static bool RegistersExclusion(DocumentElement element) =>
            element is FloatingImageElement { BehindText: false, WrapType: WrapType.Square or WrapType.Tight or WrapType.Through or WrapType.TopAndBottom };

        // Emits a body float now when its Y is absolute (page/margin) or it has no recorded anchor;
        // defers a paragraph-anchored one until its anchor paragraph's top is known. Resolving from the
        // cursor at emission ran agendas-minutes/05's decorative shapes a paragraph-gap low, and static
        // previous/next-paragraph choices invert that document against menus/05 — only the recorded
        // anchor serves both.
        void EmitBodyFloat(DocumentElement element, VerticalAnchor verticalAnchor, ParagraphElement? anchor)
        {
            if (anchor != null && mirrorSubstitutes.TryGetValue(anchor, out var substituted))
            {
                anchor = substituted;
            }

            if (!IsAbsoluteY(verticalAnchor) && anchor != null &&
                paragraphPreSpacingTops.TryGetValue(anchor, out var placedTop))
            {
                // The anchor paragraph laid out BEFORE this float reached the flow (a background-shape
                // drawing flushes the paragraph first) — resolve against its recorded pre-spacing top.
                EmitBodyFloatAt(element, placedTop);
                return;
            }

            // Forward case: the anchor paragraph is still ahead. A non-wrapping float defers to that
            // paragraph's first-line placement, which pins both the anchor top and the page even when
            // the paragraph breaks to a new region first. A wrapping float emits here and now — its
            // exclusion must be registered before the anchor paragraph resolves its flow band — which
            // keeps the cursor value; that is exact whenever the anchor does not move. (An earlier
            // flush-at-page-end deferral shifted wedding/07 86px; flushing at the anchor's own
            // placement keeps the old timing byte-for-byte in the no-break case.)
            if (anchor != null && !RegistersExclusion(element))
            {
                if (!anchorDeferredFloats.TryGetValue(anchor, out var deferred))
                {
                    deferred = [];
                    anchorDeferredFloats[anchor] = deferred;
                }

                deferred.Add(element);
                return;
            }

            EmitBodyFloatAt(element, y);
        }

        // Places one body float, resolving a paragraph-relative Y against anchorTop (page/margin anchors
        // are absolute and ignore it).
        void EmitBodyFloatAt(DocumentElement element, float anchorTop)
        {
            float AnchoredY(VerticalAnchor anchor, double offset, double? percent)
            {
                var baseY = anchor switch
                {
                    VerticalAnchor.Page => 0f,
                    VerticalAnchor.Margin => contentTop,
                    _ => anchorTop
                };
                return percent is { } fraction
                    ? baseY + (float) (fraction * (anchor == VerticalAnchor.Page ? current.HeightPoints : contentHeight))
                    : baseY + (float) offset;
            }

            switch (element)
            {
                case FloatingImageElement image when DecodableImageBytes(image) is { Length: > 0 } data:
                {
                    var imageX = FloatX(image.HorizontalAnchor, image.HorizontalPositionPoints, image.HorizontalPositionPercent);
                    var imageY = AnchoredY(image.VerticalAnchor, image.VerticalPositionPoints, image.VerticalPositionPercent);
                    AddBodyFloat(new PlacedImage(imageX, imageY, (float) image.WidthPoints, (float) image.HeightPoints, data, image.RotationDegrees, image.FlipHorizontal, image.FlipVertical, image.ClipToEllipse, image.ClipSubpaths, image.Crop, Recolor: ImageRecolor.For(image.ColorEffect, image.DuotoneColorHex, image.DuotoneLightColorHex), Opacity: image.Opacity), image.BehindText, IsAbsoluteY(image.VerticalAnchor));
                    RegisterFloatExclusion(image, imageX, imageY, (float) image.WidthPoints, (float) image.HeightPoints);
                    break;
                }

                case FloatingShapeElement {ImageData: { Length: > 0 } shapeImage} shape when shape.ImageContentType != "image/svg+xml":
                    AddBodyFloat(new PlacedImage(FloatX(shape.HorizontalAnchor, shape.HorizontalPositionPoints, shape.HorizontalPositionPercent), AnchoredY(shape.VerticalAnchor, shape.VerticalPositionPoints, shape.VerticalPositionPercent), (float) shape.WidthPoints, (float) shape.HeightPoints, shapeImage, shape.RotationDegrees, shape.FlipHorizontal, shape.FlipVertical, Opacity: shape.ImageOpacity), shape.BehindText, IsAbsoluteY(shape.VerticalAnchor));
                    break;

                case FloatingShapeElement shape:
                    AddBodyFloat(new PlacedShape(FloatX(shape.HorizontalAnchor, shape.HorizontalPositionPoints, shape.HorizontalPositionPercent), AnchoredY(shape.VerticalAnchor, shape.VerticalPositionPoints, shape.VerticalPositionPercent), (float) shape.WidthPoints, (float) shape.HeightPoints, shape), shape.BehindText, IsAbsoluteY(shape.VerticalAnchor));
                    break;
            }
        }

        // Absolute X of a body float: a page-anchored offset is from the page's left; anything else (margin,
        // column, character) is from the content's left. A percentage offset (wp14:pctPosHOffset) resolves as
        // that fraction of the anchor's reference width — the page for a page anchor, else the content box —
        // mirroring the deleted production FloatingPosition. Nested-frame anchors are a later slice.
        float FloatX(HorizontalAnchor anchor, double offset, double? percent)
        {
            var baseX = anchor == HorizontalAnchor.Page ? 0f : fullContentLeft;
            return percent is { } fraction
                ? baseX + (float) (fraction * (anchor == HorizontalAnchor.Page ? current.WidthPoints : fullContentWidth))
                : baseX + (float) offset;
        }

        // Absolute Y of a body float: page-anchored from the top edge, margin-anchored from the top margin,
        // otherwise from the current flow cursor (a paragraph/character anchor, approximated by where the
        // float was reached). A percentage offset (wp14:pctPosVOffset) resolves as that fraction of the
        // anchor's reference height (the page for a page anchor, else the content box).
        float FloatY(VerticalAnchor anchor, double offset, double? percent)
        {
            var baseY = anchor switch
            {
                VerticalAnchor.Page => 0f,
                VerticalAnchor.Margin => contentTop,
                _ => y
            };
            return percent is { } fraction
                ? baseY + (float) (fraction * (anchor == VerticalAnchor.Page ? current.HeightPoints : contentHeight))
                : baseY + (float) offset;
        }

        // A page/margin anchor gives the float an absolute Y independent of the flow cursor, so it belongs to
        // the page carrying the content it is anchored to rather than the page the cursor is on when its
        // element is reached — a page-2 full-page background is declared before page-1's flow finishes.
        static bool IsAbsoluteY(VerticalAnchor anchor) => anchor is VerticalAnchor.Page or VerticalAnchor.Margin;

        readonly record struct FloatExclusion(float Left, float Top, float Right, float Bottom, bool FullWidth, WrapTextSide Side);

        // Records the measure a wrapping float takes away, expanded by its wrap distances. Tight and Through
        // wrap the image outline in Word; the rectangular extent is the approximation for both, as in
        // production. A TopAndBottom float takes the full column width, so no text sits beside it at all.
        void RegisterFloatExclusion(FloatingImageElement image, float left, float top, float width, float height)
        {
            if (image.BehindText)
            {
                return;
            }

            switch (image.WrapType)
            {
                case WrapType.Square or WrapType.Tight or WrapType.Through:
                    floatExclusions.Add(new(
                        left - (float) image.WrapDistanceLeftPoints,
                        top - (float) image.WrapDistanceTopPoints,
                        left + width + (float) image.WrapDistanceRightPoints,
                        top + height + (float) image.WrapDistanceBottomPoints,
                        FullWidth: false,
                        image.WrapTextSide));
                    break;
                case WrapType.TopAndBottom:
                    floatExclusions.Add(new(
                        ColumnLeft,
                        top - (float) image.WrapDistanceTopPoints,
                        ColumnLeft + columnWidth,
                        top + height + (float) image.WrapDistanceBottomPoints,
                        FullWidth: true,
                        WrapTextSide.BothSides));
                    break;
            }
        }

        /// <summary>
        /// Where a paragraph starting at <paramref name="top"/> can lay out given the active exclusions: the
        /// widest free horizontal segment of the column beside the floats. When no usable segment exists there
        /// (a TopAndBottom band, or floats covering the whole measure) the top advances below the blocking
        /// floats. Constrained is false when the paragraph gets the full column width. A faithful port of
        /// RenderContextBase.ResolveFlowBand, including its band-per-paragraph granularity: Word reflows back
        /// to the full measure below a float mid-paragraph, and neither engine models that yet.
        /// </summary>
        (float X, float Width, float Y, bool Constrained) ResolveFlowBand(float top)
        {
            var left = ColumnLeft;
            var right = ColumnLeft + columnWidth;
            if (floatExclusions.Count == 0)
            {
                return (left, right - left, top, false);
            }

            // The paragraph's own first-line height is not known yet, so probe with a nominal line: a
            // paragraph starting just above a float still wraps beside it.
            const float probeHeight = 12f;
            // Below half an inch the band is unusable and the text skips below the float instead — about
            // where Word stops squeezing words in.
            const float minUsableWidth = 36f;

            var currentTop = top;
            for (var guard = 0; guard < 8; guard++)
            {
                float? clearTo = null;
                var segments = new List<(float Start, float End)> { (left, right) };
                foreach (var exclusion in floatExclusions)
                {
                    if (currentTop + probeHeight <= exclusion.Top || currentTop >= exclusion.Bottom)
                    {
                        continue;
                    }

                    clearTo = clearTo is { } clear ? Math.Max(clear, exclusion.Bottom) : exclusion.Bottom;
                    var blockLeft = exclusion.FullWidth ? left : Math.Max(left, exclusion.Left);
                    var blockRight = exclusion.FullWidth ? right : Math.Min(right, exclusion.Right);
                    var remaining = new List<(float Start, float End)>();
                    foreach (var (start, end) in segments)
                    {
                        if (blockRight <= start || blockLeft >= end)
                        {
                            remaining.Add((start, end));
                            continue;
                        }

                        // An explicit @wrapText side restricts which side of THIS float text may use;
                        // BothSides/Largest leave both segments available and the widest wins below, since a
                        // single band cannot carry both sides at once.
                        if (blockLeft > start && exclusion.Side != WrapTextSide.Right)
                        {
                            remaining.Add((start, blockLeft));
                        }

                        if (blockRight < end && exclusion.Side != WrapTextSide.Left)
                        {
                            remaining.Add((blockRight, end));
                        }
                    }

                    segments = remaining;
                }

                if (clearTo == null)
                {
                    return (left, right - left, currentTop, false);
                }

                var bestStart = 0f;
                var bestWidth = 0f;
                foreach (var (start, end) in segments)
                {
                    if (end - start > bestWidth)
                    {
                        bestStart = start;
                        bestWidth = end - start;
                    }
                }

                if (bestWidth >= minUsableWidth)
                {
                    return (bestStart, bestWidth, currentTop, true);
                }

                currentTop = clearTo.Value;
            }

            return (left, right - left, currentTop, false);
        }

        // Records a body float. An absolutely-positioned one defers until the next visible line/row reveals its
        // page; a flow-anchored one is tagged with the current cursor page, where its y offset was measured.
        void AddBodyFloat(PlacedItem item, bool behind, bool absoluteY)
        {
            if (absoluteY)
            {
                pendingFloats.Add((item, behind));
            }
            else
            {
                bodyFloats.Add((bodies.Count, item, behind));
            }
        }

        // Assigns every deferred float to the page just committed to (the current bodies.Count) and clears the
        // queue. Called once the next visible line or row lands, and again when the flow ends so a trailing
        // float settles on the last page.
        void ResolvePendingFloats()
        {
            if (pendingFloats.Count == 0)
            {
                return;
            }

            foreach (var (item, behind) in pendingFloats)
            {
                bodyFloats.Add((bodies.Count, item, behind));
            }

            pendingFloats.Clear();
        }

        // A floating text box: a box (background/outline) with a mini-flow of paragraphs inside. Its chrome
        // paints as a shape and its content lays out at the box top-left — Word applies no internal inset,
        // wrapping the content to the full box width. Both are tagged with the page the flow has reached,
        // like the other body floats, and paint behind or in front of the text per BehindText.
        void PlaceTextBox(FloatingTextBoxElement textBox)
        {
            var boxX = FloatX(textBox.HorizontalAnchor, textBox.HorizontalPositionPoints, textBox.HorizontalPositionPercent);
            var boxY = FloatY(textBox.VerticalAnchor, textBox.VerticalPositionPoints, textBox.VerticalPositionPercent);
            var boxWidth = (float) textBox.WidthPoints;
            var boxHeight = (float) textBox.HeightPoints;
            var absoluteY = IsAbsoluteY(textBox.VerticalAnchor);

            if (textBox.BackgroundColorHex != null || textBox.LineColorHex != null)
            {
                var boxShape = new FloatingShapeElement
                {
                    WidthPoints = textBox.WidthPoints,
                    HeightPoints = textBox.HeightPoints,
                    FillColorHex = textBox.BackgroundColorHex,
                    LineColorHex = textBox.LineColorHex,
                    LineWidthPoints = textBox.LineWidthPoints > 0 ? textBox.LineWidthPoints : null,
                    LineAlpha = textBox.LineAlpha,
                    Subpaths = textBox.Subpaths,
                    RotationDegrees = textBox.RotationDegrees
                };
                AddBodyFloat(new PlacedShape(boxX, boxY, boxWidth, boxHeight, boxShape), textBox.BehindText, absoluteY);
            }

            foreach (var item in LayoutCellContent(
                         new()
                         {
                             Content = textBox.Content
                         },
                         boxX,
                         boxY,
                         boxWidth,
                         boxHeight,
                         CellVerticalAlignment.Top))
            {
                AddBodyFloat(item, textBox.BehindText, absoluteY);
            }
        }

        // A positioned text frame (w:framePr) — Word's legacy floating text block, which FrameGrouper lifts to
        // the top level. It auto-sizes to its content (an explicit w:w / w:h overrides), resolves an absolute
        // position from its anchors and alignment, and paints there without taking flow space. The frame has no
        // fill or border of its own, so this is content placement only. Mirrors
        // the deleted production RenderPositionedFrame, whose measurements and anchor rules it reproduces.
        void PlaceFrame(PositionedFrameElement frame)
        {
            float measuredWidth = 0;
            float measuredHeight = 0;
            foreach (var element in frame.Content)
            {
                if (element is ParagraphElement paragraph)
                {
                    measuredWidth = Math.Max(measuredWidth, measurer.MeasureParagraphNaturalWidth(paragraph, columnWidth));
                    measuredHeight += measurer.MeasureParagraphHeightWithWidth(paragraph, columnWidth);
                }
            }

            // The measured natural width IS the exact line width and the wrap check is a strict ">", so laying
            // out at exactly that width can spill the last word onto a second line. Two points is below the
            // visual threshold and avoids the wrap.
            const float autoWidthPaddingPoints = 2;
            var frameWidth = Math.Min(
                frame.WidthPoints is { } explicitWidth ? (float) explicitWidth : measuredWidth + autoWidthPaddingPoints,
                columnWidth);
            var frameHeight = frame.HeightPoints is { } explicitHeight ? (float) explicitHeight : measuredHeight;

            // Lifted frames paint over the flow (FrameGrouper appends them after the in-flow content), and
            // their anchors are page/margin (the ShouldLift gate), so they defer like the other absolute floats.
            foreach (var item in LayoutCellContent(new() { Content = frame.Content }, FrameX(frame, frameWidth), FrameY(frame, frameHeight), frameWidth, frameHeight, CellVerticalAlignment.Top))
            {
                AddBodyFloat(item, behind: false, absoluteY: true);
            }
        }

        float FrameX(PositionedFrameElement frame, float width)
        {
            var anchorLeft = frame.HorizontalAnchor switch
            {
                HorizontalAnchor.Page => 0f,
                HorizontalAnchor.Margin => (float) current.MarginLeft,
                _ => ColumnLeft
            };
            var anchorWidth = frame.HorizontalAnchor switch
            {
                HorizontalAnchor.Page => (float) current.WidthPoints,
                HorizontalAnchor.Margin => (float) (current.WidthPoints - current.MarginLeft - current.MarginRight),
                _ => columnWidth
            };

            // Word's legacy text-frame layout does not right-align these footer blocks flush to the text
            // margin — it leaves a wide band of empty space on the right. Measured from Word's
            // agendas-minutes/14 render (the block's right edge lands ~0.9" / ~12.2% of the content width
            // inside the right margin); the inset is not expressed anywhere in the frame markup, so it can
            // only be reproduced empirically. Same constant the deleted production render used.
            const float frameRightInsetFraction = 0.122f;
            var rightInset = anchorWidth * frameRightInsetFraction;

            return frame.HorizontalAlignment switch
            {
                FrameHorizontalAlignment.Right => anchorLeft + Math.Max(0, anchorWidth - width - rightInset),
                FrameHorizontalAlignment.Center => anchorLeft + Math.Max(0, (anchorWidth - width) / 2),
                FrameHorizontalAlignment.Left => anchorLeft,
                _ => anchorLeft + (float) frame.XPoints
            };
        }

        // A page/margin-anchored frame with a small explicit y (under half an inch) sitting out of flow is
        // Word's "footer info block" pattern (a right-aligned Location/Date/Time stack): Word floats it just
        // above the bottom margin rather than at the literal y from the top. A larger y is an intentional
        // upper-page placement and is honoured from the top.
        float FrameY(PositionedFrameElement frame, float height)
        {
            const float bottomAnchorYThresholdPoints = 36;
            var anchorTop = frame.VerticalAnchor switch
            {
                VerticalAnchor.Page => 0f,
                VerticalAnchor.Margin => contentTop,
                _ => y
            };
            var anchorBottom = frame.VerticalAnchor switch
            {
                VerticalAnchor.Page => (float) (current.HeightPoints - current.MarginBottom),
                _ => contentBottom
            };

            switch (frame.VerticalAlignment)
            {
                case FrameVerticalAlignment.Top:
                    return anchorTop + (float) frame.YPoints;
                case FrameVerticalAlignment.Center:
                    return anchorTop + Math.Max(0, (anchorBottom - anchorTop - height) / 2);
                case FrameVerticalAlignment.Bottom:
                    return anchorBottom - height;
                default:
                    if (frame.VerticalAnchor is VerticalAnchor.Page or VerticalAnchor.Margin)
                    {
                        return frame.YPoints >= bottomAnchorYThresholdPoints
                            ? anchorTop + (float) frame.YPoints
                            : anchorBottom - height;
                    }

                    return y + (float) frame.YPoints;
            }
        }

        // A floating table (w:tblpPr): positioned by its own offsets from a page/margin/text anchor, laid out
        // reusing the nested-table layout with no page breaks. A text-anchored table flows with the
        // surrounding text — it renders at the cursor (nudged by tblpY) and leaves the cursor at its bottom,
        // so the following content clears it (Word wraps a full-width one below rather than beside it,
        // agendas-minutes/11's Date/Time block). A page/margin-anchored table sits absolutely on top of the
        // layout and takes no flow space, deferred to its content's page like the other absolute floats.
        void PlaceFloatingTable(TableElement table)
        {
            var columnCount = TableLayout.GetColumnCount(table);
            var tableWidth = TableLayout.CalculateColumnWidths(table, columnCount, fullContentWidth, measurer).Sum();
            var offsetX = (float) table.Properties.FloatingXOffsetPoints;
            var left = table.Properties.FloatingHorizontalAnchor switch
            {
                FloatingTableHorizontalAnchor.Page => offsetX,
                FloatingTableHorizontalAnchor.Margin => fullContentLeft + offsetX,
                _ => ColumnLeft + offsetX
            };
            var offsetY = (float) table.Properties.FloatingYOffsetPoints;

            if (table.Properties.FloatingVerticalAnchor == FloatingTableVerticalAnchor.Text)
            {
                // The previous paragraph's after-spacing precedes the table (a table has no before-spacing to
                // collapse it with), exactly as for an inline table, and never applies at a region top —
                // without this the block starts one after-gap too high (agendas-minutes/11's 30pt placeholder).
                if (!atRegionTop)
                {
                    y += lastAfter;
                }

                lastAfter = 0;
                lastContextual = false;
                lastStyleId = null;

                var flowTop = y + offsetY;
                var (rows, height) = LayoutNestedTable(table, left, flowTop, tableWidth);
                items.AddRange(rows);
                ResolvePendingFloats();
                y = Math.Max(y, flowTop + height);
                atRegionTop = false;
                return;
            }

            var top = table.Properties.FloatingVerticalAnchor == FloatingTableVerticalAnchor.Margin ? contentTop + offsetY : offsetY;
            var (absoluteRows, _) = LayoutNestedTable(table, left, top, tableWidth);
            foreach (var item in absoluteRows)
            {
                AddBodyFloat(item, false, absoluteY: true);
            }
        }

        // Unwarped WordArt is Word's inline text box: a single line of the WordArt text, shrunk to fit and
        // centred in its box (reusing the cell-content layout with a centred synthetic paragraph). Warp
        // presets (arch/wave/envelope/…) are a later slice; the corpus templates are all unwarped.
        IReadOnlyList<PlacedItem> LayoutWordArtText(string text, string fontFamily, double fontSizePoints, bool bold, bool italic, string? fillColorHex, float boxX, float boxY, float boxWidth, float boxHeight)
        {
            var properties = new RunProperties { FontFamily = fontFamily, FontSizePoints = fontSizePoints, Bold = bold, Italic = italic, ColorHex = fillColorHex };
            var textWidth = measurer.MeasureRunWidth(text, properties);
            var widthScale = textWidth > 0 ? boxWidth / textWidth : 1f;
            var heightScale = fontSizePoints > 0 ? boxHeight / (float) (fontSizePoints * 1.2) : 1f;
            var scale = Math.Min(Math.Min(widthScale, heightScale), 1f);
            var paragraph = new ParagraphElement
            {
                Runs = [
                    new()
                    {
                        Text = text,
                        Properties = properties with
                        {
                            FontSizePoints = fontSizePoints * scale
                        }
                    }],
                Properties = new()
                {
                    Alignment = TextAlignment.Center
                }
            };
            return LayoutCellContent(new() { Content = [paragraph] }, boxX, boxY, boxWidth, boxHeight, CellVerticalAlignment.Center);
        }

        // The box chrome of an unwarped WordArt shape (business/06's frame, wedding/08's ellipse badge): its
        // fill and outline painted as a shape, or null when the WordArt has no box. Floating WordArt has none.
        static PlacedShape? WordArtBoxShape(WordArtElement wordArt, float boxX, float boxY)
        {
            if (wordArt.BoxFillColorHex == null && wordArt.BoxLineColorHex == null)
            {
                return null;
            }

            var boxShape = new FloatingShapeElement
            {
                WidthPoints = wordArt.WidthPoints,
                HeightPoints = wordArt.HeightPoints,
                FillColorHex = wordArt.BoxFillColorHex,
                LineColorHex = wordArt.BoxLineColorHex,
                LineWidthPoints = wordArt.BoxLineWidthPoints > 0 ? wordArt.BoxLineWidthPoints : null,
                LineAlpha = wordArt.BoxLineAlpha,
                Subpaths = wordArt.BoxSubpaths,
                Preset = wordArt.BoxIsEllipse ? PresetShape.Ellipse : PresetShape.Rect
            };
            return new(boxX, boxY, (float) wordArt.WidthPoints, (float) wordArt.HeightPoints, boxShape);
        }

        // A block-level unwarped WordArt takes flow space (its declared height) at the current cursor, aligned
        // by its w:jc — it paints its box chrome then its centred text.
        // The preceding paragraph's after-spacing precedes the shape, as it does for a table or any other
        // block. WordArt carries no spacing of its own, so without this the box rides up by that gap and
        // every later block follows it — wordart-envelope's warps each sat 10pt (the subtitle's w:after)
        // above Word. Never at a region top, where the break swallows the gap.
        void PlaceWordArt(WordArtElement wordArt)
        {
            if (!atRegionTop)
            {
                y += lastAfter;
            }

            lastAfter = 0;
            var height = (float) wordArt.HeightPoints;
            EnsureSpaceFor(height);
            var boxWidth = (float) wordArt.WidthPoints;
            var boxX = ColumnLeft + AlignmentOffset(wordArt.Alignment, columnWidth, boxWidth);
            if (wordArt.Transform != WordArtTransform.None)
            {
                items.Add(new PlacedWordArt(boxX, y, boxWidth, height, wordArt));
            }
            else
            {
                if (WordArtBoxShape(wordArt, boxX, y) is { } boxItem)
                {
                    items.Add(boxItem);
                }

                items.AddRange(LayoutWordArtText(wordArt.Text, wordArt.FontFamily, wordArt.FontSizePoints, wordArt.Bold, wordArt.Italic, wordArt.FillColorHex, boxX, y, boxWidth, height));
            }

            y += height;
            atRegionTop = false;
        }

        // Floating WordArt: absolutely positioned at its anchor, taking no flow space. A warp stays one
        // figure for the painter to rasterize; an unwarped one is centred text with no box chrome.
        void PlaceFloatingWordArt(FloatingWordArtElement wordArt)
        {
            var boxX = FloatX(wordArt.HorizontalAnchor, wordArt.HorizontalPositionPoints, wordArt.HorizontalPositionPercent);
            var boxY = FloatY(wordArt.VerticalAnchor, wordArt.VerticalPositionPoints, wordArt.VerticalPositionPercent);
            var absoluteY = IsAbsoluteY(wordArt.VerticalAnchor);
            if (wordArt.Transform != WordArtTransform.None)
            {
                AddBodyFloat(new PlacedWordArt(boxX, boxY, (float) wordArt.WidthPoints, (float) wordArt.HeightPoints, wordArt), wordArt.BehindText, absoluteY);
                return;
            }

            foreach (var item in LayoutWordArtText(wordArt.Text, wordArt.FontFamily, wordArt.FontSizePoints, wordArt.Bold, wordArt.Italic, wordArt.FillColorHex, boxX, boxY, (float) wordArt.WidthPoints, (float) wordArt.HeightPoints))
            {
                AddBodyFloat(item, wordArt.BehindText, absoluteY);
            }
        }

        // The bytes a backend can actually decode: SVG artwork carries a raster equivalent (PdfSharp, like
        // ImageSharp, cannot rasterize SVG), so fall back to it when the primary data is SVG.
        static byte[] DecodableImageBytes(FloatingImageElement image)
        {
            if (image is {ContentType: "image/svg+xml", RasterFallbackData: {Length: > 0} fallback})
            {
                return fallback;
            }

            return image.ImageData;
        }

        // The flow space a border edge occupies: its own width plus the w:space gap it holds open between
        // the line and the rule. Zero for an edge that is not drawn.
        //
        // The width is what the edge DRAWS, not what w:sz declares — a multi-line style whose declared
        // width is too small to resolve is floored up to a visible stack (BorderStroke.Extent), and
        // charging only the declared width let those paragraphs pack tighter than Word's.
        // Word's paragraph-border box overhangs the nominal (text extent − w:space) on the SIDES only, by a
        // constant that is not the stroke width: XPS-read on _probe_pbdrwidth / _probe_pbdrside (2026-09-04),
        // the left rule's inner face sits 1.0pt outside the nominal and the right rule's 1.5pt outside, at
        // every width from 1pt to 12pt, at w:space 8 and 20, under left and right indents and under a
        // different page margin (six readings each, 0.90–0.98 and 1.50–1.55; w:space=0 alone reads ~0.2pt
        // more on both sides). Top and bottom faces are nominal within 0.2pt, so this is a paint-side
        // offset with no flow reserve — the vertical reserves are unchanged.
        const float BorderBoxLeftOutset = 1.0f;
        const float BorderBoxRightOutset = 1.5f;

        static float EdgeReserve(BorderEdge edge, double spacePoints) =>
            BorderStroke.Draws(edge) ? (float) (BorderStroke.Extent(edge.Style, edge.WidthPoints) + spacePoints) : 0f;

        // Strokes the open border run's box and closes the run. Idempotent — a closed run flushes to nothing,
        // so the region-boundary and element-boundary callers can both fire without coordinating.
        //
        // Called BEFORE the content that ends the run is placed, so the box lands in items ahead of whatever
        // follows it and paints under it, while still painting over its own members' shading.
        void FlushBorderRun()
        {
            if (borderRunProperties is not {Borders: { } borders} run)
            {
                borderRunProperties = null;
                borderRunBetweens.Clear();
                return;
            }

            // Expanded by each edge's space — the gap Word leaves between the text and the line. The space is
            // drawn but not yet charged to the flow budget, so a run can still overhang the region bottom by
            // its bottom space; reserving it is a separate change with its own baseline sweep.
            //
            // The left edge clears the HANGING INDENT as well, so a list's marker sits inside the box rather
            // than astride its left rule. Word measures from the paragraph's leftmost extent — Word-probed
            // seven ways (the H-probe in docs/word-features.md): against a plain indent at x=146px the box
            // moved to 116 for a 284tw hanging and to 93 for a 511tw one, exactly the hanging in each case,
            // and gave the SAME edge whether or not a numbering marker occupied the gutter — so this is
            // indent-driven, not marker-driven. A first-line indent (which moves text RIGHT) never shifts it.
            // The right edge held at 1123px throughout, so the width takes the hanging back.
            var left = ColumnLeft + (float) (run.LeftIndentPoints - run.HangingIndentPoints) - (float) run.BorderLeftSpacePoints - BorderBoxLeftOutset;
            var width = borderRunWidth + (float) run.HangingIndentPoints + (float) run.BorderLeftSpacePoints + (float) run.BorderRightSpacePoints + BorderBoxLeftOutset + BorderBoxRightOutset;
            var top = borderRunTop - (float) run.BorderTopSpacePoints;
            var height = borderRunBottom - borderRunTop + (float) run.BorderTopSpacePoints + (float) run.BorderBottomSpacePoints;

            // Shading fills the whole border box — out to the rules, across the w:space gap —
            // not only the text lines: Word's html_css_margin_padding #CCE5FF box is solid from
            // rule to rule, 23.4px of filled padding around a 26px line at 150 DPI. Inserted at
            // the run's start so it paints BEHIND the member lines (and their own per-line bands,
            // which sit inside it), while the border itself still paints over everything.
            if (!string.IsNullOrEmpty(run.BackgroundColorHex))
            {
                items.Insert(borderRunItemsIndex, new PlacedShading(left, top, width, height, run.BackgroundColorHex));
            }

            items.Add(new PlacedBorder(left, top, width, height, borders));

            // w:between rules each internal boundary; the box itself carries no edge there. Emitted after the
            // box so it paints over it, as a zero-height top-only border (the w:hr geometry).
            foreach (var betweenY in borderRunBetweens)
            {
                items.Add(
                    new PlacedBorder(
                        left,
                        betweenY,
                        width,
                        0,
                        new()
                        {
                            Top = run.BorderBetween
                        }));
            }

            // The bottom space and rule occupy flow: the cursor clears them before whatever follows, so the
            // box cannot overhang the next paragraph or the region bottom. Word ADDS this to the paragraph's
            // spacing-after rather than collapsing the two — probe R4 (12pt after plus a 6pt space) put the
            // following line 71px below its predecessor against control R7's 55px, a 16px difference that is
            // the border reserve, where a max() rule would have left the two identical.
            y += EdgeReserve(borders.Bottom, run.BorderBottomSpacePoints);

            borderRunProperties = null;
            borderRunBetweens.Clear();
        }

        // Content overflow or a column break: move to the next column, keeping the current page's items,
        // or start a new page when the last column is full. A region boundary ends any open border run —
        // Word closes the box at the break and reopens it on the next region, and closing here at least
        // keeps the box off the gap.
        void AdvanceColumnOrPage()
        {
            FlushBorderRun();
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

            // A paragraph that does not continue the open border run closes it here — before any of this
            // paragraph's own items are appended, so the box cannot paint over the content that follows it.
            if (borderRunProperties is { } openRun && !openRun.SharesBorderGroupWith(properties))
            {
                FlushBorderRun();
            }

            if (properties.PageBreakBefore && !AtPageTop)
            {
                FinishPage(false);
            }

            // w:mirrorIndents resolves against the parity of the page the paragraph starts on, so the
            // substitution happens after the paragraph's own page break has landed it.
            paragraph = MirrorForPage(paragraph);
            properties = paragraph.Properties;

            // Space-before, collapsed with the previous paragraph's after (max, not sum) and dropped at a
            // region top. If the collapsed gap plus the first line overflows, the line-level break below
            // resets the cursor — the same space-before drop for the moved paragraph. w:contextualSpacing
            // removes the gap entirely between two same-style contextual paragraphs (Word's memo To/From/CC
            // block, list runs).
            //
            // The drop is a *break* rule, not a top-of-box rule: Word applies a leading space-before on the
            // document's own first page and only swallows it where the flow broke onto a new region. Treating
            // the document start as a region top lost it — wordart-envelope's title sat 20pt (its w:before)
            // high. The deleted production render drew the same distinction through its
            // `pagesStarted <= 1` guard.
            // Word's paragraph-anchor reference is this pre-spacing position (see paragraphPreSpacingTops).
            paragraphPreSpacingTops[paragraph] = y;

            var contextualCollapse = properties.ContextualSpacing && lastContextual && properties.StyleId == lastStyleId;
            var atDocumentStart = bodies.Count == 0 && items.Count == 0 && currentColumn == 0;
            var atSectionStart = AtPageTop && currentPageSectionStart && items.Count == 0;
            if (!atRegionTop || atDocumentStart)
            {
                y += contextualCollapse ? 0f : Math.Max(lastAfter, (float) properties.SpacingBeforePoints);
            }
            else if (atSectionStart)
            {
                // The kept spacing is the EXCESS over the previous page's trailing after (see
                // pageCarriedAfter) — Word's after-then-excess flow carries across the break.
                y += contextualCollapse ? 0f : Math.Max(0f, (float) properties.SpacingBeforePoints - pageCarriedAfter);
            }

            // Opening a border run reserves its top rule and w:space in the flow, so the box sits BELOW the
            // preceding paragraph instead of drawing back over it. Charged once per run, not per member —
            // probe group A's three-paragraph box measured exactly three lines plus ONE top space and ONE
            // bottom space. Like the bottom reserve this ADDS to the collapsed spacing-before rather than
            // merging with it (probe R3, 12pt before plus a 6pt space, put the line 70px down against
            // control R6's 54px). Reserving scales with the space as measured at 0 / 6 / 20pt: 3 / 14 / 45px
            // against a predicted 3.1 / 15.6 / 44.8.
            if (borderRunProperties == null && properties.Borders is { HasAnyBorder: true } opening)
            {
                y += EdgeReserve(opening.Top, properties.BorderTopSpacePoints);
            }

            // Where this paragraph may lay out beside any wrapping float, resolved once at its first line's
            // position and held for the whole paragraph (Word reflows to the full measure below the float
            // mid-paragraph; neither engine models that yet). With no exclusions — every document but a
            // wrapping-float one — this returns the full column unchanged, so nothing else moves.
            var (bandLeft, bandWidth, bandTop, bandConstrained) = ResolveFlowBand(y);
            if (bandTop > y)
            {
                // No usable band here: the text starts below the float that blocked it.
                y = bandTop;
            }

            var wrapWidth = bandConstrained ? bandWidth : columnWidth;
            var paragraphLines = measurer.LayoutLineContents(paragraph, wrapWidth);

            // Columns are equal width, so the width available for alignment is constant even as a line spills
            // to the next column; the left edge is read per line, after any advance.
            var availableWidth = wrapWidth - (float) properties.LeftIndentPoints - (float) properties.RightIndentPoints;

            // Paragraph-border box (w:pBdr): track the first line's top and last line's bottom so a single
            // border can be stroked around the whole paragraph. If it breaks across a column or page the
            // box would span the gap, so a break disables it (per-fragment borders are a later slice).
            float? borderTop = null;
            var borderBroke = false;
            var paragraphItemsStart = items.Count;

            var lineIndex = 0;
            while (lineIndex < paragraphLines.Count)
            {
                // How many of the remaining lines fit in the current region. What the line has to get inside
                // the bottom margin depends on w:lineRule, which three Word renders pin down (all against a
                // content bottom of exactly 720pt, box/ink/ascent bottoms measured):
                //
                //   exact flow, line 50           REJECTED   722.00 / 722.43 / 719.47
                //   auto flow, line 42            KEPT       720.56 / 718.55 / 715.59
                //   image_wrap_square col, line 6 KEPT       724.36 / 722.35 / 719.39
                //
                // No single quantity survives that: the full box is refuted by rows 2-3, the ink box by row 3,
                // the ascent by row 1 — and no threshold separates rows 1 and 3 either, since they straddle by
                // 0.08pt in ascent bottom while disagreeing, and the box overhang runs non-monotone across the
                // three (0.56 kept, 2.00 rejected, 4.36 kept). The discriminator is the spacing rule itself.
                //
                // Only AUTO tolerates an overhang: the baseline has to clear the margin, and Word lets the
                // last line's descent and trailing gap encroach it rather than pushing the line to the next
                // page, drawing the overhang and clipping it at the text area — visible in image_wrap_square,
                // whose last column line has a full-width ink band ending dead on the content bottom where the
                // line above it trails descenders 1.92pt past its own band. Every other rule reserves the
                // whole box. For exact that is what "exact" means, the declared height being an absolute
                // reservation; atLeast was assumed to follow auto and does NOT — Word-probed three ways
                // (_probe_lastline_atleast_a/b/c), it is strict at 15.5pt (Word 41, the lenient reading 42),
                // at 21pt (30 against 31), and — the case that makes this categorical rather than a
                // declared-value rule — ALSO strict at a declared 10pt that LOSES to Calibri's natural
                // 13.4277pt pitch (44 against 45). That last box is the font's own, identical to what single
                // auto produces, so the leniency belongs to the auto rule itself and not to the box's
                // provenance.
                //
                // The first line at a region top is always taken (nothing better to do than overflow it).
                var reserveWholeBox = properties.LineSpacingRule != LineSpacingRule.Auto;
                var fit = 0;
                var probeY = y;
                while (lineIndex + fit < paragraphLines.Count)
                {
                    var candidate = paragraphLines[lineIndex + fit];
                    var reserved = reserveWholeBox ? candidate.Height : candidate.Ascent;
                    if (!(atRegionTop && fit == 0) && probeY + reserved > contentBottom)
                    {
                        break;
                    }

                    probeY += candidate.Height;
                    fit++;
                }

                // Keep-lines (w:keepLines) holds a whole paragraph together — if it will not all fit here it
                // moves to the next region intact. Otherwise widow/orphan control (Word's default) keeps at
                // least two lines together: one line alone at the top of the next region (widow → carry one
                // more line down to join it) or at the bottom of this one (orphan → move the paragraph).
                // Both apply only when a break actually falls (fit < remaining) and there is somewhere better
                // to move to (not already a region top).
                //
                // The two are settled in ORDER, the orphan check acting on what the widow carry left — Word
                // does not treat them as alternatives. A three-line paragraph with room for two is the case
                // that separates the two readings: the carry takes it to one line here, and the orphan rule
                // then takes that to none. Checking them as mutually exclusive branches stops after the carry
                // and leaves behind exactly the orphan the rule exists to prevent. Verified against Word on
                // business-plans/15's "Long-term Liabilities" bullet, where the ordered form reproduces
                // Word's break (0/3) and the alternative form gives 1/2.
                var remaining = paragraphLines.Count - lineIndex;
                if (!atRegionTop && fit < remaining)
                {
                    if (properties.KeepLines)
                    {
                        fit = 0;
                    }
                    else if (properties.WidowControl && remaining >= 2)
                    {
                        if (fit == remaining - 1)
                        {
                            fit = remaining - 2;
                        }

                        if (fit == 1)
                        {
                            fit = 0;
                        }
                    }
                }

                if (fit == 0)
                {
                    AdvanceColumnOrPage();
                    if (lineIndex == 0)
                    {
                        // The first line moved to a new region, so the paragraph's pre-spacing top —
                        // Word's paragraph-anchor reference — is the new region top, not where the
                        // cursor first sat (spacing-before is dropped at a region top).
                        paragraphPreSpacingTops[paragraph] = y;
                    }

                    continue;
                }

                // The first line's region is now final: floats deferred to this anchor emit here, tagged
                // with the page the paragraph actually starts on.
                if (lineIndex == 0 && anchorDeferredFloats.Remove(paragraph, out var deferredFloats))
                {
                    foreach (var deferred in deferredFloats)
                    {
                        EmitBodyFloatAt(deferred, paragraphPreSpacingTops[paragraph]);
                    }
                }

                for (var offset = 0; offset < fit; offset++)
                {
                    var placedIndex = lineIndex + offset;
                    var line = paragraphLines[placedIndex];
                    // The float band's left edge when one narrowed this paragraph, else the column's — read
                    // per line, so a paragraph spilling into the next column follows it. A banded paragraph
                    // keeps its band even then, matching the scope production holds open across the wrap.
                    var indentLeft = (bandConstrained ? bandLeft : ColumnLeft) + (float) properties.LeftIndentPoints;
                    var firstLineShift = FirstLineIndentOffset(properties, placedIndex);
                    var lineLeft = indentLeft + firstLineShift + AlignmentOffset(properties.Alignment, availableWidth - firstLineShift, line.Width);
                    var baseline = y + line.Ascent;

                    // Paragraph shading (w:shd) fills the paragraph's column box behind the text, regardless
                    // of the text's own width or alignment — a centred title's band still spans the full
                    // column. Emitted before the line so it paints behind; one per line tiles into a band.
                    if (!string.IsNullOrEmpty(properties.BackgroundColorHex))
                    {
                        items.Add(new PlacedShading(indentLeft, y, availableWidth, line.Height, properties.BackgroundColorHex));
                    }

                    var lineRuns = LineRuns(paragraph, line, placedIndex, lineLeft);

                    // Line numbering (w:lnNumType): every counted body line carries its ordinal in
                    // the left margin, right-aligned DistancePoints left of the text column, at the
                    // line's own font — restored 2026-08-19 (the deleted production TextRenderers
                    // drew these; the engine did not, so the gutters vanished in the flip).
                    // Suppressed paragraphs (w:suppressLineNumbers) neither draw nor count.
                    if (current.LineNumbers is { } lineNumbers && !properties.SuppressLineNumbers)
                    {
                        // w:start is the value BEFORE the first counted line — Word's UI "start
                        // at 1" writes w:start="0", and the references for start="1" number their
                        // first line 2 (count_by_5 marks lines 4/9/14/19, whose values are then
                        // 5/10/15/20).
                        lineNumberCount++;
                        var ordinal = lineNumbers.Start + lineNumberCount;
                        if (ordinal % Math.Max(1, lineNumbers.CountBy) == 0)
                        {
                            var reference = lineRuns.Count > 0 ? lineRuns[0].Properties : new();
                            var digitProperties = new RunProperties
                            {
                                FontFamily = reference.FontFamily,
                                FontSizePoints = reference.FontSizePoints
                            };
                            var digits = ordinal.ToString(CultureInfo.InvariantCulture);
                            var digitsWidth = measurer.MeasureRunWidth(digits, digitProperties);
                            var withNumber = new List<PlacedRun>(lineRuns.Count + 1)
                            {
                                new(ColumnLeft - (float) lineNumbers.DistancePoints - digitsWidth, digitsWidth, digits, digitProperties)
                            };
                            withNumber.AddRange(lineRuns);
                            lineRuns = withNumber;
                        }
                    }

                    var placedLine = new PlacedLine(lineLeft, y, line.Width, line.Height, baseline, paragraph, placedIndex, lineRuns, MapImages(line, lineLeft, baseline));
                    items.Add(placedLine);

                    // A deferred float anchored above this paragraph belongs to the page this line landed on —
                    // but only once a line with real content appears, so an empty spacer paragraph at a page
                    // boundary does not claim the float for the wrong page.
                    if (pendingFloats.Count > 0 && (placedLine.Images.Count > 0 || placedLine.Runs.Any(run => !string.IsNullOrWhiteSpace(run.Text))))
                    {
                        ResolvePendingFloats();
                    }

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

            // Fold this paragraph into the open border run, or open one. The box is NOT stroked here — its
            // bottom is only known once the run ends, so FlushBorderRun emits it. A paragraph that broke
            // across a region has no single box to contribute to and ends the run instead.
            if (!borderBroke && borderTop is { } boxTop && properties.Borders is { HasAnyBorder: true })
            {
                if (borderRunProperties == null)
                {
                    borderRunProperties = properties;
                    borderRunTop = boxTop;
                    borderRunWidth = availableWidth;
                    borderRunItemsIndex = paragraphItemsStart;
                }
                else if (properties.BorderBetween.IsVisible)
                {
                    // Continuing a run whose w:between is visible: rule the boundary just crossed. It sits a
                    // between-space below the previous member's last line (probe group F: with a 6pt space the
                    // green rule fell 11px/5.3pt under F1's line bottom at 150dpi).
                    borderRunBetweens.Add(borderRunBottom + (float) properties.BorderBetweenSpacePoints);
                }

                borderRunBottom = y;
            }
            else
            {
                FlushBorderRun();
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

            var (colWidths, rowHeights) = TableGeometry(table, colCount, columnWidth);

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

            // A table that cannot fit where it stands AND has no move available to rescue it flows row by
            // row (the raster backend's needsRowByRowRendering). Two cases reach here. One is taller than
            // 110% of a column's content height, so no region could hold it whole. The other is sitting AT
            // a region top, where advancing is a no-op — every path below is gated on !atRegionTop, so a
            // table that overruns from the top used to be drawn past the bottom margin and clipped at the
            // paper edge, silently losing its tail and a page with it (Excel's basic-business-invoice: a
            // 774pt sheet on 734pt of A4 content, one page rendered against Excel's two). Fit is judged
            // with the SAME 2% slack every other break decision uses, so a table that squeezes under the
            // shared rounding tolerance still stays whole.
            // A table that merely misses the space LEFT is deliberately not routed here — the exact-row
            // pre-advance and the whole-table move below get first refusal on it.
            if (totalHeight > contentHeight * 1.10f ||
                (atRegionTop && !HasSpaceFor(totalHeight)))
            {
                PlaceTableRowByRow(table, colWidths, rowHeights, colCount, tableX, tableWidth);
                return;
            }

            // Narrow pre-advance for the fixed-height-row letter layouts: lift the whole table onto the
            // next region when it would otherwise clip its exact row, ahead of the softer whole-table move.
            // Deliberately ahead of the fit routing below, so an exact-row table keeps its whole-table lift.
            if (!atRegionTop && totalHeight > 0)
            {
                var remaining = Math.Max(0f, contentBottom - y);
                if (totalHeight > remaining + 5f && HasExactRow(table))
                {
                    AdvanceColumnOrPage();
                }
            }

            // A table that merely does not fit the space left ALSO flows row by row — Word's own behaviour
            // (probed: _probe_cantsplit_fit_off splits the boundary row 13/7 where the whole-table move put
            // all 20 lines overleaf; LibreOffice's SwTabFrame::Split deadline is likewise the remaining
            // area, not a page height). Two earlier attempts at this routing were reverted on corpus
            // damage; the piece both lacked is the split-acceptance test in PlaceSplitRow, which turns the
            // pathological splits (unbreakable content force-placed into the remainder, or a trHeight floor
            // mistaken for content) back into whole-row moves. The condition mirrors the whole-table move
            // below EXACTLY — height less 2%, against HasSpaceFor's 2%-extended bottom — so a knife-edge
            // table the move would have squeezed onto the page (business-plans/15's 79.6pt boundary table
            // clears it by 0.24pt) is not routed into a split the old path never made.
            if (!atRegionTop && !HasSpaceFor(totalHeight - contentHeight * 0.02f))
            {
                PlaceTableRowByRow(table, colWidths, rowHeights, colCount, tableX, tableWidth);
                return;
            }

            // A declared row FLOOR is strict against the hard bottom on the whole-table path too, as it is
            // on the row-by-row path: _probe_floorfit_enddoc (a 30pt atLeast floor into a 24pt remainder,
            // whole-table path) stayed on the page through the 4% the slack fits below allow — the move's
            // 2% off the height against HasSpaceFor's 2% past the bottom — where Word moves it, as it did
            // in every controlled floor fixture (_probe_floorfit_single/_last/_mid). Content keeps the
            // slack: only the sum of the rows' declared heights is tested, so a table that fits by its
            // content and misses only by rounding still squeezes on as before. Placed AFTER the row-by-row
            // routing, so a long floored table still flows from where it stands rather than moving whole.
            if (!atRegionTop && DeclaredRowFloors(table) > contentBottom - y)
            {
                AdvanceColumnOrPage();
            }

            // Whole-table move: mirrors EnsureSpaceFor(totalHeight − 2%) — a flow table may over-spill the
            // bottom by the shared rounding slack before it is pushed to the next region.
            EnsureSpaceFor(totalHeight - contentHeight * 0.02f);

            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                items.Add(BuildRow(y, table, rowIndex, colWidths, rowHeights, colCount, tableX, tableWidth, false));
                ResolvePendingFloats();
                y += rowHeights[rowIndex];
                atRegionTop = false;
            }
        }

        // The sum of a table's declared w:trHeight values, in points — the floors (atLeast) and exact
        // boxes Word never lets spill past the bottom margin, whatever the content inside them does.
        static float DeclaredRowFloors(TableElement table)
        {
            var total = 0f;
            foreach (var row in table.Rows)
            {
                if (row.HeightPoints is { } declared)
                {
                    total += (float) declared;
                }
            }

            return total;
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
                var row = table.Rows[rowIndex];

                // A vertical-merge CONTINUATION row never breaks from its predecessor — a break between
                // them would tear the merged cell apart, so the span head carries the only break decision
                // and the continuations stack under it wherever it landed, overflowing the bottom margin
                // and clipping at the paper edge if they must. Word-measured on resumes/06: a sidebar's
                // restartless-continue rows run to the paper edge (bar band at 750–792, clipped) rather
                // than moving to a page they would comfortably fit.
                if (IsMergeContinuation(row))
                {
                    items.Add(BuildRow(y, table, rowIndex, colWidths, rowHeights, colCount, tableX, tableWidth, false));
                    ResolvePendingFloats();
                    y += rowHeight;
                    atRegionTop = false;
                    continue;
                }

                // A row taller than a whole empty region cannot be rescued by moving it — it would overflow
                // wherever it went, drawing over the footer and clipping at the page edge. Word splits such a
                // row at a line boundary and continues it overleaf. Rows that DO fit keep the move-whole
                // behaviour below, which matches Word across the rest of the corpus.
                // A row that merely does not fit the space left is split too — EXCEPT a w:hRule="exact" one,
                // whose height is a verbatim box rather than a measure of its content (ECMA-376 §17.4.81).
                // Splitting one lets its sparse content collapse into the space left and the reservation
                // vanishes: table_layout_tall_row is two exact rows (80pt and 530pt) that Word carries onto a
                // second page, and splitting the tall one folded the document back to a single page.
                // Fit against the remainder is EXACT, not HasSpaceFor's 2% slack: a row fits or it
                // moves, which is Word's rule (LibreOffice's split deadline is likewise the remaining
                // area verbatim, widorp.cxx:134/157). Measured on a 1216-row summary table spanning 52
                // landscape pages (2026-08-19): the slack there is 9.6pt against a 17.7pt row, so a
                // boundary row 1.5pt past the margin stayed put where Word moves it — one extra row kept
                // on roughly every fourth page, compounding to a whole page lost by the table's end and
                // every PAGEREF below the table resolving one page low. Strict fit reproduces Word's
                // row-by-row page boundaries on that table exactly, and the corpus stays at 330/330
                // page-count matches. Zeroing HasSpaceFor globally instead costs three scenarios
                // (business-plans/15, newsletters/09, resumes/06), whose keeps ride the whole-table and
                // merge-head slack — both untouched here.
                // An earlier strict-fit attempt cost business-plans/15 a page by splitting rows the move
                // path would have kept; the piece it lacked was PlaceSplitRow's split-acceptance test,
                // which now turns those pathological splits back into whole-row moves.
                // The height is the row's FULL height, an atLeast w:trHeight floor included. A content-only
                // fit was briefly landed off a letters/04 in-situ reading ("Word keeps a floored row whose
                // content fits the remainder") and is REFUTED: all four controlled fixtures
                // (_probe_floorfit_single/_last/_mid/_enddoc, 2026-08-07) show Word MOVING a floored row
                // whose floor misses the space whatever its content does. letters/04's keep is upstream
                // height drift: Word's letter runs ~50pt more compact, so the floor simply FITS in Word's
                // layout — and the engine keeps its one-page count through the whole-table move's slack.
                //
                // A row carrying a vertical merge is exempt from the strict test: a merge span is one
                // drawn unit and Word clips its overflow at the paper edge rather than moving it
                // (resumes/06's bar span — the tie rule below stacks the continuations for the same
                // reason), so its HEAD keeps HasSpaceFor's shared slack. Without the exemption,
                // resumes/06's span head at 1.1pt over the margin moved to a fresh page and re-split the
                // document 3 pages into 6.
                var overflows = HasMergedCell(row)
                    ? !HasSpaceFor(rowHeight)
                    : y + rowHeight > contentBottom;
                var doesNotFitHere = !atRegionTop && overflows && !TableHeightCalculator.IsPinnedExact(row);
                var oversize = rowHeight > contentHeight;

                // A fit-triggered split may be rejected (the fragment placed in the remainder must
                // genuinely split — see PlaceSplitRow), falling through to the move-whole path below. An
                // oversize row has no such escape: moving it cannot rescue it, so its split always stands.
                if (rowIndex <= lastVisibleRow && (oversize || doesNotFitHere) && CanSplitRow(row) &&
                    PlaceSplitRow(table, rowIndex, colWidths, rowHeights, colCount, tableX, tableWidth, headerCount, allowReject: !oversize))
                {
                    continue;
                }

                // The move mirrors EnsureSpaceFor with the same floor-strict fit as the trigger above.
                var broke = false;
                if (rowIndex <= lastVisibleRow && !atRegionTop && !oversize && overflows)
                {
                    AdvanceColumnOrPage();
                    broke = true;
                }
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
                ResolvePendingFloats();
                y += rowHeight;
                atRegionTop = false;
            }
        }

        // Any cell continuing a vertical merge ties this row to the one above; see the tie rule in
        // PlaceTableRowByRow.
        static bool IsMergeContinuation(TableRow row)
        {
            foreach (var cell in row.Cells)
            {
                if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
                {
                    return true;
                }
            }

            return false;
        }

        // Any vertical-merge participation at all — a span HEAD included; see the strict-floor exemption.
        static bool HasMergedCell(TableRow row)
        {
            foreach (var cell in row.Cells)
            {
                if (cell.Properties.VerticalMerge != VerticalMergeType.None)
                {
                    return true;
                }
            }

            return false;
        }

        // A row may be split across pages when nothing in it ties its cells to one box. w:cantSplit says so
        // outright, and Word honours it even when splitting is the only way to show the content: a
        // cantSplit row taller than any page overflows the content area and clips at the paper edge rather
        // than continuing overleaf (Word-probed, _probe_cantsplit_tall_on — the flagged row ran to 791.5pt
        // on a 792pt page and the following paragraph was alone on page 2, where the unflagged control
        // split 53/17). Falling through to the move-whole path below reproduces that. A vertical merge ties
        // the cells too — the merged cell's height derives from the rows it spans.
        static bool CanSplitRow(TableRow row)
        {
            if (row.CannotSplit)
            {
                return false;
            }

            foreach (var cell in row.Cells)
            {
                if (cell.Properties.VerticalMerge != VerticalMergeType.None)
                {
                    return false;
                }
            }

            return true;
        }

        // Places a row too tall for one page as a run of fragments, breaking at line boundaries inside its
        // cells. Each fragment fills the space left on its page and the row continues at the top of the next,
        // with w:tblHeader rows re-emitted above each continuation exactly as an unsplit row's break does.
        //
        // With allowReject, a first fragment offered only the REMAINDER of a region must genuinely split to
        // stand: some cell has to continue overleaf, and what was placed has to fit the space. Otherwise the
        // split is rejected — nothing is emitted, false comes back, and the caller moves the row whole. This
        // is LibreOffice's lcl_RecalcSplitLine acceptance (a rejected split retries without splitting the
        // row), and it is what the two reverted trigger widenings lacked: without it, a cell whose content
        // cannot break (a nested table — LayoutCellFragment places one whole) was force-placed overflowing
        // the remainder, and a row whose w:trHeight floor was the only thing that did not fit was "split"
        // into a content-sized stub that dropped the floor. A fragment built after the sliver advance is
        // exempt — it has a whole region, and a non-continuing fragment there is the carried-whole-row case
        // (_probe_trail2_nested: authored floor honoured at the region top).
        bool PlaceSplitRow(TableElement table, int rowIndex, float[] colWidths, float[] rowHeights, int colCount, float tableX, float tableWidth, int headerCount, bool allowReject)
        {
            var row = table.Rows[rowIndex];
            var starts = new CellSplitPoint?[row.Cells.Count];
            for (var cellIndex = 0; cellIndex < starts.Length; cellIndex++)
            {
                starts[cellIndex] = default(CellSplitPoint);
            }

            var isFirstFragment = true;
            while (true)
            {
                // A sliver at the bottom of a page holds nothing useful, so start the fragment on the next
                // region rather than emitting a stub. minFragmentHeight is one generous line.
                const float minFragmentHeight = 24f;
                var advanced = false;
                if (!atRegionTop && contentBottom - y < minFragmentHeight)
                {
                    AdvanceColumnOrPage();
                    advanced = true;
                }

                // w:tblHeader rows re-emit above every fragment that follows a break — the continuations, and
                // ALSO a first fragment the sliver advance just carried to a fresh region. That advance is the
                // same page break EnsureSpaceFor produces on the unsplit path, which re-emits headers; missing
                // it here cost business-plans/13's continuation pages their three repeated header rows
                // ("Start-up costs" / agency / column headers) whenever the fit trigger routed the boundary
                // row through this path, leaving every following page to start bare at a data row.
                // Snapshotted BEFORE the repeat below, because repeated headers do not stop the row that
                // follows them from being a row CARRIED WHOLE TO A FRESH REGION — the case BuildRowFragment's
                // w:trHeight floor is for. Reading atRegionTop after the loop saw the header rows' own
                // atRegionTop = false and dropped the floor, which cost business-plans/13's page 20 its
                // first data row: content-sized at 11pt against Word's declared 21.6pt, shifting the eight
                // rows below it 23px up the page (measured, 150 DPI).
                var carriedToRegionTop = atRegionTop;
                if ((!isFirstFragment || advanced) && headerCount > 0 && rowIndex >= headerCount)
                {
                    for (var headerIndex = 0; headerIndex < headerCount; headerIndex++)
                    {
                        items.Add(BuildRow(y, table, headerIndex, colWidths, rowHeights, colCount, tableX, tableWidth, true));
                        y += rowHeights[headerIndex];
                        atRegionTop = false;
                    }
                }

                var available = Math.Max(minFragmentHeight, contentBottom - y);
                var (fragment, continuations, height, contentHeight) = BuildRowFragment(y, table, rowIndex, colWidths, colCount, tableX, tableWidth, starts, available, isFirstFragment, carriedToRegionTop);

                var finished = true;
                foreach (var continuation in continuations)
                {
                    if (continuation != null)
                    {
                        finished = false;
                        break;
                    }
                }

                // The acceptance test. Nothing has been emitted yet on this path (no advance, so no header
                // re-emission either), so rejection leaves the cursor untouched for the caller's move.
                if (allowReject && isFirstFragment && !advanced && (finished || contentHeight > available + 0.5f))
                {
                    return false;
                }

                items.Add(fragment);
                ResolvePendingFloats();
                y += height;
                atRegionTop = false;

                if (finished)
                {
                    return true;
                }

                starts = continuations;
                isFirstFragment = false;
                AdvanceColumnOrPage();
            }
        }

        // One fragment of a split row: the same geometry as BuildRow, but each cell lays out only what fits in
        // <paramref name="available"/> starting from its own carried split point. A cell that continues
        // overleaf drops its bottom border and a fragment that is not the first drops its top border, so the
        // split edge draws no rule — Word runs the cell's sides through the break and closes the box only at
        // the row's real end.
        (PlacedTableRow Row, CellSplitPoint?[] Continuations, float Height, float ContentHeight) BuildRowFragment(
            float rowY,
            TableElement table,
            int rowIndex,
            float[] colWidths,
            int colCount,
            float tableX,
            float tableWidth,
            CellSplitPoint?[] starts,
            float available,
            bool isFirstFragment,
            bool atRegionStart)
        {
            var row = table.Rows[rowIndex];
            var cells = new List<PlacedCell>();
            var continuations = new CellSplitPoint?[row.Cells.Count];
            var cellX = tableX;
            var gridColIndex = 0;
            var usedHeight = 0f;
            var anyContinues = false;

            // A fragment pays for its own horizontal edges, exactly as TableHeightCalculator's collapse pass
            // charges an unsplit row for its outer top and bottom: Word draws each edge on the boundary and
            // insets the content below it, so the edges come out of what the cells have to fill. Word closes
            // every fragment with a rule and reopens the next one with another, so both are charged on every
            // fragment rather than only at the row's real ends. Without this the budget over-reports by the
            // two edge widths, which is a whole extra line whenever the content divides the region evenly —
            // _probe_cantsplit_tall_off is exactly that case: 648pt of region against 12pt lines, where the
            // unpaid budget takes 54 lines to Word's 53. An exact row is exempt, mirroring the height model,
            // since ECMA-376 §17.4.81 makes its height verbatim against borders as against content.
            var chargesEdges = !TableHeightCalculator.IsPinnedExact(row);
            var topEdge = chargesEdges ? TableHeightCalculator.HorizontalBorderWidth(table, colCount, rowIndex, top: true) : 0f;
            var bottomEdge = chargesEdges ? TableHeightCalculator.HorizontalBorderWidth(table, colCount, rowIndex, top: false) : 0f;
            var horizontalEdges = topEdge + bottomEdge;

            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = cell.Properties.GridSpan;

                var cellWidth = 0f;
                for (var offset = 0; offset < span && gridColIndex + offset < colCount; offset++)
                {
                    cellWidth += colWidths[gridColIndex + offset];
                }

                var padding = TableLayout.GetEffectivePadding(cell.Properties, table.Properties, row);
                var borders = FragmentBorders(cell, table, rowIndex, gridColIndex, colCount, row);
                var start = starts[cellIndex];
                if (start == null)
                {
                    // This cell finished on an earlier fragment; it still draws its box and sides.
                    cells.Add(new(cellX, rowY, cellWidth, available, cell.Properties.BackgroundColorHex, borders, [], BottomEdgeInset: bottomEdge, Diagonals: cell.Properties.Diagonals));
                    cellX += cellWidth;
                    gridColIndex += span;
                    continue;
                }

                var (insetLeft, insetRight) = SideInsets(padding, borders);
                var budget = Math.Max(0f, available - (float) padding.Vertical - horizontalEdges);
                var (content, continuation, height) = LayoutCellFragment(
                    cell,
                    cellX + insetLeft,
                    rowY + (float) padding.Top + topEdge,
                    cellWidth - insetLeft - insetRight,
                    budget,
                    cell.Properties.VerticalAlignment,
                    start.Value,
                    budget);

                continuations[cellIndex] = continuation;
                anyContinues |= continuation != null;
                usedHeight = Math.Max(usedHeight, height + (float) padding.Vertical);

                // Cell-anchored behind-text art belongs to the row's first fragment; repeating it on every
                // continuation would stamp it down the document.
                if (isFirstFragment)
                {
                    var floatShapes = ResolveCellFloatShapes(cell, cellX, rowY);
                    if (floatShapes.Count > 0)
                    {
                        content = [.. floatShapes, .. content];
                    }
                }

                cells.Add(new(cellX, rowY, cellWidth, available, cell.Properties.BackgroundColorHex, borders, content, cell.Properties.ClipOverflow, (float) cell.Properties.ClipSpillLeftPoints, (float) cell.Properties.ClipSpillRightPoints, bottomEdge, cell.Properties.Diagonals));

                cellX += cellWidth;
                gridColIndex += span;
            }

            // Every fragment shrinks to what it actually used, the one that carries on included — Word closes
            // it tight around its content rather than stretching it to the region bottom (_probe_cantsplit_
            // tall_off: Word's closing rule sits at 708.96 against a 720pt content bottom, hard against the
            // 53 lines it holds, not 11pt below them). Only the drawn box is at stake: the cursor moves to
            // the next region straight after a continuing fragment either way.
            var fragmentHeight = Math.Min(available, usedHeight + horizontalEdges);

            // A whole row CARRIED TO A REGION TOP keeps its authored atLeast floor (w:trHeight): Word
            // places such a row at its declared height — probed with a 100pt row of ~25pt content
            // (_probe_trail2_nested/_bordered), where the paragraph after the table starts at 174.72pt,
            // margin plus the full declared height, against 99.36 for the content-sized fragment. The floor
            // is deliberately NARROW, each widening having been tried and adjudicated away from Word:
            // the calculator's full rowHeights model regressed even floor-less scenarios
            // (header_row_repeat/01 and table_multipage author no trHeight — actual layout beats the model's
            // estimate), and applying the authored floor to a row SQUEEZED into the page-bottom remainder
            // without advancing regressed business-plans/13 on every backend, while business/03 — whose
            // floored rows land at region tops, the probed geometry — improved. A row split across regions
            // fills by content; no probe covers distributing a declared height across fragments.
            if (isFirstFragment &&
                atRegionStart &&
                !anyContinues &&
                row is
                {
                    IsExactHeight: false,
                    HeightPoints: { } declaredFloor
                })
            {
                fragmentHeight = Math.Min(available, Math.Max(fragmentHeight, (float) declaredFloor));
            }
            var boxed = new List<PlacedCell>(cells.Count);
            foreach (var cell in cells)
            {
                boxed.Add(cell with {Height = fragmentHeight});
            }

            return (new(tableX, rowY, tableWidth, fragmentHeight, table, rowIndex, false, boxed), continuations, fragmentHeight, usedHeight + horizontalEdges);
        }

        // Every fragment draws its whole box, split edges included. This read the other way until Word was
        // asked directly (_probe_cantsplit_tall_off): Word CLOSES the fragment that carries on with a rule at
        // 708.96 and REOPENS the continuation with another at 72.00, rather than running the cell's sides
        // through the break and boxing only the row's real ends. The edges are charged to the fragment's
        // content budget above, so the rules and the space they occupy stay in step.
        static CellBorders? FragmentBorders(TableCell cell, TableElement table, int rowIndex, int gridColIndex, int colCount, TableRow row) =>
            TableLayout.ResolveCellBorders(cell.Properties, table.Properties, rowIndex, gridColIndex, table.Rows.Count, colCount, row, table.Rows);

        // Builds a placed row at the current cursor: the row box plus each cell's box, shading, borders and
        // laid-out content. Cell geometry mirrors the deleted production RenderTableRow so the tree matches the
        // raster backend's grid — merge-continuation cells contribute nothing (the originating cell covers
        // them), and a merge-restart cell's box spans the merged rows' heights.
        PlacedTableRow BuildRow(float rowY, TableElement table, int rowIndex, float[] colWidths, float[] rowHeights, int colCount, float tableX, float tableWidth, bool isRepeatedHeader)
        {
            var row = table.Rows[rowIndex];
            var rowHeight = rowHeights[rowIndex];
            var cells = new List<PlacedCell>();
            var cellX = tableX;
            var gridColIndex = 0;
            var detached = table.Properties.CellSpacingPoints > 0;

            // The horizontal edges TableHeightCalculator charged this row: its top edge always (an exact
            // row excepted), its bottom edge only when it closes the table. Word hangs each edge DOWN from
            // its grid line and starts the content under the whole declared stack — a 6pt rule puts the
            // row's text 6pt lower than an unbordered row's, a 3pt `double` 9pt lower (_probe_cellw /
            // _probe_cellfam, XPS) — so the reserve is spent above the content, not left under it.
            var lastRowIndex = table.Rows.Count - 1;
            var topEdge = TableHeightCalculator.IsPinnedExact(row) ? 0f : TableHeightCalculator.HorizontalBorderWidth(table, colCount, rowIndex, top: true);
            var bottomEdge = TableHeightCalculator.IsPinnedExact(table.Rows[lastRowIndex]) ? 0f : TableHeightCalculator.HorizontalBorderWidth(table, colCount, lastRowIndex, top: false);

            // Detached-border model: the table FRAME is its own box at the row extent — left/right
            // on every row, top/bottom closing the first/last — with each cell's box inset inside
            // its slot (TableLayout.CellSpacingInsets carries the Word-probed gap law). A synthetic
            // content-less cell carries the frame so the painters need no new item kind.
            if (detached && table.Properties.DefaultBorders is {HasAnyBorder: true} frame)
            {
                var frameEdges = new CellBorders
                {
                    Top = rowIndex == 0 ? frame.Top : BorderEdge.None,
                    Bottom = rowIndex == table.Rows.Count - 1 ? frame.Bottom : BorderEdge.None,
                    Left = frame.Left,
                    Right = frame.Right
                };
                cells.Add(new(tableX, rowY, tableWidth, rowHeight, null, frameEdges, [], BottomEdgeInset: rowIndex == lastRowIndex ? bottomEdge : 0f));
            }

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
                var endRowIndex = cell.Properties.VerticalMerge == VerticalMergeType.Restart
                    ? TableLayout.VerticalMergeEndRow(table, rowIndex, gridColIndex)
                    : rowIndex;
                var cellBottomEdge = endRowIndex == lastRowIndex ? bottomEdge : 0f;

                var boxX = cellX;
                var boxY = rowY;
                var boxWidth = cellWidth;
                var boxHeight = cellHeight;
                if (detached)
                {
                    var insets = TableLayout.CellSpacingInsets(table.Properties, gridColIndex, span, colCount, rowIndex, table.Rows.Count);
                    boxX += (float) insets.Left;
                    boxY += (float) insets.Top;
                    boxWidth -= (float) insets.Horizontal;
                    boxHeight -= (float) insets.Vertical;
                }

                var padding = TableLayout.GetEffectivePadding(cell.Properties, table.Properties, row);
                var borders = TableLayout.ResolveCellBorders(cell.Properties, table.Properties, rowIndex, gridColIndex, table.Rows.Count, colCount, row, table.Rows);
                var (insetLeft, insetRight) = SideInsets(padding, borders);
                var content = cell.Properties.TextDirection == CellTextDirection.LeftToRight
                    ? LayoutCellContent(cell, boxX + insetLeft, boxY + (float) padding.Top + topEdge, boxWidth - insetLeft - insetRight, boxHeight - (float) padding.Vertical - topEdge - cellBottomEdge, cell.Properties.VerticalAlignment)
                    : LayoutRotatedCellContent(cell, boxX + insetLeft, boxY + (float) padding.Top + topEdge, boxWidth - insetLeft - insetRight, boxHeight - (float) padding.Vertical - topEdge - cellBottomEdge);

                // Behind-text floats (a label template's coloured cell background and freeform blobs) paint
                // before the cell's paragraphs, so prepend them to the content.
                var floatShapes = ResolveCellFloatShapes(cell, cellX, rowY);
                if (floatShapes.Count > 0)
                {
                    content = [.. floatShapes, .. content];
                }

                cells.Add(new(boxX, boxY, boxWidth, boxHeight, cell.Properties.BackgroundColorHex, borders, content, cell.Properties.ClipOverflow, (float) cell.Properties.ClipSpillLeftPoints, (float) cell.Properties.ClipSpillRightPoints, cellBottomEdge, cell.Properties.Diagonals));

                cellX += cellWidth;
                gridColIndex += span;
            }

            return new(tableX, rowY, tableWidth, rowHeight, table, rowIndex, isRepeatedHeader, cells);
        }

        // Word insets a cell's content by the LARGER of its margin and half its border stack on each side —
        // the same max the autofit measure charges (TableLayout.HalfBorder carries the probe) — so a wide
        // rule with a small margin pushes the text in, and the default 5.4pt margin swallows any rule
        // under 10.8pt.
        static (float Left, float Right) SideInsets(CellSpacing padding, CellBorders? borders) =>
            ((float) Math.Max(padding.Left, TableLayout.HalfBorder(borders?.Left)),
                (float) Math.Max(padding.Right, TableLayout.HalfBorder(borders?.Right)));

        // Cell-anchored behind-text shapes resolved to absolute boxes: the offset is measured from the
        // cell's top-left (Word anchors these to the cell frame). Solid, gradient, outline and image
        // fills render (an image-fill shape paints as a plain image, mirroring the body-float case —
        // brochures/04's construction photo is one, silently dropped before this); in-front-of-text
        // floats and the paragraph-anchor walk that positions non-cell-top floats are later slices
        // (each painter's PaintShape skips what it cannot draw).
        static IReadOnlyList<PlacedItem> ResolveCellFloatShapes(TableCell cell, float cellX, float cellY)
        {
            if (cell.Floats.Count == 0)
            {
                return [];
            }

            var shapes = new List<PlacedItem>();
            foreach (var element in cell.Floats)
            {
                if (element is not FloatingShapeElement {BehindText: true} shape)
                {
                    continue;
                }

                var shapeX = cellX + (float) shape.HorizontalPositionPoints;
                var shapeY = cellY + (float) shape.VerticalPositionPoints;
                if (shape.ImageData is { Length: > 0 } shapeImage && shape.ImageContentType != "image/svg+xml")
                {
                    shapes.Add(new PlacedImage(shapeX, shapeY, (float) shape.WidthPoints, (float) shape.HeightPoints, shapeImage, shape.RotationDegrees, shape.FlipHorizontal, shape.FlipVertical, Opacity: shape.ImageOpacity));
                }
                else if (shape.ImageData == null)
                {
                    shapes.Add(new PlacedShape(shapeX, shapeY, (float) shape.WidthPoints, (float) shape.HeightPoints, shape));
                }
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

            var (colWidths, rowHeights) = TableGeometry(table, colCount, width);

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

        // A table's column widths and row heights at one measure. Both derive only from the table, that
        // width and the measurer, and every caller treats them as read-only, so they are computed once per
        // (table, width) and shared. The repeat this removes is per PAGE: a header or footer table has its
        // height measured to reserve the band (HeaderReservedTop / FooterBand) and is then laid out again by
        // LayoutNestedTable, and both halves ran the full autofit and row-height math on every page of the
        // document. A floating table pays a similar pair — the column widths that size it, then the layout.
        readonly Dictionary<(TableElement Table, float Width), (float[] ColWidths, float[] RowHeights)> tableGeometryCache = [];

        (float[] ColWidths, float[] RowHeights) TableGeometry(TableElement table, int colCount, float width)
        {
            if (tableGeometryCache.TryGetValue((table, width), out var cached))
            {
                return cached;
            }

            var colWidths = TableLayout.CalculateColumnWidths(table, colCount, width, measurer);
            var rowHeights = TableHeightCalculator.CalculateRowHeights(
                table, colWidths, measurer, addInteriorBorders: true);
            var geometry = (colWidths, rowHeights);
            tableGeometryCache[(table, width)] = geometry;
            return geometry;
        }

        // The total height a table would occupy at the given width — the sum of its row heights. Used to
        // anchor a footer band (whose bottom sits a fixed distance above the page edge) before laying it out,
        // and to reserve a header table's height above the body (HeaderReservedTop).
        float NestedTableHeight(TableElement table, float width)
        {
            var colCount = TableLayout.GetColumnCount(table);
            if (colCount == 0 || table.Rows.Count == 0)
            {
                return 0f;
            }

            var (_, rowHeights) = TableGeometry(table, colCount, width);
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
        // A w:textDirection cell (btLr / tbRl): the content wraps against the cell's HEIGHT and stacks
        // across its width — laid out in an unrotated box of those swapped dimensions centred on the
        // content box, then rotated into place by the painters (PlacedRotatedGroup). btLr reads bottom to
        // top (−90°), tbRl top to bottom (+90°). Word wraps "Header" in feature_capture/01's 75pt-wide
        // header cell into two vertical lines against the row's height.
        IReadOnlyList<PlacedItem> LayoutRotatedCellContent(TableCell cell, float contentLeft, float contentTop, float contentWidth, float contentHeight)
        {
            var centreX = contentLeft + contentWidth / 2;
            var centreY = contentTop + contentHeight / 2;
            var unrotatedLeft = centreX - contentHeight / 2;
            var unrotatedTop = centreY - contentWidth / 2;
            var items = LayoutCellContent(cell, unrotatedLeft, unrotatedTop, contentHeight, contentWidth, cell.Properties.VerticalAlignment);
            var rotation = cell.Properties.TextDirection == CellTextDirection.BottomToTop ? -90 : 90;
            return [new PlacedRotatedGroup(unrotatedLeft, unrotatedTop, contentHeight, contentWidth, items, rotation)];
        }

        IReadOnlyList<PlacedItem> LayoutCellContent(TableCell cell, float contentLeft, float contentTop, float contentWidth, float availableHeight, CellVerticalAlignment verticalAlignment) =>
            LayoutCellFragment(cell, contentLeft, contentTop, contentWidth, availableHeight, verticalAlignment, default, budget: null).Items;

        /// <summary>
        /// Lays a cell's content out from <paramref name="start"/>, stopping once <paramref name="budget"/>
        /// points of height are used. A null budget places everything — the whole-row case, byte-identical to
        /// before row splitting existed. With a budget the return carries the point the content reached, so
        /// the caller can continue the cell on the next page; <c>null</c> means the cell finished.
        /// At least one line is always placed, so a budget too small to hold anything still advances.
        /// </summary>
        (IReadOnlyList<PlacedItem> Items, CellSplitPoint? Continuation, float Height) LayoutCellFragment(
            TableCell cell,
            float contentLeft,
            float contentTop,
            float contentWidth,
            float availableHeight,
            CellVerticalAlignment verticalAlignment,
            CellSplitPoint start,
            float? budget)
        {
            var lines = new List<PlacedItem>();
            var cellY = contentTop;
            var lastCellAfter = 0f;
            var lastCellVisibleAfter = 0f;
            var lastCellContextual = false;
            string? lastCellStyleId = null;
            // A continuation fragment is never at the cell's start, so its first paragraph does not get the
            // first-paragraph spacing-before treatment (and a paragraph resumed mid-way gets no spacing at all).
            var first = start is {ElementIndex: 0, LineIndex: 0};
            var hasNestedTable = false;
            var limit = budget is { } allowed ? contentTop + allowed : float.MaxValue;
            CellSplitPoint? continuation = null;

            // Paragraph-border run (w:pBdr) within the cell — the same group law the page flow applies
            // through SharesBorderGroupWith: consecutive same-bordered paragraphs share ONE box, stroked
            // when the run ends, with w:between ruling the internal boundaries. cover-letters/10's date
            // cell is the shape that demanded it: three Heading2 paragraphs whose STYLE carries a top
            // rule draw a single rule above the first, which the cell path silently dropped.
            // TableHeightCalculator.MeasureCellHeight charges the same reserves so measure = placement.
            ParagraphProperties? cellBorderRun = null;
            var cellBorderRunTop = 0f;
            var cellBorderRunBottom = 0f;
            List<float>? cellBorderBetweens = null;

            void FlushCellBorderRun()
            {
                if (cellBorderRun is not {Borders: { } runBorders} run)
                {
                    cellBorderRun = null;
                    cellBorderBetweens = null;
                    return;
                }

                var boxLeft = contentLeft + (float) (run.LeftIndentPoints - run.HangingIndentPoints) - (float) run.BorderLeftSpacePoints - BorderBoxLeftOutset;
                var boxWidth = contentWidth - (float) run.LeftIndentPoints - (float) run.RightIndentPoints
                               + (float) run.HangingIndentPoints + (float) run.BorderLeftSpacePoints + (float) run.BorderRightSpacePoints
                               + BorderBoxLeftOutset + BorderBoxRightOutset;
                var boxTop = cellBorderRunTop - (float) run.BorderTopSpacePoints;
                var boxHeight = cellBorderRunBottom - cellBorderRunTop + (float) run.BorderTopSpacePoints + (float) run.BorderBottomSpacePoints;
                lines.Add(new PlacedBorder(boxLeft, boxTop, boxWidth, boxHeight, runBorders));
                if (cellBorderBetweens != null)
                {
                    foreach (var betweenY in cellBorderBetweens)
                    {
                        lines.Add(
                            new PlacedBorder(
                                boxLeft,
                                betweenY,
                                boxWidth,
                                0,
                                new()
                                {
                                    Top = run.BorderBetween
                                }));
                    }

                    cellBorderBetweens = null;
                }

                cellY += EdgeReserve(runBorders.Bottom, run.BorderBottomSpacePoints);
                cellBorderRun = null;
            }

            for (var elementIndex = start.ElementIndex; elementIndex < cell.Content.Count; elementIndex++)
            {
                var element = cell.Content[elementIndex];
                // Where a paragraph was cut in half by a page break, resume at the line it reached.
                var resumeLine = elementIndex == start.ElementIndex ? start.LineIndex : 0;

                // Anything already placed and no room left for this element: stop here and let the caller
                // continue the cell on the next page.
                if (budget != null && lines.Count > 0 && cellY >= limit)
                {
                    continuation = new(elementIndex, resumeLine);
                    break;
                }

                // A nested table lays out at the cell cursor with no page breaks — the outer row height
                // already accommodates it (TableHeightCalculator measures nested tables). Its rows are
                // PlacedTableRows, which the vertical-alignment shift below cannot move, so a cell holding one
                // stays top-aligned.
                if (element is TableElement nestedTable)
                {
                    FlushCellBorderRun();
                    cellY += first ? 0 : lastCellAfter;
                    first = false;
                    hasNestedTable = true;
                    var (nestedItems, nestedHeight) = LayoutNestedTable(nestedTable, contentLeft, cellY, contentWidth);
                    lines.AddRange(nestedItems);
                    cellY += nestedHeight;
                    lastCellAfter = 0;
                    lastCellVisibleAfter = 0;
                    lastCellContextual = false;
                    continue;
                }

                // An unwarped WordArt in a cell (menus/03's EVENT/DATE labels, wedding/08's badge) paints its
                // box chrome and its centred text inside the cell, taking its declared height.
                if (element is WordArtElement cellWordArt)
                {
                    FlushCellBorderRun();
                    cellY += first ? 0 : lastCellAfter;
                    first = false;
                    var wordArtWidth = Math.Min((float) cellWordArt.WidthPoints, contentWidth);
                    if (cellWordArt.Transform != WordArtTransform.None)
                    {
                        lines.Add(new PlacedWordArt(contentLeft, cellY, wordArtWidth, (float) cellWordArt.HeightPoints, cellWordArt));
                    }
                    else
                    {
                        if (WordArtBoxShape(cellWordArt, contentLeft, cellY) is { } boxItem)
                        {
                            lines.Add(boxItem);
                        }

                        lines.AddRange(LayoutWordArtText(cellWordArt.Text, cellWordArt.FontFamily, cellWordArt.FontSizePoints, cellWordArt.Bold, cellWordArt.Italic, cellWordArt.FillColorHex, contentLeft, cellY, wordArtWidth, (float) cellWordArt.HeightPoints));
                    }

                    cellY += (float) cellWordArt.HeightPoints;
                    lastCellAfter = 0;
                    lastCellVisibleAfter = 0;
                    lastCellContextual = false;
                    continue;
                }

                // A content control in a cell (labels/07's [Name] placeholders) renders as its synthetic
                // paragraph — the parser resolved each control's visible text (checkbox glyph, dropdown
                // selection, formatted date, plain text) into that paragraph's runs.
                var paragraph = element as ParagraphElement ?? (element as ContentControlElement)?.CellParagraph;
                if (paragraph is null)
                {
                    // Non-paragraph content separates the paragraphs either side of it, so the next one
                    // is not the contextual neighbour of the last (wedding/08's cell is Title, inline
                    // oval, Title — the drawing keeps the two contextual Titles apart).
                    FlushCellBorderRun();
                    lastCellContextual = false;
                    lastCellStyleId = null;
                    continue;
                }

                var properties = paragraph.Properties;

                // A paragraph that does not continue the open border run closes it here, before its own
                // spacing is charged — mirroring PlaceParagraph's order in the page flow.
                if (cellBorderRun is { } openCellRun && !openCellRun.SharesBorderGroupWith(properties))
                {
                    FlushCellBorderRun();
                }
                // Space-before, collapsed with the previous paragraph's after (max, not sum). Unlike page
                // flow, a cell's FIRST paragraph keeps its space-before — TableHeightCalculator sizes the
                // cell with it, so the content must be positioned with it too or it floats to the top and
                // leaves the gap at the bottom. w:contextualSpacing removes the gap entirely between two
                // same-style contextual paragraphs — production collapses in bounded (cell) rendering too
                // (PdfTextEngine.TrackContextualSpacing "in both flow and bounded"), so the placement here
                // collapses as well (cover-letters/09's Address block: five 42pt-after contextual
                // paragraphs rendered 42pt apart instead of tight). TableHeightCalculator once measured
                // WITHOUT the collapse, leaving the row oversized by every suppressed gap; it now applies
                // the same rule, so measure and placement agree.
                var cellContextualCollapse = properties.ContextualSpacing && lastCellContextual && properties.StyleId == lastCellStyleId;
                cellY += first
                    ? (float) properties.SpacingBeforePoints
                    : cellContextualCollapse
                        ? 0f
                        : Math.Max(lastCellAfter, (float) properties.SpacingBeforePoints);
                first = false;

                // Opening a border run reserves its top rule and w:space, exactly as the page flow does
                // after the collapsed spacing-before.
                if (cellBorderRun == null && properties.Borders is {HasAnyBorder: true} openingBorders)
                {
                    cellY += EdgeReserve(openingBorders.Top, properties.BorderTopSpacePoints);
                }

                // A single-line cell wraps against an unbounded width — the line simply runs past the
                // cell's edge. ALIGNMENT still uses the cell's real width, so a short line in such a
                // cell centres and right-aligns exactly as it would in any other (AlignmentOffset
                // leaves an over-long line at the left edge).
                var wrapWidth = cell.Properties.SingleLine ? TableHeightCalculator.UnboundedWidth : contentWidth;
                var paragraphLines = measurer.LayoutLineContents(paragraph, wrapWidth);
                var isEmpty = paragraphLines is [{Width: <= 0} _];
                var textLeft = contentLeft + (float) properties.LeftIndentPoints;
                var availableWidth = contentWidth - (float) properties.LeftIndentPoints - (float) properties.RightIndentPoints;

                // How many of the paragraph's remaining lines this fragment takes. Without a budget that is
                // all of them (the whole-row path, unchanged). With one, count what fits — always at least
                // one line, so a fragment can never come out empty and loop forever — then apply the same
                // keep-lines and widow/orphan rules PlaceParagraph uses for the page flow, so a paragraph
                // breaks at the same place whether it sits in the flow or inside a splittable row.
                var remaining = paragraphLines.Count - resumeLine;
                var take = remaining;
                if (budget != null)
                {
                    // A cell paragraph must fit WITH its after-spacing: in the flow Word lets a last
                    // line's after-spacing hang past the page bottom, in a cell it does not — XPS-read
                    // on _probe_cellheight2 (89 one-line Normal paragraphs, 8pt after, line 276): the flow
                    // page holds 28 lines and the same cell 27, its 28th line (bottom 719.2 of 720) moved
                    // overleaf with the 8pt it could not fit. Zero after-spacing (_probe_cellheight)
                    // holds 48 in both. The row-height law already sizes an unsplit row with the trailing
                    // after (TableHeightCalculator), so only the split needs the reserve.
                    take = 0;
                    var probeY = cellY;
                    for (var lineIndex = resumeLine; lineIndex < paragraphLines.Count; lineIndex++)
                    {
                        var candidate = paragraphLines[lineIndex];
                        var afterReserve = lineIndex == paragraphLines.Count - 1 ? (float) properties.SpacingAfterPoints : 0f;
                        if ((lines.Count > 0 || take > 0) && probeY + candidate.Height + afterReserve > limit)
                        {
                            break;
                        }

                        probeY += candidate.Height;
                        take++;
                    }

                    // Only when a break actually falls here and there is somewhere better to put the
                    // paragraph — with nothing placed yet, moving it on would just empty this fragment.
                    if (lines.Count > 0 && take < remaining)
                    {
                        if (properties.KeepLines)
                        {
                            take = 0;
                        }
                        else if (properties.WidowControl && remaining >= 2)
                        {
                            // Word settles the two rules in order and lets the second act on the first's
                            // result. A widow — one line alone at the top of the next page — is fixed by
                            // carrying a second line down to join it; if that leaves a lone line behind, the
                            // orphan rule then moves the whole paragraph. business-plans/15's three-line
                            // "Long-term Liabilities" bullet is the case: two lines fit, the widow carry
                            // takes it to one, and the orphan rule takes that to none — which is exactly
                            // where Word breaks.
                            if (take == remaining - 1)
                            {
                                take = remaining - 2;
                            }

                            if (take == 1)
                            {
                                take = 0;
                            }
                        }
                    }
                }

                var memberTop = cellY;
                for (var lineIndex = resumeLine; lineIndex < resumeLine + take; lineIndex++)
                {
                    var line = paragraphLines[lineIndex];
                    var firstLineShift = FirstLineIndentOffset(properties, lineIndex);
                    var lineLeft = textLeft + firstLineShift + AlignmentOffset(properties.Alignment, availableWidth - firstLineShift, line.Width, cell.Properties.SingleLine);
                    var baseline = cellY + line.Ascent;
                    lines.Add(new PlacedLine(lineLeft, cellY, line.Width, line.Height, baseline, paragraph, lineIndex, LineRuns(paragraph, line, lineIndex, lineLeft), MapImages(line, lineLeft, baseline)));
                    cellY += line.Height;
                }

                if (take < remaining)
                {
                    // The rest of this paragraph continues on the next page. Any open run covering the
                    // PREVIOUS paragraphs still strokes (the loop-exit flush); this split paragraph's own
                    // box is dropped, as in the flow (per-fragment borders are a later slice).
                    continuation = new(elementIndex, resumeLine + take);
                    break;
                }

                // Fold this whole-placed paragraph into the open border run, or open one. A paragraph
                // resumed mid-way (a continuation fragment) has no single box and ends the run instead.
                if (resumeLine == 0 && properties.Borders is {HasAnyBorder: true})
                {
                    if (cellBorderRun == null)
                    {
                        cellBorderRun = properties;
                        cellBorderRunTop = memberTop;
                    }
                    else if (properties.BorderBetween.IsVisible)
                    {
                        (cellBorderBetweens ??= []).Add(cellBorderRunBottom + (float) properties.BorderBetweenSpacePoints);
                    }

                    cellBorderRunBottom = cellY;
                }
                else
                {
                    FlushCellBorderRun();
                }

                // An empty paragraph's after-spacing still separates it from the NEXT paragraph —
                // production's bounded render advances past EmptyLineHeight + full after
                // (PdfTextEngine.Draw's empty arm) — but a TRAILING empty's after does not count as
                // content space for centre/bottom alignment (ParagraphHasVisibleContent in
                // the deleted production mirror). cover-letters/09 ends its Address block with an empty
                // 42pt-after paragraph before the salutation: dropping that gap sat the whole letter
                // body 42pt high.
                lastCellAfter = (float) properties.SpacingAfterPoints;
                lastCellVisibleAfter = isEmpty ? 0 : lastCellAfter;
                lastCellContextual = properties.ContextualSpacing;
                lastCellStyleId = properties.StyleId;
            }

            // A run still open at the end of the cell's content strokes here, before the trailing
            // spacing, so the box closes at its last member's bottom.
            FlushCellBorderRun();

            // The trailing after-spacing counts as content space in full — MeasureCellHeight sizes the
            // row with it and the production render counted it in its alignment content height, so
            // leaving it out here made every centre/bottom-aligned cell sit lower by that spacing
            // (labels/12's bottom-aligned label cells ran exactly their 10pt after-spacing low). A
            // trailing EMPTY paragraph's after is excluded, mirroring ParagraphHasVisibleContent. A
            // fragment that continues overleaf has no trailing paragraph yet, so it charges none.
            if (continuation == null)
            {
                cellY += lastCellVisibleAfter;
            }

            // Centre/bottom alignment shifts the whole content down within the cell's available height
            // (top alignment leaves it at the padded top).
            // Skipped when a nested table is present, since ShiftDown only moves text lines, and for a split
            // row — its content no longer has one box to be centred in, and Word tops each fragment out.
            var offset = hasNestedTable || budget != null
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

            return (lines, continuation, cellY - contentTop);
        }

        // Moves a placed line (and its inline images) down the page by an offset, for cell vertical
        // alignment. Runs carry no Y of their own — the painter draws them at the line's baseline — so
        // shifting Y and Baseline moves the text with the box.
        static PlacedItem ShiftDown(PlacedItem item, float offset)
        {
            if (item is PlacedLine line)
            {
                return ShiftLine(line, 0, offset);
            }

            // A paragraph-border box rides with the lines it wraps.
            if (item is PlacedBorder border)
            {
                return border with {Y = border.Y + offset};
            }

            return item;
        }

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
                    shifted[imageIndex] = images[imageIndex] with
                    {
                        X = images[imageIndex].X + dx,
                        Y = images[imageIndex].Y + dy
                    };
                }

                images = shifted;
            }

            return line with
            {
                X = line.X + dx,
                Y = line.Y + dy,
                Baseline = line.Baseline + dy,
                Runs = runs,
                Images = images
            };
        }

        // Lays out a header or footer's content as a self-contained band from (left, top), wrapping each
        // paragraph to the band width and stacking it with its own spacing — no page breaks (a band fits
        // its margin area). Reuses the body's line mapping, alignment and shading; a band table lays out
        // inline via the nested-table layout, and page-number fields are substituted per page before the
        // band lays out (SubstitutePageFields). Band paragraph borders are a later slice; an empty
        // paragraph adds only its invisible mark line.
        IReadOnlyList<PlacedItem> LayoutBand(IReadOnlyList<DocumentElement> elements, float left, float top, float width)
        {
            var result = new List<PlacedItem>();
            var bandY = top;
            foreach (var element in elements)
            {
                // A header/footer table (a page-number bar, an agenda's footer grid) lays out inline in the
                // band, reusing the nested-table layout.
                //
                // w:tblInd applies here as it does in the body. A band table is the usual way to draw a
                // FULL-BLEED rule or colour bar, and it does that with a NEGATIVE indent: a one-column
                // 12792-twip table indented -1593 starts 79.65pt left of a 36pt margin, i.e. off the paper,
                // and runs the full width of the sheet. Pinning it at the band's left edge instead left the
                // bar starting at the margin and overhanging the right edge — a banner visibly inset on one
                // side and clipped on the other.
                if (element is TableElement bandTable)
                {
                    var (tableItems, tableHeight) = LayoutNestedTable(bandTable, left + (float) bandTable.Properties.IndentPoints, bandY, width);
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
                    var firstLineShift = FirstLineIndentOffset(properties, lineIndex);
                    var lineLeft = indentLeft + firstLineShift + AlignmentOffset(properties.Alignment, availableWidth - firstLineShift, line.Width);
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
        // may be empty); an even page its even variant when the document opts in; other pages the default.
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
        // page's bottom edge, with PAGE/NUMPAGES fields resolved for this page (the bands assemble in a
        // post-pass once the total is known). Page 1 takes the first-page footer when present (often empty —
        // Word shows no footer on a title page); an even page its even variant when the document opts in.
        // 3-way footer tab alignment is a later slice.
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
                if (element is ParagraphElement paragraph &&
                    paragraph.Runs.Any(_ => _.PageField != PageFieldKind.None))
                {
                    var runs = paragraph.Runs
                        .Select(_ => _.PageField switch
                        {
                            PageFieldKind.Page => _.WithText(pageNumber.ToString(CultureInfo.InvariantCulture)),
                            PageFieldKind.NumberOfPages or PageFieldKind.SectionPages => _.WithText(totalPages.ToString(CultureInfo.InvariantCulture)),
                            _ => _
                        })
                        .ToList();
                    result.Add(
                        new ParagraphElement
                    {
                        Runs = runs,
                        Properties = paragraph.Properties,
                        IsAnchorOnlyMark = paragraph.IsAnchorOnlyMark,
                        IsCollapsedCellMark = paragraph.IsCollapsedCellMark,
                        IsSectionBreakMark = paragraph.IsSectionBreakMark
                    });
                }
                else
                {
                    result.Add(element);
                }
            }

            return result;
        }

        // Resolves a header's behind-text floating images and shapes to absolute page positions. The
        // full-page decorative frames of letter/label templates are anchored here — page/margin/column
        // horizontally, at the header paragraph vertically — and a header-band-top estimate suffices since
        // they span the whole page. Front-text (foreground) header art is a later slice; header/footer
        // text and band tables lay out in LayoutBand.
        static IReadOnlyList<PlacedItem> ResolveBandImages(HeaderFooterContent? band, PageSettings settings, bool isFooter)
        {
            if (band == null)
            {
                return [];
            }

            var items = new List<PlacedItem>();
            var marginLeft = (float) settings.MarginLeft;
            // A paragraph-anchored band float positions from the band's own origin: the header distance for
            // a header, the footer distance up from the page bottom for a footer. Page/margin anchors are
            // absolute and shared by both.
            var bandTop = isFooter
                ? (float) (settings.HeightPoints - settings.FooterDistance)
                : (float) settings.HeaderDistance;

            // A band's behind-text floating art is anchored like a body float but from the band origin —
            // the page edge, the top margin, or the band itself. Both band images and band shapes (e.g.
            // cover-letters/10's charcoal banner and its 10%-alpha accent tint) paint behind the band text
            // and the body.
            (float X, float Y) Position(HorizontalAnchor horizontalAnchor, double horizontalOffset, VerticalAnchor verticalAnchor, double verticalOffset) =>
            (
                horizontalAnchor == HorizontalAnchor.Page ? (float) horizontalOffset : marginLeft + (float) horizontalOffset,
                verticalAnchor switch
                {
                    VerticalAnchor.Page => (float) verticalOffset,
                    VerticalAnchor.Margin => (float) settings.MarginTop + (float) verticalOffset,
                    _ => bandTop + (float) verticalOffset
                });

            foreach (var element in band.Elements)
            {
                // Front-of-text band images are admitted on the same terms as front-of-text band
                // shapes below: a logo anchored in the header is overwhelmingly written behindDoc="0"
                // (Word's default when you drop a picture into a header and set Square/None wrapping),
                // and the behind-only gate dropped it silently — a right-aligned letterhead logo simply
                // never appeared. Painting it with the rest of the band is right for the same reason it
                // is right for shapes: the band paints before the body, and header art sits above the
                // body's top margin, so front/behind ordering against body text almost never arises.
                if (element is FloatingImageElement image &&
                    DecodableImageBytes(image) is { Length: > 0 } data)
                {
                    var (imageX, imageY) = Position(image.HorizontalAnchor, image.HorizontalPositionPoints, image.VerticalAnchor, image.VerticalPositionPoints);
                    items.Add(new PlacedImage(imageX, imageY, (float) image.WidthPoints, (float) image.HeightPoints, data, Recolor: ImageRecolor.For(image.ColorEffect, image.DuotoneColorHex, image.DuotoneLightColorHex), Opacity: image.Opacity));
                }
                else if (element is FloatingShapeElement shape)
                {
                    var (shapeX, shapeY) = Position(shape.HorizontalAnchor, shape.HorizontalPositionPoints, shape.VerticalAnchor, shape.VerticalPositionPoints);
                    // An image-fill shape paints as a plain image (PaintShape skips image fills); a solid,
                    // gradient or outlined shape paints as a PlacedShape — mirroring the body-float shape cases.
                    // Front-of-text band shapes are admitted too: production painted the whole header before
                    // the body, so its front-text shapes also sat under the body content — cards/05's two
                    // 0.5pt fold-guide rules (a full-height vertical and a full-width horizontal, both
                    // front-text page-anchored header shapes) were silently dropped by the behind-only gate.
                    if (shape.ImageData is { Length: > 0 } shapeImage && shape.ImageContentType != "image/svg+xml")
                    {
                        items.Add(new PlacedImage(shapeX, shapeY, (float) shape.WidthPoints, (float) shape.HeightPoints, shapeImage, shape.RotationDegrees, shape.FlipHorizontal, shape.FlipVertical, Opacity: shape.ImageOpacity));
                    }
                    else if (shape.ImageData == null && (shape.Gradient != null || shape.FillColorHex != null || shape.LineColorHex != null))
                    {
                        items.Add(new PlacedShape(shapeX, shapeY, (float) shape.WidthPoints, (float) shape.HeightPoints, shape));
                    }
                }
            }

            return items;
        }

        // Table X within the current column, by w:jc alignment: centred and right collapse the indent into
        // the slack, left applies w:tblInd (non-floating). The slack may be NEGATIVE — a table wider than
        // the column centres on the column's centre and right-aligns to its right edge, overhanging the
        // margins (Word-read on _probe_wide15: a 630pt table centred on a 468pt column spans −9 to 621pt,
        // right-aligned it ends at the column's 540).
        float ComputeTableX(TableElement table, float tableWidth)
        {
            var slack = columnWidth - tableWidth;
            return table.Properties.Alignment switch
            {
                TextAlignment.Center => ColumnLeft + slack / 2,
                TextAlignment.Right => ColumnLeft + slack,
                _ => ColumnLeft + (float) table.Properties.IndentPoints
            };
        }

        // Advances to the next column or page when the height will
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
        // later slice). A line wider than the available width (rare — an unbreakable word) is not shifted,
        // unless <paramref name="allowOverhang"/> — a spreadsheet cell that must not wrap, where the line
        // is MEANT to be wider and the alignment says which way it grows. Word has no such case: its
        // over-long line is an unbreakable word that would look no better hanging off the left margin,
        // whereas Excel's right-aligned label ends at its column and runs backwards over the empty cells
        // beside it (modern-corporate-blue's "Client Company Name", 96pt of text in a 90.75pt column).
        static float AlignmentOffset(TextAlignment alignment, float availableWidth, float lineWidth, bool allowOverhang = false)
        {
            var slack = availableWidth - lineWidth;
            if (slack <= 0 && !allowOverhang)
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

        // Only the FIRST line of a paragraph moves off the block's LeftIndent: w:firstLine shifts it right,
        // w:hanging outdents it left (toward the outer margin). Subsequent lines sit at the LeftIndent. Both
        // indents are zero for a plain paragraph. The measurer re-sizes the first line by the same amount so
        // the wrap and the paint agree. A LIST paragraph is exempt: its hanging indent positions the marker
        // (see MarkerRun) while the text stays at the LeftIndent on every line, so the text must not shift.
        static float FirstLineIndentOffset(ParagraphProperties properties, int lineIndex) =>
            lineIndex == 0 && properties.Numbering is not { Text.Length: > 0 }
                ? (float) (properties.FirstLineIndentPoints - properties.HangingIndentPoints)
                : 0f;

        // The runs to paint for a line: its text run segments, plus — on the first line of a list
        // paragraph — the list marker positioned in the hanging-indent gutter to the left of the text.
        IReadOnlyList<PlacedRun> LineRuns(ParagraphElement paragraph, LaidOutLine line, int lineIndex, float lineLeft)
        {
            if (lineIndex == 0 && paragraph.Properties.Numbering is { Text.Length: > 0 } numbering)
            {
                // A marker that overruns the text indent pushes the FIRST line's text to the
                // next tab stop (see CanonicalParagraphMeasurer.MarkerTextShift, which narrowed
                // the wrap by the same amount); the marker itself stays in the gutter.
                var shift = measurer.MarkerTextShift(paragraph);
                return [MarkerRun(paragraph, numbering, lineLeft), .. MapRuns(line, lineLeft + shift)];
            }

            return MapRuns(line, lineLeft);
        }

        // The list marker as a placed run: its text in the bullet or number font, a hanging indent to the
        // left of the text (or right-aligned just before it when there is no hanging indent). Font, colour
        // and position mirror PdfTextEngine's marker placement; the paragraph's LeftIndent already sets the
        // text edge, so the marker offsets back from there.
        PlacedRun MarkerRun(ParagraphElement paragraph, NumberingInfo numbering, float lineLeft)
        {
            var markerProperties = CanonicalParagraphMeasurer.MarkerProperties(paragraph, numbering);
            var markerWidth = measurer.MeasureRunWidth(numbering.Text, markerProperties);
            var hanging = (float) paragraph.Properties.HangingIndentPoints;

            // w:lvlJc="right" anchors the marker's RIGHT edge at the number position, the numeral
            // growing leftward into the margin so the periods of I./VIII./XVIII. line up —
            // _probe_numtab's right edges landed within 3px of the position at two geometries.
            var markerX = hanging > 0.01f
                ? numbering.MarkerRightAligned
                    ? lineLeft - hanging - markerWidth
                    : lineLeft - hanging
                : lineLeft - markerWidth - 3f;

            return new(markerX, markerWidth, numbering.Text, markerProperties);
        }

        // Projects a laid-out line's run segments to placed runs at absolute X (the line's left edge plus
        // each segment's canonical offset). Shared by page-flow and cell-flow line placement.
        static PlacedRun[] MapRuns(LaidOutLine line, float lineLeft)
        {
            var runs = new PlacedRun[line.Runs.Count];
            for (var runIndex = 0; runIndex < line.Runs.Count; runIndex++)
            {
                var run = line.Runs[runIndex];
                runs[runIndex] = new(lineLeft + run.X, run.Width, run.Text, run.Properties, run.Leader, run.BaselineShift);
            }

            return runs;
        }

        // Projects a laid-out line's inline boxes (images and shape groups) to placed images: the line's
        // left edge plus each box's offset, with its bottom sitting on the text baseline. A shape-group box
        // carries its group through and leaves Data null.
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
                images[imageIndex] = new(lineLeft + image.X, baseline - image.Height, image.Width, image.Height, image.Data, image.RotationDegrees, image.FlipHorizontal, image.FlipVertical, Crop: image.Crop, ShapeGroup: image.ShapeGroup, Recolor: image.Recolor, Opacity: image.Opacity);
            }

            return images;
        }
    }
}
