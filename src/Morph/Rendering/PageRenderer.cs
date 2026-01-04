/// <summary>
/// Renders document pages to PNG images.
/// </summary>
sealed class PageRenderer : IDisposable
{
    readonly RenderContext context;
    readonly TextRenderer textRenderer;
    readonly List<SKBitmap> pages = [];

    SKBitmap? currentPage;
    SKCanvas? currentCanvas;
    HeaderFooterContent? header;
    HeaderFooterContent? footer;
    float headerHeight;
    float footerHeight;

    // Track whether meaningful content (text/images/tables) was rendered on current page
    // Used to detect and discard spurious blank trailing pages
    bool hasSignificantContentOnCurrentPage;

    // Track whether the current page was started due to an explicit break
    // (page break, section break) - such pages should not be discarded even if blank
    bool currentPageFromExplicitBreak;

    public PageRenderer(RenderContext context)
    {
        this.context = context;
        textRenderer = new(context);
    }

    /// <summary>
    /// Renders a parsed document to a list of page bitmaps.
    /// </summary>
    public IReadOnlyList<SKBitmap> RenderDocument(ParsedDocument document)
    {
        header = document.Header;
        footer = document.Footer;

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

            // Render background shapes on the current page only (not on every page)
            if (element is FloatingShapeElement {BehindText: true} shape)
            {
                RenderBackgroundShape(shape);
                continue;
            }

            // Get next non-background element for KeepWithNext handling
            DocumentElement? nextElement = null;
            for (var j = i + 1; j < elements.Count; j++)
            {
                if (elements[j] is not FloatingShapeElement {BehindText: true})
                {
                    nextElement = elements[j];
                    break;
                }
            }

            RenderElement(element, nextElement);
        }

        // Finish the last page
        FinishCurrentPage();

        // Remove any trailing blank page that was created by content overflow or section breaks.
        RemoveBlankTrailingPage();

