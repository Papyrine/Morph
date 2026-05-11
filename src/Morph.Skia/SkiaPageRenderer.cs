/// <summary>
/// Renders document pages to PNG images.
/// </summary>
sealed class SkiaPageRenderer(SkiaRenderContext context) :
    PageRendererBase(context),
    IDisposable
{
    protected override IParagraphMeasurer Measurer => textRenderer;
    protected override bool HasOutput => currentCanvas != null;

    /// <summary>
    /// Safely decodes image data, returning null for unsupported formats
    /// instead of throwing when <see cref="SKCodec"/> cannot handle the data.
    /// </summary>
    static SKBitmap? DecodeBitmap(byte[] data)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData);
        return codec != null ? SKBitmap.Decode(codec) : null;
    }

    TextRenderer textRenderer = new(context);

    Action<Action<Stream>>? pageCallback;
    int pageCount;
    SKBitmap? pendingPage;
    SKBitmap? currentPage;
    SKCanvas? currentCanvas;
    float headerHeight;
    IReadOnlyList<Watermark> watermarks = [];

    // Track whether meaningful content (text/images/tables) was rendered on current page
    // Used to detect and discard spurious blank trailing pages
    bool hasSignificantContentOnCurrentPage;

    // Track whether the current page was started due to an explicit break
    // (page break, section break) - such pages should not be discarded even if blank
    bool currentPageFromExplicitBreak;

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
        headerHeight = MeasureHeaderFooterHeight(header);
        footerHeight = MeasureHeaderFooterHeight(footer);

        // Adjust context for header/footer space
        context.SetHeaderFooterSpace(headerHeight, footerHeight);

        // Initialize line numbering
        context.InitializeLineNumbers();

        StartNewPage();

        var elements = document.Elements;
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];

            // Render background shapes on the current page only (not on every page).
            // Word anchors a behind-text drawing to the same page as its anchor paragraph.
            // If the next significant content won't fit on the current page (a page break is
            // imminent), the background should render on the next page — otherwise the page
            // ends up with two stacked backgrounds and the actual target page has none.
            if (element is FloatingShapeElement {BehindText: true} shape)
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

    // ReSharper disable once UnusedParameter.Local
    static float MeasureHeaderFooterHeight(HeaderFooterContent? content) =>
        // For now, return 0 to not adjust body content area based on header/footer.
        // Headers and footers render in their own areas (HeaderDistance/FooterDistance)
        // and shouldn't push body content in typical Word documents.
        // This matches Word's behavior where body content area is determined solely
        // by page margins, not by header/footer content size.
        0;

    void RenderNotesAppendix(ParsedDocument document)
    {
        // Footnote/endnote ids 0 and -1 are Word's "separator" / "continuation separator" stubs —
        // skip them so the appendix only contains user-authored notes.
        var footnotes = document.Footnotes
            .Where(_ => _.Id != "0" && _.Id != "-1" && !string.IsNullOrWhiteSpace(_.Text))
            .ToList();
        var endnotes = document.Endnotes
            .Where(_ => _.Id != "0" && _.Id != "-1" && !string.IsNullOrWhiteSpace(_.Text))
            .ToList();

        if (footnotes.Count == 0 && endnotes.Count == 0)
        {
            return;
        }

        AppendNotesSection("Footnotes", footnotes.Select(_ => (_.Id, _.Text)).ToList());
        AppendNotesSection("Endnotes", endnotes.Select(_ => (_.Id, _.Text)).ToList());
    }

    void AppendNotesSection(string heading, List<(string Id, string Text)> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var headingParagraph = new ParagraphElement
        {
            Runs =
            [
                new()
                {
                    Text = heading,
                    Properties = new()
                    {
                        Bold = true,
                        FontSizePoints = 12
                    }
                }
            ],
            Properties = new()
            {
                SpacingBeforePoints = 12,
                SpacingAfterPoints = 6
            }
        };
        RenderParagraph(headingParagraph);

        foreach (var (id, text) in entries)
        {
            var noteParagraph = new ParagraphElement
            {
                Runs =
                [
                    new()
                    {
                        Text = $"{id}. ",
                        Properties = new()
                        {
                            Bold = true,
                            FontSizePoints = 10
                        }
                    },
                    new()
                    {
                        Text = text,
                        Properties = new()
                        {
                            FontSizePoints = 10
                        }
                    }
                ],
                Properties = new()
                {
                    SpacingAfterPoints = 4
                }
            };
            RenderParagraph(noteParagraph);
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

            case FloatingShapeElement:
                // Background shapes are handled in RenderDocument pre-scan
                // and rendered at page start in StartNewPage
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

        var height = textRenderer.MeasureParagraphHeight(paragraph);

        // Handle KeepWithNext (KeepNext) - keep this paragraph on the same page as the next element
        // This is commonly used for headings to prevent them from appearing alone at the bottom of a page
        if (paragraph.Properties.KeepNext &&
            nextElement != null &&
            !isCompletelyEmpty)
        {
            var nextHeight = MeasureElementHeight(nextElement);
            var combinedHeight = height + nextHeight;

            // If combined height won't fit on current page, but both would fit on a new page,
            // move to new page before rendering this paragraph
            if (!context.HasSpaceFor(combinedHeight) &&
                combinedHeight <= context.ContentHeight &&
                context.CurrentY > context.ContentTop)
            {
                FinishCurrentPage();
                StartNewPage();
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
                FinishCurrentPage();
                StartNewPage();
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
                    FinishCurrentPage();
                    StartNewPage();
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

    protected override void DrawBlockImage(byte[] imageData, string? contentType, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, ImageCrop? crop, BlipColorEffect colorEffect)
    {
        var destRect = new SKRect(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);
        DrawBlockImage(imageData, contentType, destRect, rotation, crop, colorEffect);
    }

    void DrawBlockImage(byte[] imageData, string? contentType, SKRect destRect, float rotation, ImageCrop? crop, BlipColorEffect colorEffect = BlipColorEffect.None)
    {
        if (currentCanvas == null)
        {
            return;
        }

        if (rotation != 0)
        {
            currentCanvas.Save();
            currentCanvas.RotateDegrees(rotation, destRect.MidX, destRect.MidY);
        }

        if (contentType == "image/svg+xml")
        {
            RenderSvgImage(imageData, destRect, crop);
        }
        else
        {
            using var skImage = DecodeBitmap(imageData);
            if (skImage != null)
            {
                using var paint = BuildBlipColorEffectPaint(colorEffect);
                if (crop is {IsCropped: true} c)
                {
                    var srcLeft = (float) (c.Left * skImage.Width);
                    var srcTop = (float) (c.Top * skImage.Height);
                    var srcRight = (float) ((1 - c.Right) * skImage.Width);
                    var srcBottom = (float) ((1 - c.Bottom) * skImage.Height);
                    currentCanvas.DrawBitmap(skImage, new(srcLeft, srcTop, srcRight, srcBottom), destRect, paint);
                }
                else
                {
                    currentCanvas.DrawBitmap(skImage, destRect, paint);
                }
            }
        }

        if (rotation != 0)
        {
            currentCanvas.Restore();
        }
    }

    static SKPaint? BuildBlipColorEffectPaint(BlipColorEffect effect)
    {
        // Standard ITU-R BT.601 luminance weights for grayscale conversion.
        const float lumR = 0.299f;
        const float lumG = 0.587f;
        const float lumB = 0.114f;

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
                    // gain 0.50 + bias 128 on each channel, producing a faded version of the image.
                    ColorFilter = SKColorFilter.CreateColorMatrix(
                    [
                        0.5f, 0, 0, 0, 128,
                        0, 0.5f, 0, 0, 128,
                        0, 0, 0.5f, 0, 128,
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

        var processedData = SvgPreprocessor.StripStyleAndClass(svgData);

        using var svg = new SKSvg();
        using var stream = new MemoryStream(processedData);
        var picture = svg.Load(stream);

        if (picture == null)
        {
            return;
        }

        // Calculate scale to fit the destination rectangle
        var svgBounds = picture.CullRect;
        if (svgBounds is not {Width: > 0, Height: > 0})
        {
            return;
        }

        // a:srcRect crop: stretch only the requested sub-rect of the SVG into destRect.
        // l/t/r/b are fractions of the source extent (Right/Bottom are insets from the edge).
        var srcLeft = svgBounds.Left;
        var srcTop = svgBounds.Top;
        var srcWidth = svgBounds.Width;
        var srcHeight = svgBounds.Height;
        if (crop is {IsCropped: true} c)
        {
            srcLeft = svgBounds.Left + (float) c.Left * svgBounds.Width;
            srcTop = svgBounds.Top + (float) c.Top * svgBounds.Height;
            srcWidth = (float) (1 - c.Left - c.Right) * svgBounds.Width;
            srcHeight = (float) (1 - c.Top - c.Bottom) * svgBounds.Height;
            if (srcWidth <= 0 || srcHeight <= 0)
            {
                return;
            }
        }

        var scaleX = destRect.Width / srcWidth;
        var scaleY = destRect.Height / srcHeight;

        // Render SVG to a bitmap first (more reliable than DrawPicture on some canvases).
        // Translate so the source sub-rect's top-left lands at the bitmap origin, then scale.
        using var bitmap = new SKBitmap((int) destRect.Width, (int) destRect.Height);
        using var tempCanvas = new SKCanvas(bitmap);
        tempCanvas.Clear(SKColors.Transparent);
        tempCanvas.Scale(scaleX, scaleY);
        tempCanvas.Translate(-srcLeft, -srcTop);
        tempCanvas.DrawPicture(picture);

        currentCanvas.DrawBitmap(bitmap, destRect.Left, destRect.Top);
    }

    void RenderWordArt(WordArtElement wordArt)
    {
        var height = (float) wordArt.HeightPoints;
        EnsureSpaceFor(height);

        if (currentCanvas == null)
        {
            return;
        }

        var x = context.PointsToPixels(context.ContentLeft);
        var y = context.PointsToPixels(context.CurrentY);
        var width = context.PointsToPixels((float) wordArt.WidthPoints);
        var pixelHeight = context.PointsToPixels(height);

        // Create font with WordArt properties
        var typeface = SKTypeface.FromFamilyName(
            wordArt.FontFamily,
            wordArt.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            wordArt.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        var pixelFontSize = context.PointsToPixels((float) wordArt.FontSizePoints);

        // Measure text to calculate scale
        using var measurePaint = new SKPaint
        {
            Typeface = typeface,
            TextSize = pixelFontSize,
            IsAntialias = true
        };

        var textBounds = new SKRect();
        measurePaint.MeasureText(wordArt.Text, ref textBounds);

        // Only shrink to fit; never enlarge past the explicit font size. The shape's
        // bounding box for arc/circle warps is sized for the curve, not the glyph cluster.
        var scaleX = textBounds.Width > 0 ? width / textBounds.Width : 1;
        var scaleY = textBounds.Height > 0 ? pixelHeight / textBounds.Height : 1;
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1f);

        if (TryRenderWordArtOnPath(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, wordArt.OutlineColorHex, wordArt.OutlineWidthPoints, x, y, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            context.CurrentY += height;
            return;
        }

        // Calculate centered position
        var scaledWidth = textBounds.Width * scale;
        var scaledHeight = textBounds.Height * scale;
        var textX = x + (width - scaledWidth) / 2;
        var textY = y + (pixelHeight + scaledHeight) / 2;

        currentCanvas.Save();

        // Apply transform based on WordArt type
        ApplyWordArtTransform(wordArt.Transform, x, y, width, pixelHeight);

        // Draw shadow first if enabled
        if (wordArt.HasShadow)
        {
            using var shadowPaint = new SKPaint
            {
                Typeface = typeface,
                TextSize = pixelFontSize * scale,
                IsAntialias = true,
                Color = new(0, 0, 0, 80),
                Style = SKPaintStyle.Fill
            };
            currentCanvas.DrawText(wordArt.Text, textX + 3, textY + 3, shadowPaint);
        }

        // Draw glow if enabled
        if (wordArt.HasGlow)
        {
            using var glowPaint = new SKPaint
            {
                Typeface = typeface,
                TextSize = pixelFontSize * scale,
                IsAntialias = true,
                Color = new(255, 215, 0, 100), // Gold glow
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels(4),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3)
            };
            currentCanvas.DrawText(wordArt.Text, textX, textY, glowPaint);
        }

        // Draw outline if specified
        if (wordArt is {OutlineColorHex: not null, OutlineWidthPoints: > 0})
        {
            using var outlinePaint = new SKPaint
            {
                Typeface = typeface,
                TextSize = pixelFontSize * scale,
                IsAntialias = true,
                Color = ParseColor(wordArt.OutlineColorHex),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels((float) wordArt.OutlineWidthPoints)
            };
            currentCanvas.DrawText(wordArt.Text, textX, textY, outlinePaint);
        }

        // Draw text fill
        using var fillPaint = new SKPaint
        {
            Typeface = typeface,
            TextSize = pixelFontSize * scale,
            IsAntialias = true,
            Color = wordArt.FillColorHex != null ? ParseColor(wordArt.FillColorHex) : SKColors.Black,
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawText(wordArt.Text, textX, textY, fillPaint);

        // Draw reflection if enabled
        if (wordArt.HasReflection)
        {
            currentCanvas.Save();
            currentCanvas.Scale(1, -0.5f, textX, textY + scaledHeight / 2);

            using var reflectionPaint = new SKPaint
            {
                Typeface = typeface,
                TextSize = pixelFontSize * scale,
                IsAntialias = true,
                Color = fillPaint.Color.WithAlpha(60),
                Style = SKPaintStyle.Fill
            };
            currentCanvas.DrawText(wordArt.Text, textX, textY + scaledHeight * 2, reflectionPaint);
            currentCanvas.Restore();
        }

        currentCanvas.Restore();

        context.CurrentY += height;
    }

    void RenderFloatingWordArt(FloatingWordArtElement wordArt)
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
            wordArt.HeightPoints);
        var pixelX = bounds.PixelX;
        var pixelY = bounds.PixelY;
        var width = bounds.PixelWidth;
        var pixelHeight = bounds.PixelHeight;

        // Create font with WordArt properties
        var typeface = SKTypeface.FromFamilyName(
            wordArt.FontFamily,
            wordArt.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            wordArt.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        var pixelFontSize = context.PointsToPixels((float) wordArt.FontSizePoints);

        // Measure text to calculate scale
        using var measurePaint = new SKPaint
        {
            Typeface = typeface,
            TextSize = pixelFontSize,
            IsAntialias = true
        };

        var textBounds = new SKRect();
        measurePaint.MeasureText(wordArt.Text, ref textBounds);

        // Only shrink to fit; never enlarge past the explicit font size — see note above.
        var scaleX = textBounds.Width > 0 ? width / textBounds.Width : 1;
        var scaleY = textBounds.Height > 0 ? pixelHeight / textBounds.Height : 1;
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1f);

        if (TryRenderWordArtOnPath(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, wordArt.OutlineColorHex, wordArt.OutlineWidthPoints, pixelX, pixelY, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            return;
        }

        // Calculate centered position
        var scaledWidth = textBounds.Width * scale;
        var scaledHeight = textBounds.Height * scale;
        var textX = pixelX + (width - scaledWidth) / 2;
        var textY = pixelY + (pixelHeight + scaledHeight) / 2;

        currentCanvas.Save();

        // Apply transform based on WordArt type
        ApplyWordArtTransform(wordArt.Transform, pixelX, pixelY, width, pixelHeight);

        // Draw shadow first if enabled
        if (wordArt.HasShadow)
        {
            using var shadowPaint = new SKPaint
            {
                Typeface = typeface,
                TextSize = pixelFontSize * scale,
                IsAntialias = true,
                Color = new(0, 0, 0, 80),
                Style = SKPaintStyle.Fill
            };
            currentCanvas.DrawText(wordArt.Text, textX + 3, textY + 3, shadowPaint);
        }

        // Draw glow if enabled
        if (wordArt.HasGlow)
        {
            using var glowPaint = new SKPaint
            {
                Typeface = typeface,
                TextSize = pixelFontSize * scale,
                IsAntialias = true,
                Color = new(255, 215, 0, 100), // Gold glow
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels(4),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3)
            };
            currentCanvas.DrawText(wordArt.Text, textX, textY, glowPaint);
        }

        // Draw outline if specified
        if (wordArt is {OutlineColorHex: not null, OutlineWidthPoints: > 0})
        {
            using var outlinePaint = new SKPaint
            {
                Typeface = typeface,
                TextSize = pixelFontSize * scale,
                IsAntialias = true,
                Color = ParseColor(wordArt.OutlineColorHex),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels((float) wordArt.OutlineWidthPoints)
            };
            currentCanvas.DrawText(wordArt.Text, textX, textY, outlinePaint);
        }

        // Draw text fill
        using var fillPaint = new SKPaint
        {
            Typeface = typeface,
            TextSize = pixelFontSize * scale,
            IsAntialias = true,
            Color = wordArt.FillColorHex != null ? ParseColor(wordArt.FillColorHex) : SKColors.Black,
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawText(wordArt.Text, textX, textY, fillPaint);

        // Draw reflection if enabled
        if (wordArt.HasReflection)
        {
            currentCanvas.Save();
            currentCanvas.Scale(1, -0.5f, textX, textY + scaledHeight / 2);

            using var reflectionPaint = new SKPaint
            {
                Typeface = typeface,
                TextSize = pixelFontSize * scale,
                IsAntialias = true,
                Color = fillPaint.Color.WithAlpha(60),
                Style = SKPaintStyle.Fill
            };
            currentCanvas.DrawText(wordArt.Text, textX, textY + scaledHeight * 2, reflectionPaint);
            currentCanvas.Restore();
        }

        currentCanvas.Restore();
        // Note: No CurrentY advancement for floating elements
    }

    /// <summary>
    /// Renders WordArt that follows a curved path (ArchUp/ArchDown/Circle) using
    /// <see cref="SKCanvas.DrawTextOnPath(string, SKPath, SKPoint, SKPaint)"/>. Returns true when the warp was handled,
    /// false for warps that should fall back to flat-text rendering.
    /// </summary>
    bool TryRenderWordArtOnPath(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        string? outlineColorHex,
        double outlineWidthPoints,
        float x, float y, float width, float height,
        SKTypeface typeface, float fontSize)
    {
        if (currentCanvas == null)
        {
            return false;
        }

        SKPath? path;
        SKTextAlign align;
        switch (transform)
        {
            case WordArtTransform.ArchUp:
                // Dome arc: starts at bottom-left, peaks at top-centre, ends at bottom-right.
                // The ellipse is centred at (x+w/2, y+h) with semi-axes (w/2, h), so its top
                // touches y and its sides touch (y+h). Sweep 180° clockwise from 180°.
                path = BuildArc(new(x, y, x + width, y + 2 * height), 180, 180);
                align = SKTextAlign.Center;
                break;
            case WordArtTransform.ArchDown:
                // Valley arc: starts at top-left, dips through bottom-centre, ends at top-right.
                path = BuildArc(new(x, y - height, x + width, y + height), 180, -180);
                align = SKTextAlign.Center;
                break;
            case WordArtTransform.Circle:
                // Full circle starting at the top, clockwise. Left-align so the text begins at
                // the start of the path (top of the circle); Centre would land the text at the
                // midpoint of the circumference, which sits at the bottom upside-down.
                path = BuildArc(new(x, y, x + width, y + height), 270, 360);
                align = SKTextAlign.Left;
                break;
            default:
                return false;
        }

        using (path)
        using (var font = new SKFont(typeface, fontSize)
               {
                   Edging = SKFontEdging.Antialias
               })
        using (var fillPaint = new SKPaint
               {
                   IsAntialias = true,
                   Color = fillColorHex != null ? ParseColor(fillColorHex) : SKColors.Black
               })
        {
            if (outlineColorHex != null &&
                outlineWidthPoints > 0)
            {
                using var outlinePaint = new SKPaint
                {
                    IsAntialias = true,
                    Color = ParseColor(outlineColorHex),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = context.PointsToPixels((float) outlineWidthPoints)
                };
                currentCanvas.DrawTextOnPath(text, path, new(0, 0), align, font, outlinePaint);
            }

            currentCanvas.DrawTextOnPath(text, path, new(0, 0), align, font, fillPaint);
        }

        return true;
    }

    static SKPath BuildArc(SKRect oval, float startAngle, float sweepAngle)
    {
        var path = new SKPath();
        path.AddArc(oval, startAngle, sweepAngle);
        return path;
    }

    void ApplyWordArtTransform(WordArtTransform transform, float x, float y, float width, float height)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var centerX = x + width / 2;
        var centerY = y + height / 2;

        switch (transform)
        {
            case WordArtTransform.ArchUp:
                // Simulate arch up with a slight rotation around center
                currentCanvas.Translate(centerX, centerY);
                currentCanvas.Scale(1, 0.8f);
                currentCanvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.ArchDown:
                // Simulate arch down
                currentCanvas.Translate(centerX, centerY);
                currentCanvas.Scale(1, 0.8f);
                currentCanvas.RotateDegrees(180);
                // Flip back to readable
                currentCanvas.Scale(1, -1);
                currentCanvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.Wave:
                // Simulate wave with slight skew
                currentCanvas.Translate(centerX, centerY);
                currentCanvas.Skew(0.1f, 0);
                currentCanvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.ChevronUp:
                // Simulate chevron up
                currentCanvas.Translate(centerX, centerY);
                currentCanvas.Scale(1, 0.7f);
                currentCanvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.ChevronDown:
                // Simulate chevron down
                currentCanvas.Translate(centerX, y + height);
                currentCanvas.Scale(1, 0.7f);
                currentCanvas.Translate(-centerX, -(y + height));
                break;

            case WordArtTransform.SlantUp:
                // Slant up with rotation
                currentCanvas.RotateDegrees(-10, centerX, centerY);
                break;

            case WordArtTransform.SlantDown:
                // Slant down with rotation
                currentCanvas.RotateDegrees(10, centerX, centerY);
                break;

            case WordArtTransform.Triangle:
                // Triangle shape - scale width at bottom
                currentCanvas.Translate(centerX, centerY);
                currentCanvas.Scale(0.8f, 1);
                currentCanvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.FadeRight:
                // Fade right - slight perspective
                currentCanvas.Translate(x, centerY);
                currentCanvas.Skew(0, 0.05f);
                currentCanvas.Translate(-x, -centerY);
                break;

            case WordArtTransform.FadeLeft:
                // Fade left - slight perspective
                currentCanvas.Translate(x + width, centerY);
                currentCanvas.Skew(0, -0.05f);
                currentCanvas.Translate(-(x + width), -centerY);
                break;

            case WordArtTransform.Circle:
                // Circle - approximate with scaling
                currentCanvas.Translate(centerX, centerY);
                currentCanvas.Scale(0.9f, 0.9f);
                currentCanvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.None:
            default:
                // No transform
                break;
        }
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
    /// Measures the total height of a table for pagination purposes.
    /// </summary>
    float MeasureTableHeight(TableElement table)
    {
        if (table.Rows.Count == 0)
        {
            return 0;
        }

        var colCount = TableLayout.GetColumnCount(table);

        var colWidths = TableLayout.CalculateColumnWidths(table, colCount, context.ContentWidth, textRenderer);
        var rowHeights = TableHeightCalculator.CalculateRowHeights(table, colWidths, textRenderer, TableLayout.HasVerticalMerge(table));

        return rowHeights.Sum();
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
            using var skImage = DecodeBitmap(image.ImageData);
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

        using var bgPaint = new SKPaint
        {
            Color = ParseColor(hexColor),
            Style = SKPaintStyle.Fill
        };
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
            using var image = SKImage.FromBitmap(page);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            pageCallback!(stream => data.SaveTo(stream));
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

        // Clear with background color if specified, otherwise white
        var bgColor = context.PageSettings.BackgroundColorHex;
        if (string.IsNullOrEmpty(bgColor))
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
            if (watermark.ImageData != null)
            {
                DrawPictureWatermark(watermark);
            }
            else if (!string.IsNullOrEmpty(watermark.Text))
            {
                DrawTextWatermark(watermark);
            }
        }
    }

    void DrawPictureWatermark(Watermark watermark)
    {
        using var bitmap = DecodeBitmap(watermark.ImageData!);
        if (bitmap == null)
        {
            return;
        }

        // Word's gain/blacklevel washout: out = in * gain + blackLevel (per channel). The
        // documented formula compresses dynamic range to e.g. 35–65% grey at gain=0.30 / bl=0.35,
        // but Word also alpha-blends the result so the page background bleeds through, which is
        // how it ends up nearly invisible. Without that extra alpha the watermark dominates.
        var gain = (float) watermark.Gain;
        var bias = (float) watermark.BlackLevel * 255f;
        var alpha = (float) Math.Clamp(watermark.Gain, 0.2, 1.0);
        var colorMatrix = new[]
        {
            gain,
            0,
            0,
            0,
            bias,
            0,
            gain,
            0,
            0,
            bias,
            0,
            0,
            gain,
            0,
            bias,
            0,
            0,
            0,
            alpha,
            0
        };

        using var paint = new SKPaint
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High,
            ColorFilter = SKColorFilter.CreateColorMatrix(colorMatrix)
        };

        // Word's emitted style typically says "width:612pt;height:11in" — full letter page,
        // not just the content area. Scale the image uniformly to fit the full page.
        var pageWidth = (float) context.PageWidthPixels;
        var pageHeight = (float) context.PageHeightPixels;
        var imageAspect = (float) bitmap.Width / bitmap.Height;
        var pageAspect = pageWidth / pageHeight;

        float drawW, drawH;
        if (imageAspect > pageAspect)
        {
            drawW = pageWidth;
            drawH = drawW / imageAspect;
        }
        else
        {
            drawH = pageHeight;
            drawW = drawH * imageAspect;
        }

        var x = (pageWidth - drawW) / 2;
        var y = (pageHeight - drawH) / 2;
        var rect = new SKRect(x, y, x + drawW, y + drawH);
        currentCanvas!.DrawBitmap(bitmap, rect, paint);
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
            height);
        var pixelX = bounds.PixelX;
        var pixelY = bounds.PixelY;
        var pixelWidth = bounds.PixelWidth;
        var pixelHeight = bounds.PixelHeight;

        // Check for image fill first
        if (shape.ImageData != null)
        {
            using var bitmap = DecodeBitmap(shape.ImageData);
            if (bitmap != null)
            {
                var destRect = new SKRect(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    FilterQuality = SKFilterQuality.High
                };
                currentCanvas.DrawBitmap(bitmap, destRect, paint);
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
            using var strokePaint = new SKPaint
            {
                Color = SKColor.Parse(lineColor),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels((float) lineWidthPt),
                IsAntialias = true
            };
            if (shape.PolygonPoints != null)
            {
                using var path = BuildPolygonPath(shape, pixelX, pixelY, pixelWidth, pixelHeight);
                currentCanvas.DrawPath(path, strokePaint);
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
        if (shape.PolygonPoints != null)
        {
            using var path = BuildPolygonPath(shape, x, y, width, height);
            currentCanvas!.DrawPath(path, paint);
            return;
        }

        if (shape.Preset == PresetShape.Ellipse)
        {
            currentCanvas!.DrawOval(x + width / 2, y + height / 2, width / 2, height / 2, paint);
        }
        else
        {
            currentCanvas!.DrawRect(x, y, width, height, paint);
        }
    }

    static SKPath BuildPolygonPath(FloatingShapeElement shape, float x, float y, float width, float height)
    {
        var pts = shape.PolygonPoints!;
        var path = new SKPath();
        for (var i = 0; i < pts.Count; i++)
        {
            var (px, py) = pts[i];
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
            height);

        var destRect = new SKRect(bounds.PixelX, bounds.PixelY, bounds.PixelX + bounds.PixelWidth, bounds.PixelY + bounds.PixelHeight);

        DrawBlockImage(image.ImageData, image.ContentType, destRect, (float) image.RotationDegrees, image.Crop);
    }

    void RenderFloatingTextBox(FloatingTextBoxElement textBox)
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
            textBox.HeightPoints);
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

        // Draw background if specified
        if (textBox.BackgroundColorHex != null)
        {
            using var bgPaint = new SKPaint
            {
                Color = ParseColor(textBox.BackgroundColorHex),
                Style = SKPaintStyle.Fill
            };
            currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);
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

    public void Dispose()
    {
        currentCanvas?.Dispose();
        currentPage?.Dispose();
        // Note: Don't dispose _pages here - caller owns them
    }
}
