/// <summary>
/// Renders text content with formatting using SixLabors.ImageSharp.
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
        var isEmpty = IsParagraphEmpty(paragraph);
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
            lineHeights.Add(TableLayout.CalculateCompactLineHeight(line.Height, props));
        }

        return lineHeights;
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
    /// Renders a paragraph to the image at the current position.
    /// </summary>
    public void RenderParagraph(Image<Rgba32> currentPage, ParagraphElement paragraph)
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

            var bgColor = RenderContext.ParseColor(props.BackgroundColorHex);

            var bgX = context.PointsToPixels(context.ContentLeft + (float)props.LeftIndentPoints);
            var bgY = context.PointsToPixels(context.CurrentY);
            var bgWidth = context.PointsToPixels(context.ContentWidth - (float)props.LeftIndentPoints - (float)props.RightIndentPoints);
            var bgHeight = context.PointsToPixels(paragraphHeight);

            currentPage.Mutate(_ => _.Fill(bgColor, new RectangleF(bgX, bgY, bgWidth, bgHeight)));
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
                RenderLineNumber(currentPage, lineNumber, y, lineNumberSettings!);
            }

            // Render bullet/number on first line
            if (isFirstLine && props.Numbering != null)
            {
                RenderBullet(currentPage, props.Numbering, y, paragraph);
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
                RenderFragment(currentPage, fragment, currentX, y);
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
    public void RenderParagraphInBounds(Image<Rgba32> currentPage, ParagraphElement paragraph, float startX, float width)
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
                RenderBulletInBounds(currentPage, props.Numbering, y, paragraph, startX);
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
                RenderFragment(currentPage, fragment, currentX, y);
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
        var isEmpty = IsParagraphEmpty(paragraph);
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
            var font = context.GetFont(run.Properties);
            var (runHeight, baseline) = RenderContext.GetFontMetrics(font);

            foreach (var word in words)
            {
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

                // Measure word width in points
                var wordWidth = RenderContext.MeasureText(font, word);

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
            // Fallback default
            float emptyHeight = 12;
            float emptyBaseline = 10;

            if (paragraph.Runs.Count > 0)
            {
                var firstRun = paragraph.Runs[0];
                var font = context.GetFont(firstRun.Properties);
                (emptyHeight, emptyBaseline) = RenderContext.GetFontMetrics(font);
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
    void RenderLineNumber(Image<Rgba32> currentPage, int lineNumber, float baselineY, LineNumberSettings settings)
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
        var props = new RunProperties { FontFamily = "Aptos", FontSizePoints = 9 };
        var font = context.GetFont(props);
        var (_, baseline) = RenderContext.GetFontMetrics(font);

        var numberText = lineNumber.ToString();

        // Measure text width so we can right-align it
        var textWidth = RenderContext.MeasureText(font, numberText) * context.Scale;

        var textOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            Origin = new PointF(pixelX - textWidth, pixelY - baseline * context.Scale)
        };
        currentPage.Mutate(_ => _.DrawText(textOptions, numberText, new SolidBrush(Color.Black)));
    }

    /// <summary>
    /// Renders a bullet or number for a list item.
    /// </summary>
    void RenderBullet(Image<Rgba32> currentPage, NumberingInfo numbering, float baselineY, ParagraphElement paragraph)
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

        // Use Arial for bullets since Symbol/Wingdings characters have been mapped to Unicode equivalents
        // Arial is available on all platforms and has good Unicode coverage including bullet characters
        var bulletProps = new RunProperties { FontFamily = "Arial", FontSizePoints = fontSize };
        var font = context.GetFont(bulletProps);
        var (_, baseline) = RenderContext.GetFontMetrics(font);

        var color = colorHex != null ? RenderContext.ParseColor(colorHex) : Color.Black;

        var textOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            Origin = new PointF(pixelX, pixelY - baseline * context.Scale)
        };
        currentPage.Mutate(_ => _.DrawText(textOptions, numbering.Text, new SolidBrush(color)));
    }

    /// <summary>
    /// Renders a bullet or number for a list item within specific bounds (for table cells).
    /// </summary>
    void RenderBulletInBounds(Image<Rgba32> currentPage, NumberingInfo numbering, float baselineY, ParagraphElement paragraph, float startX)
    {
        // Get font size from paragraph's first run, or use default
        float fontSize = 11;
        string? colorHex = null;
        if (paragraph.Runs.Count > 0)
        {
            fontSize = (float)paragraph.Runs[0].Properties.FontSizePoints;
            colorHex = paragraph.Runs[0].Properties.ColorHex;
        }

        // Use Arial for bullets since Symbol/Wingdings characters have been mapped to Unicode equivalents
        // Arial is available on all platforms and has good Unicode coverage including bullet characters
        var bulletProps = new RunProperties { FontFamily = "Arial", FontSizePoints = fontSize };
        var font = context.GetFont(bulletProps);
        var (_, baseline) = RenderContext.GetFontMetrics(font);

        var color = colorHex != null ? RenderContext.ParseColor(colorHex) : Color.Black;

        // Render bullet at the start of the content area (text is indented to the right)
        var pixelX = context.PointsToPixels(startX);
        var pixelY = context.PointsToPixels(baselineY);

        var textOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            Origin = new PointF(pixelX, pixelY - baseline * context.Scale)
        };
        currentPage.Mutate(_ => _.DrawText(textOptions, numbering.Text, new SolidBrush(color)));
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

    static bool IsParagraphEmpty(ParagraphElement paragraph)
    {
        if (paragraph.Runs.Count == 0)
        {
            return true;
        }

        foreach (var r in paragraph.Runs)
        {
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

    void RenderFragment(Image<Rgba32> currentPage, TextFragment fragment, float x, float y)
    {
        // Handle inline images
        if (fragment.InlineImageData is {Length: > 0})
        {
            RenderInlineImage(currentPage, fragment, x, y);
            return;
        }

        var font = context.GetFont(fragment.Properties);
        var color = RenderContext.ParseColor(fragment.Properties.ColorHex);

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
            var bgColor = RenderContext.ParseColor(fragment.Properties.BackgroundColorHex);

            var textWidth = context.PointsToPixels(fragment.Width);
            var (runHeight, runBaseline) = RenderContext.GetFontMetrics(font);
            // Top of text: baseline position minus ascent (in pixels)
            var textTop = pixelY - runBaseline * context.Scale;
            var textBottom = pixelY + (runHeight - runBaseline) * context.Scale;

            currentPage.Mutate(_ => _.Fill(bgColor, new RectangleF(pixelX, textTop, textWidth, textBottom - textTop)));
        }

        // Get baseline for coordinate conversion (Skia uses baseline Y, ImageSharp uses top-left Y)
        var (_, baseline) = RenderContext.GetFontMetrics(font);

        var textOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            Origin = new PointF(pixelX, pixelY - baseline * context.Scale)
        };
        currentPage.Mutate(_ => _.DrawText(textOptions, fragment.Text, new SolidBrush(color)));

        // Draw underline if needed
        if (fragment.Properties.Underline)
        {
            var underlineY = pixelY + 2 * context.Scale;
            var width = context.PointsToPixels(fragment.Width);
            var strokeWidth = 1 * context.Scale;
            currentPage.Mutate(_ => _.DrawLine(new SolidPen(color, strokeWidth), new PointF(pixelX, underlineY), new PointF(pixelX + width, underlineY)));
        }

        // Draw strikethrough if needed
        if (fragment.Properties.Strikethrough)
        {
            var strikeY = pixelY - font.Size * 0.3f;
            var width = context.PointsToPixels(fragment.Width);
            var strokeWidth = 1 * context.Scale;
            currentPage.Mutate(_ => _.DrawLine(new SolidPen(color, strokeWidth), new PointF(pixelX, strikeY), new PointF(pixelX + width, strikeY)));
        }
    }

    void RenderInlineImage(Image<Rgba32> currentPage, TextFragment fragment, float x, float y)
    {
        // Convert to pixels - y is the baseline, need to adjust for image height
        var pixelX = context.PointsToPixels(x);
        var pixelWidth = context.PointsToPixels(fragment.Width);
        var pixelHeight = context.PointsToPixels(fragment.InlineImageHeightPoints);
        // Position image so its bottom aligns with the baseline
        var pixelY = context.PointsToPixels(y) - pixelHeight;

        if (fragment.InlineImageContentType == "image/svg+xml")
        {
            // SVG rendering not supported in ImageSharp - skip silently
            return;
        }

        // Render bitmap image
        try
        {
            using var img = Image.Load<Rgba32>(fragment.InlineImageData!);
            img.Mutate(_ => _.Resize(new Size((int)pixelWidth, (int)pixelHeight)));
            currentPage.Mutate(_ => _.DrawImage(img, new Point((int)pixelX, (int)pixelY), 1f));
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
            var font = context.GetFont(run.Properties);
            var (runHeight, runBaseline) = RenderContext.GetFontMetrics(font);

            foreach (var word in words)
            {
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
                var wordWidth = RenderContext.MeasureText(font, displayWord) * context.FontWidthScale;

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
            // Fallback default
            float emptyHeight = 12;
            float emptyBaseline = 10;

            // Get height from first run's font if available
            if (paragraph.Runs.Count > 0)
            {
                var firstRun = paragraph.Runs[0];
                var font = context.GetFont(firstRun.Properties);
                (emptyHeight, emptyBaseline) = RenderContext.GetFontMetrics(font);
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
            else if (fragment.Text.Contains(softHyphen))
            {
                // Remove soft hyphen if not at end of line
                result.Add(new()
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

    static List<TextFragment> RemoveSoftHyphens(List<TextFragment> fragments)
    {
        var result = new List<TextFragment>(fragments.Count);
        foreach (var f in fragments)
        {
            if (f.Text.Contains(softHyphen))
            {
                result.Add(new()
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
}