        return pages;
    }

    static float MeasureHeaderFooterHeight(HeaderFooterContent? content) =>
        // For now, return 0 to not adjust body content area based on header/footer.
        // Headers and footers render in their own areas (HeaderDistance/FooterDistance)
        // and shouldn't push body content in typical Word documents.
        // This matches Word's behavior where body content area is determined solely
        // by page margins, not by header/footer content size.
        0;

    void RenderHeader()
    {
        if (header == null || currentCanvas == null)
        {
            return;
        }

        var savedY = context.CurrentY;
        context.CurrentY = (float) context.PageSettings.HeaderDistance;

        foreach (var element in header.Elements)
        {
            if (element is ParagraphElement para)
            {
                textRenderer.RenderParagraph(currentCanvas, para);
            }
        }

        context.CurrentY = savedY;
    }

    void RenderFooter()
    {
        if (footer == null || currentCanvas == null)
        {
            return;
        }

        var savedY = context.CurrentY;
        // Position footer from bottom
        context.CurrentY = (float) (context.PageSettings.HeightPoints - context.PageSettings.FooterDistance - footerHeight);

        foreach (var element in footer.Elements)
        {
            if (element is ParagraphElement para)
            {
                textRenderer.RenderParagraph(currentCanvas, para);
            }
        }

        context.CurrentY = savedY;
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

    void RenderSectionBreak(SectionBreakElement sectionBreak)
    {
        switch (sectionBreak.BreakType)
        {
            case SectionBreakType.NextPage:
                FinishCurrentPage();
                ApplySectionSettings(sectionBreak.NewSectionSettings);
                StartNewPage();
                currentPageFromExplicitBreak = true;
                break;

            case SectionBreakType.Continuous:
                // Continuous break - apply new settings but stay on same page
                ApplySectionSettings(sectionBreak.NewSectionSettings);
                // Reset to first column if column count changed
                context.ResetColumn();
                break;

            case SectionBreakType.EvenPage:
                FinishCurrentPage();
                ApplySectionSettings(sectionBreak.NewSectionSettings);
                StartNewPage();
                currentPageFromExplicitBreak = true;
                // If current page is odd, add another page
                if (context.CurrentPageNumber % 2 != 0)
                {
                    FinishCurrentPage();
                    StartNewPage();
                    currentPageFromExplicitBreak = true;
                }

                break;

            case SectionBreakType.OddPage:
                FinishCurrentPage();
                ApplySectionSettings(sectionBreak.NewSectionSettings);
                StartNewPage();
                currentPageFromExplicitBreak = true;
                // If current page is even, add another page
                if (context.CurrentPageNumber % 2 == 0)
                {
                    FinishCurrentPage();
                    StartNewPage();
                    currentPageFromExplicitBreak = true;
                }

                break;

            case SectionBreakType.NextColumn:
                // Move to next column, or new page if no more columns
                ApplySectionSettings(sectionBreak.NewSectionSettings);
                if (!context.MoveToNextColumn())
                {
                    FinishCurrentPage();
                    StartNewPage();
                    currentPageFromExplicitBreak = true;
                }

                break;
        }
    }

    void ApplySectionSettings(PageSettings? settings)
    {
        if (settings != null)
        {
            context.UpdatePageSettings(settings);
            // Reset line numbers for new section if needed
            context.ResetLineNumbersForSection();
        }
    }

    /// <summary>
    /// Ensures there's space for content. Moves to next column or new page if needed.
    /// </summary>
    void EnsureSpaceFor(float height)
    {
        // If the content is taller than a full page, don't trigger a page break
        // since moving to a new page won't help - just render at current position
        if (height > context.ContentHeight)
        {
            return;
        }

        if (!context.HasSpaceFor(height) && context.CurrentY > context.ContentTop)
        {
            // Try to move to next column first
            if (!context.MoveToNextColumn())
            {
                // No more columns, need a new page
                FinishCurrentPage();
                StartNewPage();
            }
        }
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

    void RenderParagraph(ParagraphElement paragraph, DocumentElement? nextElement = null)
    {
        // Check if this paragraph has significant content (actual text)
        var hasSignificantContent = paragraph.Runs.Any(r => !string.IsNullOrWhiteSpace(r.Text));

        // Check if paragraph is completely empty (no runs at all)
        var isCompletelyEmpty = paragraph.Runs.Count == 0;

        // Handle PageBreakBefore - force a page break before this paragraph
        // But only if we're not already at the top of a page (to avoid blank pages)
        if (paragraph.Properties.PageBreakBefore && !isCompletelyEmpty &&
            context.CurrentY > context.ContentTop)
        {
            FinishCurrentPage();
            StartNewPage();
            currentPageFromExplicitBreak = true;
        }

        var height = textRenderer.MeasureParagraphHeight(paragraph);

        // Handle KeepWithNext (KeepNext) - keep this paragraph on the same page as the next element
        // This is commonly used for headings to prevent them from appearing alone at the bottom of a page
        if (paragraph.Properties.KeepNext && nextElement != null && !isCompletelyEmpty)
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
        if (paragraph.Properties.KeepLines && !isCompletelyEmpty)
        {
            if (!context.HasSpaceFor(height) &&
                height <= context.ContentHeight &&
                context.CurrentY > context.ContentTop)
            {
                FinishCurrentPage();
                StartNewPage();
            }
        }

        // Handle WidowControl - prevent orphans (single line at bottom) and widows (single line at top)
        // Note: WidowControl implementation is complex and can cause regressions with some documents.
        // The property is parsed and stored but full rendering support requires more careful implementation
        // to handle edge cases around page break tolerance and multi-column layouts.
        // TODO: Implement WidowControl rendering without causing regressions

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
            textRenderer.RenderParagraph(currentCanvas, paragraph);
        }

        // Track significant content for blank page removal
        if (hasSignificantContent)
        {
            hasSignificantContentOnCurrentPage = true;
        }
    }

    void RenderImage(ImageElement image)
    {
        var height = (float) image.HeightPoints;
        EnsureSpaceFor(height);

        if (currentCanvas == null)
        {
            return;
        }

        var x = context.PointsToPixels(context.ContentLeft);
        var y = context.PointsToPixels(context.CurrentY);
        var width = context.PointsToPixels((float) image.WidthPoints);
        var pixelHeight = context.PointsToPixels(height);

        var destRect = new SKRect(x, y, x + width, y + pixelHeight);

        // Check if this is an SVG image
        if (image.ContentType == "image/svg+xml")
        {
            RenderSvgImage(image.ImageData, destRect);
        }
        else
        {
            // Regular bitmap image
            using var skImage = SKBitmap.Decode(image.ImageData);
            if (skImage != null)
            {
                currentCanvas.DrawBitmap(skImage, destRect);
            }
        }

        context.CurrentY += height;
    }

    void RenderSvgImage(byte[] svgData, SKRect destRect)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Pre-process SVG to remove class attributes and style elements that Svg.Skia might not handle correctly
        var svgContent = Encoding.UTF8.GetString(svgData);

        // Remove style elements (CSS can interfere with fill processing in Svg.Skia)
        svgContent = Regex.Replace(
            svgContent,
            "<style[^>]*>.*?</style>",
            "",
            RegexOptions.Singleline);

        // Remove class attributes from paths
        svgContent = Regex.Replace(
            svgContent,
            """
            \s+class="[^"]*"
            """,
            "");

        var processedData = Encoding.UTF8.GetBytes(svgContent);

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

        var scaleX = destRect.Width / svgBounds.Width;
        var scaleY = destRect.Height / svgBounds.Height;

        // Render SVG to a bitmap first (more reliable than DrawPicture on some canvases)
        using var bitmap = new SKBitmap((int) destRect.Width, (int) destRect.Height);
        using var tempCanvas = new SKCanvas(bitmap);
        tempCanvas.Clear(SKColors.Transparent);
        tempCanvas.Scale(scaleX, scaleY);
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

        // Calculate scale to fit text within the bounding box
        var scaleX = textBounds.Width > 0 ? width / textBounds.Width : 1;
        var scaleY = textBounds.Height > 0 ? pixelHeight / textBounds.Height : 1;
        var scale = Math.Min(scaleX, scaleY);

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
        var x = CalculateFloatingWordArtX(wordArt);
        var y = CalculateFloatingWordArtY(wordArt);

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var width = context.PointsToPixels((float) wordArt.WidthPoints);
        var pixelHeight = context.PointsToPixels((float) wordArt.HeightPoints);

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

        // Calculate scale to fit text within the bounding box
        var scaleX = textBounds.Width > 0 ? width / textBounds.Width : 1;
        var scaleY = textBounds.Height > 0 ? pixelHeight / textBounds.Height : 1;
        var scale = Math.Min(scaleX, scaleY);

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

    float CalculateFloatingWordArtX(FloatingWordArtElement wordArt)
    {
        var baseX = wordArt.HorizontalAnchor switch
        {
            HorizontalAnchor.Page => 0,
            HorizontalAnchor.Margin => (float) context.PageSettings.MarginLeft,
            HorizontalAnchor.Column => context.ContentLeft,
            HorizontalAnchor.Character => context.ContentLeft, // Approximate
            _ => 0
        };

        return baseX + (float) wordArt.HorizontalPositionPoints;
    }

    float CalculateFloatingWordArtY(FloatingWordArtElement wordArt)
    {
        var baseY = wordArt.VerticalAnchor switch
        {
            VerticalAnchor.Page => 0,
            VerticalAnchor.Margin => (float) context.PageSettings.MarginTop,
            VerticalAnchor.Paragraph => context.CurrentY, // Approximate - relative to current paragraph
            VerticalAnchor.Line => context.CurrentY, // Approximate
            _ => 0
        };

        return baseY + (float) wordArt.VerticalPositionPoints;
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
                currentCanvas.Scale(1, -1); // Flip back to readable
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

        // Calculate column count and widths
        int colCount;
        if (table.Properties.GridColumnWidths?.Count > 0)
        {
            colCount = table.Properties.GridColumnWidths.Count;
        }
        else
        {
            colCount = table.Rows.Max(r => r.Cells.Sum(c => c.Properties.GridSpan));
        }

        var colWidths = CalculateColumnWidths(table, colCount);
        var rowHeights = CalculateRowHeights(table, colWidths);

        return rowHeights.Sum();
    }

    void RenderTable(TableElement table)
    {
        if (currentCanvas == null || table.Rows.Count == 0)
        {
            return;
        }

        // Calculate column widths (accounting for table indent which can expand available width)
        // Use grid column count if available, otherwise calculate from max gridSpan sum per row
        int colCount;
        if (table.Properties.GridColumnWidths?.Count > 0)
        {
            colCount = table.Properties.GridColumnWidths.Count;
        }
        else
        {
            // Calculate max grid columns by summing gridSpans across cells in each row
            colCount = table.Rows.Max(r => r.Cells.Sum(c => c.Properties.GridSpan));
        }

        var colWidths = CalculateColumnWidths(table, colCount);

        // Calculate row heights
        var rowHeights = CalculateRowHeights(table, colWidths);

        var totalHeight = rowHeights.Sum();

        // Check if table is larger than a single page's content area
        // Only use row-by-row rendering for tables that significantly exceed a full page
        // Allow tolerance since table height measurements can be slightly conservative
        // Tables that are close to page height should use simpler single-page rendering
        var tableTolerance = context.ContentHeight * 0.10f; // 10% tolerance
        var needsRowByRowRendering = totalHeight > context.ContentHeight + tableTolerance;

        if (!needsRowByRowRendering)
        {
            // Table fits on a single page - use existing behavior
            // Apply a tolerance when checking if table fits on current page.
            // Word's layout often allows tables to slightly overflow (line height rounding, etc.)
            // Word 2013+ (mode 15) has more consistent table handling, use slightly higher tolerance.
            var tolerancePercent = context.Compatibility.CompatibilityMode >= 15 ? 0.02f : 0.01f;
            var tolerance = context.ContentHeight * tolerancePercent;
            var requiredHeight = totalHeight - tolerance;
            EnsureSpaceFor(requiredHeight);
            RenderTableRows(table, colCount, colWidths, rowHeights);
        }
        else
        {
            // Table is larger than a page - render row by row with page breaks
            RenderTableRowByRow(table, colCount, colWidths, rowHeights);
        }
    }

    /// <summary>
    /// Renders all table rows at the current position (used when table fits on current page).
    /// </summary>
    void RenderTableRows(TableElement table, int colCount, float[] colWidths, float[] rowHeights)
    {
        var tableX = context.ContentLeft;
        var startY = context.CurrentY;

        // Check if table has vertical merges - if so, use column-based Y tracking
        var hasVerticalMerge = table.Rows.Any(r => r.Cells.Any(c =>
            c.Properties.VerticalMerge is VerticalMergeType.Restart or VerticalMergeType.Continue));

        if (hasVerticalMerge)
        {
            // Track Y position per column for proper merged cell layout
            var columnYPositions = new float[colCount];
            for (var i = 0; i < colCount; i++)
            {
                columnYPositions[i] = startY;
            }

            RenderTableWithColumnTracking(table, colCount, colWidths, rowHeights, tableX, columnYPositions);

            // Set final Y to the maximum column Y position
            context.CurrentY = columnYPositions.Max();
        }
        else
        {
            // Standard row-by-row rendering for tables without vertical merges
            var currentY = startY;
            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                RenderTableRow(table, rowIndex, colCount, colWidths, rowHeights, tableX, currentY);
                currentY += rowHeights[rowIndex];
            }

            context.CurrentY = currentY;
        }
    }

    /// <summary>
    /// Renders a table with vertical merges using per-column Y tracking.
    /// </summary>
    void RenderTableWithColumnTracking(TableElement table, int colCount, float[] colWidths, float[] rowHeights, float tableX, float[] columnYPositions)
    {
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var currentX = tableX;
            var gridColIndex = 0;

            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = cell.Properties.GridSpan;

                // Sum column widths for horizontally merged cells
                float cellWidth = 0;
                for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                {
                    cellWidth += colWidths[gridColIndex + i];
                }

                // Skip cells that continue a vertical merge
                if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
                {
                    currentX += cellWidth;
                    gridColIndex += span;
                    continue;
                }

                // Get Y position for this column
                var cellY = columnYPositions[gridColIndex];

                // Calculate cell height
                float cellHeight;
                if (cell.Properties.VerticalMerge == VerticalMergeType.Restart)
                {
                    // For vertically merged cells, use the full merged height so background fills entire area
                    cellHeight = CalculateVerticalMergeHeight(table, rowIndex, gridColIndex, rowHeights);
                }
                else
                {
                    // For regular cells, use content-based height
                    var padding = GetEffectivePadding(cell.Properties, table.Properties);
                    var contentWidth = cellWidth - (float) padding.Horizontal;
                    var contentHeight = MeasureCellHeight(cell, contentWidth, table.Properties);
                    cellHeight = contentHeight + (float) padding.Vertical;
                }

                // Render the cell
                RenderTableCell(cell, currentX, cellY, cellWidth, cellHeight, table.Properties);

                // Update Y position for all columns this cell spans
                for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                {
                    columnYPositions[gridColIndex + i] = cellY + cellHeight;
                }

                currentX += cellWidth;
                gridColIndex += span;
            }
        }
    }

    /// <summary>
    /// Renders table rows one by one, triggering page breaks as needed.
    /// Used for tables larger than a single page.
    /// </summary>
    void RenderTableRowByRow(TableElement table, int colCount, float[] colWidths, float[] rowHeights)
    {
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var rowHeight = rowHeights[rowIndex];

            // Ensure space for this row - may trigger a page break
            EnsureSpaceFor(rowHeight);

            // Get current position after potential page break
            var tableX = context.ContentLeft;
            var currentY = context.CurrentY;

            // Render this row
            RenderTableRow(table, rowIndex, colCount, colWidths, rowHeights, tableX, currentY);

            // Advance Y position
            context.CurrentY += rowHeight;
        }
    }

    /// <summary>
    /// Renders a single table row at the specified position.
    /// </summary>
    void RenderTableRow(TableElement table, int rowIndex, int colCount, float[] colWidths, float[] rowHeights, float tableX, float currentY)
    {
        var row = table.Rows[rowIndex];
        var rowHeight = rowHeights[rowIndex];

        var currentX = tableX;
        var gridColIndex = 0; // Track actual grid column position

        for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
        {
            var cell = row.Cells[cellIndex];
            var span = cell.Properties.GridSpan;

            // Sum column widths for merged cells
            float cellWidth = 0;
            for (var i = 0; i < span && gridColIndex + i < colCount; i++)
            {
                cellWidth += colWidths[gridColIndex + i];
            }

            // Handle vertical merge
            if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
            {
                // Skip this cell - it's part of a vertically merged cell above
                currentX += cellWidth;
                gridColIndex += span;
                continue;
            }

            // Calculate cell height, considering vertical merge
            var cellHeight = rowHeight;
            if (cell.Properties.VerticalMerge == VerticalMergeType.Restart)
            {
                // For vertically merged cells, use the full merged height so background fills entire area
                cellHeight = CalculateVerticalMergeHeight(table, rowIndex, gridColIndex, rowHeights);
            }

            RenderTableCell(cell, currentX, currentY, cellWidth, cellHeight, table.Properties);

            currentX += cellWidth;
            gridColIndex += span;
        }
    }

    /// <summary>
    /// Calculates the total height of a vertically merged cell starting at the given row and column.
    /// </summary>
    static float CalculateVerticalMergeHeight(TableElement table, int startRowIndex, int gridColIndex, float[] rowHeights)
    {
        var totalHeight = rowHeights[startRowIndex];

        // Look at subsequent rows to find cells that continue the merge
        for (var rowIndex = startRowIndex + 1; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];

            // Find the cell at the same grid column position
            var currentGridCol = 0;
            TableCell? cellAtColumn = null;
            foreach (var cell in row.Cells)
            {
                if (currentGridCol == gridColIndex)
                {
                    cellAtColumn = cell;
                    break;
                }

                currentGridCol += cell.Properties.GridSpan;
                if (currentGridCol > gridColIndex)
                {
                    break;
                }
            }

            // If we found a cell that continues the merge, add its row height
            if (cellAtColumn?.Properties.VerticalMerge == VerticalMergeType.Continue)
            {
                totalHeight += rowHeights[rowIndex];
            }
            else
            {
                // Merge ends here
                break;
            }
        }

        return totalHeight;
    }

    float[] CalculateColumnWidths(TableElement table, int colCount)
    {
        var widths = new float[colCount];
        var availableWidth = context.ContentWidth;
        var gridWidths = table.Properties.GridColumnWidths;

        // First pass: gather explicit widths from cell properties
        // Only consider cells that don't span multiple columns (gridSpan=1)
        var hasExplicitWidths = false;

        foreach (var row in table.Rows)
        {
            var gridColIndex = 0; // Track actual grid column position
            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var props = cell.Properties;
                var span = props.GridSpan;

                // Only use explicit width from cells that don't span multiple columns
                if (span == 1 && props.WidthPoints.HasValue)
                {
                    widths[gridColIndex] = Math.Max(widths[gridColIndex], (float) props.WidthPoints.Value);
                    hasExplicitWidths = true;
                }

                gridColIndex += span; // Advance by the number of columns this cell spans
            }
        }

        if (hasExplicitWidths)
        {
            // Fill in any remaining columns without widths
            var totalExplicitWidth = widths.Sum();
            var columnsWithoutWidth = widths.Count(w => w == 0);

            if (columnsWithoutWidth > 0 && totalExplicitWidth < availableWidth)
            {
                var remainingWidth = availableWidth - totalExplicitWidth;
                var perColumnWidth = remainingWidth / columnsWithoutWidth;
                for (var i = 0; i < colCount; i++)
                {
                    if (widths[i] == 0)
                    {
                        widths[i] = perColumnWidth;
                    }
                }
            }

            // Scale to fit if total exceeds available width
            var totalWidth = widths.Sum();
            if (totalWidth > availableWidth)
            {
                var scale = availableWidth / totalWidth;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] *= scale;
                }
            }
        }
        else if (gridWidths is {Count: > 0})
        {
            // No cell widths - use grid column widths as fallback
            for (var i = 0; i < colCount && i < gridWidths.Count; i++)
            {
                widths[i] = (float) gridWidths[i];
            }

            // Fill remaining columns if grid has fewer entries than actual columns
            if (gridWidths.Count < colCount)
            {
                var avgWidth = (float) gridWidths.Average();
                for (var i = gridWidths.Count; i < colCount; i++)
                {
                    widths[i] = avgWidth;
                }
            }

            // Scale grid widths to fit available width while maintaining proportions
            var totalWidth = widths.Sum();
            if (totalWidth > availableWidth && totalWidth > 0)
            {
                var scale = availableWidth / totalWidth;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] *= scale;
                }
            }
        }
        else
        {
            // Distribute evenly
            var cellWidth = availableWidth / colCount;
            for (var i = 0; i < colCount; i++)
            {
                widths[i] = cellWidth;
            }
        }

        return widths;
    }

    float[] CalculateRowHeights(TableElement table, float[] colWidths)
    {
        var heights = new float[table.Rows.Count];
        var colCount = colWidths.Length;


        // First pass: Calculate heights for non-merged cells only
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            float maxHeight = 20; // Minimum row height

            var gridColIndex = 0;
            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = cell.Properties.GridSpan;

                // Skip cells that are part of a vertical merge (their height is handled separately)
                if (cell.Properties.VerticalMerge is VerticalMergeType.Continue or VerticalMergeType.Restart)
                {
                    gridColIndex += span;
                    continue;
                }

                // Sum column widths for horizontally merged cells
                float cellWidth = 0;
                for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                {
                    cellWidth += colWidths[gridColIndex + i];
                }

                var cellHeight = MeasureCellHeight(cell, cellWidth, table.Properties);
                maxHeight = Math.Max(maxHeight, cellHeight);

                gridColIndex += span;
            }

            heights[rowIndex] = maxHeight;
        }

        // Second pass: Apply explicit row heights if specified (w:trHeight)
        // For tables with vMerge AND all rows having explicit heights, use heights directly.
        // This pattern is common in letterheads where row heights define the layout precisely.
        // For other tables, use atLeast behavior (content can expand).
        var hasVMerge = table.Rows.Any(r => r.Cells.Any(c =>
            c.Properties.VerticalMerge is VerticalMergeType.Restart or VerticalMergeType.Continue));
        var allRowsHaveExplicitHeight = table.Rows.All(r => r.HeightPoints.HasValue);
        var useStrictHeights = hasVMerge && allRowsHaveExplicitHeight;

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            if (row.HeightPoints.HasValue)
            {
                var explicitHeight = (float) row.HeightPoints.Value;
                if (row.IsExactHeight || useStrictHeights)
                {
                    // Exact height, or vMerge table with all explicit heights: use specified height
                    heights[rowIndex] = explicitHeight;
                }
                else
                {
                    // Minimum (atLeast): allow content expansion
                    heights[rowIndex] = Math.Max(heights[rowIndex], explicitHeight);
                }
            }
        }

        // Third pass: Handle vertically merged cells
        // Find vMerge.Restart cells and distribute their height among spanned rows
        // This runs AFTER explicit heights so vMerge can expand rows if needed
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var gridColIndex = 0;

            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = cell.Properties.GridSpan;

                if (cell.Properties.VerticalMerge == VerticalMergeType.Restart)
                {
                    // Calculate how many rows this cell spans
                    var rowSpan = CalculateVerticalMergeRowSpan(table, rowIndex, gridColIndex);

                    // Calculate cell width
                    float cellWidth = 0;
                    for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                    {
                        cellWidth += colWidths[gridColIndex + i];
                    }

                    // Measure the content height of this merged cell
                    var contentHeight = MeasureCellHeight(cell, cellWidth, table.Properties);

                    // Calculate current total height of spanned rows
                    float currentTotalHeight = 0;
                    for (var r = rowIndex; r < rowIndex + rowSpan && r < table.Rows.Count; r++)
                    {
                        currentTotalHeight += heights[r];
                    }

                    // If content needs more space, distribute the extra height among spanned rows
                    if (contentHeight > currentTotalHeight)
                    {
                        var extraHeight = contentHeight - currentTotalHeight;
                        var extraPerRow = extraHeight / rowSpan;

                        for (var r = rowIndex; r < rowIndex + rowSpan && r < table.Rows.Count; r++)
                        {
                            heights[r] += extraPerRow;
                        }
                    }
                }

                gridColIndex += span;
            }
        }

        return heights;
    }

    /// <summary>
    /// Calculates how many rows a vertically merged cell spans.
    /// </summary>
    static int CalculateVerticalMergeRowSpan(TableElement table, int startRowIndex, int gridColIndex)
    {
        var rowSpan = 1;

        for (var rowIndex = startRowIndex + 1; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];

            // Find the cell at the same grid column position
            var currentGridCol = 0;
            TableCell? cellAtColumn = null;
            foreach (var cell in row.Cells)
            {
                if (currentGridCol == gridColIndex)
                {
                    cellAtColumn = cell;
                    break;
                }

                currentGridCol += cell.Properties.GridSpan;
                if (currentGridCol > gridColIndex)
                {
                    break;
                }
            }

            // If we found a cell that continues the merge, increment row span
            if (cellAtColumn?.Properties.VerticalMerge == VerticalMergeType.Continue)
            {
                rowSpan++;
            }
            else
            {
                break;
            }
        }

        return rowSpan;
    }

    float MeasureCellHeight(TableCell cell, float cellWidth, TableProperties tableProps)
    {
        var padding = GetEffectivePadding(cell.Properties, tableProps);
        var margin = GetEffectiveMargin(cell.Properties, tableProps);

        // Calculate available content width within the cell
        var contentWidth = cellWidth - (float) (padding.Horizontal + margin.Horizontal);

        var height = (float) (padding.Vertical + margin.Vertical);

        // Collect all paragraphs to determine first/last
        var paragraphs = new List<(ParagraphElement para, float bulletIndent)>();
        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                float bulletIndent = para.Properties.Numbering != null ? 12 : 0;
                paragraphs.Add((para, bulletIndent));
            }
            else if (element is ContentControlElement contentControl)
            {
                ParagraphElement? measurePara = null;
                if (contentControl.Runs is {Count: > 0})
                {
                    measurePara = new()
                    {
                        Runs = contentControl.Runs,
                        Properties = new()
                    };
                }
                else if (!string.IsNullOrEmpty(contentControl.Content))
                {
                    measurePara = new()
                    {
                        Runs =
                        [
                            new()
                            {
                                Text = contentControl.Content,
                                Properties = new()
                            }
                        ],
                        Properties = new()
                    };
                }

                if (measurePara != null)
                {
                    paragraphs.Add((measurePara, 0));
                }
            }
            else if (element is TableElement {Properties.IsFloating: false})
            {
                height += 50;
            }
        }

        // Measure paragraphs, but don't add spacing before first paragraph
        // or spacing after last paragraph (absorbed by cell padding)
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var (para, bulletIndent) = paragraphs[i];
            var lines = textRenderer.LayoutParagraphForMeasurement(para, contentWidth - bulletIndent);
            var props = para.Properties;

            // Add spacing before (skip for first paragraph - absorbed by cell padding)
            if (i > 0)
            {
                height += (float) props.SpacingBeforePoints;
            }

            // Add line heights
            foreach (var lineHeight in lines)
            {
                height += lineHeight;
            }

            // Add spacing after (skip for last paragraph - absorbed by cell padding)
            if (i < paragraphs.Count - 1)
            {
                height += (float) props.SpacingAfterPoints;
            }
        }

        return height;
    }

    static CellSpacing GetEffectivePadding(TableCellProperties cellProps, TableProperties tableProps) =>
        cellProps.Padding ?? tableProps.DefaultCellPadding;

    static CellSpacing GetEffectiveMargin(TableCellProperties cellProps, TableProperties tableProps) =>
        cellProps.Margin ?? tableProps.DefaultCellMargin;

    void RenderTableCell(TableCell cell, float x, float y, float width, float height, TableProperties tableProps)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var padding = GetEffectivePadding(cell.Properties, tableProps);
        var margin = GetEffectiveMargin(cell.Properties, tableProps);

        // Apply margin to position
        var cellX = x + (float) margin.Left;
        var cellY = y + (float) margin.Top;
        var cellWidth = width - (float) margin.Horizontal;
        var cellHeight = height - (float) margin.Vertical;

        var pixelX = context.PointsToPixels(cellX);
        var pixelY = context.PointsToPixels(cellY);
        var pixelWidth = context.PointsToPixels(cellWidth);
        var pixelHeight = context.PointsToPixels(cellHeight);

        // Draw cell background
        if (cell.Properties.BackgroundColorHex != null)
        {
            using var bgPaint = new SKPaint
            {
                Color = ParseColor(cell.Properties.BackgroundColorHex),
                Style = SKPaintStyle.Fill
            };
            currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);
        }

        // Draw cell borders - use cell-level borders if specified, otherwise table defaults
        var borders = cell.Properties.Borders ?? tableProps.DefaultBorders;
        if (borders != null)
        {
            // Draw top border
            if (borders.Top.IsVisible)
            {
                using var paint = CreateBorderPaint(borders.Top);
                currentCanvas.DrawLine(pixelX, pixelY, pixelX + pixelWidth, pixelY, paint);
            }

            // Draw right border
            if (borders.Right.IsVisible)
            {
                using var paint = CreateBorderPaint(borders.Right);
                currentCanvas.DrawLine(pixelX + pixelWidth, pixelY, pixelX + pixelWidth, pixelY + pixelHeight, paint);
            }

            // Draw bottom border
            if (borders.Bottom.IsVisible)
            {
                using var paint = CreateBorderPaint(borders.Bottom);
                currentCanvas.DrawLine(pixelX, pixelY + pixelHeight, pixelX + pixelWidth, pixelY + pixelHeight, paint);
            }

            // Draw left border
            if (borders.Left.IsVisible)
            {
                using var paint = CreateBorderPaint(borders.Left);
                currentCanvas.DrawLine(pixelX, pixelY, pixelX, pixelY + pixelHeight, paint);
            }
        }

        // Render cell content
        var savedY = context.CurrentY;

        // Calculate content dimensions for vertical alignment
        var contentX = cellX + (float) padding.Left;
        var contentWidth = cellWidth - (float) padding.Horizontal;
        var availableHeight = cellHeight - (float) padding.Vertical;

        // Measure actual content height
        float contentHeight = 0;
        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                // Account for bullet indent to match RenderParagraphInBounds behavior
                float bulletIndent = para.Properties.Numbering != null ? 12 : 0;
                contentHeight += textRenderer.MeasureParagraphHeightWithWidth(para, contentWidth - bulletIndent);
            }
            else if (element is ContentControlElement contentControl)
            {
                // Approximate content control height as a single line
                var measurePara = new ParagraphElement
                {
                    Runs = contentControl.Runs!,
                    Properties = new()
                };
                contentHeight += textRenderer.MeasureParagraphHeightWithWidth(measurePara, contentWidth);
            }
            else if (element is ImageElement image)
            {
                // Calculate image height, accounting for scaling if needed
                var imageWidth = (float) image.WidthPoints;
                var imageHeight = (float) image.HeightPoints;
                if (imageWidth > contentWidth)
                {
                    var scale = contentWidth / imageWidth;
                    imageHeight *= scale;
                }

                contentHeight += imageHeight;
            }
        }

        // Calculate vertical offset based on alignment
        // For vertically merged cells (vMerge restart), Word appears to use reduced centering
        // to prevent excessive whitespace above content when the merged cell is tall
        var verticalOffset = cell.Properties.VerticalAlignment switch
        {
            CellVerticalAlignment.Center => Math.Max(0, (availableHeight - contentHeight) / 2),
            CellVerticalAlignment.Bottom => Math.Max(0, availableHeight - contentHeight),
            _ => 0 // Top alignment
        };

        // Limit vertical centering for cells that start a vertical merge (vMerge="restart")
        // Word seems to use reduced centering in this case, positioning content closer to top
        if (cell.Properties is {VerticalMerge: VerticalMergeType.Restart, VerticalAlignment: CellVerticalAlignment.Center})
        {
            // Use a maximum offset to prevent too much whitespace above content
            const float maxCenterOffset = 12f; // ~0.17 inches
            verticalOffset = Math.Min(verticalOffset, maxCenterOffset);
        }

        // Temporarily adjust context for cell rendering with padding and vertical alignment
        context.CurrentY = cellY + (float) padding.Top + verticalOffset;

        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                // Render paragraph within cell bounds with padding
                RenderParagraphInBounds(para, contentX, contentWidth);
            }
            else if (element is ContentControlElement contentControl)
            {
                // Render content control text as simple text in cell
                RenderContentControlInCell(contentControl, contentX, contentWidth);
            }
            else if (element is ImageElement image)
            {
                // Render image within cell bounds
                RenderImageInCell(image, contentX, contentWidth);
            }
        }

        context.CurrentY = savedY;
    }

    void RenderParagraphInBounds(ParagraphElement paragraph, float x, float maxWidth)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Render paragraph within specific bounds (for tables and text boxes)
        textRenderer.RenderParagraphInBounds(currentCanvas, paragraph, x, maxWidth);
    }

    void RenderImageInCell(ImageElement image, float x, float maxWidth)
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
            using var skImage = SKBitmap.Decode(image.ImageData);
            if (skImage != null)
            {
                currentCanvas.DrawBitmap(skImage, destRect);
            }
        }

        context.CurrentY += imageHeight;
    }

    static SKColor ParseColor(string hexColor)
    {
        if (string.IsNullOrEmpty(hexColor) || hexColor == "auto")
        {
            return SKColors.Black;
        }

        if (hexColor.Length == 6)
        {
            if (uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var rgb))
            {
                return new(
                    (byte) ((rgb >> 16) & 0xFF),
                    (byte) ((rgb >> 8) & 0xFF),
                    (byte) (rgb & 0xFF)
                );
            }
        }

        return SKColors.Black;
    }

    SKPaint CreateBorderPaint(BorderEdge edge) =>
        new()
        {
            Color = ParseColor(edge.ColorHex ?? "000000"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = context.PointsToPixels((float) edge.WidthPoints),
            IsAntialias = true
        };

    /// <summary>
    /// Renders a text form field as a text box with border.
    /// </summary>
    void RenderTextFormField(TextFormFieldElement textField)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var fieldWidth = (float) textField.WidthPoints;
        float fieldHeight = 18; // Standard form field height
        var x = context.ContentLeft;
        var y = context.CurrentY;

        // Check for page break
        if (y + fieldHeight > context.ContentBottom)
        {
            FinishCurrentPage();
            StartNewPage();
            y = context.CurrentY;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels(fieldWidth);
        var pixelHeight = context.PointsToPixels(fieldHeight);

        // Draw field background (light gray for inactive)
        using var bgPaint = new SKPaint
        {
            Color = textField.Enabled ? SKColors.White : new(240, 240, 240),
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);

        // Draw field border
        using var borderPaint = new SKPaint
        {
            Color = SKColors.Gray,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1 * context.Scale,
            IsAntialias = true
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, borderPaint);

        // Draw the text value
        var displayText = !string.IsNullOrEmpty(textField.Value) ? textField.Value : textField.DefaultText ?? "";
        if (!string.IsNullOrEmpty(displayText))
        {
            using var typeface = SKTypeface.FromFamilyName("Aptos", SKFontStyle.Normal);
            using var font = context.CreateFontFromTypeface(typeface, 10);
            using var textPaint = new SKPaint
            {
                Color = textField.Enabled ? SKColors.Black : SKColors.Gray,
                IsAntialias = true
            };

            var textX = pixelX + 3 * context.Scale;
            var textY = pixelY + pixelHeight - 4 * context.Scale;
            currentCanvas.DrawText(displayText, textX, textY, SKTextAlign.Left, font, textPaint);
        }

        context.CurrentY += fieldHeight + 4; // Add some spacing after
    }

    /// <summary>
    /// Renders a checkbox form field.
    /// </summary>
    void RenderCheckBoxFormField(CheckBoxFormFieldElement checkBox)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var boxSize = checkBox.SizePoints > 0 ? (float) checkBox.SizePoints : 12;
        var x = context.ContentLeft;
        var y = context.CurrentY;

        // Check for page break
        if (y + boxSize > context.ContentBottom)
        {
            FinishCurrentPage();
            StartNewPage();
            y = context.CurrentY;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelSize = context.PointsToPixels(boxSize);

        // Draw checkbox background
        using var bgPaint = new SKPaint
        {
            Color = checkBox.Enabled ? SKColors.White : new(240, 240, 240),
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelSize, pixelSize, bgPaint);

        // Draw checkbox border
        using var borderPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1 * context.Scale,
            IsAntialias = true
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelSize, pixelSize, borderPaint);

        // Draw checkmark if checked
        if (checkBox.Checked)
        {
            using var checkPaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2 * context.Scale,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };

            // Draw checkmark as two lines
            var padding = pixelSize * 0.2f;
            var left = pixelX + padding;
            var right = pixelX + pixelSize - padding;
            var top = pixelY + padding;
            var bottom = pixelY + pixelSize - padding;
            var midX = pixelX + pixelSize * 0.4f;

            currentCanvas.DrawLine(left, top + (bottom - top) * 0.5f, midX, bottom, checkPaint);
            currentCanvas.DrawLine(midX, bottom, right, top, checkPaint);
        }

        context.CurrentY += boxSize + 4; // Add some spacing after
    }

    /// <summary>
    /// Renders a drop-down form field.
    /// </summary>
    void RenderDropDownFormField(DropDownFormFieldElement dropDown)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var fieldWidth = (float) dropDown.WidthPoints;
        float fieldHeight = 18; // Standard form field height
        var x = context.ContentLeft;
        var y = context.CurrentY;

        // Check for page break
        if (y + fieldHeight > context.ContentBottom)
        {
            FinishCurrentPage();
            StartNewPage();
            y = context.CurrentY;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels(fieldWidth);
        var pixelHeight = context.PointsToPixels(fieldHeight);

        // Draw field background
        using var bgPaint = new SKPaint
        {
            Color = dropDown.Enabled ? SKColors.White : new(240, 240, 240),
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);

        // Draw field border
        using var borderPaint = new SKPaint
        {
            Color = SKColors.Gray,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1 * context.Scale,
            IsAntialias = true
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, borderPaint);

        // Draw the selected value
        var selectedValue = dropDown.SelectedIndex >= 0 && dropDown.SelectedIndex < dropDown.Items.Count
            ? dropDown.Items[dropDown.SelectedIndex]
            : "";

        if (!string.IsNullOrEmpty(selectedValue))
        {
            using var typeface = SKTypeface.FromFamilyName("Aptos", SKFontStyle.Normal);
            using var font = context.CreateFontFromTypeface(typeface, 10);
            using var textPaint = new SKPaint
            {
                Color = dropDown.Enabled ? SKColors.Black : SKColors.Gray,
                IsAntialias = true
            };

            var textX = pixelX + 3 * context.Scale;
            var textY = pixelY + pixelHeight - 4 * context.Scale;
            currentCanvas.DrawText(selectedValue, textX, textY, SKTextAlign.Left, font, textPaint);
        }

        // Draw dropdown arrow
        var arrowSize = pixelHeight * 0.3f;
        var arrowX = pixelX + pixelWidth - 12 * context.Scale;
        var arrowY = pixelY + pixelHeight / 2;

        using var arrowPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX, arrowY - arrowSize / 2);
        arrowPath.LineTo(arrowX + arrowSize, arrowY - arrowSize / 2);
        arrowPath.LineTo(arrowX + arrowSize / 2, arrowY + arrowSize / 2);
        arrowPath.Close();
        currentCanvas.DrawPath(arrowPath, arrowPaint);

        context.CurrentY += fieldHeight + 4; // Add some spacing after
    }

    /// <summary>
    /// Renders a content control element.
    /// </summary>
    void RenderContentControl(ContentControlElement control)
    {
        if (currentCanvas == null)
        {
            return;
        }

        switch (control.ControlType)
        {
            case ContentControlType.CheckBox:
                RenderContentControlCheckBox(control);
                break;

            case ContentControlType.ComboBox:
            case ContentControlType.DropDownList:
                RenderContentControlDropDown(control);
                break;

            case ContentControlType.Date:
                RenderContentControlDate(control);
                break;

            default:
                RenderContentControlText(control);
                break;
        }
    }

    void RenderContentControlCheckBox(ContentControlElement control)
    {
        if (currentCanvas == null)
        {
            return;
        }

        float boxSize = 12;
        var x = context.ContentLeft;
        var y = context.CurrentY;

        if (y + boxSize > context.ContentBottom)
        {
            FinishCurrentPage();
            StartNewPage();
            y = context.CurrentY;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelSize = context.PointsToPixels(boxSize);

        // Draw checkbox background
        using var bgPaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelSize, pixelSize, bgPaint);

        // Draw checkbox border
        using var borderPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1 * context.Scale,
            IsAntialias = true
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelSize, pixelSize, borderPaint);

        // Draw checkmark or X if checked
        if (control.Checked == true)
        {
            using var checkPaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2 * context.Scale,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };

            var padding = pixelSize * 0.25f;
            var left = pixelX + padding;
            var right = pixelX + pixelSize - padding;
            var top = pixelY + padding;
            var bottom = pixelY + pixelSize - padding;

            // Draw X
            currentCanvas.DrawLine(left, top, right, bottom, checkPaint);
            currentCanvas.DrawLine(right, top, left, bottom, checkPaint);
        }

        context.CurrentY += boxSize + 4;
    }

    /// <summary>
    /// Renders a content control's text content within a table cell (without form field styling).
    /// </summary>
    void RenderContentControlInCell(ContentControlElement control, float x, float maxWidth)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Use styled runs if available, otherwise fall back to plain text
        ParagraphElement para;
        if (control.Runs is {Count: > 0})
        {
            para = new()
            {
                Runs = control.Runs,
                Properties = new()
            };
        }
        else if (!string.IsNullOrEmpty(control.Content))
        {
            para = new()
            {
                Runs =
                [
                    new()
                    {
                        Text = control.Content,
                        Properties = new()
                    }
                ],
                Properties = new()
            };
        }
        else
        {
            return;
        }

        RenderParagraphInBounds(para, x, maxWidth);
    }

    void RenderContentControlText(ContentControlElement control)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // For RichText and PlainText content controls, render as regular paragraph text
        // (these are typically styled text placeholders in templates, not form fields)
        if (control.ControlType is ContentControlType.RichText or ContentControlType.PlainText)
        {
            // Use styled runs if available, otherwise fall back to plain text
            if (control.Runs is {Count: > 0})
            {
                var styledPara = new ParagraphElement
                {
                    Runs = control.Runs,
                    Properties = new()
                };
                RenderParagraph(styledPara);
            }
            else
            {
                var displayText = !string.IsNullOrEmpty(control.Content) ? control.Content : control.PlaceholderText ?? "";
                if (!string.IsNullOrEmpty(displayText))
                {
                    var simplePara = new ParagraphElement
                    {
                        Runs =
                        [
                            new()
                            {
                                Text = displayText,
                                Properties = new()
                            }
                        ],
                        Properties = new()
                    };
                    RenderParagraph(simplePara);
                }
            }

            return;
        }

        // Other control types get form-field styling
        var fieldWidth = (float) control.WidthPoints;
        float fieldHeight = 18;
        var x = context.ContentLeft;
        var y = context.CurrentY;

        if (y + fieldHeight > context.ContentBottom)
        {
            FinishCurrentPage();
            StartNewPage();
            y = context.CurrentY;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels(fieldWidth);
        var pixelHeight = context.PointsToPixels(fieldHeight);

        // Draw field background with subtle border
        using var bgPaint = new SKPaint
        {
            Color = new(245, 245, 245),
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);

        using var borderPaint = new SKPaint
        {
            Color = new(200, 200, 200),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1 * context.Scale,
            IsAntialias = true
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, borderPaint);

        // Draw the content or placeholder
        var text = !string.IsNullOrEmpty(control.Content) ? control.Content : control.PlaceholderText ?? "";
        var isPlaceholder = string.IsNullOrEmpty(control.Content) && !string.IsNullOrEmpty(control.PlaceholderText);

        if (!string.IsNullOrEmpty(text))
        {
            using var typeface = SKTypeface.FromFamilyName("Aptos", SKFontStyle.Normal);
            using var font = context.CreateFontFromTypeface(typeface, 10);
            using var textPaint = new SKPaint
            {
                Color = isPlaceholder ? SKColors.Gray : SKColors.Black,
                IsAntialias = true
            };

            var textX = pixelX + 3 * context.Scale;
            var textY = pixelY + pixelHeight - 4 * context.Scale;
            currentCanvas.DrawText(text, textX, textY, SKTextAlign.Left, font, textPaint);
        }

        context.CurrentY += fieldHeight + 4;
    }

    void RenderContentControlDropDown(ContentControlElement control)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var fieldWidth = (float) control.WidthPoints;
        float fieldHeight = 18;
        var x = context.ContentLeft;
        var y = context.CurrentY;

        if (y + fieldHeight > context.ContentBottom)
        {
            FinishCurrentPage();
            StartNewPage();
            y = context.CurrentY;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels(fieldWidth);
        var pixelHeight = context.PointsToPixels(fieldHeight);

        // Draw field background
        using var bgPaint = new SKPaint
        {
            Color = new(245, 245, 245),
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);

        using var borderPaint = new SKPaint
        {
            Color = new(200, 200, 200),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1 * context.Scale,
            IsAntialias = true
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, borderPaint);

        // Draw content or first list item
        string? first = null;
        foreach (var item in control.ListItems!)
        {
            first = item;
            break;
        }

        var displayText = !string.IsNullOrEmpty(control.Content)
            ? control.Content
            : first ?? control.PlaceholderText ?? "";

        if (!string.IsNullOrEmpty(displayText))
        {
            using var typeface = SKTypeface.FromFamilyName("Aptos", SKFontStyle.Normal);
            using var font = context.CreateFontFromTypeface(typeface, 10);
            using var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true
            };

            var textX = pixelX + 3 * context.Scale;
            var textY = pixelY + pixelHeight - 4 * context.Scale;
            currentCanvas.DrawText(displayText, textX, textY, SKTextAlign.Left, font, textPaint);
        }

        // Draw dropdown arrow
        var arrowSize = pixelHeight * 0.3f;
        var arrowX = pixelX + pixelWidth - 12 * context.Scale;
        var arrowY = pixelY + pixelHeight / 2;

        using var arrowPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX, arrowY - arrowSize / 2);
        arrowPath.LineTo(arrowX + arrowSize, arrowY - arrowSize / 2);
        arrowPath.LineTo(arrowX + arrowSize / 2, arrowY + arrowSize / 2);
        arrowPath.Close();
        currentCanvas.DrawPath(arrowPath, arrowPaint);

        context.CurrentY += fieldHeight + 4;
    }

    void RenderContentControlDate(ContentControlElement control)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var fieldWidth = (float) control.WidthPoints;
        float fieldHeight = 18;
        var x = context.ContentLeft;
        var y = context.CurrentY;

        if (y + fieldHeight > context.ContentBottom)
        {
            FinishCurrentPage();
            StartNewPage();
            y = context.CurrentY;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels(fieldWidth);
        var pixelHeight = context.PointsToPixels(fieldHeight);

        // Draw field background
        using var bgPaint = new SKPaint
        {
            Color = new(245, 245, 245),
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);

        using var borderPaint = new SKPaint
        {
            Color = new(200, 200, 200),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1 * context.Scale,
            IsAntialias = true
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, borderPaint);

        // Draw the date value or placeholder
        var displayText = control.DateValue.HasValue
            ? control.DateValue.Value.ToShortDateString()
            : !string.IsNullOrEmpty(control.Content) ? control.Content : control.PlaceholderText ?? "";

        if (!string.IsNullOrEmpty(displayText))
        {
            using var typeface = SKTypeface.FromFamilyName("Aptos", SKFontStyle.Normal);
            using var font = context.CreateFontFromTypeface(typeface, 10);
            using var textPaint = new SKPaint
            {
                Color = control.DateValue.HasValue || !string.IsNullOrEmpty(control.Content) ? SKColors.Black : SKColors.Gray,
                IsAntialias = true
            };

            var textX = pixelX + 3 * context.Scale;
            var textY = pixelY + pixelHeight - 4 * context.Scale;
            currentCanvas.DrawText(displayText, textX, textY, SKTextAlign.Left, font, textPaint);
        }

        // Draw calendar icon
        var iconSize = pixelHeight * 0.5f;
        var iconX = pixelX + pixelWidth - 12 * context.Scale;
        var iconY = pixelY + (pixelHeight - iconSize) / 2;

        using var iconPaint = new SKPaint
        {
            Color = SKColors.Gray,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1 * context.Scale,
            IsAntialias = true
        };
        currentCanvas.DrawRect(iconX, iconY, iconSize, iconSize, iconPaint);

        context.CurrentY += fieldHeight + 4;
    }

    void StartNewPage()
    {
        currentPage = new(
            context.PageWidthPixels,
            context.PageHeightPixels,
            SKColorType.Rgba8888,
            SKAlphaType.Premul
        );

        currentCanvas = new(currentPage);

        // Clear with background color if specified, otherwise white
        var bgColor = context.PageSettings.BackgroundColorHex;
        if (!string.IsNullOrEmpty(bgColor))
        {
            currentCanvas.Clear(ParseColor(bgColor));
        }
        else
        {
            currentCanvas.Clear(SKColors.White);
        }

        // Background shapes are now rendered inline when encountered in document flow
        // (not repeated on every page)

        if (pages.Count > 0)
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

    void FinishCurrentPage()
    {
        if (currentPage != null)
        {
            // Render footer before finishing
            RenderFooter();

            pages.Add(currentPage);
            currentCanvas?.Dispose();
            currentCanvas = null;
            currentPage = null;
        }
    }

    /// <summary>
    /// Removes the last page if it has no significant content.
    /// Called at the end of document rendering.
    /// Trailing blank pages are almost always spurious, even from explicit breaks
    /// (section breaks at document end don't create visible blank pages in Word).
    /// </summary>
    void RemoveBlankTrailingPage()
    {
        // Only remove if there's more than one page and the last page is blank
        if (pages.Count > 1 && !hasSignificantContentOnCurrentPage && !currentPageFromExplicitBreak)
        {
            var lastPage = pages[^1];
            pages.RemoveAt(pages.Count - 1);
            lastPage.Dispose();
        }
    }

    void RenderBackgroundShape(FloatingShapeElement shape)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Calculate position based on anchor type
        var x = CalculateShapeX(shape);
        var y = CalculateShapeY(shape);

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels((float) shape.WidthPoints);
        var pixelHeight = context.PointsToPixels((float) shape.HeightPoints);

        // Check for image fill first
        if (shape.ImageData != null)
        {
            using var bitmap = SKBitmap.Decode(shape.ImageData);
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
        else if (shape.FillColorHex != null)
        {
            // Solid fill
            using var paint = new SKPaint
            {
                Color = SKColor.Parse(shape.FillColorHex),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, paint);
        }
    }

    float CalculateShapeX(FloatingShapeElement shape)
    {
        // For background shapes (BehindText=true), use margin as base for column-relative
        // since these are typically full-page backgrounds
        var baseX = shape.HorizontalAnchor switch
        {
            HorizontalAnchor.Page => 0,
            HorizontalAnchor.Margin => (float) context.PageSettings.MarginLeft,
            HorizontalAnchor.Column => (float) context.PageSettings.MarginLeft,
            _ => (float) context.PageSettings.MarginLeft
        };

        return baseX + (float) shape.HorizontalPositionPoints;
    }

    float CalculateShapeY(FloatingShapeElement shape)
    {
        // For background shapes (BehindText=true), use margin as base for paragraph-relative
        // since these are rendered at page start before any content is placed
        var baseY = shape.VerticalAnchor switch
        {
            VerticalAnchor.Page => 0,
            VerticalAnchor.Margin => (float) context.PageSettings.MarginTop,
            VerticalAnchor.Paragraph => (float) context.PageSettings.MarginTop,
            VerticalAnchor.Line => (float) context.PageSettings.MarginTop,
            _ => (float) context.PageSettings.MarginTop
        };

        return baseY + (float) shape.VerticalPositionPoints;
    }

    void RenderFloatingImage(FloatingImageElement image)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Calculate absolute position based on anchor type
        var x = CalculateFloatingImageX(image);
        var y = CalculateFloatingImageY(image);

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels((float) image.WidthPoints);
        var pixelHeight = context.PointsToPixels((float) image.HeightPoints);

        var destRect = new SKRect(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);

        if (image.ContentType == "image/svg+xml")
        {
            RenderSvgImage(image.ImageData, destRect);
        }
        else
        {
            using var skImage = SKBitmap.Decode(image.ImageData);
            if (skImage != null)
            {
                currentCanvas.DrawBitmap(skImage, destRect);
            }
        }
    }

    float CalculateFloatingImageX(FloatingImageElement image)
    {
        var baseX = image.HorizontalAnchor switch
        {
            HorizontalAnchor.Page => 0,
            HorizontalAnchor.Margin => (float) context.PageSettings.MarginLeft,
            HorizontalAnchor.Column => context.ContentLeft,
            HorizontalAnchor.Character => context.ContentLeft, // Approximate
            _ => 0
        };

        return baseX + (float) image.HorizontalPositionPoints;
    }

    float CalculateFloatingImageY(FloatingImageElement image)
    {
        var baseY = image.VerticalAnchor switch
        {
            VerticalAnchor.Page => 0,
            VerticalAnchor.Margin => (float) context.PageSettings.MarginTop,
            VerticalAnchor.Paragraph => context.CurrentY, // Approximate - relative to current paragraph
            VerticalAnchor.Line => context.CurrentY, // Approximate
            _ => 0
        };

        return baseY + (float) image.VerticalPositionPoints;
    }

    void RenderFloatingTextBox(FloatingTextBoxElement textBox)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Calculate absolute position
        var x = CalculateFloatingTextBoxX(textBox);
        var y = CalculateFloatingTextBoxY(textBox);

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels((float) textBox.WidthPoints);
        var pixelHeight = context.PointsToPixels((float) textBox.HeightPoints);

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

    float CalculateFloatingTextBoxX(FloatingTextBoxElement textBox)
    {
        var baseX = textBox.HorizontalAnchor switch
        {
            HorizontalAnchor.Page => 0,
            HorizontalAnchor.Margin => (float) context.PageSettings.MarginLeft,
            HorizontalAnchor.Column => context.ContentLeft,
            HorizontalAnchor.Character => context.ContentLeft,
            _ => 0
        };

        return baseX + (float) textBox.HorizontalPositionPoints;
    }

    float CalculateFloatingTextBoxY(FloatingTextBoxElement textBox)
    {
        var baseY = textBox.VerticalAnchor switch
        {
            VerticalAnchor.Page => 0,
            VerticalAnchor.Margin => (float) context.PageSettings.MarginTop,
            VerticalAnchor.Paragraph => context.CurrentY,
            VerticalAnchor.Line => context.CurrentY,
            _ => 0
        };

        return baseY + (float) textBox.VerticalPositionPoints;
    }

    public void Dispose()
    {
        currentCanvas?.Dispose();
        currentPage?.Dispose();
        // Note: Don't dispose _pages here - caller owns them
    }
}
