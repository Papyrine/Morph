/// <summary>
/// Renders text content with formatting using SixLabors.ImageSharp.
/// </summary>
sealed class TextRenderer(ImageSharpRenderContext context) :
    IParagraphMeasurer
{
    // See SkiaTextRenderer for the rationale — same shape, same hot-path benefit.
    readonly Dictionary<ParagraphElement, List<TextLine>> pagedLayoutCache = [];
    readonly Dictionary<(ParagraphElement, float), List<TextLine>> boundedLayoutCache = [];
    /// <summary>
    /// Measures the height of a paragraph when rendered at the given width.
    /// </summary>
    public float MeasureParagraphHeight(ParagraphElement paragraph)
    {
        var lines = LayoutParagraph(paragraph);
        var props = paragraph.Properties;

        // Add spacing before (collapsed if contextual spacing from previous paragraph)
        // Contextual spacing only collapses spacing between paragraphs of the SAME STYLE
        var sameStyle = props.StyleId != null &&
                        props.StyleId == context.LastParagraphStyleId;
        var collapseSpacingBefore = props.ContextualSpacing &&
                                    context.LastParagraphHadContextualSpacing && sameStyle;
        var totalHeight = collapseSpacingBefore ? 0 : (float)props.SpacingBeforePoints;

        foreach (var line in lines)
        {
            totalHeight += CalculateLineHeight(line, props);
        }

        // Add spacing after (collapsed if this paragraph has contextual spacing)
        if (!props.ContextualSpacing)
        {
            totalHeight += (float)props.SpacingAfterPoints;
        }

        // Border space: only expand height by the amount w:space exceeds the
        // spacing that would otherwise absorb it (matches Word's layout).
        totalHeight += BorderSpaceExcess(props);

        return totalHeight;
    }

    static float BorderSpaceExcess(ParagraphProperties props)
    {
        if (props.Borders is not {HasAnyBorder: true} borders)
        {
            return 0;
        }

        var extra = 0f;
        if (borders.Top.IsVisible)
        {
            extra += Math.Max(0, (float) props.BorderTopSpacePoints - (float) props.SpacingBeforePoints);
        }
        if (borders.Bottom.IsVisible)
        {
            extra += Math.Max(0, (float) props.BorderBottomSpacePoints - (float) props.SpacingAfterPoints);
        }
        return extra;
    }

    /// <summary>
    /// Measures the height of a paragraph when rendered within a specific width constraint.
    /// </summary>
    public float MeasureParagraphHeightWithWidth(ParagraphElement paragraph, float maxWidth)
    {
        var lines = LayoutParagraphWithWidth(paragraph, maxWidth);
        var props = paragraph.Properties;
        var totalHeight = (float)props.SpacingBeforePoints;

        foreach (var line in lines)
        {
            totalHeight += CalculateLineHeight(line, props);
        }

        // Don't add spacing after for empty paragraphs (they're typically just visual spacers)
        var isEmpty = IsParagraphEmpty(paragraph);
        if (!isEmpty)
        {
            totalHeight += (float)props.SpacingAfterPoints;
        }

        totalHeight += BorderSpaceExcess(props);

        return totalHeight;
    }

    /// <summary>
    /// Layouts a paragraph for measurement purposes, returning individual line heights.
    /// Used by table cell measurement where spacing is handled separately.
    /// Table cells use more compact line height without the Word compatibility boost.
    /// </summary>
    public List<float> LayoutParagraphForMeasurement(ParagraphElement paragraph, float maxWidth)
    {
        var lines = LayoutParagraphWithWidth(paragraph, maxWidth);
        var props = paragraph.Properties;
        var lineHeights = new List<float>(lines.Count);
        foreach (var line in lines)
        {
            lineHeights.Add(TableLayout.CalculateCompactLineHeight(line.Height, props));
        }
        return lineHeights;
    }

    /// <summary>
    /// Measures the maximum line width when a paragraph is laid out at the given wrap width.
    /// Pass a very large maxWidth to obtain the natural single-line width.
    /// </summary>
    public float MeasureParagraphNaturalWidth(ParagraphElement paragraph, float maxWidth)
    {
        var lines = LayoutParagraphWithWidth(paragraph, maxWidth);
        var widest = 0f;
        foreach (var line in lines)
        {
            if (line.Width > widest)
            {
                widest = line.Width;
            }
        }
        return widest;
    }




    /// <summary>
    /// Calculates the effective line height based on the line spacing rule.
    /// </summary>
    float CalculateLineHeight(TextLine line, ParagraphProperties props)
    {
        var naturalHeight = line.Height;
        var lineHeight = props.LineSpacingRule switch
        {
            LineSpacingRule.Exactly => (float)props.LineSpacingPoints,
            LineSpacingRule.AtLeast => Math.Max(naturalHeight, (float)props.LineSpacingPoints),
            _ => naturalHeight * (float)props.LineSpacingMultiplier // Auto
        };

        // Word compatibility floor: when the font's metrics under-report (Ascent+Descent
        // gives less than 120% of font size), lift the line height to match Word's "single
        // line spacing" rule (~120%). For fonts already at/above 120% (Calibri ≈137%) the
        // floor is a no-op. Only applied for Auto with multiplier in the "single-ish"
        // range; compact (<0.9) is intentional and exact is exact.
        if (props is {LineSpacingRule: LineSpacingRule.Auto, LineSpacingMultiplier: >= 0.9 and <= 1.15})
        {
            var largestFontSize = LargestFontSizePoints(line, props);
            if (largestFontSize > 0)
            {
                var floor = largestFontSize * 1.20f * (float)props.LineSpacingMultiplier;
                lineHeight = Math.Max(lineHeight, floor);
            }

            // Empirical leading boost: Word's pagination for documents that use the
            // built-in Normal-with-1.08-multiplier style packs slightly less per page
            // than ImageSharp's natural metrics × 1.08 alone produces. Apply a small
            // extra boost only for the 1.08-ish range so it kicks in for Word's built-in
            // default and explicit settings near 1.08, but not for our 1.04 styled
            // default (which already bakes in just enough leading on its own).
            if (props.LineSpacingMultiplier >= 1.06)
            {
                var boost = 1.0f + 0.50f * (1.15f - (float)props.LineSpacingMultiplier);
                lineHeight *= Math.Max(1.0f, boost);
            }
        }

        // Only apply document-grid line pitch when Word pagination hints are prevalent in the document.
        // (Some docs contain a handful of markers that don't correspond to stable pagination.)
        if (context.PageSettings.LastRenderedPageBreakCount >= 20 &&
            props.LineSpacingRule != LineSpacingRule.Exactly &&
            context.PageSettings.DocumentGridLinePitchPoints > 0)
        {
            lineHeight = Math.Max(lineHeight, (float)context.PageSettings.DocumentGridLinePitchPoints);
        }

        return lineHeight;
    }

    static float LargestFontSizePoints(TextLine line, ParagraphProperties props)
    {
        var max = 0f;
        foreach (var fragment in line.Fragments)
        {
            if (fragment.Properties.FontSizePoints > max)
            {
                max = (float)fragment.Properties.FontSizePoints;
            }
        }
        if (max == 0 && props.ParagraphMarkFontSizePoints is { } markSize)
        {
            max = (float)markSize;
        }
        return max;
    }

    /// <summary>
    /// Renders a paragraph to the canvas at the current position.
    /// </summary>
    public void RenderParagraph(DrawingCanvas canvas, ParagraphElement paragraph, DocumentElement? nextElement = null)
    {
        var lines = LayoutParagraph(paragraph);
        var props = paragraph.Properties;
        var lineNumberSettings = context.PageSettings.LineNumbers;
        var showLineNumbers = lineNumberSettings != null &&
                              !props.SuppressLineNumbers;

        // Snapshot the cursor before spacing-before so bar tabs span the full
        // paragraph cell (including spacing-before/after). Adjacent bar-tab
        // paragraphs then meet at the same Y and the vertical bars connect.
        var barTabStartY = context.CurrentY;

        // Add spacing before with margin collapsing (similar to CSS)
        // When two paragraphs are adjacent, use max(SpacingAfter, SpacingBefore) instead of sum
        // Contextual spacing only collapses spacing between paragraphs of the SAME STYLE
        var sameStyle = props.StyleId != null &&
                        props.StyleId == context.LastParagraphStyleId;
        var collapseSpacingBefore = props.ContextualSpacing &&
                                    context.LastParagraphHadContextualSpacing && sameStyle;
        // Also collapse when we're continuing a w:between border chain — the borders fuse,
        // so there must be no gap between this paragraph and the previous one.
        var inBetweenChain = context.SuppressNextParagraphTopBorder;
        if (!collapseSpacingBefore && !inBetweenChain)
        {
            var spacingBefore = (float)props.SpacingBeforePoints;
            var lastSpacingAfter = context.LastParagraphSpacingAfterPoints;

            // Margin collapsing: only add the excess over what was already added as SpacingAfter
            var effectiveSpacingBefore = Math.Max(0, spacingBefore - lastSpacingAfter);
            context.CurrentY += effectiveSpacingBefore;
        }
        // Reset the tracked spacing after since it's been accounted for
        context.LastParagraphSpacingAfterPoints = 0;

        // Draw paragraph background/shading if specified
        if (!string.IsNullOrEmpty(props.BackgroundColorHex))
        {
            // Calculate total paragraph height (all lines)
            float paragraphHeight = 0;
            foreach (var line in lines)
            {
                paragraphHeight += CalculateLineHeight(line, props);
            }

            var bgColor = ImageSharpRenderContext.ParseColor(props.BackgroundColorHex);

            var bgX = context.PointsToPixels(context.ContentLeft + (float)props.LeftIndentPoints);
            var bgY = context.PointsToPixels(context.CurrentY);
            var bgWidth = context.PointsToPixels(context.ContentWidth - (float)props.LeftIndentPoints - (float)props.RightIndentPoints);
            var bgHeight = context.PointsToPixels(paragraphHeight);

            canvas.Fill(context.GetBrush(bgColor), new RectangleF(bgX, bgY, bgWidth, bgHeight));
        }

        // Reserve vertical space for border w:space that isn't already absorbed by
        // SpacingBefore/After. When inBetweenChain, the spacing-before above was
        // suppressed, so the full top-space must be reserved to keep text clear of
        // the between line drawn by the previous paragraph.
        var hasTopBorder = props.Borders?.Top.IsVisible ?? false;
        var hasBottomBorder = props.Borders?.Bottom.IsVisible ?? false;
        var topSpaceExtra = hasTopBorder || inBetweenChain
            ? inBetweenChain
                ? (float) props.BorderTopSpacePoints
                : Math.Max(0f, (float) props.BorderTopSpacePoints - (float) props.SpacingBeforePoints)
            : 0f;
        var bottomSpaceExtra = hasBottomBorder
            ? Math.Max(0f, (float) props.BorderBottomSpacePoints - (float) props.SpacingAfterPoints)
            : 0f;

        context.CurrentY += topSpaceExtra;
        var paragraphStartY = context.CurrentY;

        var isFirstLine = true;
        foreach (var line in lines)
        {
            var lineHeight = CalculateLineHeight(line, props);

            // Calculate X position based on alignment
            var x = CalculateLineX(line, props);
            var y = context.CurrentY + line.Baseline;

            // Render line number if enabled
            if (showLineNumbers)
            {
                var lineNumber = context.GetNextLineNumber();
                RenderLineNumber(canvas, lineNumber, y, lineNumberSettings!);
            }

            // Render bullet/number on first line
            if (isFirstLine && props.Numbering != null)
            {
                RenderBullet(canvas, props.Numbering, y, paragraph);
                isFirstLine = false;
            }

            // Calculate extra space per gap for justified text
            // Justified alignment distributes extra space between words, except on the last line
            float extraSpacePerGap = 0;
            if (props.Alignment == TextAlignment.Justify && line is {IsLastLine: false, Fragments.Count: > 1})
            {
                var availableWidth = context.ContentWidth - (float)props.LeftIndentPoints - (float)props.RightIndentPoints;
                // First line uses FirstLineIndent, subsequent use HangingIndent
                if (line.IsFirstLine)
                {
                    availableWidth -= (float)props.FirstLineIndentPoints;
                }
                else
                {
                    availableWidth -= (float)props.HangingIndentPoints;
                }

                var extraSpace = availableWidth - line.Width;
                var gapCount = CountWordGaps(line.Fragments);
                if (gapCount > 0 && extraSpace > 0)
                {
                    extraSpacePerGap = extraSpace / gapCount;
                }
            }

            // Render each fragment in the line
            var currentX = x;
            for (var i = 0; i < line.Fragments.Count; i++)
            {
                var fragment = line.Fragments[i];
                RenderFragment(canvas, fragment, currentX, y);
                currentX += fragment.Width;

                // Add extra space after whitespace fragments for justified text
                if (extraSpacePerGap > 0 &&
                    IsWhitespaceFragment(fragment))
                {
                    currentX += extraSpacePerGap;
                }
            }

            // For the last line of a paragraph with compact auto spacing (< 1.0x),
            // ensure the cursor advances by at least the natural line height to prevent
            // overlap with the next paragraph. Compact spacing only compresses the
            // distance between lines within the same paragraph.
            if (line.IsLastLine && props is {LineSpacingRule: LineSpacingRule.Auto, LineSpacingMultiplier: < 1.0})
            {
                lineHeight = Math.Max(lineHeight, line.Height);
            }

            context.CurrentY += lineHeight;
        }

        // Draw paragraph borders if specified. Top sits BorderTopSpace above the text;
        // bottom sits BorderBottomSpace below the last line. When the space exceeds
        // SpacingBefore/After, we reserved the excess above/below to avoid overlap.
        if (props.Borders is {HasAnyBorder: true} borders)
        {
            var borderLeft = context.PointsToPixels(context.ContentLeft + (float) props.LeftIndentPoints - (float) props.BorderLeftSpacePoints);
            var borderRight = context.PointsToPixels(context.ContentLeft + context.ContentWidth - (float) props.RightIndentPoints + (float) props.BorderRightSpacePoints);
            var borderTopY = context.PointsToPixels(paragraphStartY - (float) props.BorderTopSpacePoints);
            var borderBottomY = context.PointsToPixels(context.CurrentY + (float) props.BorderBottomSpacePoints);

            var collapseBottom = nextElement is ParagraphElement nextPara
                && props.BordersCollapseWith(nextPara.Properties);
            var suppressTop = context.SuppressNextParagraphTopBorder;
            context.SuppressNextParagraphTopBorder = false;

            void DrawBorder(BorderEdge edge, PointF start, PointF end)
            {
                var color = ImageSharpRenderContext.ParseColor(edge.ColorHex ?? "000000");
                var pen = context.GetPen(color, context.PointsToPixels((float) edge.WidthPoints));
                canvas.DrawLine(pen, start, end);
            }

            if (borders.Bottom.IsVisible && !collapseBottom)
            {
                DrawBorder(borders.Bottom, new(borderLeft, borderBottomY), new(borderRight, borderBottomY));
            }

            if (borders.Top.IsVisible && !suppressTop)
            {
                DrawBorder(borders.Top, new(borderLeft, borderTopY), new(borderRight, borderTopY));
            }

            if (borders.Left.IsVisible)
            {
                DrawBorder(borders.Left, new(borderLeft, borderTopY), new(borderLeft, borderBottomY));
            }

            if (borders.Right.IsVisible)
            {
                DrawBorder(borders.Right, new(borderRight, borderTopY), new(borderRight, borderBottomY));
            }

            if (collapseBottom)
            {
                DrawBorder(props.BorderBetween, new(borderLeft, borderBottomY), new(borderRight, borderBottomY));
                context.SuppressNextParagraphTopBorder = true;
                // Advance past the between line so the next paragraph's top space starts here.
                context.CurrentY += (float) props.BorderBottomSpacePoints;
            }
        }

        // Add spacing after and track for margin collapsing with next paragraph
        // Contextual spacing removes space between paragraphs to create tighter visual grouping.
        // When this paragraph collapsed its bottom border into a w:between line, suppress
        // spacing-after too so the shared edges visually fuse with the next paragraph.
        var spacingAfter = (float)props.SpacingAfterPoints;
        var collapsedBottom = context.SuppressNextParagraphTopBorder;
        if (!props.ContextualSpacing && !collapsedBottom)
        {
            context.CurrentY += spacingAfter + bottomSpaceExtra;
            context.LastParagraphSpacingAfterPoints = spacingAfter;
        }
        else
        {
            context.LastParagraphSpacingAfterPoints = 0;
        }

        // Bar tabs draw a vertical line at each bar position spanning the full
        // paragraph cell — including spacing-before and spacing-after — so
        // consecutive bar-tab paragraphs render as a continuous vertical bar.
        DrawBarTabs(canvas, props, barTabStartY, context.CurrentY - barTabStartY);

        // Track contextual spacing state for next paragraph
        context.LastParagraphHadContextualSpacing = props.ContextualSpacing;
        context.LastParagraphStyleId = props.StyleId;
    }

    /// <summary>
    /// Renders a paragraph at a specific position with a specific width (for floating text boxes).
    /// </summary>
    public void RenderParagraphInBounds(DrawingCanvas canvas, ParagraphElement paragraph, float startX, float width)
    {
        var props = paragraph.Properties;

        // Layout uses the cell width directly. LayoutParagraphWithWidth already subtracts
        // LeftIndentPoints internally so a bullet paragraph wraps within the indented region;
        // the bullet itself is drawn into the hanging area below.
        var lines = LayoutParagraphWithWidth(paragraph, width);

        // Contextual spacing: collapse before-spacing when this paragraph and the previous
        // one share a style and both opt in. Same logic as the body path.
        var sameStyle = props.StyleId != null && props.StyleId == context.LastParagraphStyleId;
        var collapseSpacingBefore = props.ContextualSpacing &&
                                    context.LastParagraphHadContextualSpacing && sameStyle;
        if (!collapseSpacingBefore)
        {
            context.CurrentY += (float)props.SpacingBeforePoints;
        }

        var isFirstLine = true;
        foreach (var line in lines)
        {
            var lineHeight = CalculateLineHeight(line, props);

            var x = CalculateLineXInBounds(line, props, startX, width);
            var y = context.CurrentY + line.Baseline;

            // Render bullet/number on first line
            if (isFirstLine && props.Numbering != null)
            {
                RenderBulletInBounds(canvas, props.Numbering, y, paragraph, startX);
                isFirstLine = false;
            }

            // Calculate extra space per gap for justified text
            float extraSpacePerGap = 0;
            var effectiveWidth = width - (float)props.LeftIndentPoints;
            if (props.Alignment == TextAlignment.Justify && line is {IsLastLine: false, Fragments.Count: > 1})
            {
                if (line.IsFirstLine)
                {
                    effectiveWidth -= (float)props.FirstLineIndentPoints;
                }

                var extraSpace = effectiveWidth - line.Width;
                var gapCount = CountWordGaps(line.Fragments);
                if (gapCount > 0 && extraSpace > 0)
                {
                    extraSpacePerGap = extraSpace / gapCount;
                }
            }

            // Render each fragment in the line
            var currentX = x;
            for (var i = 0; i < line.Fragments.Count; i++)
            {
                var fragment = line.Fragments[i];
                RenderFragment(canvas, fragment, currentX, y);
                currentX += fragment.Width;

                // Add extra space after whitespace fragments for justified text
                if (extraSpacePerGap > 0 && IsWhitespaceFragment(fragment))
                {
                    currentX += extraSpacePerGap;
                }
            }

            if (line.IsLastLine && props is {LineSpacingRule: LineSpacingRule.Auto, LineSpacingMultiplier: < 1.0})
            {
                lineHeight = Math.Max(lineHeight, line.Height);
            }

            context.CurrentY += lineHeight;
        }

        // Add spacing after (but not for empty paragraphs which are typically just visual spacers).
        // Contextual spacing suppresses spacing-after; the next same-style paragraph picks up the cue.
        var isEmpty = IsParagraphEmpty(paragraph);
        if (!isEmpty && !props.ContextualSpacing)
        {
            context.CurrentY += (float)props.SpacingAfterPoints;
        }

        context.LastParagraphHadContextualSpacing = props.ContextualSpacing;
        context.LastParagraphStyleId = props.StyleId;
    }

    static float CalculateLineXInBounds(TextLine line, ParagraphProperties props, float startX, float width)
    {
        var contentLeft = startX + (float)props.LeftIndentPoints;
        var availableWidth = width - (float)props.LeftIndentPoints - (float)props.RightIndentPoints;

        return props.Alignment switch
        {
            TextAlignment.Center => contentLeft + (availableWidth - line.Width) / 2,
            TextAlignment.Right => contentLeft + availableWidth - line.Width,
            _ => contentLeft + (line.IsFirstLine ? (float)props.FirstLineIndentPoints : 0)
        };
    }

    List<TextLine> LayoutParagraphWithWidth(ParagraphElement paragraph, float maxWidth)
    {
        var key = (paragraph, maxWidth);
        if (boundedLayoutCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var result = LayoutParagraphWithWidthCore(paragraph, maxWidth);
        boundedLayoutCache[key] = result;
        return result;
    }

    List<TextLine> LayoutParagraphWithWidthCore(ParagraphElement paragraph, float maxWidth)
    {
        var lines = new List<TextLine>();
        var props = paragraph.Properties;
        var runs = DropCapsExpander.Expand(SmallCapsExpander.Expand(paragraph.Runs), paragraph.Properties);

        var adjustedMaxWidth = maxWidth - (float)props.LeftIndentPoints - (float)props.RightIndentPoints;
        float currentLineWidth = 0;
        float maxLineHeight = 0;
        float maxBaseline = 0;
        var currentFragments = new List<TextFragment>();
        var isFirstLine = true;

        var firstLineIndent = (float)props.FirstLineIndentPoints;
        var effectiveWidth = adjustedMaxWidth - (isFirstLine ? firstLineIndent : 0);
        var hasDecimalTabStop = props.HasDecimalTabStop();

        for (var runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];

            // Tab snap: emit a tab-filler fragment that advances the cursor to the next tab stop.
            if (run.IsTab)
            {
                var followingWidth = MeasureFollowingWidth(runs, runIndex + 1);
                var leftIndentPts = (float)props.LeftIndentPoints;
                var cursorAbs = leftIndentPts + currentLineWidth;
                double? decimalPrefix = hasDecimalTabStop
                    ? MeasureFollowingDecimalPrefix(runs, runIndex + 1)
                    : null;
                var (destinationAbs, matchedStop, suppressFollowing) = TabStopResolver.Resolve(
                    cursorAbs, followingWidth,
                    props.TabStops, props.DefaultTabStopPoints, leftIndentPts,
                    decimalPrefix,
                    leftIndentPts + effectiveWidth);
                var gap = (float)(destinationAbs - cursorAbs);
                if (gap <= 0 || currentLineWidth + gap > effectiveWidth)
                {
                    if (suppressFollowing)
                    {
                        runIndex = SkipFollowingTabContent(runs, runIndex);
                    }
                    continue;
                }

                var tabFont = context.GetFont(run.Properties);
                var (tabRunHeight, tabBaseline) = ImageSharpRenderContext.GetFontMetrics(tabFont);

                currentFragments.Add(
                    new()
                {
                    Text = "",
                    Width = gap,
                    Properties = run.Properties,
                    IsTabFiller = true,
                    TabLeader = matchedStop?.Leader ?? TabLeader.None
                });
                currentLineWidth += gap;
                maxLineHeight = Math.Max(maxLineHeight, tabRunHeight);
                maxBaseline = Math.Max(maxBaseline, tabBaseline);
                if (suppressFollowing)
                {
                    runIndex = SkipFollowingTabContent(runs, runIndex);
                }
                continue;
            }

            // Handle inline images / shape groups — treat as a single "word" in the text flow.
            if (run.InlineImageData is {Length: > 0} || run.InlineShapeGroup != null)
            {
                var imageWidth = (float)run.InlineImageWidthPoints;
                var imageHeight = (float)run.InlineImageHeightPoints;

                // Check if we need to wrap before the image
                if (currentLineWidth + imageWidth > effectiveWidth && currentFragments.Count > 0)
                {
                    // Finish current line
                    lines.Add(
                        new()
                    {
                        Fragments = [..currentFragments],
                        Width = currentLineWidth,
                        Height = maxLineHeight,
                        Baseline = maxBaseline,
                        IsFirstLine = isFirstLine
                    });
                    currentFragments.Clear();
                    currentLineWidth = 0;
                    maxLineHeight = 0;
                    maxBaseline = 0;
                    isFirstLine = false;
                    effectiveWidth = adjustedMaxWidth;
                }

                // Add inline image fragment
                currentFragments.Add(
                    new()
                {
                    Text = "",
                    Width = imageWidth,
                    Properties = run.Properties,
                    InlineImageData = run.InlineImageData,
                    InlineImageHeightPoints = imageHeight,
                    InlineImageContentType = run.InlineImageContentType,
                    InlineImageRasterFallbackData = run.InlineImageRasterFallbackData,
                    InlineImageRasterFallbackContentType = run.InlineImageRasterFallbackContentType,
                    InlineImageRotationDegrees = run.InlineImageRotationDegrees,
                    InlineImageCrop = run.InlineImageCrop,
                    InlineShapeGroup = run.InlineShapeGroup
                });
                currentLineWidth += imageWidth;
                maxLineHeight = Math.Max(maxLineHeight, imageHeight);
                // The baseline needs to be at least the image height so the image doesn't overlap content above
                // Image bottom aligns with baseline, so baseline must be >= imageHeight
                maxBaseline = Math.Max(maxBaseline, imageHeight);
                continue;
            }

            // Apply AllCaps text transform if specified
            var text = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            var words = SplitIntoWords(text);
            var font = context.GetFont(run.Properties);
            var (runHeight, baseline) = ImageSharpRenderContext.GetFontMetrics(font);

            foreach (var word in words)
            {
                // Handle explicit line break (newline character)
                if (word is "\n" or "\r\n" or "\r")
                {
                    // Force a line break - finish current line
                    if (currentFragments.Count > 0)
                    {
                        lines.Add(
                            new()
                            {
                                Fragments = [..currentFragments],
                                Width = currentLineWidth,
                                Height = maxLineHeight,
                                Baseline = maxBaseline,
                                IsFirstLine = isFirstLine
                            });
                    }
                    else
                    {
                        // Empty line - still add it with font metrics
                        lines.Add(
                            new()
                            {
                                Fragments = [],
                                Width = 0,
                                Height = runHeight,
                                Baseline = baseline,
                                IsFirstLine = isFirstLine
                            });
                    }

                    // Start new line
                    currentFragments.Clear();
                    currentLineWidth = 0;
                    maxLineHeight = 0;
                    maxBaseline = 0;
                    isFirstLine = false;
                    effectiveWidth = adjustedMaxWidth;
                    continue;
                }

                // Measure word width in points, including character spacing
                var wordWidth = ImageSharpRenderContext.MeasureText(font, word, ResolveKerningMode(run.Properties))
                                + (float) (run.Properties.CharacterSpacingPoints * word.Length);

                // Check if we need to wrap
                if (currentLineWidth + wordWidth > effectiveWidth && currentFragments.Count > 0)
                {
                    // Finish current line
                    lines.Add(
                        new()
                        {
                            Fragments = [..currentFragments],
                            Width = currentLineWidth,
                            Height = maxLineHeight,
                            Baseline = maxBaseline,
                            IsFirstLine = isFirstLine
                        });
                    currentFragments.Clear();
                    currentLineWidth = 0;
                    maxLineHeight = 0;
                    maxBaseline = 0;
                    isFirstLine = false;
                    effectiveWidth = adjustedMaxWidth;
                }

                // Add word to current line
                currentFragments.Add(
                    new()
                    {
                        Text = word,
                        Width = wordWidth,
                        Properties = run.Properties
                    });
                currentLineWidth += wordWidth;
                maxLineHeight = Math.Max(maxLineHeight, runHeight);
                maxBaseline = Math.Max(maxBaseline, baseline);
            }
        }

        // Add final line if not empty
        if (currentFragments.Count > 0)
        {
            lines.Add(
                new()
            {
                Fragments = [..currentFragments],
                Width = currentLineWidth,
                Height = maxLineHeight,
                Baseline = maxBaseline,
                IsFirstLine = isFirstLine,
                IsLastLine = true  // This is the last line
            });
        }

        // Handle empty paragraph - use font metrics from runs or paragraph mark font size
        if (lines.Count == 0 && !paragraph.IsAnchorOnlyMark)
        {
            // Fallback default
            float emptyHeight = 12;
            float emptyBaseline = 10;

            if (paragraph.Runs.Count > 0)
            {
                var firstRun = paragraph.Runs[0];
                var font = context.GetFont(firstRun.Properties);
                (emptyHeight, emptyBaseline) = ImageSharpRenderContext.GetFontMetrics(font);
            }
            else if (props.ParagraphMarkFontSizePoints.HasValue)
            {
                // Use paragraph mark font size for empty paragraphs
                emptyHeight = (float) props.ParagraphMarkFontSizePoints.Value * 1.2f;
                emptyBaseline = (float) props.ParagraphMarkFontSizePoints.Value;
            }

            lines.Add(
                new()
                {
                    Fragments = [],
                    Width = 0,
                    Height = emptyHeight,
                    Baseline = emptyBaseline,
                    IsFirstLine = true,
                    IsLastLine = true
                });
        }

        // Mark the last line if we have lines (in case final line wasn't added above)
        if (lines.Count > 0 && !lines[^1].IsLastLine)
        {
            var lastLine = lines[^1];
            lines[^1] = lastLine with { IsLastLine = true };
        }

        return lines;
    }

    /// <summary>
    /// Renders a line number in the left margin.
    /// </summary>
    void RenderLineNumber(DrawingCanvas canvas, int lineNumber, float baselineY, LineNumberSettings settings)
    {
        // Only show line numbers at the countBy interval
        var adjustedNumber = lineNumber - settings.Start;
        if (adjustedNumber % settings.CountBy != 0)
        {
            return;
        }

        // Position the line number in the left margin
        var x = context.ContentLeft - (float)settings.DistancePoints;
        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(baselineY);

        // Use the configured default font for line numbers (9pt, same as typical Word default)
        var props = new RunProperties { FontFamily = DefaultFontSettings.DefaultFont, FontSizePoints = 9 };
        var font = context.GetFont(props);
        var (_, baseline) = ImageSharpRenderContext.GetFontMetrics(font);

        var numberText = lineNumber.ToString();

        // Measure text width so we can right-align it
        var textWidth = ImageSharpRenderContext.MeasureText(font, numberText) * context.Scale;

        var textOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            Origin = new PointF(pixelX - textWidth, pixelY - baseline * context.Scale)
        };
        canvas.DrawText(textOptions, numberText, context.GetBrush(Color.Black));
    }

    /// <summary>
    /// Renders a bullet or number for a list item.
    /// </summary>
    void RenderBullet(DrawingCanvas canvas, NumberingInfo numbering, float baselineY, ParagraphElement paragraph)
    {
        // Position bullet at the cascaded paragraph indent (the parser resolves direct,
        // numbering-level and style <w:ind> per Word's precedence), not the raw numbering value.
        var bulletX = context.ContentLeft + (float)paragraph.Properties.LeftIndentPoints - (float)paragraph.Properties.HangingIndentPoints;
        var pixelX = context.PointsToPixels(bulletX);
        var pixelY = context.PointsToPixels(baselineY);

        // Bullets declared in Symbol/Wingdings need the embedded "Morph Bullets"
        // subset (Linux/macOS don't ship those proprietary faces). Bullets that
        // are already plain Unicode (e.g. <w:lvlText w:val="•"/> with no rFonts)
        // render in the paragraph's own font - that's what Word does, and the
        // paragraph font's bullet glyph is what the user actually sees in Word.
        var bulletProps = ResolveBulletRunProperties(numbering, paragraph);
        var font = context.GetFont(bulletProps);
        var (_, baseline) = ImageSharpRenderContext.GetFontMetrics(font);

        var colorHex = bulletProps.ColorHex;
        var color = colorHex != null ? ImageSharpRenderContext.ParseColor(colorHex) : Color.Black;

        var textOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            Origin = new PointF(pixelX, pixelY - baseline * context.Scale)
        };
        canvas.DrawText(textOptions, numbering.Text, context.GetBrush(color));
    }

    static RunProperties ResolveBulletRunProperties(NumberingInfo numbering, ParagraphElement paragraph)
    {
        var paragraphProps = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Properties : new();
        if (IsProprietaryBulletFont(numbering.FontFamily))
        {
            return new()
            {
                FontFamily = "Morph Bullets",
                FontSizePoints = paragraphProps.FontSizePoints,
                ColorHex = paragraphProps.ColorHex
            };
        }
        return new()
        {
            FontFamily = paragraphProps.FontFamily,
            FontSizePoints = paragraphProps.FontSizePoints,
            Bold = paragraphProps.Bold,
            Italic = paragraphProps.Italic,
            ColorHex = paragraphProps.ColorHex
        };
    }

    static bool IsProprietaryBulletFont(string? fontFamily) =>
        fontFamily != null &&
        (fontFamily.StartsWith("Symbol", StringComparison.OrdinalIgnoreCase) ||
         fontFamily.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Renders a bullet or number for a list item within specific bounds (for table cells).
    /// </summary>
    void RenderBulletInBounds(DrawingCanvas canvas, NumberingInfo numbering, float baselineY, ParagraphElement paragraph, float startX)
    {
        // See RenderBullet for why this picks Morph Bullets vs the paragraph font.
        var bulletProps = ResolveBulletRunProperties(numbering, paragraph);
        var font = context.GetFont(bulletProps);
        var (_, baseline) = ImageSharpRenderContext.GetFontMetrics(font);

        var colorHex = bulletProps.ColorHex;
        var color = colorHex != null ? ImageSharpRenderContext.ParseColor(colorHex) : Color.Black;

        // Bullet sits at the cascaded paragraph indent (= LeftIndent - HangingIndent),
        // so the gap between bullet and text matches the style's hanging indent.
        var bulletX = startX + (float)paragraph.Properties.LeftIndentPoints - (float)paragraph.Properties.HangingIndentPoints;
        var pixelX = context.PointsToPixels(bulletX);
        var pixelY = context.PointsToPixels(baselineY);

        var textOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            Origin = new PointF(pixelX, pixelY - baseline * context.Scale)
        };
        canvas.DrawText(textOptions, numbering.Text, context.GetBrush(color));
    }

    float CalculateLineX(TextLine line, ParagraphProperties props)
    {
        var contentLeft = context.ContentLeft + (float)props.LeftIndentPoints;
        var availableWidth = context.ContentWidth - (float)props.LeftIndentPoints - (float)props.RightIndentPoints;

        // For hanging indent: first line at Left+FirstLineIndent, subsequent at Left+Hanging
        // For regular first line indent: first line at Left+FirstLineIndent, subsequent at Left
        var firstLineOffset = (float)props.FirstLineIndentPoints;
        var subsequentOffset = (float)props.HangingIndentPoints;

        // RTL paragraphs flip "leading-edge" alignment to the page's right edge (no BiDi reorder).
        var alignment = props is {IsRightToLeft: true, Alignment: TextAlignment.Left}
            ? TextAlignment.Right
            : props.Alignment;

        return alignment switch
        {
            TextAlignment.Center => contentLeft + (availableWidth - line.Width) / 2,
            TextAlignment.Right => contentLeft + availableWidth - line.Width,
            _ => line.IsFirstLine
                ? contentLeft + firstLineOffset
                : contentLeft + subsequentOffset
        };
    }

    /// <summary>
    /// Counts the number of word gaps (spaces) in a line for justified text distribution.
    /// </summary>
    static int CountWordGaps(List<TextFragment> fragments)
    {
        var count = 0;
        foreach (var fragment in fragments)
        {
            if (IsWhitespaceFragment(fragment))
            {
                count++;
            }
        }
        return count;
    }

    static bool IsParagraphEmpty(ParagraphElement paragraph)
    {
        if (paragraph.Runs.Count == 0)
        {
            return true;
        }

        foreach (var run in paragraph.Runs)
        {
            if (run.IsTab)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(run.Text) || run.InlineImageData != null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a fragment is whitespace (space between words).
    /// </summary>
    static bool IsWhitespaceFragment(TextFragment fragment)
    {
        var text = fragment.Text;
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch))
            {
                return false;
            }
        }

        return true;
    }

    void RenderFragment(DrawingCanvas canvas, TextFragment fragment, float x, float y)
    {
        // Handle inline images
        if (fragment.InlineImageData is {Length: > 0})
        {
            RenderInlineImage(canvas, fragment, x, y);
            return;
        }

        if (fragment.InlineShapeGroup is { } group)
        {
            RenderInlineShapeGroup(canvas, group, fragment, x, y);
            return;
        }

        if (fragment.IsTabFiller)
        {
            RenderTabFiller(canvas, fragment, x, y);
            return;
        }

        var font = context.GetFont(fragment.Properties);
        var color = ImageSharpRenderContext.ParseColor(fragment.Properties.ColorHex);

        // Convert to pixels
        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);

        // Adjust Y position for subscript/superscript
        // Superscript: raise by approximately 35% of the original font size
        // Subscript: lower by approximately 15% of the original font size
        if (fragment.Properties.VerticalAlignment == VerticalRunAlignment.Superscript)
        {
            var originalFontSize = (float)fragment.Properties.FontSizePoints * context.Scale;
            pixelY -= originalFontSize * 0.35f;
        }
        else if (fragment.Properties.VerticalAlignment == VerticalRunAlignment.Subscript)
        {
            var originalFontSize = (float)fragment.Properties.FontSizePoints * context.Scale;
            pixelY += originalFontSize * 0.15f;
        }

        // w:position — additive baseline shift in points. Positive raises the glyph,
        // negative lowers it. Stacks on top of vertAlign for combined shifts.
        if (fragment.Properties.BaselineShiftPoints != 0)
        {
            pixelY -= context.PointsToPixels((float) fragment.Properties.BaselineShiftPoints);
        }

        // Draw background/shading color if specified
        if (!string.IsNullOrEmpty(fragment.Properties.BackgroundColorHex))
        {
            var bgColor = ImageSharpRenderContext.ParseColor(fragment.Properties.BackgroundColorHex);

            var textWidth = context.PointsToPixels(fragment.Width);
            var (runHeight, runBaseline) = ImageSharpRenderContext.GetFontMetrics(font);
            // Top of text: baseline position minus ascent (in pixels)
            var textTop = pixelY - runBaseline * context.Scale;
            var textBottom = pixelY + (runHeight - runBaseline) * context.Scale;

            canvas.Fill(context.GetBrush(bgColor), new RectangleF(pixelX, textTop, textWidth, textBottom - textTop));
        }

        // Get baseline for coordinate conversion (Skia uses baseline Y, ImageSharp uses top-left Y)
        var (_, baseline) = ImageSharpRenderContext.GetFontMetrics(font);

        var textOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            Origin = new PointF(pixelX, pixelY - baseline * context.Scale),
            KerningMode = ResolveKerningMode(fragment.Properties)
        };

        DrawTextEffectsBehind(canvas, fragment, textOptions, font, pixelX, pixelY, baseline);

        // w:emboss / w:imprint — paint a tonal companion glyph offset from the main glyph
        // so the run reads as raised (emboss) or engraved (imprint). The offset scales with
        // font size so the effect stays visible at large display sizes (a fixed 1px is
        // invisible against 72pt Impact). Companion uses fixed light/dark grey matched to
        // a white background.
        if (fragment.Properties.Emboss)
        {
            var offset = Math.Max(1f, font.Size * context.Scale * 0.04f);
            var lightOptions = new RichTextOptions(font)
            {
                Dpi = context.Dpi,
                Origin = new PointF(pixelX + offset, pixelY - baseline * context.Scale + offset),
                KerningMode = textOptions.KerningMode
            };
            canvas.DrawText(lightOptions, fragment.Text, context.GetBrush(Color.White));
        }
        else if (fragment.Properties.Imprint)
        {
            var offset = Math.Max(1f, font.Size * context.Scale * 0.04f);
            var darkOptions = new RichTextOptions(font)
            {
                Dpi = context.Dpi,
                Origin = new PointF(pixelX - offset, pixelY - baseline * context.Scale - offset),
                KerningMode = textOptions.KerningMode
            };
            canvas.DrawText(darkOptions, fragment.Text, context.GetBrush(Color.Gray));
        }

        // w:outline — render the glyph as a stroke instead of a fill.
        if (fragment.Properties.OutlineOnly)
        {
            var strokePen = context.GetPen(color, Math.Max(0.5f, context.Scale * 0.5f));
            canvas.DrawText(textOptions, fragment.Text, strokePen);
        }
        else
        {
            canvas.DrawText(textOptions, fragment.Text, context.GetBrush(color));
        }

        // w:bdr — per-run border drawn around the text box. Doesn't reserve space, so
        // adjacent runs may sit close to the rectangle's edge — matches Word.
        if (fragment.Properties.Border is {IsVisible: true} runBdr)
        {
            var (runHeight2, runBaseline2) = ImageSharpRenderContext.GetFontMetrics(font);
            var bdrTop = pixelY - runBaseline2 * context.Scale;
            var bdrBottom = pixelY + (runHeight2 - runBaseline2) * context.Scale;
            var bdrWidth = context.PointsToPixels(fragment.Width);
            var bdrColor = ImageSharpRenderContext.ParseColor(runBdr.ColorHex);
            var bdrPen = context.GetPen(bdrColor, Math.Max(0.5f, (float) runBdr.WidthPoints * context.Scale));
            canvas.Draw(bdrPen, new RectangleF(pixelX, bdrTop, bdrWidth, bdrBottom - bdrTop));
        }

        if (fragment.Properties.Outline is { } outline)
        {
            var outlineColor = ImageSharpRenderContext.ParseColor(outline.ColorHex);
            var pen = context.GetPen(outlineColor, Math.Max(0.5f, (float) outline.WidthPoints * context.Scale));
            canvas.DrawText(textOptions, fragment.Text, pen);
        }

        // Draw underline if needed
        if (fragment.Properties.Underline)
        {
            var underlineY = pixelY + 2 * context.Scale;
            var width = context.PointsToPixels(fragment.Width);
            var strokeWidth = 1 * context.Scale;
            canvas.DrawLine(context.GetPen(color, strokeWidth), new PointF(pixelX, underlineY), new PointF(pixelX + width, underlineY));
        }

        // Draw strikethrough if needed
        if (fragment.Properties.Strikethrough)
        {
            // ImageSharp Font.Size is in points (unlike Skia's SKFont.Size which is in
            // pixels), so the offset must be scaled by points-to-pixels.
            var strikeY = pixelY - font.Size * 0.3f * context.Scale;
            var width = context.PointsToPixels(fragment.Width);
            var strokeWidth = 1 * context.Scale;
            canvas.DrawLine(context.GetPen(color, strokeWidth), new PointF(pixelX, strikeY), new PointF(pixelX + width, strikeY));
        }
    }

    static KerningMode ResolveKerningMode(RunProperties props) => TextShaping.ResolveKerningMode(props);

    void DrawBarTabs(DrawingCanvas canvas, ParagraphProperties props, float lineTopY, float lineHeight)
    {
        if (props.TabStops.Count == 0)
        {
            return;
        }

        var pen = context.GetPen(Color.Black, Math.Max(1, 0.5f * context.Scale));
        var top = context.PointsToPixels(lineTopY);
        var bottom = context.PointsToPixels(lineTopY + lineHeight);

        foreach (var stop in props.TabStops)
        {
            if (stop.Alignment != TabAlignment.Bar)
            {
                continue;
            }

            var x = context.PointsToPixels(context.ContentLeft + (float) stop.PositionPoints);
            canvas.DrawLine(pen, new PointF(x, top), new PointF(x, bottom));
        }
    }

    /// <summary>
    /// Draws shadow / glow / reflection underneath the text. Outline is drawn over the fill
    /// in <see cref="RenderFragment"/>. ImageSharp's drawing pipeline doesn't expose a per-draw
    /// blur filter, so blur radii are approximated by drawing the effect at multiple offsets.
    /// </summary>
    void DrawTextEffectsBehind(DrawingCanvas canvas, TextFragment fragment, RichTextOptions baseOptions,
        Font font, float pixelX, float pixelY, float baseline)
    {
        var props = fragment.Properties;

        if (props.Shadow is { } shadow)
        {
            var dirRad = shadow.DirectionDegrees * Math.PI / 180.0;
            var offsetX = (float) (Math.Cos(dirRad) * shadow.DistancePoints) * context.Scale;
            var offsetY = (float) (Math.Sin(dirRad) * shadow.DistancePoints) * context.Scale;

            var rgba = ImageSharpRenderContext.ParseColor(shadow.ColorHex).ToPixel<Rgba32>();
            var alpha = (byte) Math.Clamp(shadow.AlphaPercent * 255 / 100, 0, 255);
            var shadowColor = Color.FromPixel(new Rgba32(rgba.R, rgba.G, rgba.B, alpha));

            var shadowOptions = new RichTextOptions(font)
            {
                Dpi = context.Dpi,
                Origin = new PointF(pixelX + offsetX, pixelY - baseline * context.Scale + offsetY)
            };
            canvas.DrawText(shadowOptions, fragment.Text, new SolidBrush(shadowColor));
        }

        if (props.Glow is { } glow)
        {
            // No blur filter — approximate the halo by stroking at increasing radii with low alpha.
            var rgba = ImageSharpRenderContext.ParseColor(glow.ColorHex).ToPixel<Rgba32>();
            var maxRadius = Math.Max(1f, (float) glow.RadiusPoints * context.Scale);
            var rings = Math.Max(2, (int) maxRadius);
            for (var i = 1; i <= rings; i++)
            {
                var r = maxRadius * i / rings;
                var ringAlpha = (byte) Math.Clamp(glow.AlphaPercent * 255 / 100 / rings, 0, 255);
                var ringColor = Color.FromPixel(new Rgba32(rgba.R, rgba.G, rgba.B, ringAlpha));
                var pen = new SolidPen(ringColor, r);
                canvas.DrawText(baseOptions, fragment.Text, pen);
            }
        }

        if (props.HasReflection)
        {
            // Approximate Word's "tight reflection" preset by drawing a single mirrored copy at
            // half opacity directly below the baseline. A full alpha gradient would require a
            // custom processor in ImageSharp; the half-opacity shortcut is visually close.
            var fillColor = ImageSharpRenderContext.ParseColor(props.ColorHex).ToPixel<Rgba32>();
            var reflectionAlpha = (byte) (fillColor.A * 60 / 100);
            var reflectionColor = Color.FromPixel(new Rgba32(fillColor.R, fillColor.G, fillColor.B, reflectionAlpha));

            var ascent = baseline * context.Scale;
            var descent = font.FontMetrics.VerticalMetrics.Descender / (float) font.FontMetrics.UnitsPerEm * font.Size;

            var reflectOptions = new RichTextOptions(font)
            {
                Dpi = context.Dpi,
                Origin = new PointF(pixelX, pixelY - baseline * context.Scale),
                // Vertical scaling -1 mirrors around the origin's Y; the origin is the glyph top.
                // Translate down by 2*ascent so the mirrored top sits at the original baseline.
                TextRuns = []
            };
            // ImageSharp doesn't expose per-draw vertical-flip transforms cleanly, so the reflection
            // approximation falls back to a faded duplicate drawn directly below the original — close
            // enough for presence-aware rendering.
            var ghostOptions = new RichTextOptions(font)
            {
                Dpi = context.Dpi,
                Origin = new PointF(pixelX, pixelY - baseline * context.Scale + ascent + descent)
            };
            _ = reflectOptions;
            canvas.DrawText(ghostOptions, fragment.Text, new SolidBrush(reflectionColor));
        }
    }

    void RenderTabFiller(DrawingCanvas canvas, TextFragment fragment, float x, float y)
    {
        if (fragment.Width <= 0 || fragment.TabLeader == TabLeader.None)
        {
            return;
        }

        var color = ImageSharpRenderContext.ParseColor(fragment.Properties.ColorHex);
        var pixelY = context.PointsToPixels(y);
        var pixelStartX = context.PointsToPixels(x);
        var pixelEndX = context.PointsToPixels(x + fragment.Width);

        if (fragment.TabLeader == TabLeader.Underscore)
        {
            var strokeWidth = Math.Max(1f, (float)fragment.Properties.FontSizePoints * context.Scale * 0.07f);
            canvas.DrawLine(
                context.GetPen(color, strokeWidth),
                new PointF(pixelStartX, pixelY),
                new PointF(pixelEndX, pixelY));
            return;
        }

        var leaderChar = fragment.TabLeader switch
        {
            TabLeader.Dot => '.',
            TabLeader.Hyphen => '-',
            TabLeader.MiddleDot => '·',
            TabLeader.Heavy => '—',
            _ => '.'
        };

        var font = context.GetFont(fragment.Properties);
        var glyphWidthPoints = ImageSharpRenderContext.MeasureText(font, leaderChar.ToString());
        if (glyphWidthPoints <= 0)
        {
            return;
        }

        // Leave roughly one glyph of trailing padding before the snapped text begins.
        var availablePoints = fragment.Width - glyphWidthPoints;
        if (availablePoints <= 0)
        {
            return;
        }

        var count = (int)Math.Floor(availablePoints / glyphWidthPoints);
        if (count <= 0)
        {
            return;
        }

        var leaderText = new string(leaderChar, count);
        var (_, baseline) = ImageSharpRenderContext.GetFontMetrics(font);
        var textOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            Origin = new PointF(pixelStartX, pixelY - baseline * context.Scale)
        };
        canvas.DrawText(textOptions, leaderText, context.GetBrush(color));
    }

    void RenderInlineShapeGroup(DrawingCanvas pageCanvas, InlineShapeGroup group, TextFragment fragment, float x, float y)
    {
        var pixelX = context.PointsToPixels(x);
        var pixelWidth = context.PointsToPixels(fragment.Width);
        var pixelHeight = context.PointsToPixels(fragment.InlineImageHeightPoints);
        var pixelY = context.PointsToPixels(y) - pixelHeight;

        var sx = pixelWidth / (float) group.ChildExtentX;
        var sy = pixelHeight / (float) group.ChildExtentY;

        // Most icon-style header arrows are 90° group rotations. Push a geometry-space
        // rotation around the group centre and draw shapes at their original positions; the
        // canvas transform handles the rotation. Replaces the previous temp-image route.
        var hasRotation = group.RotationDegrees != 0;
        if (hasRotation)
        {
            var centerX = pixelX + pixelWidth / 2f;
            var centerY = pixelY + pixelHeight / 2f;
            pageCanvas.Save(BuildRotation((float) (group.RotationDegrees * Math.PI / 180.0), centerX, centerY));
        }

        foreach (var shape in group.Shapes)
        {
            var x1 = pixelX + (float) shape.X * sx;
            var y1 = pixelY + (float) shape.Y * sy;
            var w = (float) shape.Width * sx;
            var h = (float) shape.Height * sy;

            if (shape.Geometry == GroupShapeGeometry.Line)
            {
                var startX = x1;
                var startY = y1;
                var endX = x1 + w;
                var endY = y1 + h;
                if (shape.FlipVertical)
                {
                    (startY, endY) = (endY, startY);
                }
                if (shape.FlipHorizontal)
                {
                    (startX, endX) = (endX, startX);
                }

                var color = ImageSharpRenderContext.ParseColor(shape.ColorHex);
                var width = (float) (shape.LineWidthEmu > 0 ? shape.LineWidthEmu / emusPerPoint : 0.75) * context.Scale;
                // Square end caps make the perpendicular connector lines extend half-stroke-width
                // past their endpoints — that's how Word's icon-style arrows form a clean L corner
                // (each line's square cap fills the gap where a butt/round cap would leave a notch).
                var pen = new SolidPen(new PenOptions(color, width)
                {
                    StrokeOptions = new()
                    {
                        LineCap = LineCap.Square,
                        LineJoin = LineJoin.Bevel
                    }
                });
                pageCanvas.DrawLine(pen, new PointF(startX, startY), new PointF(endX, endY));
            }
            else
            {
                if (shape.FillColorHex is { } fillHex)
                {
                    var fill = ImageSharpRenderContext.ParseColor(fillHex);
                    pageCanvas.Fill(context.GetBrush(fill), new RectangleF(x1, y1, w, h));
                }
                if (shape.LineWidthEmu > 0)
                {
                    var color = ImageSharpRenderContext.ParseColor(shape.ColorHex);
                    var width = (float) (shape.LineWidthEmu / emusPerPoint) * context.Scale;
                    var pen = context.GetPen(color, width);
                    pageCanvas.Draw(pen, new RectangleF(x1, y1, w, h));
                }
            }
        }

        if (hasRotation)
        {
            pageCanvas.Restore();
        }
    }

    static DrawingOptions BuildRotation(float radians, float pivotX, float pivotY) =>
        new()
        {
            Transform = new(Matrix3x2.CreateRotation(radians, new(pivotX, pivotY)))
        };

    // EMU = English Metric Units. 1 point = 12700 EMU.
    const float emusPerPoint = 12700f;

    void RenderInlineImage(DrawingCanvas canvas, TextFragment fragment, float x, float y)
    {
        // Convert to pixels - y is the baseline, need to adjust for image height
        var pixelX = context.PointsToPixels(x);
        var pixelWidth = context.PointsToPixels(fragment.Width);
        var pixelHeight = context.PointsToPixels(fragment.InlineImageHeightPoints);
        // Position image so its bottom aligns with the baseline
        var pixelY = context.PointsToPixels(y) - pixelHeight;

        var imageBytes = fragment.InlineImageData;
        if (fragment.InlineImageContentType == "image/svg+xml")
        {
            // SVG isn't supported here; use the raster fallback the parser kept from
            // the primary <a:blip>, or skip if we don't have one.
            if (fragment.InlineImageRasterFallbackData == null)
            {
                return;
            }

            imageBytes = fragment.InlineImageRasterFallbackData;
        }

        // Render bitmap image
        try
        {
            var img = Image.Load<Rgba32>(imageBytes!);
            context.RetainForPage(img);

            if (fragment.InlineImageCrop is { IsCropped: true } crop)
            {
                var srcLeft = (int) (crop.Left * img.Width);
                var srcTop = (int) (crop.Top * img.Height);
                var srcWidth = Math.Max(1, img.Width - srcLeft - (int) (crop.Right * img.Width));
                var srcHeight = Math.Max(1, img.Height - srcTop - (int) (crop.Bottom * img.Height));
                img.Mutate(_ => _.Crop(new(srcLeft, srcTop, srcWidth, srcHeight)));
            }

            img.Mutate(_ => _.Resize(new Size((int)pixelWidth, (int)pixelHeight)));
            var rotation = (float) fragment.InlineImageRotationDegrees;
            if (rotation == 0)
            {
                canvas.DrawImage(img, new((int) pixelX, (int) pixelY));
            }
            else
            {
                img.Mutate(_ => _.Rotate(rotation));
                // After rotation the image's bounding box grew; recentre over the original location.
                var newX = pixelX + pixelWidth / 2 - img.Width / 2f;
                var newY = pixelY + pixelHeight / 2 - img.Height / 2f;
                canvas.DrawImage(img, new((int) newX, (int) newY));
            }
        }
        catch
        {
            // Skip images that fail to decode
        }
    }

    /// <summary>
    /// Layouts paragraph text into lines with word wrapping.
    /// </summary>
    List<TextLine> LayoutParagraph(ParagraphElement paragraph)
    {
        if (pagedLayoutCache.TryGetValue(paragraph, out var cached))
        {
            return cached;
        }
        var result = LayoutParagraphCore(paragraph);
        pagedLayoutCache[paragraph] = result;
        return result;
    }

    List<TextLine> LayoutParagraphCore(ParagraphElement paragraph)
    {
        var lines = new List<TextLine>();
        var props = paragraph.Properties;
        var runs = DropCapsExpander.Expand(SmallCapsExpander.Expand(paragraph.Runs), paragraph.Properties);

        // Base width accounts for left and right indents
        var baseWidth = context.ContentWidth - (float)props.LeftIndentPoints - (float)props.RightIndentPoints;
        float currentLineWidth = 0;
        float maxLineHeight = 0;
        float maxBaseline = 0;
        var currentFragments = new List<TextFragment>();
        var isFirstLine = true;

        // First line: offset by FirstLineIndent (positive = indent right)
        // Subsequent lines: offset by HangingIndent (positive = indent right)
        var firstLineOffset = (float)props.FirstLineIndentPoints;
        var subsequentOffset = (float)props.HangingIndentPoints;
        var effectiveWidth = baseWidth - (isFirstLine ? firstLineOffset : subsequentOffset);
        var hasDecimalTabStop = props.HasDecimalTabStop();

        for (var runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];

            // Tab snap: emit a tab-filler fragment that advances the cursor to the next tab stop.
            if (run.IsTab)
            {
                // LayoutParagraph applies FontWidthScale to word widths, so the following-text width
                // measurement that feeds the tab-stop resolver must apply the same scale — otherwise
                // a Right tab snaps to a destination that's just-too-tight, and the page-number
                // word wraps to the next line (TOC dot-leader case).
                var followingWidth = MeasureFollowingWidth(runs, runIndex + 1, applyFontWidthScale: true);
                var leftIndentPts = (float) props.LeftIndentPoints;
                var cursorAbs = leftIndentPts + currentLineWidth;
                double? decimalPrefix = hasDecimalTabStop
                    ? MeasureFollowingDecimalPrefix(runs, runIndex + 1, applyFontWidthScale: true)
                    : null;
                var (destinationAbs, matchedStop, suppressFollowing) = TabStopResolver.Resolve(
                    cursorAbs, followingWidth,
                    props.TabStops, props.DefaultTabStopPoints, leftIndentPts,
                    decimalPrefix,
                    leftIndentPts + effectiveWidth);
                var gap = (float) (destinationAbs - cursorAbs);
                if (gap <= 0 || currentLineWidth + gap > effectiveWidth)
                {
                    if (suppressFollowing)
                    {
                        runIndex = SkipFollowingTabContent(runs, runIndex);
                    }
                    continue;
                }

                var tabFont = context.GetFont(run.Properties);
                var (tabRunHeight, tabBaseline) = ImageSharpRenderContext.GetFontMetrics(tabFont);

                currentFragments.Add(
                    new()
                    {
                        Text = "",
                        Width = gap,
                        Properties = run.Properties,
                        IsTabFiller = true,
                        TabLeader = matchedStop?.Leader ?? TabLeader.None
                    });
                currentLineWidth += gap;
                maxLineHeight = Math.Max(maxLineHeight, tabRunHeight);
                maxBaseline = Math.Max(maxBaseline, tabBaseline);
                if (suppressFollowing)
                {
                    runIndex = SkipFollowingTabContent(runs, runIndex);
                }
                continue;
            }

            // Handle inline images - treat as a single "word" in the text flow
            if (run.InlineImageData is {Length: > 0} || run.InlineShapeGroup != null)
            {
                var imageWidth = (float) run.InlineImageWidthPoints;
                var imageHeight = (float) run.InlineImageHeightPoints;

                // Check if we need to wrap before the image
                if (currentLineWidth + imageWidth > effectiveWidth && currentFragments.Count > 0)
                {
                    // Finish current line
                    var finalizedFragments = FinalizeLine(currentFragments);
                    lines.Add(
                        new()
                        {
                            Fragments = finalizedFragments,
                            Width = currentLineWidth,
                            Height = maxLineHeight,
                            Baseline = maxBaseline,
                            IsFirstLine = isFirstLine
                        });
                    currentFragments.Clear();
                    currentLineWidth = 0;
                    maxLineHeight = 0;
                    maxBaseline = 0;
                    isFirstLine = false;
                    effectiveWidth = baseWidth - subsequentOffset;
                }

                // Add inline image fragment
                currentFragments.Add(
                    new()
                    {
                        Text = "",
                        Width = imageWidth,
                        Properties = run.Properties,
                        InlineImageData = run.InlineImageData,
                        InlineImageHeightPoints = imageHeight,
                        InlineImageContentType = run.InlineImageContentType,
                        InlineImageRasterFallbackData = run.InlineImageRasterFallbackData,
                        InlineImageRasterFallbackContentType = run.InlineImageRasterFallbackContentType,
                        InlineImageRotationDegrees = run.InlineImageRotationDegrees,
                        InlineImageCrop = run.InlineImageCrop,
                        InlineShapeGroup = run.InlineShapeGroup
                    });
                currentLineWidth += imageWidth;
                maxLineHeight = Math.Max(maxLineHeight, imageHeight);
                // The baseline needs to be at least the image height so the image doesn't overlap content above
                // Image bottom aligns with baseline, so baseline must be >= imageHeight
                maxBaseline = Math.Max(maxBaseline, imageHeight);
                continue;
            }

            // Apply AllCaps text transform if specified
            var text = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            var words = SplitIntoWords(text);
            var font = context.GetFont(run.Properties);
            var (runHeight, runBaseline) = ImageSharpRenderContext.GetFontMetrics(font);

            foreach (var word in words)
            {
                // Handle explicit line break (newline character)
                if (word is "\n" or "\r\n" or "\r")
                {
                    // Force a line break - finish current line
                    if (currentFragments.Count > 0)
                    {
                        var finalizedFragments = FinalizeLine(currentFragments);
                        lines.Add(
                            new()
                            {
                                Fragments = finalizedFragments,
                                Width = currentLineWidth,
                                Height = maxLineHeight,
                                Baseline = maxBaseline,
                                IsFirstLine = isFirstLine
                            });
                    }
                    else
                    {
                        // Empty line - still add it with font metrics
                        lines.Add(
                            new()
                            {
                                Fragments = [],
                                Width = 0,
                                Height = runHeight,
                                Baseline = runBaseline,
                                IsFirstLine = isFirstLine
                            });
                    }

                    // Start new line
                    currentFragments.Clear();
                    currentLineWidth = 0;
                    maxLineHeight = 0;
                    maxBaseline = 0;
                    isFirstLine = false;
                    effectiveWidth = baseWidth - subsequentOffset;
                    continue;
                }

                // Check if word ends with soft hyphen
                var hasSoftHyphen = word.EndsWith(softHyphen);
                var displayWord = hasSoftHyphen ? word.TrimEnd(softHyphen) : word;

                // Measure the display word (without soft hyphen)
                // Apply FontWidthScale and character spacing to better match Word's text rendering
                var wordWidth = ImageSharpRenderContext.MeasureText(font, displayWord, ResolveKerningMode(run.Properties)) * context.FontWidthScale
                                + (float) (run.Properties.CharacterSpacingPoints * displayWord.Length);

                // Check if we need to wrap to a new line
                if (currentLineWidth + wordWidth > effectiveWidth && currentFragments.Count > 0)
                {
                    // Finish current line - convert any trailing soft hyphens to visible hyphens
                    var finalizedFragments = FinalizeLine(currentFragments);
                    lines.Add(
                        new()
                        {
                            Fragments = finalizedFragments,
                            Width = currentLineWidth,
                            Height = maxLineHeight,
                            Baseline = maxBaseline,
                            IsFirstLine = isFirstLine
                        });

                    // Start new line
                    currentFragments.Clear();
                    currentLineWidth = 0;
                    maxLineHeight = 0;
                    maxBaseline = 0;
                    isFirstLine = false;
                    effectiveWidth = baseWidth - subsequentOffset;
                }

                // Add word to current line (keep soft hyphen marker for now)
                currentFragments.Add(
                    new()
                    {
                        Text = hasSoftHyphen ? displayWord + softHyphen : displayWord,
                        Width = wordWidth,
                        Properties = run.Properties
                    });

                currentLineWidth += wordWidth;
                maxLineHeight = Math.Max(maxLineHeight, runHeight);
                maxBaseline = Math.Max(maxBaseline, runBaseline);
            }
        }

        // Add final line if there's content (remove trailing soft hyphens, they're not at a break)
        if (currentFragments.Count > 0)
        {
            var finalizedFragments = RemoveSoftHyphens(currentFragments);
            lines.Add(
                new()
                {
                    Fragments = finalizedFragments,
                    Width = currentLineWidth,
                    Height = maxLineHeight,
                    Baseline = maxBaseline,
                    IsFirstLine = isFirstLine,
                    IsLastLine = true // This is the last line with content
                });
        }

        // Handle empty paragraph - use font metrics from runs if available
        if (lines.Count == 0 && !paragraph.IsAnchorOnlyMark)
        {
            // Fallback default
            float emptyHeight = 12;
            float emptyBaseline = 10;

            // Get height from first run's font if available
            if (paragraph.Runs.Count > 0)
            {
                var firstRun = paragraph.Runs[0];
                var font = context.GetFont(firstRun.Properties);
                (emptyHeight, emptyBaseline) = ImageSharpRenderContext.GetFontMetrics(font);
            }
            else if (props.ParagraphMarkFontSizePoints.HasValue)
            {
                // Use paragraph mark font size for empty paragraphs
                // Approximate height based on font size (typical ascent + descent ratio)
                emptyHeight = (float) props.ParagraphMarkFontSizePoints.Value * 1.2f;
                emptyBaseline = (float) props.ParagraphMarkFontSizePoints.Value;
            }

            lines.Add(
                new()
                {
                    Fragments = [],
                    Width = 0,
                    Height = emptyHeight,
                    Baseline = emptyBaseline,
                    IsFirstLine = true,
                    IsLastLine = true
                });
        }

        // Mark last line (may have been set during final line add, but ensure it's set)
        if (lines.Count > 0)
        {
            var lastLine = lines[^1];
            if (!lastLine.IsLastLine)
            {
                lines[^1] = lastLine with { IsLastLine = true };
            }
        }

        return lines;
    }

    /// <summary>
    /// Finalizes a line by converting trailing soft hyphens to visible hyphens.
    /// </summary>
    static List<TextFragment> FinalizeLine(List<TextFragment> fragments)
    {
        var result = new List<TextFragment>();
        for (var i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i];
            if (i == fragments.Count - 1 && fragment.Text.EndsWith(softHyphen))
            {
                // Last fragment ends with soft hyphen - convert to visible hyphen
                result.Add(
                    new()
                    {
                        Text = fragment.Text.TrimEnd(softHyphen) + "-",
                        Width = fragment.Width, // Width was already measured without soft hyphen
                        Properties = fragment.Properties
                    });
            }
            else if (fragment.Text.Contains(softHyphen))
            {
                // Remove soft hyphen if not at end of line
                result.Add(
                    new()
                    {
                        Text = fragment.Text.Replace(softHyphenString, ""),
                        Width = fragment.Width,
                        Properties = fragment.Properties
                    });
            }
            else
            {
                result.Add(fragment);
            }
        }

        return result;
    }

    /// <summary>
    /// Removes all soft hyphens from fragments (used for final line).
    /// </summary>
    // Unicode characters for hyphenation
    const char softHyphen = '\u00AD';
    const string softHyphenString = "\u00AD";

    /// <summary>
    /// Measures text widths for runs following a tab, up to the next tab or line break.
    /// </summary>
    // Width of the following text up to (but not including) the first '.' character. Returns null
    // when no '.' is present — resolver then treats the Decimal stop as Right alignment.
    float? MeasureFollowingDecimalPrefix(IReadOnlyList<Run> runs, int startRunIndex, bool applyFontWidthScale = false)
    {
        var scale = applyFontWidthScale ? context.FontWidthScale : 1f;
        float total = 0;
        for (var i = startRunIndex; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run.IsTab || run.InlineImageData is {Length: > 0})
            {
                break;
            }

            if (run.Text.Contains('\n') || run.Text.Contains('\r'))
            {
                break;
            }

            var text = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            var font = context.GetFont(run.Properties);
            var dotIndex = text.IndexOf('.');
            if (dotIndex >= 0)
            {
                var prefix = text[..dotIndex];
                total += ImageSharpRenderContext.MeasureText(font, prefix, ResolveKerningMode(run.Properties)) * scale
                         + (float)(run.Properties.CharacterSpacingPoints * prefix.Length);
                return total;
            }

            total += ImageSharpRenderContext.MeasureText(font, text, ResolveKerningMode(run.Properties)) * scale
                     + (float)(run.Properties.CharacterSpacingPoints * text.Length);
        }

        return null;
    }

    /// <summary>
    /// Returns the index of the last run consumed when suppressing post-tab content. The caller
    /// is in a <c>for</c> loop that increments runIndex, so we return the index of the final
    /// run to drop — the loop's own increment then lands on the next-tab boundary.
    /// </summary>
    static int SkipFollowingTabContent(IReadOnlyList<Run> runs, int tabRunIndex)
    {
        var lastConsumed = tabRunIndex;
        for (var i = tabRunIndex + 1; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run.IsTab)
            {
                break;
            }

            if (!string.IsNullOrEmpty(run.Text) && (run.Text.Contains('\n') || run.Text.Contains('\r')))
            {
                break;
            }

            lastConsumed = i;
        }

        return lastConsumed;
    }

    float MeasureFollowingWidth(IReadOnlyList<Run> runs, int startRunIndex, bool applyFontWidthScale = false)
    {
        var scale = applyFontWidthScale ? context.FontWidthScale : 1f;
        float total = 0;
        for (var i = startRunIndex; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run.IsTab)
            {
                break;
            }

            if (run.InlineImageData is {Length: > 0})
            {
                total += (float)run.InlineImageWidthPoints;
                continue;
            }

            if (run.Text.Contains('\n') || run.Text.Contains('\r'))
            {
                break;
            }

            var text = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            var font = context.GetFont(run.Properties);
            total += ImageSharpRenderContext.MeasureText(font, text, ResolveKerningMode(run.Properties)) * scale
                     + (float)(run.Properties.CharacterSpacingPoints * text.Length);
        }

        return total;
    }

    static List<TextFragment> RemoveSoftHyphens(List<TextFragment> fragments)
    {
        var result = new List<TextFragment>(fragments.Count);
        foreach (var fragment in fragments)
        {
            if (fragment.Text.Contains(softHyphen))
            {
                result.Add(
                    new()
                    {
                        Text = fragment.Text.Replace(softHyphenString, ""),
                        Width = fragment.Width,
                        Properties = fragment.Properties
                    });
            }
            else
            {
                result.Add(fragment);
            }
        }

        return result;
    }

    const char nonBreakingHyphen = '\u2011';

    static List<string> SplitIntoWords(string text)
    {
        if (text.Contains(nonBreakingHyphen))
        {
            text = text.Replace(nonBreakingHyphen, '-');
        }

        var words = new List<string>();
        var wordStart = -1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (wordStart >= 0)
                {
                    words.Add(text[wordStart..i]);
                    wordStart = -1;
                }

                words.Add(text[i..(i + 1)]);
            }
            else if (c == softHyphen)
            {
                if (wordStart >= 0)
                {
                    words.Add(text[wordStart..(i + 1)]);
                    wordStart = -1;
                }
            }
            else
            {
                if (wordStart < 0)
                {
                    wordStart = i;
                }
            }
        }

        if (wordStart >= 0)
        {
            words.Add(text[wordStart..]);
        }

        return words;
    }
}

