namespace WordRender;

/// <summary>
/// Renders text content with formatting using SkiaSharp.
/// </summary>
sealed class TextRenderer(RenderContext context)
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
        var sameStyle = props.StyleId != null && props.StyleId == context.LastParagraphStyleId;
        var collapseSpacingBefore = props.ContextualSpacing && context.LastParagraphHadContextualSpacing && sameStyle;
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

        return totalHeight;
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
        var isEmpty = paragraph.Runs.Count == 0 || paragraph.Runs.All(r => string.IsNullOrEmpty(r.Text) && r.InlineImageData == null);
        if (!isEmpty)
        {
            totalHeight += (float)props.SpacingAfterPoints;
        }

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
            // Use compact line height for table cells (no boost)
            lineHeights.Add(CalculateCompactLineHeight(line.Height, props));
        }

        return lineHeights;
    }

    /// <summary>
    /// Calculates line height without the Word compatibility boost.
    /// Used for table cell measurement to achieve more compact layout.
    /// </summary>
    static float CalculateCompactLineHeight(float naturalHeight, ParagraphProperties props)
    {
        var lineHeight = props.LineSpacingRule switch
        {
            LineSpacingRule.Exactly => (float)props.LineSpacingPoints,
            LineSpacingRule.AtLeast => Math.Max(naturalHeight, (float)props.LineSpacingPoints),
            _ => naturalHeight * (float)props.LineSpacingMultiplier // Auto
        };

        // Skip the Word compatibility boost for compact measurement
        // This helps table cells fit their content more tightly

        return lineHeight;
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
    public void RenderParagraph(SKCanvas canvas, ParagraphElement paragraph)
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
        if (!collapseSpacingBefore)
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
                if (extraSpacePerGap > 0 && IsWhitespaceFragment(fragment))
                {
                    currentX += extraSpacePerGap;
                }
            }

            context.CurrentY += lineHeight;
        }

        // Add spacing after and track for margin collapsing with next paragraph
        // Contextual spacing removes space between paragraphs to create tighter visual grouping
        var spacingAfter = (float)props.SpacingAfterPoints;
        if (!props.ContextualSpacing)
        {
            context.CurrentY += spacingAfter;
            context.LastParagraphSpacingAfterPoints = spacingAfter;
        }
        else
        {
            context.LastParagraphSpacingAfterPoints = 0;
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

        // Calculate bullet indent for table cells - use a compact indent
        float bulletIndent = 0;
        if (props.Numbering != null)
        {
            // Use a fixed compact indent for bullets in table cells (12pt is typical for compact lists)
            bulletIndent = 12;
        }

        // Layout with adjusted width to account for bullet indent
        var lines = LayoutParagraphWithWidth(paragraph, width - bulletIndent);

        // Add spacing before
        context.CurrentY += (float)props.SpacingBeforePoints;

        var isFirstLine = true;
        foreach (var line in lines)
        {
            var lineHeight = CalculateLineHeight(line.Height, props);

            // Calculate X position based on alignment within the specified bounds
            // Add bullet indent to shift text right
            var x = CalculateLineXInBounds(line, props, startX + bulletIndent, width - bulletIndent);
            var y = context.CurrentY + line.Baseline;

            // Render bullet/number on first line
            if (isFirstLine && props.Numbering != null)
            {
                RenderBulletInBounds(canvas, props.Numbering, y, paragraph, startX);
                isFirstLine = false;
            }

            // Calculate extra space per gap for justified text
            float extraSpacePerGap = 0;
            var effectiveWidth = width - bulletIndent - (float)props.LeftIndentPoints;
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

            context.CurrentY += lineHeight;
        }

        // Add spacing after (but not for empty paragraphs which are typically just visual spacers)
        var isEmpty = paragraph.Runs.Count == 0 || paragraph.Runs.All(r => string.IsNullOrEmpty(r.Text) && r.InlineImageData == null);
        if (!isEmpty)
        {
            context.CurrentY += (float)props.SpacingAfterPoints;
        }
    }

    static float CalculateLineXInBounds(TextLine line, ParagraphProperties props, float startX, float width)
    {
        var contentLeft = startX + (float)props.LeftIndentPoints;
        var availableWidth = width - (float)props.LeftIndentPoints;

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

        var adjustedMaxWidth = maxWidth - (float)props.LeftIndentPoints;
        float currentLineWidth = 0;
        float maxLineHeight = 0;
        float maxBaseline = 0;
        var currentFragments = new List<TextFragment>();
        var isFirstLine = true;

        var firstLineIndent = (float)props.FirstLineIndentPoints;
        var effectiveWidth = adjustedMaxWidth - (isFirstLine ? firstLineIndent : 0);

        foreach (var run in paragraph.Runs)
        {
            // Handle inline images - treat as a single "word" in the text flow
            if (run.InlineImageData is {Length: > 0})
            {
                var imageWidth = (float)run.InlineImageWidthPoints;
                var imageHeight = (float)run.InlineImageHeightPoints;

                // Get baseline from run's font properties for proper alignment
                using var font = context.CreateFont(run.Properties);
                var metrics = font.Metrics;
                var textBaseline = -metrics.Ascent / context.Scale;

                // Check if we need to wrap before the image
                if (currentLineWidth + imageWidth > effectiveWidth && currentFragments.Count > 0)
                {
                    // Finish current line
                    lines.Add(new()
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
                currentFragments.Add(new()
                {
                    Text = "",
                    Width = imageWidth,
                    Properties = run.Properties,
                    InlineImageData = run.InlineImageData,
                    InlineImageHeightPoints = imageHeight,
                    InlineImageContentType = run.InlineImageContentType
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

            foreach (var word in words)
            {
                using var font = context.CreateFont(run.Properties);

                // Get line metrics (convert from pixels to points)
                var metrics = font.Metrics;
                var runHeight = (-metrics.Ascent + metrics.Descent) / context.Scale;
                var baseline = -metrics.Ascent / context.Scale;

                // Handle explicit line break (newline character)
                if (word is "\n" or "\r\n" or "\r")
                {
                    // Force a line break - finish current line
                    if (currentFragments.Count > 0)
                    {
                        lines.Add(new()
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
                        lines.Add(new()
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

                // Convert pixel measurements back to points
                var wordWidth = font.MeasureText(word) / context.Scale;

                // Check if we need to wrap
                if (currentLineWidth + wordWidth > effectiveWidth && currentFragments.Count > 0)
                {
                    // Finish current line
                    lines.Add(new()
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
                currentFragments.Add(new()
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
            lines.Add(new()
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
        if (lines.Count == 0)
        {
            float emptyHeight = 12; // Fallback default
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
                emptyHeight = (float)props.ParagraphMarkFontSizePoints.Value * 1.2f;
                emptyBaseline = (float)props.ParagraphMarkFontSizePoints.Value;
            }

            lines.Add(new()
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

        // Use a standard font for line numbers (9pt, same as typical Word default)
        using var typeface = SKTypeface.FromFamilyName("Aptos", SKFontStyle.Normal);
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
        // Position bullet at the indent position (before the hanging indent)
        var bulletX = context.ContentLeft + (float)numbering.IndentPoints - (float)numbering.HangingIndentPoints;
        var pixelX = context.PointsToPixels(bulletX);
        var pixelY = context.PointsToPixels(baselineY);

        // Get font size from paragraph's first run, or use default
        float fontSize = 11;
        string? colorHex = null;
        if (paragraph.Runs.Count > 0)
        {
            fontSize = (float)paragraph.Runs[0].Properties.FontSizePoints;
            colorHex = paragraph.Runs[0].Properties.ColorHex;
        }

        // Use a standard font for bullets since they've been mapped to Unicode characters
        // Symbol and Wingdings fonts don't have proper Unicode mappings in SkiaSharp
        var fontFamily = numbering.FontFamily;
        if (fontFamily is null or "Symbol" or "Wingdings" or "Wingdings 2" or "Wingdings 3")
        {
            fontFamily = "Aptos";
        }

        using var typeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyle.Normal);
        using var font = context.CreateFontFromTypeface(typeface, fontSize);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = colorHex != null ? SKColor.Parse("#" + colorHex) : SKColors.Black
        };

        canvas.DrawText(numbering.Text, pixelX, pixelY, SKTextAlign.Left, font, paint);
    }

    /// <summary>
    /// Renders a bullet or number for a list item within specific bounds (for table cells).
    /// </summary>
    void RenderBulletInBounds(SKCanvas canvas, NumberingInfo numbering, float baselineY, ParagraphElement paragraph, float startX)
    {
        // Get font size from paragraph's first run, or use default
        float fontSize = 11;
        string? colorHex = null;
        if (paragraph.Runs.Count > 0)
        {
            fontSize = (float)paragraph.Runs[0].Properties.FontSizePoints;
            colorHex = paragraph.Runs[0].Properties.ColorHex;
        }

        // Use a standard font for bullets since they've been mapped to Unicode characters
        var fontFamily = numbering.FontFamily;
        if (fontFamily is null or "Symbol" or "Wingdings" or "Wingdings 2" or "Wingdings 3")
        {
            fontFamily = "Aptos";
        }

        using var typeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyle.Normal);
        using var font = context.CreateFontFromTypeface(typeface, fontSize);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = colorHex != null ? SKColor.Parse("#" + colorHex) : SKColors.Black
        };

        // Render bullet at the start of the content area (text is indented to the right)
        var pixelX = context.PointsToPixels(startX);
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

        return props.Alignment switch
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

    /// <summary>
    /// Checks if a fragment is whitespace (space between words).
    /// </summary>
    static bool IsWhitespaceFragment(TextFragment fragment) =>
        fragment.Text.Length > 0 && fragment.Text.All(char.IsWhiteSpace);

    void RenderFragment(SKCanvas canvas, TextFragment fragment, float x, float y)
    {
        // Handle inline images
        if (fragment.InlineImageData is {Length: > 0})
        {
            RenderInlineImage(canvas, fragment, x, y);
            return;
        }

        using var font = context.CreateFont(fragment.Properties);
        using var paint = RenderContext.CreateTextPaint(fragment.Properties);

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

        // Draw background/shading color if specified
        if (!string.IsNullOrEmpty(fragment.Properties.BackgroundColorHex))
        {
            var bgColor = SKColor.TryParse(fragment.Properties.BackgroundColorHex, out var parsedBgColor)
                ? parsedBgColor
                : SKColor.Parse("#" + fragment.Properties.BackgroundColorHex);

            var textWidth = context.PointsToPixels(fragment.Width);
            var metrics = font.Metrics;
            var textTop = pixelY + metrics.Ascent; // Ascent is negative
            var textBottom = pixelY + metrics.Descent;

            using var bgPaint = new SKPaint
            {
                Color = bgColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(pixelX, textTop, textWidth, textBottom - textTop, bgPaint);
        }

        canvas.DrawText(fragment.Text, pixelX, pixelY, SKTextAlign.Left, font, paint);

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

    void RenderInlineImage(SKCanvas canvas, TextFragment fragment, float x, float y)
    {
        // Convert to pixels - y is the baseline, need to adjust for image height
        var pixelX = context.PointsToPixels(x);
        var pixelWidth = context.PointsToPixels(fragment.Width);
        var pixelHeight = context.PointsToPixels(fragment.InlineImageHeightPoints);
        // Position image so its bottom aligns with the baseline
        var pixelY = context.PointsToPixels(y) - pixelHeight;

        var destRect = new SKRect(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);

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
            using var skImage = SKBitmap.Decode(fragment.InlineImageData);
            if (skImage != null)
            {
                canvas.DrawBitmap(skImage, destRect);
            }
        }
    }

    /// <summary>
    /// Layouts paragraph text into lines with word wrapping.
    /// </summary>
    List<TextLine> LayoutParagraph(ParagraphElement paragraph)
    {
        var lines = new List<TextLine>();
        var props = paragraph.Properties;

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

        foreach (var run in paragraph.Runs)
        {
            // Handle inline images - treat as a single "word" in the text flow
            if (run.InlineImageData is {Length: > 0})
            {
                var imageWidth = (float)run.InlineImageWidthPoints;
                var imageHeight = (float)run.InlineImageHeightPoints;

                // Get baseline from run's font properties for proper alignment
                using var font = context.CreateFont(run.Properties);
                var metrics = font.Metrics;
                var textBaseline = -metrics.Ascent / context.Scale;

                // Check if we need to wrap before the image
                if (currentLineWidth + imageWidth > effectiveWidth && currentFragments.Count > 0)
                {
                    // Finish current line
                    var finalizedFragments = FinalizeLine(currentFragments);
                    lines.Add(new()
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
                currentFragments.Add(new()
                {
                    Text = "",
                    Width = imageWidth,
                    Properties = run.Properties,
                    InlineImageData = run.InlineImageData,
                    InlineImageHeightPoints = imageHeight,
                    InlineImageContentType = run.InlineImageContentType
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

            foreach (var word in words)
            {
                using var font = context.CreateFont(run.Properties);

                // Get font metrics for line height
                var metrics = font.Metrics;
                var runHeight = (-metrics.Ascent + metrics.Descent) / context.Scale;
                var runBaseline = -metrics.Ascent / context.Scale;

                // Handle explicit line break (newline character)
                if (word is "\n" or "\r\n" or "\r")
                {
                    // Force a line break - finish current line
                    if (currentFragments.Count > 0)
                    {
                        var finalizedFragments = FinalizeLine(currentFragments);
                        lines.Add(new()
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
                        lines.Add(new()
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
                // Apply FontWidthScale to better match Word's text rendering
                var wordWidth = font.MeasureText(displayWord) / context.Scale * context.FontWidthScale;

                // Check if we need to wrap to a new line
                if (currentLineWidth + wordWidth > effectiveWidth && currentFragments.Count > 0)
                {
                    // Finish current line - convert any trailing soft hyphens to visible hyphens
                    var finalizedFragments = FinalizeLine(currentFragments);
                    lines.Add(new()
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
                currentFragments.Add(new()
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
            lines.Add(new()
            {
                Fragments = finalizedFragments,
                Width = currentLineWidth,
                Height = maxLineHeight,
                Baseline = maxBaseline,
                IsFirstLine = isFirstLine,
                IsLastLine = true  // This is the last line with content
            });
        }

        // Handle empty paragraph - use font metrics from runs if available
        if (lines.Count == 0)
        {
            float emptyHeight = 12; // Fallback default
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
                emptyHeight = (float)props.ParagraphMarkFontSizePoints.Value * 1.2f;
                emptyBaseline = (float)props.ParagraphMarkFontSizePoints.Value;
            }

            lines.Add(new()
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
                result.Add(new()
                {
                    Text = fragment.Text.TrimEnd(softHyphen) + "-",
                    Width = fragment.Width, // Width was already measured without soft hyphen
                    Properties = fragment.Properties
                });
            }
            else
            {
                // Remove soft hyphen if not at end of line
                result.Add(new()
                {
                    Text = fragment.Text.Replace(softHyphen.ToString(), ""),
                    Width = fragment.Width,
                    Properties = fragment.Properties
                });
            }
        }
        return result;
    }

    /// <summary>
    /// Removes all soft hyphens from fragments (used for final line).
    /// </summary>
    static List<TextFragment> RemoveSoftHyphens(List<TextFragment> fragments) =>
        fragments.Select(f =>
                new TextFragment
        {
            Text = f.Text.Replace(softHyphen.ToString(), ""),
            Width = f.Width,
            Properties = f.Properties
        })
            .ToList();

    // Unicode characters for hyphenation
    const char softHyphen = '\u00AD';
    const char nonBreakingHyphen = '\u2011';

    static List<string> SplitIntoWords(string text)
    {
        var words = new List<string>();
        var currentWord = new StringBuilder();

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (currentWord.Length > 0)
                {
                    words.Add(currentWord.ToString());
                    currentWord.Clear();
                }
                // Preserve spaces as separate "words" for accurate width calculation
                words.Add(c.ToString());
            }
            else if (c == softHyphen)
            {
                // Soft hyphen is a potential break point
                // Add current word fragment with trailing soft hyphen marker
                if (currentWord.Length > 0)
                {
                    // Mark this fragment as having a soft hyphen at the end
                    // We'll handle this in the layout logic
                    currentWord.Append(c);
                    words.Add(currentWord.ToString());
                    currentWord.Clear();
                }
            }
            else if (c == nonBreakingHyphen)
            {
                // Non-breaking hyphen: render as regular hyphen but don't break here
                // Keep it as part of the current word
                currentWord.Append('-'); // Render as visible hyphen
            }
            else
            {
                currentWord.Append(c);
            }
        }

        if (currentWord.Length > 0)
        {
            words.Add(currentWord.ToString());
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
}
