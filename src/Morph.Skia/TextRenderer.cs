/// <summary>
/// Renders text content with formatting using SkiaSharp.
/// </summary>
sealed class TextRenderer(SkiaRenderContext context) :
    IParagraphMeasurer
{
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
            totalHeight += CalculateLineHeight(line.Height, props);
        }

        // Add spacing after (collapsed if this paragraph has contextual spacing)
        if (!props.ContextualSpacing)
        {
            totalHeight += (float)props.SpacingAfterPoints;
        }

        // Border space: Word draws borders inside the spacing regions when possible,
        // only expanding the paragraph height when w:space exceeds available spacing.
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
            totalHeight += CalculateLineHeight(line.Height, props);
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
        var linePitch = (float)context.PageSettings.DocumentGridLinePitchPoints;
        // docGrid linePitch only applies to empty paragraphs in cells: Word uses linePitch
        // as the default line slot for the end-of-cell paragraph mark, but ignores it for
        // paragraphs that contain text (the actual font/lineSpacing controls those).
        var applyLinePitch = linePitch > 0 &&
            props.LineSpacingRule != LineSpacingRule.Exactly &&
            paragraph.Runs.Count == 0;

        foreach (var line in lines)
        {
            var lineHeight = TableLayout.CalculateCompactLineHeight(line.Height, props);
            if (applyLinePitch)
            {
                lineHeight = Math.Max(lineHeight, linePitch);
            }

            lineHeights.Add(lineHeight);
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
    float CalculateLineHeight(float naturalHeight, ParagraphProperties props)
    {
        var lineHeight = props.LineSpacingRule switch
        {
            LineSpacingRule.Exactly => (float)props.LineSpacingPoints,
            LineSpacingRule.AtLeast => Math.Max(naturalHeight, (float)props.LineSpacingPoints),
            _ => naturalHeight * (float)props.LineSpacingMultiplier // Auto
        };

        // Word compatibility: Word's "single line spacing" (multiplier 1.0) uses approximately
        // 120% of the font size, while font metrics (Ascent + Descent) often give ~111-117%.
        // Apply a graduated correction factor for Auto mode to match Word's line spacing behavior.
        // Only apply for multipliers >= 0.9 to respect intentionally compact spacing (e.g., 70% spacing for decorative lines).
        if (props is {LineSpacingRule: LineSpacingRule.Auto, LineSpacingMultiplier: >= 0.9 and <= 1.15})
        {
            // Graduated boost: ~12.5% for 0.9, ~7.5% for 1.0, ~3.5% for 1.08, 0% for 1.15+
            var boost = 1.0f + 0.50f * (1.15f - (float)props.LineSpacingMultiplier);
            lineHeight *= Math.Max(1.0f, boost);
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

    /// <summary>
    /// Renders a paragraph to the canvas at the current position.
    /// </summary>
    public void RenderParagraph(SKCanvas canvas, ParagraphElement paragraph, DocumentElement? nextElement = null)
    {
        var lines = LayoutParagraph(paragraph);
        var props = paragraph.Properties;
        var lineNumberSettings = context.PageSettings.LineNumbers;
        var showLineNumbers = lineNumberSettings != null && !props.SuppressLineNumbers;

        // Add spacing before with margin collapsing (similar to CSS)
        // When two paragraphs are adjacent, use max(SpacingAfter, SpacingBefore) instead of sum
        // Contextual spacing only collapses spacing between paragraphs of the SAME STYLE
        var sameStyle = props.StyleId != null && props.StyleId == context.LastParagraphStyleId;
        var collapseSpacingBefore = props.ContextualSpacing && context.LastParagraphHadContextualSpacing && sameStyle;
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
                paragraphHeight += CalculateLineHeight(line.Height, props);
            }

            var bgColor = SKColor.TryParse(props.BackgroundColorHex, out var parsedBgColor)
                ? parsedBgColor
                : SKColor.Parse("#" + props.BackgroundColorHex);

            var bgX = context.PointsToPixels(context.ContentLeft + (float)props.LeftIndentPoints);
            var bgY = context.PointsToPixels(context.CurrentY);
            var bgWidth = context.PointsToPixels(context.ContentWidth - (float)props.LeftIndentPoints - (float)props.RightIndentPoints);
            var bgHeight = context.PointsToPixels(paragraphHeight);

            using var bgPaint = new SKPaint
            {
                Color = bgColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(bgX, bgY, bgWidth, bgHeight, bgPaint);
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

        // Reserve "excess" top space so the border sits clear of the previous paragraph.
        context.CurrentY += topSpaceExtra;
        var paragraphStartY = context.CurrentY;

        var isFirstLine = true;
        foreach (var line in lines)
        {
            var lineHeight = CalculateLineHeight(line.Height, props);

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
            if (props.Alignment == TextAlignment.Justify &&
                line is
                {
                    IsLastLine: false,
                    Fragments.Count: > 1
                })
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
            foreach (var fragment in line.Fragments)
            {
                RenderFragment(canvas, fragment, currentX, y);
                currentX += fragment.Width;

                // Add extra space after whitespace fragments for justified text
                if (extraSpacePerGap > 0 && IsWhitespaceFragment(fragment))
                {
                    currentX += extraSpacePerGap;
                }
            }

            // Bar tabs draw a vertical line at each bar position on every line of the paragraph,
            // independent of any explicit <w:tab/> character.
            DrawBarTabs(canvas, props, context.CurrentY, lineHeight);

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

        // Draw paragraph borders if specified. Top border sits at paragraphStartY
        // (shifted up by the requested top space); bottom border sits at contentEnd
        // plus the full bottom space — if the space exceeds SpacingAfter, we already
        // reserved the excess as bottomSpaceExtra below, so the border still lands
        // inside the paragraph's reserved region.
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

            static SKPaint CreatePaint(BorderEdge edge, float strokeWidth) => new()
            {
                Color = SKColor.Parse("#" + (edge.ColorHex ?? "000000")),
                StrokeWidth = strokeWidth,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };

            if (borders.Bottom.IsVisible && !collapseBottom)
            {
                using var paint = CreatePaint(borders.Bottom, context.PointsToPixels((float) borders.Bottom.WidthPoints));
                canvas.DrawLine(borderLeft, borderBottomY, borderRight, borderBottomY, paint);
            }

            if (borders.Top.IsVisible && !suppressTop)
            {
                using var paint = CreatePaint(borders.Top, context.PointsToPixels((float) borders.Top.WidthPoints));
                canvas.DrawLine(borderLeft, borderTopY, borderRight, borderTopY, paint);
            }

            if (borders.Left.IsVisible)
            {
                using var paint = CreatePaint(borders.Left, context.PointsToPixels((float) borders.Left.WidthPoints));
                canvas.DrawLine(borderLeft, borderTopY, borderLeft, borderBottomY, paint);
            }

            if (borders.Right.IsVisible)
            {
                using var paint = CreatePaint(borders.Right, context.PointsToPixels((float) borders.Right.WidthPoints));
                canvas.DrawLine(borderRight, borderTopY, borderRight, borderBottomY, paint);
            }

            if (collapseBottom)
            {
                using var paint = CreatePaint(props.BorderBetween, context.PointsToPixels((float) props.BorderBetween.WidthPoints));
                canvas.DrawLine(borderLeft, borderBottomY, borderRight, borderBottomY, paint);
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
        if (props.ContextualSpacing ||
            collapsedBottom)
        {
            context.LastParagraphSpacingAfterPoints = 0;
        }
        else
        {
            context.CurrentY += spacingAfter + bottomSpaceExtra;
            context.LastParagraphSpacingAfterPoints = spacingAfter;
        }

        // Track contextual spacing state for next paragraph
        context.LastParagraphHadContextualSpacing = props.ContextualSpacing;
        context.LastParagraphStyleId = props.StyleId;
    }

    /// <summary>
    /// Renders a paragraph at a specific position with a specific width (for floating text boxes).
    /// </summary>
    public void RenderParagraphInBounds(SKCanvas canvas, ParagraphElement paragraph, float startX, float width)
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
            var lineHeight = CalculateLineHeight(line.Height, props);

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
            if (props.Alignment == TextAlignment.Justify &&
                line is {IsLastLine: false, Fragments.Count: > 1})
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
            foreach (var fragment in line.Fragments)
            {
                RenderFragment(canvas, fragment, currentX, y);
                currentX += fragment.Width;

                // Add extra space after whitespace fragments for justified text
                if (extraSpacePerGap > 0 && IsWhitespaceFragment(fragment))
                {
                    currentX += extraSpacePerGap;
                }
            }

            if (line.IsLastLine &&
                props is
                {
                    LineSpacingRule: LineSpacingRule.Auto,
                    LineSpacingMultiplier: < 1.0
                })
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

        for (var runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];

            // Tab snap: emit a tab-filler fragment that advances the cursor to the next tab stop.
            if (run.IsTab)
            {
                var followingWidth = MeasureFollowingWidthNoScale(runs, runIndex + 1);
                var leftIndentPts = (float) props.LeftIndentPoints;
                var cursorAbs = leftIndentPts + currentLineWidth;
                double? decimalPrefix = props.TabStops.Any(_ => _.Alignment == TabAlignment.Decimal)
                    ? MeasureFollowingDecimalPrefixNoScale(runs, runIndex + 1)
                    : null;
                var (destinationAbs, matchedStop) = TabStopResolver.Resolve(
                    cursorAbs, followingWidth,
                    props.TabStops, props.DefaultTabStopPoints, leftIndentPts,
                    decimalPrefix,
                    leftIndentPts + effectiveWidth);
                var gap = (float) (destinationAbs - cursorAbs);
                if (gap <= 0 || currentLineWidth + gap > effectiveWidth)
                {
                    continue;
                }

                using var tabFont = context.CreateFont(run.Properties);
                var tabMetrics = tabFont.Metrics;
                var tabRunHeight = (-tabMetrics.Ascent + tabMetrics.Descent) / context.Scale;
                var tabBaseline = -tabMetrics.Ascent / context.Scale;

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
                continue;
            }

            // Handle inline images / shape groups — treat as a single "word" in the text flow.
            if (run.InlineImageData is {Length: > 0} || run.InlineShapeGroup != null)
            {
                var imageWidth = (float) run.InlineImageWidthPoints;
                var imageHeight = (float) run.InlineImageHeightPoints;

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
            using var font = context.CreateFont(run.Properties);
            var metrics = font.Metrics;
            var runHeight = (-metrics.Ascent + metrics.Descent) / context.Scale;
            var baseline = -metrics.Ascent / context.Scale;

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

                // Convert pixel measurements back to points, including character spacing
                var wordWidth = font.MeasureText(word) / context.Scale
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
                    IsLastLine = true // This is the last line
                });
        }

        // Handle empty paragraph - use font metrics from runs or paragraph mark font size
        if (lines.Count == 0)
        {
            // Fallback default
            float emptyHeight = 12;
            float emptyBaseline = 10;

            if (paragraph.Runs.Count > 0)
            {
                var firstRun = paragraph.Runs[0];
                using var font = context.CreateFont(firstRun.Properties);
                var metrics = font.Metrics;
                emptyHeight = (-metrics.Ascent + metrics.Descent) / context.Scale;
                emptyBaseline = -metrics.Ascent / context.Scale;
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
        if (lines.Count > 0 &&
            !lines[^1].IsLastLine)
        {
            var lastLine = lines[^1];
            lines[^1] = lastLine with { IsLastLine = true };
        }

        return lines;
    }

    /// <summary>
    /// Renders a line number in the left margin.
    /// </summary>
    void RenderLineNumber(SKCanvas canvas, int lineNumber, float baselineY, LineNumberSettings settings)
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
        var typeface = context.GetTypeface(DefaultFontSettings.DefaultFont, false, false);
        using var font = context.CreateFontFromTypeface(typeface, 9);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black
        };

        var numberText = lineNumber.ToString();
        canvas.DrawText(numberText, pixelX, pixelY, SKTextAlign.Right, font, paint);
    }

    /// <summary>
    /// Renders a bullet or number for a list item.
    /// </summary>
    void RenderBullet(SKCanvas canvas, NumberingInfo numbering, float baselineY, ParagraphElement paragraph)
    {
        // Position bullet at the cascaded paragraph indent (style's <w:ind> wins over the
        // numbering level's, per the OOXML cascade), not the raw numbering value.
        var bulletX = context.ContentLeft + (float)paragraph.Properties.LeftIndentPoints - (float)paragraph.Properties.HangingIndentPoints;
        var pixelX = context.PointsToPixels(bulletX);
        var pixelY = context.PointsToPixels(baselineY);

        // Default to the paragraph's first run for size/colour/family.
        float fontSize = 11;
        string? colorHex = null;
        var fontFamily = DefaultFontSettings.DefaultFont;
        var bold = false;
        var italic = false;
        if (paragraph.Runs.Count > 0)
        {
            var props = paragraph.Runs[0].Properties;
            fontSize = (float)props.FontSizePoints;
            colorHex = props.ColorHex;
            fontFamily = props.FontFamily;
            bold = props.Bold;
            italic = props.Italic;
        }

        // Bullets declared in Symbol/Wingdings need the embedded "Morph Bullets"
        // subset (Linux/macOS don't ship those proprietary faces). Bullets that
        // are already plain Unicode (e.g. <w:lvlText w:val="•"/> with no rFonts)
        // render in the paragraph's own font - that's what Word does, and the
        // paragraph font's bullet glyph is what the user actually sees in Word.
        var typeface = ResolveBulletTypeface(numbering.FontFamily, fontFamily, bold, italic);
        using var font = context.CreateFontFromTypeface(typeface, fontSize);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = colorHex != null ? SKColor.Parse("#" + colorHex) : SKColors.Black
        };

        canvas.DrawText(numbering.Text, pixelX, pixelY, SKTextAlign.Left, font, paint);
    }

    SKTypeface ResolveBulletTypeface(string? bulletFontFamily, string paragraphFontFamily, bool bold, bool italic)
    {
        if (IsProprietaryBulletFont(bulletFontFamily))
        {
            return context.GetTypeface("Morph Bullets", bold: false, italic: false);
        }
        return context.GetTypeface(paragraphFontFamily, bold, italic);
    }

    static bool IsProprietaryBulletFont(string? fontFamily) =>
        fontFamily != null &&
        (fontFamily.StartsWith("Symbol", StringComparison.OrdinalIgnoreCase) ||
         fontFamily.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Renders a bullet or number for a list item within specific bounds (for table cells).
    /// </summary>
    void RenderBulletInBounds(SKCanvas canvas, NumberingInfo numbering, float baselineY, ParagraphElement paragraph, float startX)
    {
        float fontSize = 11;
        string? colorHex = null;
        var fontFamily = DefaultFontSettings.DefaultFont;
        var bold = false;
        var italic = false;
        if (paragraph.Runs.Count > 0)
        {
            var props = paragraph.Runs[0].Properties;
            fontSize = (float)props.FontSizePoints;
            colorHex = props.ColorHex;
            fontFamily = props.FontFamily;
            bold = props.Bold;
            italic = props.Italic;
        }

        // See RenderBullet for why this picks Morph Bullets vs the paragraph font.
        var typeface = ResolveBulletTypeface(numbering.FontFamily, fontFamily, bold, italic);
        using var font = context.CreateFontFromTypeface(typeface, fontSize);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = colorHex != null ? SKColor.Parse("#" + colorHex) : SKColors.Black
        };

        // Bullet sits at the cascaded paragraph indent (= LeftIndent - HangingIndent),
        // so the gap between bullet and text matches the style's hanging indent.
        var bulletX = startX + (float)paragraph.Properties.LeftIndentPoints - (float)paragraph.Properties.HangingIndentPoints;
        var pixelX = context.PointsToPixels(bulletX);
        var pixelY = context.PointsToPixels(baselineY);

        canvas.DrawText(numbering.Text, pixelX, pixelY, SKTextAlign.Left, font, paint);
    }

    float CalculateLineX(TextLine line, ParagraphProperties props)
    {
        var contentLeft = context.ContentLeft + (float)props.LeftIndentPoints;
        var availableWidth = context.ContentWidth - (float)props.LeftIndentPoints - (float)props.RightIndentPoints;

        // For hanging indent: first line at Left+FirstLineIndent, subsequent at Left+Hanging
        // For regular first line indent: first line at Left+FirstLineIndent, subsequent at Left
        var firstLineOffset = (float)props.FirstLineIndentPoints;
        var subsequentOffset = (float)props.HangingIndentPoints;

        // RTL paragraphs flip the visual meaning of "leading-edge" alignment to the page's right
        // edge. We don't reorder glyphs (no BiDi shaper) but at least the line lands on the right.
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

        foreach (var r in paragraph.Runs)
        {
            if (r.IsTab)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(r.Text) || r.InlineImageData != null)
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

        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return true;
    }

    void RenderFragment(SKCanvas canvas, TextFragment fragment, float x, float y)
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

        using var font = context.CreateFont(fragment.Properties);
        using var paint = SkiaRenderContext.CreateTextPaint(fragment.Properties);

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
            var bgColor = SKColor.TryParse(fragment.Properties.BackgroundColorHex, out var parsedBgColor)
                ? parsedBgColor
                : SKColor.Parse("#" + fragment.Properties.BackgroundColorHex);

            var textWidth = context.PointsToPixels(fragment.Width);
            var metrics = font.Metrics;
            // Ascent is negative
            var textTop = pixelY + metrics.Ascent;
            var textBottom = pixelY + metrics.Descent;

            using var bgPaint = new SKPaint
            {
                Color = bgColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(pixelX, textTop, textWidth, textBottom - textTop, bgPaint);
        }

        // Effects drawn behind the main glyph fill: shadow, glow, reflection.
        DrawTextEffectsBehind(canvas, fragment, font, pixelX, pixelY);

        // w:emboss / w:imprint — drop a tonal companion glyph one device pixel away from
        // the main glyph so the text reads as raised (emboss) or engraved (imprint).
        // Approximation: the companion uses a fixed light/dark grey rather than per-page
        // blending; this matches Word's default look on white backgrounds.
        if (fragment.Properties.Emboss)
        {
            using var lightPaint = new SKPaint
            {
                Color = new(0xFF, 0xFF, 0xFF),
                IsAntialias = true
            };
            canvas.DrawText(fragment.Text, pixelX + 1, pixelY + 1, SKTextAlign.Left, font, lightPaint);
        }
        else if (fragment.Properties.Imprint)
        {
            using var darkPaint = new SKPaint
            {
                Color = new(0x80, 0x80, 0x80),
                IsAntialias = true
            };
            canvas.DrawText(fragment.Text, pixelX - 1, pixelY - 1, SKTextAlign.Left, font, darkPaint);
        }

        // w:outline — render glyphs as stroke-only (no fill). Falls back to the main paint
        // colour with stroke style; otherwise use the normal filled glyph.
        if (fragment.Properties.OutlineOnly)
        {
            using var strokePaint = new SKPaint
            {
                Color = paint.Color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(0.5f, context.Scale * 0.5f),
                IsAntialias = true
            };
            canvas.DrawText(fragment.Text, pixelX, pixelY, SKTextAlign.Left, font, strokePaint);
        }
        else
        {
            canvas.DrawText(fragment.Text, pixelX, pixelY, SKTextAlign.Left, font, paint);
        }

        // w:bdr — per-run border drawn around the text box (ascent..descent vertically,
        // fragment width horizontally). Doesn't reserve space, so adjacent runs may sit
        // close to the rectangle's edge — matches Word's behaviour for inline run borders.
        if (fragment.Properties.Border is {IsVisible: true} runBdr)
        {
            var metrics = font.Metrics;
            var bdrTop = pixelY + metrics.Ascent;
            var bdrBottom = pixelY + metrics.Descent;
            var bdrWidth = context.PointsToPixels(fragment.Width);
            using var bdrPaint = new SKPaint
            {
                Color = SkiaRenderContext.ParseColor(runBdr.ColorHex),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(0.5f, (float) runBdr.WidthPoints * context.Scale),
                IsAntialias = true
            };
            canvas.DrawRect(pixelX, bdrTop, bdrWidth, bdrBottom - bdrTop, bdrPaint);
        }

        // Outline stroked over the fill so the stroke is crisp on top.
        if (fragment.Properties.Outline is { } outline)
        {
            using var outlinePaint = new SKPaint
            {
                Color = SkiaRenderContext.ParseColor(outline.ColorHex),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(0.5f, (float) outline.WidthPoints * context.Scale),
                IsAntialias = true
            };
            canvas.DrawText(fragment.Text, pixelX, pixelY, SKTextAlign.Left, font, outlinePaint);
        }

        // Draw underline if needed
        if (fragment.Properties.Underline)
        {
            var underlineY = pixelY + 2 * context.Scale;
            var width = context.PointsToPixels(fragment.Width);
            using var linePaint = new SKPaint
            {
                Color = paint.Color,
                StrokeWidth = 1 * context.Scale,
                IsAntialias = true
            };
            canvas.DrawLine(pixelX, underlineY, pixelX + width, underlineY, linePaint);
        }

        // Draw strikethrough if needed
        if (fragment.Properties.Strikethrough)
        {
            var strikeY = pixelY - font.Size * 0.3f;
            var width = context.PointsToPixels(fragment.Width);
            using var linePaint = new SKPaint
            {
                Color = paint.Color,
                StrokeWidth = 1 * context.Scale,
                IsAntialias = true
            };
            canvas.DrawLine(pixelX, strikeY, pixelX + width, strikeY, linePaint);
        }
    }

    void DrawBarTabs(SKCanvas canvas, ParagraphProperties props, float lineTopY, float lineHeight)
    {
        if (props.TabStops.Count == 0)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            StrokeWidth = Math.Max(1, 0.5f * context.Scale),
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        var top = context.PointsToPixels(lineTopY);
        var bottom = context.PointsToPixels(lineTopY + lineHeight);

        foreach (var stop in props.TabStops)
        {
            if (stop.Alignment != TabAlignment.Bar)
            {
                continue;
            }

            var x = context.PointsToPixels(context.ContentLeft + (float) stop.PositionPoints);
            canvas.DrawLine(x, top, x, bottom, paint);
        }
    }

    /// <summary>
    /// Draws shadow / glow / reflection (in that order) underneath the text fragment.
    /// Outline is drawn over the fill in <see cref="RenderFragment"/>.
    /// </summary>
    void DrawTextEffectsBehind(SKCanvas canvas, TextFragment fragment, SKFont font, float pixelX, float pixelY)
    {
        var props = fragment.Properties;

        if (props.Shadow is { } shadow)
        {
            // Word's direction convention: 0deg = right, 90deg = down (clockwise).
            var dirRad = shadow.DirectionDegrees * Math.PI / 180.0;
            var offsetX = (float) (Math.Cos(dirRad) * shadow.DistancePoints) * context.Scale;
            var offsetY = (float) (Math.Sin(dirRad) * shadow.DistancePoints) * context.Scale;
            var alpha = (byte) Math.Clamp(shadow.AlphaPercent * 255 / 100, 0, 255);
            var color = SkiaRenderContext.ParseColor(shadow.ColorHex).WithAlpha(alpha);

            using var shadowPaint = new SKPaint
            {
                Color = color,
                IsAntialias = true
            };
            if (shadow.BlurPoints > 0)
            {
                shadowPaint.ImageFilter = SKImageFilter.CreateBlur(
                    (float) shadow.BlurPoints * context.Scale,
                    (float) shadow.BlurPoints * context.Scale);
            }
            canvas.DrawText(fragment.Text, pixelX + offsetX, pixelY + offsetY, SKTextAlign.Left, font, shadowPaint);
        }

        if (props.Glow is { } glow)
        {
            var alpha = (byte) Math.Clamp(glow.AlphaPercent * 255 / 100, 0, 255);
            var color = SkiaRenderContext.ParseColor(glow.ColorHex).WithAlpha(alpha);
            using var glowPaint = new SKPaint
            {
                Color = color,
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(
                    (float) glow.RadiusPoints * context.Scale,
                    (float) glow.RadiusPoints * context.Scale)
            };
            // Drawing the same glyph twice deepens the halo so it shows through the fill.
            canvas.DrawText(fragment.Text, pixelX, pixelY, SKTextAlign.Left, font, glowPaint);
            canvas.DrawText(fragment.Text, pixelX, pixelY, SKTextAlign.Left, font, glowPaint);
        }

        if (props.HasReflection)
        {
            // Mirror below baseline with a top→bottom alpha fade. Defaults match Word's
            // built-in "Tight Reflection, touching" preset: starts at ~50% alpha and fades to 0.
            var width = context.PointsToPixels(fragment.Width);
            var ascent = -font.Metrics.Ascent;
            var height = ascent + font.Metrics.Descent;

            var reflectPaint = new SKPaint
            {
                Color = SkiaRenderContext.ParseColor(props.ColorHex),
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(
                    new(pixelX, pixelY),
                    new(pixelX, pixelY + height),
                    [new(255, 255, 255, 128), new(255, 255, 255, 0)],
                    SKShaderTileMode.Clamp),
                BlendMode = SKBlendMode.SrcIn
            };

            canvas.Save();
            // Translate to the baseline so flipping pivots there, then mirror Y.
            canvas.Translate(0, pixelY * 2);
            canvas.Scale(1, -1);

            // First lay the mirrored glyph in the destination region with the requested colour.
            using var mirrorFill = new SKPaint
            {
                Color = SkiaRenderContext.ParseColor(props.ColorHex),
                IsAntialias = true
            };
            canvas.DrawText(fragment.Text, pixelX, pixelY, SKTextAlign.Left, font, mirrorFill);

            // Apply the alpha gradient on top of the mirrored glyph.
            using (reflectPaint)
            {
                canvas.DrawRect(pixelX, pixelY - ascent, width, height, reflectPaint);
            }
            canvas.Restore();
        }
    }

    void RenderTabFiller(SKCanvas canvas, TextFragment fragment, float x, float y)
    {
        if (fragment.Width <= 0 || fragment.TabLeader == TabLeader.None)
        {
            return;
        }

        var pixelY = context.PointsToPixels(y);
        var pixelStartX = context.PointsToPixels(x);
        var pixelEndX = context.PointsToPixels(x + fragment.Width);

        if (fragment.TabLeader == TabLeader.Underscore)
        {
            // Draw a horizontal line at baseline for cleaner output than tiled underscore glyphs.
            using var linePaint = SkiaRenderContext.CreateTextPaint(fragment.Properties);
            linePaint.Style = SKPaintStyle.Stroke;
            linePaint.StrokeWidth = Math.Max(1f, (float)fragment.Properties.FontSizePoints * context.Scale * 0.07f);
            canvas.DrawLine(pixelStartX, pixelY, pixelEndX, pixelY, linePaint);
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

        using var font = context.CreateFont(fragment.Properties);
        using var paint = SkiaRenderContext.CreateTextPaint(fragment.Properties);
        var glyphPixelWidth = font.MeasureText(leaderChar.ToString());
        if (glyphPixelWidth <= 0)
        {
            return;
        }

        // Leave roughly one glyph of trailing padding before the snapped text begins.
        var availablePixels = pixelEndX - pixelStartX - glyphPixelWidth;
        if (availablePixels <= 0)
        {
            return;
        }

        var count = (int)Math.Floor(availablePixels / glyphPixelWidth);
        if (count <= 0)
        {
            return;
        }

        var leaderText = new string(leaderChar, count);
        canvas.DrawText(leaderText, pixelStartX, pixelY, SKTextAlign.Left, font, paint);
    }

    void RenderInlineShapeGroup(SKCanvas canvas, InlineShapeGroup group, TextFragment fragment, float x, float y)
    {
        var pixelX = context.PointsToPixels(x);
        var pixelWidth = context.PointsToPixels(fragment.Width);
        var pixelHeight = context.PointsToPixels(fragment.InlineImageHeightPoints);
        var pixelY = context.PointsToPixels(y) - pixelHeight;

        // Map child-coord units to pixels.
        var sx = pixelWidth / (float) group.ChildExtentX;
        var sy = pixelHeight / (float) group.ChildExtentY;

        canvas.Save();
        if (group.RotationDegrees != 0)
        {
            canvas.RotateDegrees((float) group.RotationDegrees, pixelX + pixelWidth / 2f, pixelY + pixelHeight / 2f);
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

                using var paint = new SKPaint
                {
                    Color = SkiaRenderContext.ParseColor(shape.ColorHex),
                    Style = SKPaintStyle.Stroke,
                    // EMU → points → pixels. Default to 0.75pt when the shape doesn't carry a width.
                    StrokeWidth = (float) (shape.LineWidthEmu > 0 ? shape.LineWidthEmu / emusPerPoint : 0.75) * context.Scale,
                    StrokeCap = SKStrokeCap.Square,
                    IsAntialias = true
                };
                canvas.DrawLine(startX, startY, endX, endY, paint);
            }
            else
            {
                if (shape.FillColorHex is { } fillHex)
                {
                    using var fillPaint = new SKPaint
                    {
                        Color = SkiaRenderContext.ParseColor(fillHex),
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true
                    };
                    canvas.DrawRect(x1, y1, w, h, fillPaint);
                }
                if (shape.LineWidthEmu > 0)
                {
                    using var strokePaint = new SKPaint
                    {
                        Color = SkiaRenderContext.ParseColor(shape.ColorHex),
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = (float) (shape.LineWidthEmu / emusPerPoint) * context.Scale,
                        IsAntialias = true
                    };
                    canvas.DrawRect(x1, y1, w, h, strokePaint);
                }
            }
        }

        canvas.Restore();
    }

    // EMU = English Metric Units. 1 point = 12700 EMU.
    const float emusPerPoint = 12700f;

    void RenderInlineImage(SKCanvas canvas, TextFragment fragment, float x, float y)
    {
        // Convert to pixels - y is the baseline, need to adjust for image height
        var pixelX = context.PointsToPixels(x);
        var pixelWidth = context.PointsToPixels(fragment.Width);
        var pixelHeight = context.PointsToPixels(fragment.InlineImageHeightPoints);
        // Position image so its bottom aligns with the baseline
        var pixelY = context.PointsToPixels(y) - pixelHeight;

        var destRect = new SKRect(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);

        var rotation = (float) fragment.InlineImageRotationDegrees;
        if (rotation != 0)
        {
            canvas.Save();
            canvas.RotateDegrees(rotation, pixelX + pixelWidth / 2, pixelY + pixelHeight / 2);
        }

        if (fragment.InlineImageContentType == "image/svg+xml")
        {
            // Pre-process SVG to remove class attributes and style elements that Svg.Skia might not handle correctly
            var svgContent = Encoding.UTF8.GetString(fragment.InlineImageData!);

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

            // Render SVG
            using var svg = new SKSvg();
            using var stream = new MemoryStream(processedData);
            var picture = svg.Load(stream);

            if (picture != null)
            {
                var svgBounds = picture.CullRect;
                if (svgBounds is {Width: > 0, Height: > 0})
                {
                    var scaleX = destRect.Width / svgBounds.Width;
                    var scaleY = destRect.Height / svgBounds.Height;

                    // Render SVG to a bitmap first (more reliable than DrawPicture on some canvases)
                    using var bitmap = new SKBitmap((int) destRect.Width, (int) destRect.Height);
                    using var tempCanvas = new SKCanvas(bitmap);
                    tempCanvas.Clear(SKColors.Transparent);
                    tempCanvas.Scale(scaleX, scaleY);
                    tempCanvas.DrawPicture(picture);

                    canvas.DrawBitmap(bitmap, destRect.Left, destRect.Top);
                }
            }
        }
        else
        {
            // Render bitmap image
            using var skData = SKData.CreateCopy(fragment.InlineImageData);
            using var codec = SKCodec.Create(skData);
            if (codec != null)
            {
                using var skImage = SKBitmap.Decode(codec);
                if (skImage != null)
                {
                    if (fragment.InlineImageCrop is { IsCropped: true } crop)
                    {
                        var srcLeft = (float) (crop.Left * skImage.Width);
                        var srcTop = (float) (crop.Top * skImage.Height);
                        var srcRight = (float) ((1 - crop.Right) * skImage.Width);
                        var srcBottom = (float) ((1 - crop.Bottom) * skImage.Height);
                        var srcRect = new SKRect(srcLeft, srcTop, srcRight, srcBottom);
                        canvas.DrawBitmap(skImage, srcRect, destRect);
                    }
                    else
                    {
                        canvas.DrawBitmap(skImage, destRect);
                    }
                }
            }
        }

        if (rotation != 0)
        {
            canvas.Restore();
        }
    }

    /// <summary>
    /// Layouts paragraph text into lines with word wrapping.
    /// </summary>
    List<TextLine> LayoutParagraph(ParagraphElement paragraph)
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

        for (var runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];

            // Tab snap: emit a tab-filler fragment that advances the cursor to the next tab stop.
            if (run.IsTab)
            {
                var followingWidth = MeasureFollowingWidthScaled(runs, runIndex + 1);
                var leftIndentPts = (float) props.LeftIndentPoints;
                var cursorAbs = leftIndentPts + currentLineWidth;
                double? decimalPrefix = props.TabStops.Any(_ => _.Alignment == TabAlignment.Decimal)
                    ? MeasureFollowingDecimalPrefixScaled(runs, runIndex + 1)
                    : null;
                var (destinationAbs, matchedStop) = TabStopResolver.Resolve(
                    cursorAbs, followingWidth,
                    props.TabStops, props.DefaultTabStopPoints, leftIndentPts,
                    decimalPrefix,
                    leftIndentPts + effectiveWidth);
                var gap = (float) (destinationAbs - cursorAbs);
                if (gap <= 0 || currentLineWidth + gap > effectiveWidth)
                {
                    continue;
                }

                using var tabFont = context.CreateFont(run.Properties);
                var tabMetrics = tabFont.Metrics;
                var tabRunHeight = (-tabMetrics.Ascent + tabMetrics.Descent) / context.Scale;
                var tabBaseline = -tabMetrics.Ascent / context.Scale;

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
                continue;
            }

            // Handle inline images / shape groups — treat as a single "word" in the text flow.
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
            using var font = context.CreateFont(run.Properties);
            var fontMetrics = font.Metrics;
            var runHeight = (-fontMetrics.Ascent + fontMetrics.Descent) / context.Scale;
            var runBaseline = -fontMetrics.Ascent / context.Scale;

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
                var wordWidth = font.MeasureText(displayWord) / context.Scale * context.FontWidthScale
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
                using var font = context.CreateFont(firstRun.Properties);
                var metrics = font.Metrics;
                emptyHeight = (-metrics.Ascent + metrics.Descent) / context.Scale;
                emptyBaseline = -metrics.Ascent / context.Scale;
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
    /// Used by LayoutParagraphWithWidth (no FontWidthScale).
    /// </summary>
    // Width of the following text up to (but not including) the first '.' character, applying the
    // same per-glyph metrics as MeasureFollowingWidthNoScale. Returns null if no '.' is found —
    // resolver then treats the Decimal stop as Right alignment, matching Word's fallback.
    float? MeasureFollowingDecimalPrefixNoScale(IReadOnlyList<Run> runs, int startRunIndex)
    {
        float total = 0;
        for (var i = startRunIndex; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run.IsTab ||
                run.InlineImageData is {Length: > 0})
            {
                break;
            }

            if (run.Text.Contains('\n') ||
                run.Text.Contains('\r'))
            {
                break;
            }

            var text = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            var dotIndex = text.IndexOf('.');
            using var font = context.CreateFont(run.Properties);
            if (dotIndex >= 0)
            {
                var prefix = text[..dotIndex];
                total += font.MeasureText(prefix) / context.Scale
                         + (float)(run.Properties.CharacterSpacingPoints * prefix.Length);
                return total;
            }

            total += font.MeasureText(text) / context.Scale
                     + (float)(run.Properties.CharacterSpacingPoints * text.Length);
        }

        return null;
    }

    // Same measurement, with FontWidthScale applied to match LayoutParagraph's measurement style.
    float? MeasureFollowingDecimalPrefixScaled(IReadOnlyList<Run> runs, int startRunIndex)
    {
        float total = 0;
        for (var i = startRunIndex; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run.IsTab ||
                run.InlineImageData is {Length: > 0})
            {
                break;
            }

            if (run.Text.Contains('\n') ||
                run.Text.Contains('\r'))
            {
                break;
            }

            var text = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            var dotIndex = text.IndexOf('.');
            using var font = context.CreateFont(run.Properties);
            if (dotIndex >= 0)
            {
                var prefix = text[..dotIndex];
                total += font.MeasureText(prefix) / context.Scale * context.FontWidthScale
                         + (float)(run.Properties.CharacterSpacingPoints * prefix.Length);
                return total;
            }

            total += font.MeasureText(text) / context.Scale * context.FontWidthScale
                     + (float)(run.Properties.CharacterSpacingPoints * text.Length);
        }

        return null;
    }

    float MeasureFollowingWidthNoScale(IReadOnlyList<Run> runs, int startRunIndex)
    {
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
            using var font = context.CreateFont(run.Properties);
            total += font.MeasureText(text) / context.Scale
                     + (float)(run.Properties.CharacterSpacingPoints * text.Length);
        }

        return total;
    }

    /// <summary>
    /// Like <see cref="MeasureFollowingWidthNoScale"/> but applies <c>context.FontWidthScale</c>,
    /// matching the measurement style used by <see cref="LayoutParagraph"/>.
    /// </summary>
    float MeasureFollowingWidthScaled(IReadOnlyList<Run> runs, int startRunIndex)
    {
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
            using var font = context.CreateFont(run.Properties);
            total += font.MeasureText(text) / context.Scale * context.FontWidthScale
                     + (float)(run.Properties.CharacterSpacingPoints * text.Length);
        }

        return total;
    }

    static List<TextFragment> RemoveSoftHyphens(List<TextFragment> fragments)
    {
        var result = new List<TextFragment>(fragments.Count);
        foreach (var f in fragments)
        {
            if (f.Text.Contains(softHyphen))
            {
                result.Add(
                    new()
                    {
                        Text = f.Text.Replace(softHyphenString, ""),
                        Width = f.Width,
                        Properties = f.Properties
                    });
            }
            else
            {
                result.Add(f);
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

internal sealed record TextLine
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
