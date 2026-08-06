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
        float contentTop = HeaderReservedTop(measurer, page, SelectVariant(1, firstPageHeader, evenPageHeader, header, page));
        float contentBottom = (float) (page.HeightPoints - page.MarginBottom);
        float contentHeight = (float) (page.HeightPoints - page.MarginBottom) - HeaderReservedTop(measurer, page, SelectVariant(1, firstPageHeader, evenPageHeader, header, page));
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
        // every new page.
        float columnTop = HeaderReservedTop(measurer, page, SelectVariant(1, firstPageHeader, evenPageHeader, header, page));
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

        // Left edge of the current column, in points from the page's left.
        float ColumnLeft => fullContentLeft + currentColumn * (columnWidth + columnSpacing);

        // Top of a fresh *page* — the first column's region top. Page-level breaks skip when already here.
        bool AtPageTop => atRegionTop && currentColumn == 0;

        // Adopts a new section's page geometry — page size, margins and column layout — recomputing the
        // derived content box and column metrics so subsequent flow uses the new section's dimensions.
        void ApplyGeometry(PageSettings settings)
        {
            current = settings;
            contentTop = HeaderReservedTop(measurer, settings, header);
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
        static float HeaderReservedTop(IParagraphMeasurer measurer, PageSettings settings, HeaderFooterContent? header)
        {
            var marginTop = (float) settings.MarginTop;
            if (settings.TopMarginIsAbsolute || header == null)
            {
                return marginTop;
            }

            var bandWidth = (float) (settings.WidthPoints - settings.MarginLeft - settings.MarginRight);
            var headerHeight = 0f;
            foreach (var element in header.Elements)
            {
                if (element is ParagraphElement paragraph)
                {
                    headerHeight += measurer.MeasureParagraphHeightWithWidth(paragraph, bandWidth);
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

                    // A block-level content control renders as its synthetic paragraph (the parser resolved
                    // its value — checkbox glyph, dropdown selection, formatted date, plain text — into runs).
                    case ContentControlElement control when control.CellParagraph is { } controlParagraph:
                        PlaceParagraph(controlParagraph);
                        break;

                    case TableElement { Properties.IsFloating: true } table:
                        PlaceFloatingTable(table);
                        break;

                    case TableElement table:
                        PlaceTable(table);
                        break;

                    case FloatingImageElement image when DecodableImageBytes(image) is { Length: > 0 }:
                        EmitBodyFloat(image, image.VerticalAnchor, image.AnchorParagraph);
                        break;

                    // An image-filled shape (a full-bleed background photo) paints as a plain image — the shape
                    // painter skips image fills. It carries the shape's rotation and flip; a shape image has no
                    // source crop or clip geometry of its own.
                    case FloatingShapeElement shape when shape.ImageData is { Length: > 0 } && shape.ImageContentType != "image/svg+xml":
                        EmitBodyFloat(shape, shape.VerticalAnchor, shape.AnchorParagraph);
                        break;

                    case FloatingShapeElement shape when shape.ImageData == null && (shape.Gradient != null || shape.FillColorHex != null || shape.LineColorHex != null):
                        EmitBodyFloat(shape, shape.VerticalAnchor, shape.AnchorParagraph);
                        break;

                    case FloatingTextBoxElement textBox when textBox.WrapType == WrapType.None:
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
                        items.Add(new PlacedBorder(ColumnLeft, y + 3, columnWidth, 0, new()
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
            pageCarriedAfter = lastAfter;
            lastAfter = 0;
            currentPageExplicit = nextPageExplicit;
            currentPageSectionStart = false;
            // A float's exclusion belongs to the page it was anchored on; the new page starts clear.
            floatExclusions.Clear();
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

                var pageItems = backgroundImages.Count == 0 && footerImages.Count == 0 && headerBand.Count == 0 && footerBand.Count == 0 && behindFloats.Count == 0 && frontFloats.Count == 0
                    ? body
                    : (IReadOnlyList<PlacedItem>) [.. backgroundImages, .. footerImages, .. headerBand, .. behindFloats, .. body, .. frontFloats, .. footerBand];
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
                    AddBodyFloat(new PlacedImage(imageX, imageY, (float) image.WidthPoints, (float) image.HeightPoints, data, image.RotationDegrees, image.FlipHorizontal, image.FlipVertical, image.ClipToEllipse, image.ClipSubpaths, image.Crop), image.BehindText, IsAbsoluteY(image.VerticalAnchor));
                    RegisterFloatExclusion(image, imageX, imageY, (float) image.WidthPoints, (float) image.HeightPoints);
                    break;
                }

                case FloatingShapeElement shape when shape.ImageData is { Length: > 0 } shapeImage && shape.ImageContentType != "image/svg+xml":
                    AddBodyFloat(new PlacedImage(FloatX(shape.HorizontalAnchor, shape.HorizontalPositionPoints, shape.HorizontalPositionPercent), AnchoredY(shape.VerticalAnchor, shape.VerticalPositionPoints, shape.VerticalPositionPercent), (float) shape.WidthPoints, (float) shape.HeightPoints, shapeImage, shape.RotationDegrees, shape.FlipHorizontal, shape.FlipVertical), shape.BehindText, IsAbsoluteY(shape.VerticalAnchor));
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

            foreach (var item in LayoutCellContent(new() { Content = textBox.Content }, boxX, boxY, boxWidth, boxHeight, CellVerticalAlignment.Top))
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
                Runs = [new Run { Text = text, Properties = properties with { FontSizePoints = fontSizePoints * scale } }],
                Properties = new ParagraphProperties { Alignment = TextAlignment.Center }
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
            return new PlacedShape(boxX, boxY, (float) wordArt.WidthPoints, (float) wordArt.HeightPoints, boxShape);
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

                    var placedLine = new PlacedLine(lineLeft, y, line.Width, line.Height, baseline, paragraph, placedIndex, LineRuns(paragraph, line, placedIndex, lineLeft), MapImages(line, lineLeft, baseline));
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
                ResolvePendingFloats();
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

                // A row taller than a whole empty region cannot be rescued by moving it — it would overflow
                // wherever it went, drawing over the footer and clipping at the page edge. Word splits such a
                // row at a line boundary and continues it overleaf. Rows that DO fit keep the move-whole
                // behaviour below, which matches Word across the rest of the corpus.
                if (rowIndex <= lastVisibleRow && rowHeight > contentHeight && CanSplitRow(table.Rows[rowIndex]))
                {
                    PlaceSplitRow(table, rowIndex, colWidths, rowHeights, colCount, tableX, tableWidth, headerCount);
                    continue;
                }

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
                ResolvePendingFloats();
                y += rowHeight;
                atRegionTop = false;
            }
        }

        // A row may be split across pages when nothing in it ties its cells to one box. A vertical merge
        // does exactly that — the merged cell's height is derived from the rows it spans — so a row carrying
        // one keeps the move-whole behaviour.
        static bool CanSplitRow(TableRow row)
        {
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
        void PlaceSplitRow(TableElement table, int rowIndex, float[] colWidths, float[] rowHeights, int colCount, float tableX, float tableWidth, int headerCount)
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
                if (!atRegionTop && contentBottom - y < minFragmentHeight)
                {
                    AdvanceColumnOrPage();
                }

                if (!isFirstFragment && headerCount > 0 && rowIndex >= headerCount)
                {
                    for (var headerIndex = 0; headerIndex < headerCount; headerIndex++)
                    {
                        items.Add(BuildRow(y, table, headerIndex, colWidths, rowHeights, colCount, tableX, tableWidth, true));
                        y += rowHeights[headerIndex];
                        atRegionTop = false;
                    }
                }

                var available = Math.Max(minFragmentHeight, contentBottom - y);
                var (fragment, continuations, height) = BuildRowFragment(y, table, rowIndex, colWidths, colCount, tableX, tableWidth, starts, available, isFirstFragment);
                items.Add(fragment);
                ResolvePendingFloats();
                y += height;
                atRegionTop = false;

                var finished = true;
                foreach (var continuation in continuations)
                {
                    if (continuation != null)
                    {
                        finished = false;
                        break;
                    }
                }

                if (finished)
                {
                    return;
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
        (PlacedTableRow Row, CellSplitPoint?[] Continuations, float Height) BuildRowFragment(
            float rowY,
            TableElement table,
            int rowIndex,
            float[] colWidths,
            int colCount,
            float tableX,
            float tableWidth,
            CellSplitPoint?[] starts,
            float available,
            bool isFirstFragment)
        {
            var row = table.Rows[rowIndex];
            var cells = new List<PlacedCell>();
            var continuations = new CellSplitPoint?[row.Cells.Count];
            var cellX = tableX;
            var gridColIndex = 0;
            var usedHeight = 0f;
            var anyContinues = false;

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
                var start = starts[cellIndex];
                if (start == null)
                {
                    // This cell finished on an earlier fragment; it still draws its box and sides.
                    cells.Add(new PlacedCell(cellX, rowY, cellWidth, available, cell.Properties.BackgroundColorHex, FragmentBorders(cell, table, rowIndex, gridColIndex, colCount, row, continues: false, isFirstFragment), []));
                    cellX += cellWidth;
                    gridColIndex += span;
                    continue;
                }

                var budget = Math.Max(0f, available - (float) padding.Vertical);
                var (content, continuation, height) = LayoutCellFragment(
                    cell,
                    cellX + (float) padding.Left,
                    rowY + (float) padding.Top,
                    cellWidth - (float) padding.Horizontal,
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

                cells.Add(new PlacedCell(cellX, rowY, cellWidth, available, cell.Properties.BackgroundColorHex, FragmentBorders(cell, table, rowIndex, gridColIndex, colCount, row, continuation != null, isFirstFragment), content));

                cellX += cellWidth;
                gridColIndex += span;
            }

            // A fragment that continues fills its page; the last one shrinks to what it actually used.
            var fragmentHeight = anyContinues ? available : Math.Min(available, usedHeight);
            var boxed = new List<PlacedCell>(cells.Count);
            foreach (var cell in cells)
            {
                boxed.Add(cell with {Height = fragmentHeight});
            }

            return (new PlacedTableRow(tableX, rowY, tableWidth, fragmentHeight, table, rowIndex, false, boxed), continuations, fragmentHeight);
        }

        // The row's resolved borders with the split edges removed: no bottom rule where the cell carries on
        // overleaf, no top rule where it is resuming.
        static CellBorders? FragmentBorders(TableCell cell, TableElement table, int rowIndex, int gridColIndex, int colCount, TableRow row, bool continues, bool isFirstFragment)
        {
            var borders = TableLayout.ResolveCellBorders(cell.Properties, table.Properties, rowIndex, gridColIndex, table.Rows.Count, colCount, row);
            if (borders == null || (!continues && isFirstFragment))
            {
                return borders;
            }

            return borders with
            {
                Top = isFirstFragment ? borders.Top : BorderEdge.None,
                Bottom = continues ? BorderEdge.None : borders.Bottom
            };
        }

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
                if (element is not FloatingShapeElement shape || !shape.BehindText)
                {
                    continue;
                }

                var shapeX = cellX + (float) shape.HorizontalPositionPoints;
                var shapeY = cellY + (float) shape.VerticalPositionPoints;
                if (shape.ImageData is { Length: > 0 } shapeImage && shape.ImageContentType != "image/svg+xml")
                {
                    shapes.Add(new PlacedImage(shapeX, shapeY, (float) shape.WidthPoints, (float) shape.HeightPoints, shapeImage, shape.RotationDegrees, shape.FlipHorizontal, shape.FlipVertical));
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
                    continue;
                }

                var properties = paragraph.Properties;
                // Space-before, collapsed with the previous paragraph's after (max, not sum). Unlike page
                // flow, a cell's FIRST paragraph keeps its space-before — TableHeightCalculator sizes the
                // cell with it, so the content must be positioned with it too or it floats to the top and
                // leaves the gap at the bottom. w:contextualSpacing removes the gap entirely between two
                // same-style contextual paragraphs — production collapses in bounded (cell) rendering too
                // (PdfTextEngine.TrackContextualSpacing "in both flow and bounded"), while the shared
                // TableHeightCalculator measures WITHOUT the collapse; both paths draw tight content in
                // the uncollapsed row, so the placement here must match (cover-letters/09's Address block:
                // five 42pt-after contextual paragraphs rendered 42pt apart instead of tight).
                var cellContextualCollapse = properties.ContextualSpacing && lastCellContextual && properties.StyleId == lastCellStyleId;
                cellY += first
                    ? (float) properties.SpacingBeforePoints
                    : cellContextualCollapse
                        ? 0f
                        : Math.Max(lastCellAfter, (float) properties.SpacingBeforePoints);
                first = false;

                var paragraphLines = measurer.LayoutLineContents(paragraph, contentWidth);
                var isEmpty = paragraphLines.Count == 1 && paragraphLines[0].Width <= 0;
                var textLeft = contentLeft + (float) properties.LeftIndentPoints;
                var availableWidth = contentWidth - (float) properties.LeftIndentPoints - (float) properties.RightIndentPoints;

                var stopped = false;
                for (var lineIndex = resumeLine; lineIndex < paragraphLines.Count; lineIndex++)
                {
                    var line = paragraphLines[lineIndex];
                    // The budget is exhausted mid-paragraph: the rest of it continues on the next page. One
                    // line always goes down, so a fragment can never be empty and loop forever.
                    if (budget != null && lines.Count > 0 && cellY + line.Height > limit)
                    {
                        continuation = new(elementIndex, lineIndex);
                        stopped = true;
                        break;
                    }

                    var firstLineShift = FirstLineIndentOffset(properties, lineIndex);
                    var lineLeft = textLeft + firstLineShift + AlignmentOffset(properties.Alignment, availableWidth - firstLineShift, line.Width);
                    var baseline = cellY + line.Ascent;
                    lines.Add(new PlacedLine(lineLeft, cellY, line.Width, line.Height, baseline, paragraph, lineIndex, LineRuns(paragraph, line, lineIndex, lineLeft), MapImages(line, lineLeft, baseline)));
                    cellY += line.Height;
                }

                if (stopped)
                {
                    break;
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
        static IReadOnlyList<PlacedItem> ResolveBandImages(HeaderFooterContent? band, PageSettings page, bool isFooter)
        {
            if (band == null)
            {
                return [];
            }

            var items = new List<PlacedItem>();
            var marginLeft = (float) page.MarginLeft;
            // A paragraph-anchored band float positions from the band's own origin: the header distance for
            // a header, the footer distance up from the page bottom for a footer. Page/margin anchors are
            // absolute and shared by both.
            var bandTop = isFooter
                ? (float) (page.HeightPoints - page.FooterDistance)
                : (float) page.HeaderDistance;

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
                    VerticalAnchor.Margin => (float) page.MarginTop + (float) verticalOffset,
                    _ => bandTop + (float) verticalOffset
                });

            foreach (var element in band.Elements)
            {
                if (element is FloatingImageElement image && image.BehindText && DecodableImageBytes(image) is { Length: > 0 } data)
                {
                    var (imageX, imageY) = Position(image.HorizontalAnchor, image.HorizontalPositionPoints, image.VerticalAnchor, image.VerticalPositionPoints);
                    items.Add(new PlacedImage(imageX, imageY, (float) image.WidthPoints, (float) image.HeightPoints, data));
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
                        items.Add(new PlacedImage(shapeX, shapeY, (float) shape.WidthPoints, (float) shape.HeightPoints, shapeImage, shape.RotationDegrees, shape.FlipHorizontal, shape.FlipVertical));
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
        // the slack, left applies w:tblInd (non-floating).
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
                images[imageIndex] = new PlacedImage(lineLeft + image.X, baseline - image.Height, image.Width, image.Height, image.Data, image.RotationDegrees, image.FlipHorizontal, image.FlipVertical, Crop: image.Crop, ShapeGroup: image.ShapeGroup);
            }

            return images;
        }
    }
}