sealed record TextLine
{
    public required List<TextFragment> Fragments { get; init; }
    public required float Width { get; init; }
    public required float Height { get; init; }
    public required float Baseline { get; init; }
    public required bool IsFirstLine { get; init; }
    public bool IsLastLine { get; init; }
}

sealed class TextFragment
{
    public required string Text { get; init; }
    public required float Width { get; init; }
    public required RunProperties Properties { get; init; }

    /// <summary>Inline image data (when this fragment represents an inline image).</summary>
    public byte[]? InlineImageData { get; init; }

    /// <summary>Height of inline image in points.</summary>
    public float InlineImageHeightPoints { get; init; }

    /// <summary>Content type of inline image (e.g., "image/png", "image/svg+xml").</summary>
    public string? InlineImageContentType { get; init; }

    /// <summary>Raster fallback bytes used when <see cref="InlineImageContentType"/> is SVG
    /// and the backend can't render SVG.</summary>
    public byte[]? InlineImageRasterFallbackData { get; init; }

    /// <summary>Content type for <see cref="InlineImageRasterFallbackData"/>.</summary>
    public string? InlineImageRasterFallbackContentType { get; init; }

    /// <summary>Inline image rotation in degrees (clockwise).</summary>
    public double InlineImageRotationDegrees { get; init; }

    /// <summary>Inline image source-rectangle crop. Null = no crop.</summary>
    public ImageCrop? InlineImageCrop { get; init; }

    /// <summary>Inline shape-group payload (wpg:wgp) — mutually exclusive with <see cref="InlineImageData"/>.</summary>
    public InlineShapeGroup? InlineShapeGroup { get; init; }

    /// <summary>True when this fragment represents a tab-stop gap (leader glyphs or empty spacer).</summary>
    public bool IsTabFiller { get; init; }

    /// <summary>Leader character to tile across a tab-filler fragment.</summary>
    public TabLeader TabLeader { get; init; } = TabLeader.None;
}
