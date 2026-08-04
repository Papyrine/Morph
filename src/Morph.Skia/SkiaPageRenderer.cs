/// <summary>
/// Renders document pages to PNG images.
/// </summary>
sealed class SkiaPageRenderer(SkiaRenderContext context) :
    PageRendererBase(context),
    IDisposable
{
    protected override IParagraphMeasurer Measurer => textRenderer;
    protected override bool HasOutput => currentCanvas != null;

    TextRenderer textRenderer = new(context);

    Action<Action<Stream>>? pageCallback;
    int pageCount;

    /// <summary>
    /// When true, pages are laid out and counted but not encoded to PNG or handed to the callback.
    /// Used by the gated counting pass that resolves NUMPAGES before the real render.
    /// </summary>
    public bool CountOnly { get; init; }

    SKBitmap? pendingPage;
    SKBitmap? currentPage;
    SKCanvas? currentCanvas;
    IReadOnlyList<Watermark> watermarks = [];

    // Track whether meaningful content (text/images/tables) was rendered on current page
    // Used to detect and discard spurious blank trailing pages
    bool hasSignificantContentOnCurrentPage;

    // Track whether the current page was started due to an explicit break
    // (page break, section break) - such pages should not be discarded even if blank
    bool currentPageFromExplicitBreak;

    // Track whether the current page was started by a section break (a new page setup):
    // Word keeps a paragraph's spacing-before at the top of such pages in every mode.
    bool currentPageFromSectionBreak;

    // Pages started so far (incremented in StartNewPage; pageCount only counts flushed pages,
    // which lags behind while a finished page is still pending).
    int pagesStarted;

    /// <summary>
    /// Renders a parsed document, calling the callback for each page.
    /// Returns the total page count.
    /// </summary>
    public int RenderDocument(ParsedDocument document, Action<Action<Stream>> callback)
    {
        pageCallback = callback;

        header = document.Header;
        footer = document.Footer;
        firstPageHeader = document.FirstPageHeader;
        firstPageFooter = document.FirstPageFooter;
        evenPageHeader = document.EvenPageHeader;
        watermarks = document.Watermarks;
        evenPageFooter = document.EvenPageFooter;
        differentFirstPage = document.PageSettings.DifferentFirstPage;

        // Measure header and footer heights
        // Header and footer space are both resolved per page from the header/footer actually
        // active there (see RenderHeader / RenderFooter), so nothing is reserved up front.
        context.SetHeaderFooterSpace(0, 0);

        // Initialize line numbering
        context.InitializeLineNumbers();

        StartNewPage();

        var elements = document.Elements;
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];

            // Render anchored shapes on the current page only (not on every page).
            // Word anchors a drawing to the same page as its anchor paragraph.
            // If the next significant content won't fit on the current page (a page break is
            // imminent), the shape should render on the next page — otherwise the page
            // ends up with two stacked backgrounds and the actual target page has none.
            // Front-of-text shapes take the same advance: when their anchor paragraph's
            // content breaks to the next page the shape must follow it (resumes/10's accent
            // circle is anchored to a continuous-section paragraph that overflows).
            if (element is FloatingShapeElement shape)
            {
                AdvanceToBackgroundsTargetPage(elements, i);
                RenderBackgroundShape(shape);
                continue;
            }

            // Behind-text floating images carry the same page-anchor semantics as background
            // shapes: when their parent paragraph contains no flow text and the next significant
            // element forces a page break, the image belongs on the next page (it's the
            // background for the *upcoming* content, not the current one).
            if (element is FloatingImageElement {BehindText: true} bgImage)
            {
                AdvanceToBackgroundsTargetPage(elements, i);
                RenderFloatingImage(bgImage);
                continue;
            }

            // Get next non-background element for KeepWithNext handling
            DocumentElement? nextElement = null;
            for (var j = i + 1; j < elements.Count; j++)
            {
                if (elements[j] is FloatingShapeElement {BehindText: true} ||
                    elements[j] is FloatingImageElement {BehindText: true})
                {
                    continue;
                }
                nextElement = elements[j];
                break;
            }

            RenderElement(element, nextElement);
        }

        // Append footnotes and endnotes as a document-end section. Word draws footnotes at the
        // bottom of the page where the reference appears; that needs page-level reservation in
        // the layout pass (not currently wired). Until then we list them at document end so the
        // content isn't lost.
        RenderNotesAppendix(document);

        // Finish the last page
        FinishCurrentPage();

        // Remove any trailing blank page that was created by content overflow or section breaks.
        RemoveBlankTrailingPage();

        return pageCount;
    }

    protected override float MeasureHeaderFooterHeight(HeaderFooterContent content)
    {
        var total = 0f;
        foreach (var element in content.Elements)
        {
            total += MeasureElementHeight(element);
        }

        return total;
    }

    void RenderNotesAppendix(ParsedDocument document)
    {
        foreach (var paragraph in NotesAppendix.BuildElements(document))
        {
            RenderParagraph(paragraph);
        }
    }

    protected override void RenderHeaderFooterParagraph(ParagraphElement paragraph)
    {
        if (currentCanvas != null)
        {
            textRenderer.RenderParagraph(currentCanvas, paragraph);
        }
    }

    void RenderElement(DocumentElement element, DocumentElement? nextElement = null)
    {
        switch (element)
        {
            case PageBreakElement:
                FinishCurrentPage();
                StartNewPage();
                currentPageFromExplicitBreak = true;
                break;

            case ColumnBreakElement:
                // Move to next column, or new page if no more columns
                if (!context.MoveToNextColumn())
                {
                    FinishCurrentPage();
                    StartNewPage();
                    currentPageFromExplicitBreak = true;
                }

                break;

            case SectionBreakElement sectionBreak:
                RenderSectionBreak(sectionBreak);
                break;

            case ParagraphElement paragraph:
                RenderParagraph(paragraph, nextElement);
                break;

            case HorizontalRuleElement:
                RenderHorizontalRule();
                hasSignificantContentOnCurrentPage = true;
                break;

            case ImageElement image:
                RenderImage(image);
                hasSignificantContentOnCurrentPage = true;
                break;

            case FloatingImageElement floatingImage:
                // Render floating images immediately at their absolute positions
                // They don't affect text flow (no CurrentY advancement)
                RenderFloatingImage(floatingImage);
                hasSignificantContentOnCurrentPage = true;
                break;

            case FloatingTextBoxElement floatingTextBox:
                // Render floating text boxes at their absolute positions
                // They don't affect text flow (no CurrentY advancement)
                RenderFloatingTextBox(floatingTextBox);
                hasSignificantContentOnCurrentPage = true;
                break;

            case PositionedFrameElement positionedFrame:
                // Word text frame (w:framePr): positioned out of flow, no CurrentY advancement.
                RenderPositionedFrame(positionedFrame);
                hasSignificantContentOnCurrentPage = true;
                break;

            case TableElement table:
                RenderTable(table);
                hasSignificantContentOnCurrentPage = true;
                // Reset spacing tracking after table - tables don't participate in margin collapsing
                // so the next paragraph should get its full SpacingBefore
                context.LastParagraphSpacingAfterPoints = 0;
                context.LastParagraphHadContextualSpacing = false;
                context.LastParagraphStyleId = null;
                break;

            case WordArtElement wordArt:
                RenderWordArt(wordArt);
                hasSignificantContentOnCurrentPage = true;
                break;

            case FloatingWordArtElement floatingWordArt:
                // Render floating WordArt at absolute position
                // Doesn't affect text flow (no CurrentY advancement)
                RenderFloatingWordArt(floatingWordArt);
                hasSignificantContentOnCurrentPage = true;
                break;

            case InkElement ink:
                RenderInk(ink);
                hasSignificantContentOnCurrentPage = true;
                break;

            case TextFormFieldElement textField:
                RenderTextFormField(textField);
                hasSignificantContentOnCurrentPage = true;
                break;

            case CheckBoxFormFieldElement checkBox:
                RenderCheckBoxFormField(checkBox);
                hasSignificantContentOnCurrentPage = true;
                break;

            case DropDownFormFieldElement dropDown:
                RenderDropDownFormField(dropDown);
                hasSignificantContentOnCurrentPage = true;
                break;

            case ContentControlElement contentControl:
                RenderContentControl(contentControl);
                hasSignificantContentOnCurrentPage = true;
                break;

            case FloatingShapeElement floatingShape:
                // Behind-text shapes are handled in the RenderDocument pre-scan and rendered
                // at page start in StartNewPage. FRONT-of-text shapes have no other painter
                // and draw here over the content painted so far (newsletters/08's cover photo
                // is a front-anchored blip-filled freeform; resumes/10's accent circle a
                // front-anchored solid custGeom).
                RenderBackgroundShape(floatingShape);
                break;
        }
    }

    void RenderSectionBreak(SectionBreakElement sectionBreak) =>
        SectionBreakHandler.Handle(
            sectionBreak,
            context,
            FinishCurrentPage,
            StartNewExplicitPage,
            () => !hasSignificantContentOnCurrentPage,
            DiscardCurrentPage);

    void DiscardCurrentPage()
    {
        currentCanvas?.Dispose();
        currentCanvas = null;
        currentPage?.Dispose();
        currentPage = null;
    }

    void StartNewExplicitPage()
    {
        StartNewPage();
        currentPageFromExplicitBreak = true;
        currentPageFromSectionBreak = true;
    }

    // Word does not apply a body paragraph's spacing-before at the top of a page reached by an
    // automatic break; compatibilityMode 15 also drops it after explicit page breaks, while a
    // section break (a new page setup) and the document's first page keep it. Column tops are
    // left unchanged (Word drops there too on automatic flow, but Morph's column handling is
    // measured separately). See page_counts.md, pass 4.
    bool ShouldSuppressPageTopSpacingBefore()
    {
        if (pagesStarted <= 1 ||
            context.CurrentColumn != 0 ||
            context.CurrentY > context.ContentTop + 0.01f)
        {
            return false;
        }

        if (currentPageFromSectionBreak)
        {
            return false;
        }

        if (currentPageFromExplicitBreak)
        {
            return context.Compatibility.CompatibilityMode >= 15;
        }

        return true;
    }

    /// <summary>
    /// Measures the height of an element for pagination purposes.
    /// </summary>
    float MeasureElementHeight(DocumentElement element) =>
        element switch
        {
            ParagraphElement para => textRenderer.MeasureParagraphHeight(para),
            ImageElement img => (float) img.HeightPoints,
            TableElement table => MeasureTableHeight(table),
            _ => 0 // Other elements don't participate in KeepWithNext
        };

    protected override void RenderParagraph(ParagraphElement paragraph, DocumentElement? nextElement = null)
    {
        // Substitute live page numbers for any PAGE/NUMPAGES/SECTIONPAGES field before measuring,
        // so wrapping and drawing both use the resolved text.
        paragraph = ResolveParagraphPageFields(paragraph);

        // Check if this paragraph has significant content (actual text)
        var hasSignificantContent = paragraph.Runs.Any(_ => !string.IsNullOrWhiteSpace(_.Text));

        // Check if paragraph is completely empty (no runs at all)
        var isCompletelyEmpty = paragraph.Runs.Count == 0;

        // Handle PageBreakBefore - force a page break before this paragraph
        // But only if we're not already at the top of a page (to avoid blank pages)
        if (paragraph.Properties.PageBreakBefore &&
            !isCompletelyEmpty &&
            context.CurrentY > context.ContentTop)
        {
            FinishCurrentPage();
            StartNewPage();
            currentPageFromExplicitBreak = true;
        }

        // Float wrap: a paragraph starting beside a wrap-enabled floating image is measured and
        // rendered inside the widest free band next to it (Word additionally reflows back to
        // full width below the float mid-paragraph; here the band holds for the paragraph's
        // whole height). A wrapTopAndBottom float advances Y below itself instead.
        var (bandX, bandWidth, bandY, bandConstrained) = context.ResolveFlowBand(context.CurrentY);
        if (bandY > context.CurrentY)
        {
            context.CurrentY = bandY;
        }

        using var floatScope = bandConstrained ? context.PushContentContainer(bandX, bandWidth) : null;

        var height = textRenderer.MeasureParagraphHeight(paragraph);

        // Handle KeepWithNext (KeepNext) - keep this paragraph on the same page as the next element
        // This is commonly used for headings to prevent them from appearing alone at the bottom of a page
        if (paragraph.Properties.KeepNext &&
            nextElement != null &&
            !isCompletelyEmpty)
        {
            var nextHeight = MeasureElementHeight(nextElement);
            var combinedHeight = height + nextHeight;

            // If combined height won't fit in the current column, but both would fit in a fresh
            // one, advance to the next column (or page) before rendering this paragraph.
            if (!context.HasSpaceFor(combinedHeight) &&
                combinedHeight <= context.ContentHeight &&
                context.CurrentY > context.ContentTop)
            {
                AdvanceToNextColumnOrPage();
            }
        }

        // Handle KeepLines - keep all lines of this paragraph on the same page
        // If the paragraph doesn't fit on current page but would fit on a new page, move it
        if (paragraph.Properties.KeepLines &&
            !isCompletelyEmpty)
        {
            if (!context.HasSpaceFor(height) &&
                height <= context.ContentHeight &&
                context.CurrentY > context.ContentTop)
            {
                AdvanceToNextColumnOrPage();
            }
        }

        // Handle WidowControl - prevent orphans (1 line stranded at bottom) and widows (1 line at top).
        // We can't currently break a paragraph in the middle, so the only enforceable case is the
        // orphan: when ≥1 line fits at the bottom of the current page but the rest would create a
        // single-line orphan there. Push the whole paragraph forward instead.
        if (paragraph.Properties.WidowControl &&
            !isCompletelyEmpty &&
            !context.HasSpaceFor(height) &&
            height <= context.ContentHeight &&
            context.CurrentY > context.ContentTop)
        {
            var lineHeights = textRenderer.LayoutParagraphForMeasurement(paragraph, context.ContentWidth);
            if (lineHeights.Count >= 3)
            {
                var spaceLeft = context.ContentBottom - context.CurrentY - (float) paragraph.Properties.SpacingBeforePoints;
                var fit = 0;
                var running = 0f;
                foreach (var lh in lineHeights)
                {
                    if (running + lh > spaceLeft)
                    {
                        break;
                    }

                    running += lh;
                    fit++;
                }

                // Orphan: 1 line fits at the bottom, ≥2 lines wrap to next page → push whole paragraph.
                // Widow: lines split such that only 1 wraps to next page → also push whole paragraph.
                if (fit == 1 || fit == lineHeights.Count - 1)
                {
                    AdvanceToNextColumnOrPage();
                }
            }
        }

        // For completely empty paragraphs (no runs), don't force page breaks
        // as these often appear at document end and cause spurious extra pages.
        // Paragraphs with whitespace-only runs are considered intentional spacing
        // and should still trigger page breaks normally to maintain layout.
        if (!isCompletelyEmpty)
        {
            EnsureSpaceFor(height);
        }

        // Render the paragraph
        if (currentCanvas != null)
        {
            context.SuppressPageTopSpacingBefore = ShouldSuppressPageTopSpacingBefore();
            textRenderer.RenderParagraph(currentCanvas, paragraph, nextElement);
        }

        // Track significant content for blank page removal
        if (hasSignificantContent)
        {
            hasSignificantContentOnCurrentPage = true;
        }
    }

    protected override void DrawHorizontalRuleLine(float pixelX1, float pixelY, float pixelX2, string hexColor, float pixelStrokeWidth)
    {
        if (currentCanvas == null)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SkiaRenderContext.ParseColor(hexColor),
            StrokeWidth = pixelStrokeWidth,
            IsAntialias = true
        };
        currentCanvas.DrawLine(pixelX1, pixelY, pixelX2, pixelY, paint);
    }

    protected override void DrawBlockImage(byte[] imageData, string? contentType, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, bool flipHorizontal, bool flipVertical, ImageCrop? crop, BlipColorEffect colorEffect, string? duotoneColorHex, string? duotoneLightColorHex)
    {
        var destRect = new SKRect(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);
        DrawBlockImage(imageData, contentType, destRect, rotation, crop, colorEffect, flipHorizontal, flipVertical, duotoneColorHex, duotoneLightColorHex);
    }

    void DrawBlockImage(byte[] imageData, string? contentType, SKRect destRect, float rotation, ImageCrop? crop, BlipColorEffect colorEffect = BlipColorEffect.None, bool flipHorizontal = false, bool flipVertical = false, string? duotoneColorHex = null, string? duotoneLightColorHex = null)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var transformed = rotation != 0 || flipHorizontal || flipVertical;
        if (transformed)
        {
            currentCanvas.Save();
            if (rotation != 0)
            {
                currentCanvas.RotateDegrees(rotation, destRect.MidX, destRect.MidY);
            }

            if (flipHorizontal || flipVertical)
            {
                // a:xfrm/@flipH/@flipV: mirror around the image centre inside the rotated frame.
                currentCanvas.Scale(flipHorizontal ? -1 : 1, flipVertical ? -1 : 1, destRect.MidX, destRect.MidY);
            }
        }

        if (contentType == "image/svg+xml")
        {
            RenderSvgImage(imageData, destRect, crop);
        }
        else
        {
            var skImage = context.GetBitmap(imageData);
            if (skImage != null)
            {
                using var paint = BuildBlipColorEffectPaint(colorEffect, duotoneColorHex, duotoneLightColorHex);
                if (crop is {IsCropped: true})
                {
                    if (crop.HasPadding)
                    {
                        // Padding (negative srcRect): the image occupies Expand's sub-rectangle
                        // inside the frame — a source rect can't extend beyond the bitmap.
                        var (paddedX, paddedY, paddedWidth, paddedHeight) = crop.Expand(destRect.Left, destRect.Top, destRect.Width, destRect.Height);
                        currentCanvas.Save();
                        currentCanvas.ClipRect(destRect);
                        currentCanvas.DrawBitmap(skImage, new SKRect((float) paddedX, (float) paddedY, (float) (paddedX + paddedWidth), (float) (paddedY + paddedHeight)), paint);
                        currentCanvas.Restore();
                    }
                    else
                    {
                        var srcLeft = (float) (crop.Left * skImage.Width);
                        var srcTop = (float) (crop.Top * skImage.Height);
                        var srcRight = (float) ((1 - crop.Right) * skImage.Width);
                        var srcBottom = (float) ((1 - crop.Bottom) * skImage.Height);
                        currentCanvas.DrawBitmap(skImage, new(srcLeft, srcTop, srcRight, srcBottom), destRect, paint);
                    }
                }
                else
                {
                    currentCanvas.DrawBitmap(skImage, destRect, paint);
                }
            }
        }

        if (transformed)
        {
            currentCanvas.Restore();
        }
    }

    public static SKPaint? BuildBlipColorEffectPaint(BlipColorEffect effect, string? duotoneColorHex = null, string? duotoneLightColorHex = null)
    {
        // Standard ITU-R BT.601 luminance weights for grayscale conversion.
        const float lumR = 0.299f;
        const float lumG = 0.587f;
        const float lumB = 0.114f;

        // a:duotone maps luminance onto a dark→light ramp: out_c = dark_c + L·(light_c − dark_c).
        // As a colour matrix that is the luminance row scaled by (light_c − dark_c) plus a
        // dark_c bias (SkiaSharp's translate column is 0..1-normalized). Word's Recolor gallery
        // pairs a dark colour with white; letters/02 pairs black with a tinted accent instead.
        if (effect == BlipColorEffect.Duotone && (duotoneColorHex != null || duotoneLightColorHex != null))
        {
            var dark = SkiaRenderContext.ParseColor(duotoneColorHex ?? "000000");
            var light = SkiaRenderContext.ParseColor(duotoneLightColorHex ?? "FFFFFF");
            var darkRed = dark.Red / 255f;
            var darkGreen = dark.Green / 255f;
            var darkBlue = dark.Blue / 255f;
            var spanRed = light.Red / 255f - darkRed;
            var spanGreen = light.Green / 255f - darkGreen;
            var spanBlue = light.Blue / 255f - darkBlue;
            return new()
            {
                ColorFilter = SKColorFilter.CreateColorMatrix(
                [
                    lumR * spanRed, lumG * spanRed, lumB * spanRed, 0, darkRed,
                    lumR * spanGreen, lumG * spanGreen, lumB * spanGreen, 0, darkGreen,
                    lumR * spanBlue, lumG * spanBlue, lumB * spanBlue, 0, darkBlue,
                    0, 0, 0, 1, 0
                ])
            };
        }

        return effect switch
        {
            BlipColorEffect.Grayscale or BlipColorEffect.Duotone =>
                new()
                {
                    ColorFilter = SKColorFilter.CreateColorMatrix(
                    [
                        lumR, lumG, lumB, 0, 0,
                        lumR, lumG, lumB, 0, 0,
                        lumR, lumG, lumB, 0, 0,
                        0, 0, 0, 1, 0
                    ])
                },
            BlipColorEffect.Washout =>
                new()
                {
                    // Match Word's "Washout" picture preset: brightness +70%, contrast −50% — i.e.
                    // gain 0.50 + half-scale bias on each channel, producing a faded version of the
                    // image. The matrix translate column is 0..1-normalized in SkiaSharp, so the
                    // bias is 0.5 (a 255-scaled value saturates every channel to white).
                    ColorFilter = SKColorFilter.CreateColorMatrix(
                    [
                        0.5f, 0, 0, 0, 0.5f,
                        0, 0.5f, 0, 0, 0.5f,
                        0, 0, 0.5f, 0, 0.5f,
                        0, 0, 0, 1, 0
                    ])
                },
            _ => null
        };
    }

    void RenderSvgImage(byte[] svgData, SKRect destRect, ImageCrop? crop = null)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Preprocessing, parsing and rasterization are cached on the context — an SVG header
        // logo repeats on every page with a stable destination size. The raster draws to a
        // bitmap first because DrawPicture is unreliable on some canvases.
        var bitmap = context.GetSvgRaster(svgData, destRect.Width, destRect.Height, crop, originAdjusted: true);
        if (bitmap != null)
        {
            currentCanvas.DrawBitmap(bitmap, destRect.Left, destRect.Top);
        }
    }


    protected override void RenderWordArtInCell(WordArtElement wordArt) => RenderWordArt(wordArt, reserveSpace: false);

    void RenderWordArt(WordArtElement wordArt, bool reserveSpace = true)
    {
        var height = (float) wordArt.HeightPoints;
        if (reserveSpace)
        {
            EnsureSpaceFor(height);
        }

        if (currentCanvas == null)
        {
            return;
        }

        var x = context.PointsToPixels(context.ContentLeft) +
                SkiaWordArtDrawer.AlignWordArtOffset(
                    wordArt,
                    context.PointsToPixels(context.ContentWidth),
                    context.PointsToPixels((float) wordArt.WidthPoints));
        var y = context.PointsToPixels(context.CurrentY);
        var width = context.PointsToPixels((float) wordArt.WidthPoints);
        var pixelHeight = context.PointsToPixels(height);

        new SkiaWordArtDrawer(context, currentCanvas).DrawInline(wordArt, x, y, width, pixelHeight);

        context.CurrentY += height;
    }

    protected override void RenderFloatingWordArt(FloatingWordArtElement wordArt)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Calculate absolute position based on anchor type
        var bounds = FloatingPosition.ResolveBounds(
            context,
            wordArt.HorizontalAnchor,
            wordArt.VerticalAnchor,
            wordArt.HorizontalPositionPoints,
            wordArt.VerticalPositionPoints,
            wordArt.WidthPoints,
            wordArt.HeightPoints,
            wordArt.HorizontalPositionPercent,
            wordArt.VerticalPositionPercent);

        new SkiaWordArtDrawer(context, currentCanvas).DrawFloating(wordArt, bounds.PixelX, bounds.PixelY, bounds.PixelWidth, bounds.PixelHeight);
        // Note: No CurrentY advancement for floating elements
    }

    void RenderInk(InkElement ink)
    {
        var height = (float) ink.HeightPoints;
        EnsureSpaceFor(height);

        if (currentCanvas == null)
        {
            return;
        }

        var baseX = context.PointsToPixels(context.ContentLeft);
        var baseY = context.PointsToPixels(context.CurrentY);

        foreach (var stroke in ink.Strokes)
        {
            if (stroke.Points.Count < 2)
            {
                continue;
            }

            // Create paint for this stroke
            var color = ParseColor(stroke.ColorHex);

            // Apply transparency
            if (stroke.Transparency > 0 || stroke.IsHighlighter)
            {
                var alpha = stroke.IsHighlighter
                    ? (byte) 128 // Highlighter is semi-transparent
                    : (byte) (255 - stroke.Transparency);
                color = color.WithAlpha(alpha);
            }

            using var paint = new SKPaint
            {
                Color = color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels((float) stroke.WidthPoints),
                IsAntialias = true,
                StrokeCap = stroke.PenTip == InkPenTip.Rectangle ? SKStrokeCap.Square : SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };

            // Highlighters use blend mode to simulate marker effect
            if (stroke.IsHighlighter)
            {
                paint.BlendMode = SKBlendMode.Multiply;
            }

            // Build path from points
            using var path = new SKPath();
            var firstPoint = stroke.Points[0];
            path.MoveTo(
                baseX + context.PointsToPixels((float) firstPoint.X),
                baseY + context.PointsToPixels((float) firstPoint.Y));

            for (var i = 1; i < stroke.Points.Count; i++)
            {
                var point = stroke.Points[i];
                path.LineTo(
                    baseX + context.PointsToPixels((float) point.X),
                    baseY + context.PointsToPixels((float) point.Y));
            }

            currentCanvas.DrawPath(path, paint);
        }

        context.CurrentY += height;
    }

    /// <summary>
    /// Measures the total height of a table for pagination purposes. Shares the memoized
    /// layout with <see cref="PageRendererBase.RenderTable"/>, so the follow-up render
    /// doesn't recompute it.
    /// </summary>
    float MeasureTableHeight(TableElement table)
    {
        if (table.Rows.Count == 0)
        {
            return 0;
        }

        return GetTableLayout(table).RowHeights.Sum();
    }


    protected override void RenderVerticalCellContent(TableCell cell, float cellX, float cellY, float cellWidth, float cellHeight, CellSpacing padding)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var contentX = cellX + (float) padding.Left;
        var contentY = cellY + (float) padding.Top;
        var contentWidth = cellWidth - (float) padding.Horizontal;
        var availableHeight = cellHeight - (float) padding.Vertical;

        // BottomToTop: rotate -90 around bottom-left of the content rect.
        // TopToBottom: rotate +90 around top-right of the content rect.
        var bottomToTop = cell.Properties.TextDirection == CellTextDirection.BottomToTop;
        var pivotXpx = context.PointsToPixels(bottomToTop ? contentX : contentX + contentWidth);
        var pivotYpx = context.PointsToPixels(bottomToTop ? contentY + availableHeight : contentY);

        currentCanvas.Save();
        currentCanvas.Translate(pivotXpx, pivotYpx);
        currentCanvas.RotateDegrees(bottomToTop ? -90 : 90);

        var savedY = context.CurrentY;
        context.CurrentY = 0;

        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                RenderParagraphInBounds(para, 0, availableHeight);
            }
            else if (element is ContentControlElement contentControl)
            {
                RenderContentControlInCell(contentControl, 0, availableHeight);
            }
        }

        context.CurrentY = savedY;
        currentCanvas.Restore();
    }


    protected override void RenderParagraphInBounds(ParagraphElement paragraph, float x, float maxWidth)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Render paragraph within specific bounds (for tables and text boxes)
        textRenderer.RenderParagraphInBounds(currentCanvas, paragraph, x, maxWidth);
    }

    protected override void RenderImageInCell(ImageElement image, float x, float maxWidth)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var imageWidth = (float) image.WidthPoints;
        var imageHeight = (float) image.HeightPoints;

        // Scale image to fit within cell width if needed
        if (imageWidth > maxWidth)
        {
            var scale = maxWidth / imageWidth;
            imageWidth = maxWidth;
            imageHeight *= scale;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(context.CurrentY);
        var pixelWidth = context.PointsToPixels(imageWidth);
        var pixelHeight = context.PointsToPixels(imageHeight);

        var destRect = new SKRect(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);

        if (image.ContentType == "image/svg+xml")
        {
            RenderSvgImage(image.ImageData, destRect);
        }
        else
        {
            var skImage = context.GetBitmap(image.ImageData);
            if (skImage != null)
            {
                currentCanvas.DrawBitmap(skImage, destRect);
            }
        }

        context.CurrentY += imageHeight;
    }

    Dictionary<string, SKColor> colorCache = [];

    SKColor ParseColor(string hexColor)
    {
        if (string.IsNullOrEmpty(hexColor) || hexColor == "auto")
        {
            return SKColors.Black;
        }

        if (colorCache.TryGetValue(hexColor, out var cached))
        {
            return cached;
        }

        var color = ParseColorImpl(hexColor);
        colorCache[hexColor] = color;
        return color;
    }

    static SKColor ParseColorImpl(string hexColor)
    {
        if (hexColor.Length == 6 &&
            uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var rgb))
        {
            return new(
                (byte) ((rgb >> 16) & 0xFF),
                (byte) ((rgb >> 8) & 0xFF),
                (byte) (rgb & 0xFF)
            );
        }

        if (hexColor.Length == 8 &&
            uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var argb))
        {
            return new(
                (byte) ((argb >> 16) & 0xFF),
                (byte) ((argb >> 8) & 0xFF),
                (byte) (argb & 0xFF),
                (byte) ((argb >> 24) & 0xFF)
            );
        }

        return SKColors.Black;
    }

    protected override void DrawCellBackground(float pixelX, float pixelY, float pixelWidth, float pixelHeight, string hexColor)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var bgPaint = context.GetReusableFillPaint(ParseColor(hexColor), antialias: false);
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);
    }

    protected override void DrawCellBorders(float pixelX, float pixelY, float pixelWidth, float pixelHeight, CellBorders borders)
    {
        if (currentCanvas == null)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        if (borders.Top.IsVisible)
        {
            DrawBorderLine(paint, borders.Top, pixelX, pixelY, pixelX + pixelWidth, pixelY, true);
        }

        if (borders.Right.IsVisible)
        {
            DrawBorderLine(paint, borders.Right, pixelX + pixelWidth, pixelY, pixelX + pixelWidth, pixelY + pixelHeight, false);
        }

        if (borders.Bottom.IsVisible)
        {
            DrawBorderLine(paint, borders.Bottom, pixelX, pixelY + pixelHeight, pixelX + pixelWidth, pixelY + pixelHeight, true);
        }

        if (borders.Left.IsVisible)
        {
            DrawBorderLine(paint, borders.Left, pixelX, pixelY, pixelX, pixelY + pixelHeight, false);
        }
    }

    void DrawBorderLine(SKPaint paint, BorderEdge edge, float x1, float y1, float x2, float y2, bool horizontal)
    {
        ConfigureBorderPaint(paint, edge);
        if (edge.Style == BorderLineStyle.Double)
        {
            // OOXML w:val="double": render as two parallel lines whose total span (line +
            // gap + line) matches the declared width. Each line gets ~1/3 of the width.
            var totalWidth = paint.StrokeWidth;
            var lineWidth = Math.Max(0.5f, totalWidth / 3f);
            var offset = totalWidth / 2f - lineWidth / 2f;
            paint.StrokeWidth = lineWidth;
            if (horizontal)
            {
                currentCanvas!.DrawLine(x1, y1 - offset, x2, y2 - offset, paint);
                currentCanvas.DrawLine(x1, y1 + offset, x2, y2 + offset, paint);
            }
            else
            {
                currentCanvas!.DrawLine(x1 - offset, y1, x2 - offset, y2, paint);
                currentCanvas.DrawLine(x1 + offset, y1, x2 + offset, y2, paint);
            }
        }
        else
        {
            currentCanvas!.DrawLine(x1, y1, x2, y2, paint);
        }
    }

    protected override void DrawCellDiagonals(float pixelX, float pixelY, float pixelWidth, float pixelHeight, CellDiagonals diagonals)
    {
        if (currentCanvas == null)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        if (diagonals.Down.IsVisible)
        {
            ConfigureBorderPaint(paint, diagonals.Down);
            currentCanvas.DrawLine(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight, paint);
        }

        if (diagonals.Up.IsVisible)
        {
            ConfigureBorderPaint(paint, diagonals.Up);
            currentCanvas.DrawLine(pixelX + pixelWidth, pixelY, pixelX, pixelY + pixelHeight, paint);
        }
    }

    SKPaint CreateBorderPaint(BorderEdge edge) =>
        new()
        {
            Color = ParseColor(edge.ColorHex ?? "000000"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = context.PointsToPixels((float) edge.WidthPoints),
            IsAntialias = true
        };

    void ConfigureBorderPaint(SKPaint paint, BorderEdge edge)
    {
        paint.Color = ParseColor(edge.ColorHex ?? "000000");
        paint.StrokeWidth = context.PointsToPixels((float) edge.WidthPoints);
    }

    // === Form-field / content-control draw primitives (called from PageRendererBase) ===

    protected override void DrawFormFieldRect(float pixelX, float pixelY, float pixelWidth, float pixelHeight,
        string fillHex, string borderHex, float pixelBorderWidth)
    {
        if (currentCanvas == null)
        {
            return;
        }

        using var bgPaint = new SKPaint
        {
            Color = ParseColor(fillHex),
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);

        using var borderPaint = new SKPaint
        {
            Color = ParseColor(borderHex),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = pixelBorderWidth,
            IsAntialias = true
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, borderPaint);
    }

    protected override void DrawFormFieldText(string text, float pixelX, float pixelY, float pixelWidth, float pixelHeight, string textHex)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var font = context.CreateFontFromTypeface(context.GetTypeface(DefaultFontSettings.DefaultFont, false, false), 10);
        using var textPaint = new SKPaint
        {
            Color = ParseColor(textHex),
            IsAntialias = true
        };

        // Skia DrawText takes baseline Y; position it 4 pixels above the rect's bottom edge so
        // the glyph caps sit a couple of pixels below the top of the rect.
        var textX = pixelX + 3 * context.Scale;
        var textY = pixelY + pixelHeight - 4 * context.Scale;
        currentCanvas.DrawText(text, textX, textY, SKTextAlign.Left, font, textPaint);
    }

    protected override void DrawCheckMark(float pixelX, float pixelY, float pixelSize, string hexColor, float pixelStrokeWidth, bool xShape)
    {
        if (currentCanvas == null)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Color = ParseColor(hexColor),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = pixelStrokeWidth,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };

        if (xShape)
        {
            // X glyph (used by content-control checkboxes). Wider padding (0.25) than the ✓ form
            // since the X stretches to all four corners.
            var pad = pixelSize * 0.25f;
            var left = pixelX + pad;
            var right = pixelX + pixelSize - pad;
            var top = pixelY + pad;
            var bottom = pixelY + pixelSize - pad;
            currentCanvas.DrawLine(left, top, right, bottom, paint);
            currentCanvas.DrawLine(right, top, left, bottom, paint);
            return;
        }

        // ✓ glyph (used by form-field checkboxes). Two strokes meeting at 40% from the left edge,
        // sitting on the bottom — geometrically what users expect.
        var checkPad = pixelSize * 0.2f;
        var checkLeft = pixelX + checkPad;
        var checkRight = pixelX + pixelSize - checkPad;
        var checkTop = pixelY + checkPad;
        var checkBottom = pixelY + pixelSize - checkPad;
        var midX = pixelX + pixelSize * 0.4f;
        currentCanvas.DrawLine(checkLeft, checkTop + (checkBottom - checkTop) * 0.5f, midX, checkBottom, paint);
        currentCanvas.DrawLine(midX, checkBottom, checkRight, checkTop, paint);
    }

    protected override void DrawDropDownArrow(float pixelX, float pixelY, float pixelHeight, string hexColor)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // pixelX is the right edge of the field; back off 12 scaled pixels to leave a margin
        // and centre the arrow vertically.
        var arrowSize = pixelHeight * 0.3f;
        var arrowX = pixelX - 12 * context.Scale;
        var arrowY = pixelY + pixelHeight / 2;

        using var arrowPaint = new SKPaint
        {
            Color = ParseColor(hexColor),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX, arrowY - arrowSize / 2);
        arrowPath.LineTo(arrowX + arrowSize, arrowY - arrowSize / 2);
        arrowPath.LineTo(arrowX + arrowSize / 2, arrowY + arrowSize / 2);
        arrowPath.Close();
        currentCanvas.DrawPath(arrowPath, arrowPaint);
    }

    void FlushPendingPage()
    {
        if (pendingPage != null)
        {
            using var page = pendingPage;
            pendingPage = null;
            pageCount++;

            // Counting pass: the page was laid out only to advance the count; skip the encode.
            if (CountOnly)
            {
                return;
            }

            // Encode straight from the bitmap's pixels — SKImage.FromBitmap would snapshot a
            // full copy of the page (~33 MB at Letter/300 DPI) only to feed the same encoder.
            using var pixmap = page.PeekPixels();
            using var data = pixmap.Encode(SKEncodedImageFormat.Png, 100)!;
            pageCallback!(data.SaveTo);
        }
    }

    protected override void StartNewPage()
    {
        FlushPendingPage();

        currentPage = new(
            context.PageWidthPixels,
            context.PageHeightPixels,
            SKColorType.Rgba8888,
            SKAlphaType.Premul
        );

        currentCanvas = new(currentPage);

        // Clear to transparency (WordArt rasterizer), the background color if specified, else white.
        var bgColor = context.PageSettings.BackgroundColorHex;
        if (context.TransparentBackground)
        {
            currentCanvas.Clear(SKColors.Transparent);
        }
        else if (string.IsNullOrEmpty(bgColor))
        {
            currentCanvas.Clear(SKColors.White);
        }
        else
        {
            currentCanvas.Clear(ParseColor(bgColor));
        }

        // Watermarks live behind everything: drawn after the page background clear so they
        // don't show through it, but before page borders/header/body so those land on top.
        DrawWatermarks();

        DrawPageBorders();

        if (pageCount > 0)
        {
            context.StartNewPage();
            // Reset line numbers for new page (if restart mode is NewPage)
            context.ResetLineNumbersForPage();
        }

        // Render header on new page
        RenderHeader();

        // Reset tracking for the new page
        hasSignificantContentOnCurrentPage = false;
        currentPageFromExplicitBreak = false;
        currentPageFromSectionBreak = false;
        pagesStarted++;
    }

    protected override void FinishCurrentPage()
    {
        if (currentPage == null)
        {
            return;
        }

        // Render footer before finishing
        RenderFooter();

        pendingPage = currentPage;
        currentCanvas?.Dispose();
        currentCanvas = null;
        currentPage = null;
    }

    void DrawWatermarks()
    {
        if (currentCanvas == null || watermarks.Count == 0)
        {
            return;
        }

        foreach (var watermark in watermarks)
        {
            // Picture watermarks are intentionally not drawn: Word's own page exports leave no
            // visible trace of them (verified against the Word-generated references — standard
            // washout picture watermarks render as nothing, even over colored page backgrounds),
            // so drawing any wash diverges from the reference output. Text watermarks do render
            // in Word and keep drawing.
            if (watermark.ImageData == null && !string.IsNullOrEmpty(watermark.Text))
            {
                DrawTextWatermark(watermark);
            }
        }
    }

    void DrawTextWatermark(Watermark watermark)
    {
        var typeface = context.GetTypeface(watermark.FontFamily, watermark.Bold, italic: false);
        var fontSize = (float) watermark.FontSizePoints * context.Scale;
        var font = context.CreateFontFromTypeface(typeface, fontSize);
        using var paint = new SKPaint
        {
            Color = ParseColor(watermark.ColorHex),
            IsAntialias = true
        };

        var textWidth = font.MeasureText(watermark.Text);

        currentCanvas!.Save();
        currentCanvas.Translate(context.PageWidthPixels / 2f, context.PageHeightPixels / 2f);
        currentCanvas.RotateDegrees((float) watermark.RotationDegrees);
        currentCanvas.DrawText(watermark.Text, -textWidth / 2f, fontSize / 3f, SKTextAlign.Left, font, paint);
        currentCanvas.Restore();
    }

    void DrawPageBorders()
    {
        if (currentCanvas == null ||
            context.PageSettings.PageBorders is not {HasAnyBorder: true} borders)
        {
            return;
        }

        var pageWidth = context.PageWidthPixels;
        var pageHeight = context.PageHeightPixels;
        var leftX = context.PointsToPixels((float) borders.LeftSpacePoints);
        var rightX = pageWidth - context.PointsToPixels((float) borders.RightSpacePoints);
        var topY = context.PointsToPixels((float) borders.TopSpacePoints);
        var bottomY = pageHeight - context.PointsToPixels((float) borders.BottomSpacePoints);

        static SKPaint CreatePaint(BorderEdge edge, float strokeWidth) => new()
        {
            Color = SKColor.Parse("#" + (edge.ColorHex ?? "000000")),
            StrokeWidth = strokeWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        if (borders.Top.IsVisible)
        {
            using var paint = CreatePaint(borders.Top, context.PointsToPixels((float) borders.Top.WidthPoints));
            currentCanvas.DrawLine(leftX, topY, rightX, topY, paint);
        }

        if (borders.Bottom.IsVisible)
        {
            using var paint = CreatePaint(borders.Bottom, context.PointsToPixels((float) borders.Bottom.WidthPoints));
            currentCanvas.DrawLine(leftX, bottomY, rightX, bottomY, paint);
        }

        if (borders.Left.IsVisible)
        {
            using var paint = CreatePaint(borders.Left, context.PointsToPixels((float) borders.Left.WidthPoints));
            currentCanvas.DrawLine(leftX, topY, leftX, bottomY, paint);
        }

        if (borders.Right.IsVisible)
        {
            using var paint = CreatePaint(borders.Right, context.PointsToPixels((float) borders.Right.WidthPoints));
            currentCanvas.DrawLine(rightX, topY, rightX, bottomY, paint);
        }
    }

    void RemoveBlankTrailingPage()
    {
        if (pageCount > 0 &&
            !hasSignificantContentOnCurrentPage &&
            !currentPageFromExplicitBreak)
        {
            pendingPage?.Dispose();
            pendingPage = null;
        }
        else
        {
            FlushPendingPage();
        }
    }

    protected override void RenderBackgroundShape(FloatingShapeElement shape)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var (width, height) = FloatingPosition.ResolveEffectiveSize(
            context,
            shape.WidthPoints,
            shape.HeightPoints,
            shape.WidthPercent,
            shape.WidthRelativeFrom,
            shape.HeightPercent,
            shape.HeightRelativeFrom);

        // Calculate position based on anchor type
        var bounds = FloatingPosition.ResolveShapeBounds(
            context,
            shape.HorizontalAnchor,
            shape.VerticalAnchor,
            shape.HorizontalPositionPoints,
            shape.VerticalPositionPoints,
            width,
            height,
            shape.HorizontalPositionPercent,
            shape.VerticalPositionPercent);
        var pixelX = bounds.PixelX;
        var pixelY = bounds.PixelY;
        var pixelWidth = bounds.PixelWidth;
        var pixelHeight = bounds.PixelHeight;

        // Check for image fill first
        if (shape.ImageData != null)
        {
            var image = context.GetImage(shape.ImageData);
            if (image != null)
            {
                var destRect = new SKRect(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);

                // Word shows the picture through the shape's geometry (a circular profile
                // photo is an ellipse with a blip fill), not as a bare rectangle. Rotated
                // geometry is handled inside BuildPolygonPath; the ellipse clip is page-space
                // like the pic:spPr crop, so rotated ellipses stay unclipped.
                var clipGeometry = shape.Subpaths != null ||
                                   (shape.Preset == PresetShape.Ellipse && shape.RotationDegrees == 0);
                if (clipGeometry)
                {
                    currentCanvas.Save();
                    if (shape.Subpaths != null)
                    {
                        using var clipPath = BuildPolygonPath(shape, pixelX, pixelY, pixelWidth, pixelHeight);
                        currentCanvas.ClipPath(clipPath, antialias: true);
                    }
                    else
                    {
                        using var clipPath = new SKPath();
                        clipPath.AddOval(destRect);
                        currentCanvas.ClipPath(clipPath, antialias: true);
                    }
                }

                using var paint = new SKPaint
                {
                    IsAntialias = true
                };
                currentCanvas.DrawImage(image, destRect, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);

                if (clipGeometry)
                {
                    currentCanvas.Restore();
                }
            }
        }
        else if (shape.Gradient is { } gradient)
        {
            // Linear gradient: angle 0° = horizontal-X-axis pointing right, clockwise positive
            // (matches OOXML a:lin/@ang). Convert to start/end points along the bounding box.
            var rad = gradient.DirectionDegrees * Math.PI / 180.0;
            var dx = (float) Math.Cos(rad);
            var dy = (float) Math.Sin(rad);
            var cx = pixelX + pixelWidth / 2;
            var cy = pixelY + pixelHeight / 2;
            var halfDiag = (float) Math.Sqrt(pixelWidth * pixelWidth + pixelHeight * pixelHeight) / 2;

            var startPt = new SKPoint(cx - dx * halfDiag, cy - dy * halfDiag);
            var endPt = new SKPoint(cx + dx * halfDiag, cy + dy * halfDiag);

            using var shader = SKShader.CreateLinearGradient(
                startPt, endPt,
                [SKColor.Parse(gradient.StartColorHex), SKColor.Parse(gradient.EndColorHex)],
                SKShaderTileMode.Clamp);
            using var paint = new SKPaint
            {
                Shader = shader,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            FillShape(shape, pixelX, pixelY, pixelWidth, pixelHeight, paint);
        }
        else if (shape.FillColorHex != null)
        {
            // Solid fill
            var color = SKColor.Parse(shape.FillColorHex)
                .WithAlpha((byte) Math.Round(Math.Clamp(shape.FillAlpha, 0, 1) * 255));
            using var paint = new SKPaint
            {
                Color = color,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            FillShape(shape, pixelX, pixelY, pixelWidth, pixelHeight, paint);
        }

        if (shape is { LineColorHex: { } lineColor, LineWidthPoints: { } lineWidthPt and > 0 })
        {
            var strokeWidthPixels = context.PointsToPixels((float) lineWidthPt);
            using var strokePaint = new SKPaint
            {
                Color = SKColor.Parse(lineColor)
                    .WithAlpha((byte) Math.Round(Math.Clamp(shape.LineAlpha, 0, 1) * 255)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokeWidthPixels,
                IsAntialias = true
            };
            if (shape.LineDashPattern is { } dashPattern)
            {
                // Pattern lengths are multiples of the line width; Skia wants pixels.
                strokePaint.PathEffect = SKPathEffect.CreateDash(
                    dashPattern.Select(_ => (float) _ * strokeWidthPixels).ToArray(), 0);
            }
            if (shape.Subpaths != null)
            {
                using var path = BuildPolygonPath(shape, pixelX, pixelY, pixelWidth, pixelHeight);
                currentCanvas.DrawPath(path, strokePaint);
            }
            else if (shape.RotationDegrees != 0)
            {
                // Rotated preset outline: turn about the box centre (matches FillShape).
                currentCanvas.Save();
                currentCanvas.RotateDegrees((float) shape.RotationDegrees, pixelX + pixelWidth / 2, pixelY + pixelHeight / 2);
                if (shape.Preset == PresetShape.Ellipse)
                {
                    currentCanvas.DrawOval(pixelX + pixelWidth / 2, pixelY + pixelHeight / 2, pixelWidth / 2, pixelHeight / 2, strokePaint);
                }
                else
                {
                    currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, strokePaint);
                }

                currentCanvas.Restore();
            }
            else if (shape.Preset == PresetShape.Ellipse)
            {
                currentCanvas.DrawOval(
                    pixelX + pixelWidth / 2,
                    pixelY + pixelHeight / 2,
                    pixelWidth / 2,
                    pixelHeight / 2,
                    strokePaint);
            }
            else
            {
                // PageWidthPixels truncates `pageWidth * Scale` to int, leaving the canvas one
                // sub-pixel narrower than a full-page shape rect. Without clamping, a centered
                // stroke at the right/bottom edge sits ~0.99px outside the canvas and disappears.
                // Clamp the stroke path to the canvas so the centred line lands on the last visible
                // column / row.
                var strokeLeft = Math.Max(0, pixelX);
                var strokeTop = Math.Max(0, pixelY);
                var strokeRight = Math.Min(context.PageWidthPixels, pixelX + pixelWidth);
                var strokeBottom = Math.Min(context.PageHeightPixels, pixelY + pixelHeight);
                currentCanvas.DrawRect(
                    new(strokeLeft, strokeTop, strokeRight, strokeBottom),
                    strokePaint);
            }
        }
    }

    void FillShape(FloatingShapeElement shape, float x, float y, float width, float height, SKPaint paint)
    {
        if (shape.Subpaths != null)
        {
            using var path = BuildPolygonPath(shape, x, y, width, height);
            currentCanvas!.DrawPath(path, paint);
            return;
        }

        // Preset rects/ellipses rotate about their box centre like any other xfrm
        // (business-plans/08's accent rule is a 90°-rotated thin rect).
        var rotated = shape.RotationDegrees != 0;
        if (rotated)
        {
            currentCanvas!.Save();
            currentCanvas.RotateDegrees((float) shape.RotationDegrees, x + width / 2, y + height / 2);
        }

        if (shape.Preset == PresetShape.Ellipse)
        {
            currentCanvas!.DrawOval(x + width / 2, y + height / 2, width / 2, height / 2, paint);
        }
        else
        {
            currentCanvas!.DrawRect(x, y, width, height, paint);
        }

        if (rotated)
        {
            currentCanvas!.Restore();
        }
    }

    // Internal so SkiaPainter (the layout-engine raster path) can reuse the same custGeom subpath geometry.
    internal static SKPath BuildPolygonPath(FloatingShapeElement shape, float x, float y, float width, float height)
    {
        var path = new SKPath();
        // Each sub-path is its own closed contour. SKPath's default Winding (nonzero) fill type
        // matches DrawingML's default custGeom fill, so oppositely-wound nested contours read as
        // holes instead of being fused into one polygon by connector lines.
        foreach (var contour in shape.Subpaths!)
        {
            for (var i = 0; i < contour.Count; i++)
            {
                var (px, py) = contour[i];
                // Apply flips around the unit-square center, then scale into the bounding box.
                var ux = shape.FlipHorizontal ? 1 - px : px;
                var uy = shape.FlipVertical ? 1 - py : py;
                var localX = (float) (ux * width);
                var localY = (float) (uy * height);
                if (i == 0)
                {
                    path.MoveTo(localX, localY);
                }
                else
                {
                    path.LineTo(localX, localY);
                }
            }
            path.Close();
        }

        // Translate so (0,0) sits at the bbox top-left, then rotate around the bbox center.
        var matrix = SKMatrix.CreateTranslation(x, y);
        if (shape.RotationDegrees != 0)
        {
            matrix = SKMatrix.Concat(
                matrix,
                SKMatrix.CreateRotationDegrees((float) shape.RotationDegrees, width / 2, height / 2));
        }
        path.Transform(matrix);
        return path;
    }

    protected override void RenderFloatingImage(FloatingImageElement image)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var (width, height) = FloatingPosition.ResolveEffectiveSize(
            context,
            image.WidthPoints,
            image.HeightPoints,
            image.WidthPercent,
            image.WidthRelativeFrom,
            image.HeightPercent,
            image.HeightRelativeFrom);

        // Calculate absolute position based on anchor type
        var bounds = FloatingPosition.ResolveBounds(
            context,
            image.HorizontalAnchor,
            image.VerticalAnchor,
            image.HorizontalPositionPoints,
            image.VerticalPositionPoints,
            width,
            height,
            image.HorizontalPositionPercent,
            image.VerticalPositionPercent);

        // Wrap-enabled floats reserve their footprint so following flow text lays out beside
        // them instead of over them.
        context.RegisterFloatExclusion(image, bounds.X, bounds.Y, (float) width, (float) height);

        var destRect = new SKRect(bounds.PixelX, bounds.PixelY, bounds.PixelX + bounds.PixelWidth, bounds.PixelY + bounds.PixelHeight);

        // pic:spPr geometry crop (round photos, custGeom cuts). The clip sits in page space,
        // so rotated pictures (whose bitmap turns inside DrawBlockImage) are left unclipped.
        var clipGeometry = (image.ClipToEllipse || image.ClipSubpaths != null) &&
                           Math.Abs(image.RotationDegrees) < 0.01;
        if (clipGeometry)
        {
            currentCanvas.Save();
            using var clipPath = new SKPath();
            if (image.ClipToEllipse)
            {
                clipPath.AddOval(destRect);
            }
            else
            {
                foreach (var contour in image.ClipSubpaths!)
                {
                    for (var pointIndex = 0; pointIndex < contour.Count; pointIndex++)
                    {
                        var (unitX, unitY) = contour[pointIndex];
                        var pointX = destRect.Left + (float) unitX * destRect.Width;
                        var pointY = destRect.Top + (float) unitY * destRect.Height;
                        if (pointIndex == 0)
                        {
                            clipPath.MoveTo(pointX, pointY);
                        }
                        else
                        {
                            clipPath.LineTo(pointX, pointY);
                        }
                    }

                    clipPath.Close();
                }
            }

            currentCanvas.ClipPath(clipPath, antialias: true);
        }

        DrawBlockImage(image.ImageData, image.ContentType, destRect, (float) image.RotationDegrees, image.Crop, image.ColorEffect, image.FlipHorizontal, image.FlipVertical, image.DuotoneColorHex, image.DuotoneLightColorHex);

        if (clipGeometry)
        {
            currentCanvas.Restore();
        }
    }

    protected override void RenderFloatingTextBox(FloatingTextBoxElement textBox)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Calculate absolute position
        var bounds = FloatingPosition.ResolveBounds(
            context,
            textBox.HorizontalAnchor,
            textBox.VerticalAnchor,
            textBox.HorizontalPositionPoints,
            textBox.VerticalPositionPoints,
            textBox.WidthPoints,
            textBox.HeightPoints,
            textBox.HorizontalPositionPercent,
            textBox.VerticalPositionPercent);
        var x = bounds.X;
        var y = bounds.Y;
        var pixelX = bounds.PixelX;
        var pixelY = bounds.PixelY;
        var pixelWidth = bounds.PixelWidth;
        var pixelHeight = bounds.PixelHeight;

        // Save canvas state before rotation
        currentCanvas.Save();

        // Apply rotation if specified
        if (Math.Abs(textBox.RotationDegrees) > 0.01)
        {
            // Calculate center point for rotation
            var centerX = pixelX + pixelWidth / 2;
            var centerY = pixelY + pixelHeight / 2;

            // Rotate around center
            currentCanvas.RotateDegrees((float) textBox.RotationDegrees, centerX, centerY);
        }

        // The shape's chrome behind the text: fill and a:ln outline, following the shape's
        // geometry when it is richer than a rectangle (roundRect ticket outlines, plaque frames).
        using var geometryPath = BuildTextBoxPath(textBox, pixelX, pixelY, pixelWidth, pixelHeight);
        if (textBox.BackgroundColorHex != null)
        {
            using var bgPaint = new SKPaint
            {
                Color = ParseColor(textBox.BackgroundColorHex),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            if (geometryPath != null)
            {
                currentCanvas.DrawPath(geometryPath, bgPaint);
            }
            else
            {
                currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);
            }
        }

        if (textBox.LineColorHex != null && textBox.LineWidthPoints > 0)
        {
            using var strokePaint = new SKPaint
            {
                Color = ParseColor(textBox.LineColorHex)
                    .WithAlpha((byte) Math.Round(Math.Clamp(textBox.LineAlpha, 0, 1) * 255)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float) textBox.LineWidthPoints * context.Scale,
                IsAntialias = true
            };
            if (geometryPath != null)
            {
                currentCanvas.DrawPath(geometryPath, strokePaint);
            }
            else
            {
                currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, strokePaint);
            }
        }

        // Render content at the absolute position
        // Save current position and set to text box position
        var savedY = context.CurrentY;

        // Temporarily adjust context for text box rendering
        context.CurrentY = y;

        // Render each content element
        foreach (var element in textBox.Content)
        {
            if (element is ParagraphElement para)
            {
                textRenderer.RenderParagraphInBounds(currentCanvas, para, x, (float) textBox.WidthPoints);
            }
        }

        // Restore context
        context.CurrentY = savedY;

        // Restore canvas state (removes rotation)
        currentCanvas.Restore();
    }

    /// <summary>
    /// The text box's <see cref="FloatingTextBoxElement.Subpaths"/> contours scaled into its box,
    /// or null for plain rectangles. Even-odd fill keeps ring geometry hollow.
    /// </summary>
    static SKPath? BuildTextBoxPath(FloatingTextBoxElement textBox, float x, float y, float width, float height)
    {
        if (textBox.Subpaths == null)
        {
            return null;
        }

        var path = new SKPath {FillType = SKPathFillType.EvenOdd};
        foreach (var contour in textBox.Subpaths)
        {
            if (contour.Count < 3)
            {
                continue;
            }

            for (var index = 0; index < contour.Count; index++)
            {
                var (pointX, pointY) = contour[index];
                var localX = x + (float) pointX * width;
                var localY = y + (float) pointY * height;
                if (index == 0)
                {
                    path.MoveTo(localX, localY);
                }
                else
                {
                    path.LineTo(localX, localY);
                }
            }

            path.Close();
        }

        return path;
    }

    public void Dispose()
    {
        currentCanvas?.Dispose();
        currentPage?.Dispose();
        // Note: Don't dispose _pages here - caller owns them
    }
}
