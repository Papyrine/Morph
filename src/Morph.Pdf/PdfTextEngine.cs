/// <summary>
/// Paragraph layout and drawing for the PDF backend. This is the part that cannot come from the
/// shared engine (it depends on the backend's text metrics): it greedily wraps runs into lines
/// using PdfSharp font measurement, then draws them with alignment, indents, list markers, inline
/// formatting (bold/italic/underline/strike/colour), super/subscript and inline images.
///
/// It is deliberately simpler than the Skia/ImageSharp engines — no decimal tabs, drop caps,
/// justification kerning or run borders — but produces real, selectable vector text.
/// </summary>
sealed class PdfTextEngine(PdfRenderContext context) : IParagraphMeasurer
{
    readonly XGraphics measure = XGraphics.CreateMeasureContext(new(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);

    // A table-cell paragraph is laid out ~5× (autofit natural + minimum width, row height,
    // vertical-align measure, draw); positioned frames 3×. The layout is pure per
    // (paragraph, width), so memoize it for the engine's lifetime (one document render) —
    // the same design as the Skia/ImageSharp paged/bounded layout caches. Draw() never
    // mutates the returned lines.
    readonly Dictionary<(ParagraphElement Paragraph, double Width), List<Line>> layoutCache = [];

    // Space width, ascent and raw line height are constant per XFont; the layout loop used to
    // re-measure a space per whitespace token and recompute ascent/height per run.
    readonly Dictionary<XFont, (double SpaceWidth, double Ascent, double RawHeight)> fontMetricsCache = [];

    (double SpaceWidth, double Ascent, double RawHeight) Metrics(XFont font)
    {
        if (!fontMetricsCache.TryGetValue(font, out var metrics))
        {
            metrics = (measure.MeasureString(" ", font).Width, ComputeAscent(font), font.GetHeight());
            fontMetricsCache[font] = metrics;
        }

        return metrics;
    }

    // ---- IParagraphMeasurer (used by shared table-layout / pagination math) ----

    public List<float> LayoutParagraphForMeasurement(ParagraphElement paragraph, float maxWidth)
    {
        var lines = Layout(paragraph, maxWidth);
        var heights = new List<float>(lines.Count);
        foreach (var line in lines)
        {
            heights.Add((float) line.Height);
        }

        if (heights.Count == 0)
        {
            heights.Add((float) EmptyLineHeight(paragraph));
        }

        return heights;
    }

    public float MeasureParagraphNaturalWidth(ParagraphElement paragraph, float maxWidth)
    {
        var widest = 0d;
        foreach (var line in Layout(paragraph, maxWidth))
        {
            widest = Math.Max(widest, line.Width);
        }

        return (float) (widest + Indent(paragraph));
    }

    public float MeasureParagraphHeightWithWidth(ParagraphElement paragraph, float maxWidth) =>
        (float) MeasureHeight(paragraph, maxWidth);

    public double MeasureHeight(ParagraphElement paragraph, double maxWidth)
    {
        var lines = Layout(paragraph, maxWidth);
        var total = SpacingBefore(paragraph) + SpacingAfter(paragraph);
        if (lines.Count == 0)
        {
            return total + EmptyLineHeight(paragraph);
        }

        foreach (var line in lines)
        {
            total += line.Height;
        }

        return total;
    }

    double EmptyLineHeight(ParagraphElement paragraph)
    {
        var size = paragraph.Properties.ParagraphMarkFontSizePoints ?? 11;
        var font = context.GetFont(DefaultFontSettings.DefaultFont, false, false, size);
        return font.GetHeight() * paragraph.Properties.LineSpacingMultiplier;
    }

    // ---- Paragraph spacing (mirrors the Skia/ImageSharp contextual-spacing collapse) ----

    // Space above a paragraph, collapsed to zero when this paragraph and the previous one share a
    // style and both opt into w:contextualSpacing — so a run of same-style lines (e.g. a Details
    // block of Date/Time/Facilitator) sits tight instead of re-adding the style's before-spacing
    // on every line.
    double SpacingBefore(ParagraphElement paragraph)
    {
        var properties = paragraph.Properties;
        var sameStyle = properties.StyleId != null && properties.StyleId == context.LastParagraphStyleId;
        var collapse = properties.ContextualSpacing && context.LastParagraphHadContextualSpacing && sameStyle;
        return collapse ? 0 : properties.SpacingBeforePoints;
    }

    // w:contextualSpacing also suppresses the paragraph's own after-spacing; the next same-style
    // paragraph's collapsed before-spacing keeps the block tight.
    static double SpacingAfter(ParagraphElement paragraph) =>
        paragraph.Properties.ContextualSpacing ? 0 : paragraph.Properties.SpacingAfterPoints;

    void TrackContextualSpacing(ParagraphElement paragraph)
    {
        context.LastParagraphStyleId = paragraph.Properties.StyleId;
        context.LastParagraphHadContextualSpacing = paragraph.Properties.ContextualSpacing;
    }

    // ---- Drawing ----

    /// <summary>Invoked when a line won't fit on the current page during flow rendering. The
    /// renderer finishes the current page and starts a new one (resetting CurrentY / Graphics).</summary>
    public Action? RequestNewPage { get; set; }

    /// <summary>Draws the paragraph at the current flow position, advancing <see cref="RenderContextBase.CurrentY"/>.</summary>
    public void Render(ParagraphElement paragraph)
    {
        var maxWidth = context.ContentWidth - Indent(paragraph);
        Draw(paragraph, context.ContentLeft + Indent(paragraph), maxWidth, allowPageBreak: true);
    }

    /// <summary>Draws the paragraph constrained to a bounded region (table cell), no page breaks.</summary>
    public void RenderInBounds(ParagraphElement paragraph, double x, double maxWidth)
    {
        var indent = Indent(paragraph);
        Draw(paragraph, x + indent, maxWidth - indent, allowPageBreak: false);
    }

    void Draw(ParagraphElement paragraph, double left, double availableWidth, bool allowPageBreak)
    {
        var lines = Layout(paragraph, availableWidth);

        context.CurrentY += (float) SpacingBefore(paragraph);

        if (lines.Count == 0)
        {
            context.CurrentY += (float) EmptyLineHeight(paragraph);
            context.CurrentY += (float) SpacingAfter(paragraph);
            TrackContextualSpacing(paragraph);
            return;
        }

        var alignment = paragraph.Properties.Alignment;
        var markerDrawn = false;

        foreach (var line in lines)
        {
            if (allowPageBreak &&
                context.CurrentY > context.ContentTop &&
                context.CurrentY + line.Height > context.ContentBottom &&
                RequestNewPage != null)
            {
                RequestNewPage();
            }

            var graphics = context.Graphics;
            var lineTop = (double) context.CurrentY;
            var baseline = lineTop + line.Ascent;

            var penX = left;
            var extraSpace = 0d;
            if (alignment == TextAlignment.Center)
            {
                penX += Math.Max(0, (availableWidth - line.Width) / 2);
            }
            else if (alignment == TextAlignment.Right)
            {
                penX += Math.Max(0, availableWidth - line.Width);
            }
            else if (alignment == TextAlignment.Justify && line is {IsLast: false, SpaceCount: > 0})
            {
                extraSpace = Math.Max(0, availableWidth - line.Width) / line.SpaceCount;
            }

            // List marker hangs to the left of the first line's text by the paragraph's cascaded
            // hanging indent (Word's model, matching the Skia/ImageSharp backends), and takes the
            // colour of the paragraph's first run so a white-on-dark list keeps white bullets.
            // Falls back to a snug gap when no hanging indent is defined.
            if (!markerDrawn && paragraph.Properties.Numbering is {Text.Length: > 0} numbering)
            {
                markerDrawn = true;
                if (graphics != null)
                {
                    var firstProperties = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Properties : new();
                    var markerFont = context.GetFont(firstProperties.FontFamily, firstProperties.Bold, false, firstProperties.FontSizePoints);
                    var markerText = numbering.Text;
                    var markerBrush = context.GetBrush(PdfRenderContext.ParseColor(firstProperties.ColorHex));
                    var hangingIndent = paragraph.Properties.HangingIndentPoints;
                    var markerX = hangingIndent > 0.01
                        ? penX - hangingIndent
                        : penX - measure.MeasureString(markerText, markerFont).Width - 3;
                    graphics.DrawString(markerText, markerFont, markerBrush, new XPoint(markerX, baseline), baselineFormat);
                }
            }

            foreach (var item in line.Items)
            {
                if (item.IsImage)
                {
                    DrawImage(graphics, item, penX, baseline);
                    penX += item.Width;
                    continue;
                }

                if (item.IsTabFiller)
                {
                    if (graphics != null)
                    {
                        DrawTabLeader(graphics, item, penX, baseline);
                    }

                    penX += item.Width;
                    continue;
                }

                if (graphics != null && !string.IsNullOrEmpty(item.Text))
                {
                    DrawItem(graphics, item, penX, baseline);
                }

                penX += item.Width;
                if (item.IsSpace)
                {
                    penX += extraSpace;
                }
            }

            context.CurrentY += (float) line.Height;
        }

        context.CurrentY += (float) SpacingAfter(paragraph);
        TrackContextualSpacing(paragraph);
    }

    void DrawItem(XGraphics graphics, LineItem item, double penX, double baseline)
    {
        var properties = item.Props;
        var drawBaseline = baseline;
        if (properties.VerticalAlignment == VerticalRunAlignment.Superscript)
        {
            drawBaseline -= properties.FontSizePoints * 0.33;
        }
        else if (properties.VerticalAlignment == VerticalRunAlignment.Subscript)
        {
            drawBaseline += properties.FontSizePoints * 0.14;
        }

        var color = PdfRenderContext.ParseColor(properties.ColorHex);
        graphics.DrawString(item.Text!, item.Font, context.GetBrush(color), new XPoint(penX, drawBaseline), baselineFormat);

        if (properties.Underline)
        {
            var pen = context.GetPen(color, Math.Max(0.5, item.Font.Size / 16));
            var y = drawBaseline + item.Font.Size * 0.12;
            graphics.DrawLine(pen, penX, y, penX + item.Width, y);
        }

        if (properties.Strikethrough)
        {
            var pen = context.GetPen(color, Math.Max(0.5, item.Font.Size / 16));
            var y = drawBaseline - item.Ascent * 0.3;
            graphics.DrawLine(pen, penX, y, penX + item.Width, y);
        }
    }

    // Fills a tab gap with its leader: a baseline rule for Underscore, otherwise the leader glyph
    // tiled across the gap (leaving ~one glyph of trailing padding). Mirrors the Skia backend.
    void DrawTabLeader(XGraphics graphics, LineItem item, double penX, double baseline)
    {
        if (item.Width <= 0 || item.TabLeader == TabLeader.None)
        {
            return;
        }

        var color = PdfRenderContext.ParseColor(item.Props.ColorHex);

        if (item.TabLeader == TabLeader.Underscore)
        {
            var pen = context.GetPen(color, Math.Max(0.5, item.Font.Size / 16));
            var y = baseline + item.Font.Size * 0.12;
            graphics.DrawLine(pen, penX, y, penX + item.Width, y);
            return;
        }

        var leaderChar = item.TabLeader switch
        {
            TabLeader.Dot => '.',
            TabLeader.Hyphen => '-',
            TabLeader.MiddleDot => '·',
            TabLeader.Heavy => '—',
            _ => '.'
        };

        var glyphWidth = LeaderGlyphWidth(leaderChar, item.Font);
        if (glyphWidth <= 0)
        {
            return;
        }

        var count = (int) Math.Floor((item.Width - glyphWidth) / glyphWidth);
        if (count <= 0)
        {
            return;
        }

        graphics.DrawString(new(leaderChar, count), item.Font, context.GetBrush(color), new XPoint(penX, baseline), baselineFormat);
    }

    // A TOC redraws the same dot leader on every entry; the glyph width is constant per font.
    readonly Dictionary<(char Leader, XFont Font), double> leaderGlyphWidths = [];

    double LeaderGlyphWidth(char leaderChar, XFont font)
    {
        var key = (leaderChar, font);
        if (!leaderGlyphWidths.TryGetValue(key, out var width))
        {
            width = measure.MeasureString(leaderChar.ToString(), font).Width;
            leaderGlyphWidths[key] = width;
        }

        return width;
    }

    void DrawImage(XGraphics? graphics, LineItem item, double penX, double baseline)
    {
        if (graphics == null || item.ImageData == null)
        {
            return;
        }

        try
        {
            var image = context.GetImage(item.ImageData);
            graphics.DrawImage(image, penX, baseline - item.ImageHeight, item.ImageWidth, item.ImageHeight);
        }
        catch
        {
            // Unsupported inline image format (e.g. SVG): skip rather than fail the whole render.
        }
    }

    static readonly XStringFormat baselineFormat = new()
    {
        Alignment = XStringAlignment.Near,
        LineAlignment = XLineAlignment.BaseLine
    };

    // ---- Layout ----

    // LeftIndentPoints already carries the resolved numbering cascade (direct, numbering-level
    // and style <w:ind> per Word's precedence), so use it for numbered paragraphs too. Reading
    // the raw numbering.IndentPoints instead ignored a style that tightens the list indent and
    // made the list over-indent (e.g. agendas-minutes/17). Matches the Skia/ImageSharp backends.
    static double Indent(ParagraphElement paragraph) =>
        paragraph.Properties.LeftIndentPoints;

    // Width of the text that follows a tab, up to the next tab or a line break — what Right/Centre/
    // Decimal stops need to know to place the following text so it ends at (Right) or straddles
    // (Centre) the stop. Mirrors the Skia backend's MeasureFollowingWidthNoScale.
    double MeasureFollowingWidth(IReadOnlyList<Run> runs, int startIndex)
    {
        var total = 0d;
        for (var index = startIndex; index < runs.Count; index++)
        {
            var run = runs[index];
            if (run.IsTab)
            {
                break;
            }

            if (run.InlineImageData is {Length: > 0})
            {
                total += run.InlineImageWidthPoints > 0 ? run.InlineImageWidthPoints : 12;
                continue;
            }

            if (run.Text.Contains('\n') || run.Text.Contains('\r'))
            {
                break;
            }

            var text = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            total += measure.MeasureString(text, context.GetFont(run.Properties)).Width;
        }

        return total;
    }

    // Width of the text following a tab up to (but excluding) the first '.', for Decimal stops so
    // the decimal points line up. Null when no '.' is found — the resolver then treats the Decimal
    // stop as Right alignment, matching Word's fallback.
    double? MeasureFollowingDecimalPrefix(IReadOnlyList<Run> runs, int startIndex)
    {
        var total = 0d;
        for (var index = startIndex; index < runs.Count; index++)
        {
            var run = runs[index];
            if (run.IsTab ||
                run.InlineImageData is {Length: > 0} ||
                run.Text.Contains('\n') ||
                run.Text.Contains('\r'))
            {
                break;
            }

            var text = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            var font = context.GetFont(run.Properties);
            var dotIndex = text.IndexOf('.');
            if (dotIndex >= 0)
            {
                return total + measure.MeasureString(text[..dotIndex], font).Width;
            }

            total += measure.MeasureString(text, font).Width;
        }

        return null;
    }

    // When a Right/Centre/Decimal stop is clamped to the content edge (its real position lies past
    // it), Word fills the leader to the edge and hides the post-tab text (a TOC page number that
    // would overflow). Returns the last run index to consume so the caller skips that text.
    static int SkipFollowingTabContent(IReadOnlyList<Run> runs, int tabRunIndex)
    {
        var lastConsumed = tabRunIndex;
        for (var index = tabRunIndex + 1; index < runs.Count; index++)
        {
            var run = runs[index];
            if (run.IsTab ||
                (!string.IsNullOrEmpty(run.Text) && (run.Text.Contains('\n') || run.Text.Contains('\r'))))
            {
                break;
            }

            lastConsumed = index;
        }

        return lastConsumed;
    }

    List<Line> Layout(ParagraphElement paragraph, double availableWidth)
    {
        // The autofit minimum-width probe (1pt) produces a line per word that is only reduced
        // to a max; keep those out of the cache instead of retaining them for the render.
        var cacheable = availableWidth > 1;
        var cacheKey = (paragraph, availableWidth);
        if (cacheable && layoutCache.TryGetValue(cacheKey, out var cachedLines))
        {
            return cachedLines;
        }

        var lines = new List<Line>();
        if (availableWidth <= 0)
        {
            availableWidth = 1;
        }

        var multiplier = paragraph.Properties.LineSpacingRule == LineSpacingRule.Auto
            ? paragraph.Properties.LineSpacingMultiplier
            : 1;

        var leftIndent = Indent(paragraph);

        // A line carrying tab-positioned content may overflow the content width into the right margin
        // (up to the page edge) without wrapping: Word/Skia place text at a tab stop and let it spill
        // into the margin rather than breaking the word onto a new line. Plain (non-tab) lines still
        // wrap at the content width.
        var rightMarginPoints = (float) context.PageSettings.MarginRight;

        var current = new Line();
        var pendingSpaceWidth = 0d;
        XFont? pendingSpaceFont = null;
        RunProperties? pendingSpaceProps = null;
        var lineHasTab = false;

        double WrapLimit() => lineHasTab ? availableWidth + rightMarginPoints : availableWidth;

        void Flush()
        {
            if (current.Items.Count > 0)
            {
                lines.Add(current);
            }

            current = new();
            pendingSpaceWidth = 0;
            pendingSpaceFont = null;
            pendingSpaceProps = null;
            lineHasTab = false;
        }

        void Account(LineItem item)
        {
            current.Items.Add(item);
            current.Width += item.Width;
            current.Ascent = Math.Max(current.Ascent, item.Ascent);
            current.Height = Math.Max(current.Height, item.Height);
        }

        for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
        {
            var run = paragraph.Runs[runIndex];
            if (run.Properties.Hidden)
            {
                continue;
            }

            if (run.InlineImageData != null || run.InlineImageRasterFallbackData != null)
            {
                var data = run.InlineImageContentType == "image/svg+xml"
                    ? run.InlineImageRasterFallbackData
                    : run.InlineImageData ?? run.InlineImageRasterFallbackData;
                if (data == null)
                {
                    continue;
                }

                var width = run.InlineImageWidthPoints > 0 ? run.InlineImageWidthPoints : 12;
                var height = run.InlineImageHeightPoints > 0 ? run.InlineImageHeightPoints : 12;
                if (current.Items.Count > 0 && current.Width + pendingSpaceWidth + width > WrapLimit())
                {
                    Flush();
                }

                Account(
                    new()
                    {
                        IsImage = true,
                        ImageData = data,
                        ImageWidth = width,
                        ImageHeight = height,
                        Width = width,
                        Ascent = height,
                        Height = height
                    });
                continue;
            }

            var font = context.GetFont(run.Properties);
            var (spaceWidth, ascent, rawHeight) = Metrics(font);
            var lineHeight = rawHeight * multiplier;

            if (run.IsTab)
            {
                if (current.Items.Count > 0)
                {
                    // Flush any pending space so the tab measures from the real cursor position.
                    if (pendingSpaceWidth > 0)
                    {
                        Account(
                            new()
                            {
                                Text = " ",
                                Props = pendingSpaceProps ?? run.Properties,
                                Font = pendingSpaceFont ?? font,
                                Width = pendingSpaceWidth,
                                IsSpace = true,
                                Ascent = Ascent(pendingSpaceFont ?? font),
                                Height = Metrics(pendingSpaceFont ?? font).RawHeight * multiplier
                            });
                        current.SpaceCount++;
                        pendingSpaceWidth = 0;
                        pendingSpaceFont = null;
                        pendingSpaceProps = null;
                    }

                    // Snap to the next tab stop via the shared resolver, which handles left/centre/
                    // right/decimal alignment (Right is what TOC entries use) by measuring the text
                    // that follows the tab. The gap becomes a tab-filler item carrying the stop's
                    // leader (dots/hyphens/underscore) so it renders like Word, not a plain space.
                    var cursorFromLeft = leftIndent + current.Width;
                    var tabRunIndex = runIndex;
                    var decimalPrefix = paragraph.Properties.HasDecimalTabStop()
                        ? MeasureFollowingDecimalPrefix(paragraph.Runs, runIndex + 1)
                        : null;
                    var (destination, matchedStop, suppressFollowing) = TabStopResolver.Resolve(
                        cursorFromLeft,
                        () => MeasureFollowingWidth(paragraph.Runs, tabRunIndex + 1),
                        paragraph.Properties.TabStops,
                        paragraph.Properties.DefaultTabStopPoints,
                        leftIndent,
                        decimalPrefix,
                        availableEndX: leftIndent + availableWidth);
                    var gap = destination - cursorFromLeft;
                    if (gap > 0 && current.Width + gap <= WrapLimit())
                    {
                        Account(
                            new()
                            {
                                Text = "",
                                Props = run.Properties,
                                Font = font,
                                Width = gap,
                                IsTabFiller = true,
                                TabLeader = matchedStop?.Leader ?? TabLeader.None,
                                Ascent = ascent,
                                Height = lineHeight
                            });
                        lineHasTab = true;
                    }

                    if (suppressFollowing)
                    {
                        runIndex = SkipFollowingTabContent(paragraph.Runs, runIndex);
                    }
                }

                continue;
            }

            var text = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            foreach (var token in Tokenize(text))
            {
                if (token.IsSpace)
                {
                    // An explicit line break (<w:br/>) reaches us as a "\n" run. Newlines are
                    // whitespace, so without this they'd fold into the pending space and the
                    // following text would stay on the same line. Break once per newline,
                    // preserving blank lines for consecutive or leading breaks (matches the
                    // Skia/ImageSharp engines).
                    var breakCount = token.Text.Count(_ => _ == '\n');
                    if (breakCount > 0)
                    {
                        for (var i = 0; i < breakCount; i++)
                        {
                            if (current.Items.Count > 0)
                            {
                                Flush();
                            }
                            else
                            {
                                lines.Add(new() {Ascent = ascent, Height = lineHeight});
                                pendingSpaceWidth = 0;
                                pendingSpaceFont = null;
                                pendingSpaceProps = null;
                            }
                        }

                        continue;
                    }

                    pendingSpaceWidth += spaceWidth * token.Text.Length;
                    pendingSpaceFont = font;
                    pendingSpaceProps = run.Properties;
                    continue;
                }

                var wordWidth = measure.MeasureString(token.Text, font).Width;
                if (current.Items.Count > 0 && current.Width + pendingSpaceWidth + wordWidth > WrapLimit())
                {
                    Flush();
                }
                else if (pendingSpaceWidth > 0 && current.Items.Count > 0)
                {
                    Account(
                        new()
                        {
                            Text = " ",
                            Props = pendingSpaceProps ?? run.Properties,
                            Font = pendingSpaceFont ?? font,
                            Width = pendingSpaceWidth,
                            IsSpace = true,
                            Ascent = Ascent(pendingSpaceFont ?? font),
                            Height = Metrics(pendingSpaceFont ?? font).RawHeight * multiplier
                        });
                    current.SpaceCount++;
                }

                pendingSpaceWidth = 0;
                pendingSpaceFont = null;
                pendingSpaceProps = null;

                Account(
                    new()
                    {
                        Text = token.Text,
                        Props = run.Properties,
                        Font = font,
                        Width = wordWidth,
                        Ascent = ascent,
                        Height = lineHeight
                    });
            }
        }

        Flush();

        if (lines.Count > 0)
        {
            lines[^1].IsLast = true;
        }

        if (cacheable)
        {
            layoutCache[cacheKey] = lines;
        }

        return lines;
    }

    double Ascent(XFont font) => Metrics(font).Ascent;

    static double ComputeAscent(XFont font)
    {
        var metrics = font.Metrics;
        if (metrics.UnitsPerEm > 0)
        {
            return (double) metrics.Ascent / metrics.UnitsPerEm * font.Size;
        }

        return font.GetHeight() * 0.8;
    }

    static IEnumerable<Token> Tokenize(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var isSpace = char.IsWhiteSpace(text[index]);
            var start = index;
            while (index < text.Length && char.IsWhiteSpace(text[index]) == isSpace)
            {
                index++;
            }

            yield return new(isSpace, text[start..index]);
        }
    }

    readonly record struct Token(bool IsSpace, string Text);

    sealed class LineItem
    {
        public string? Text;
        public RunProperties Props = new();
        public XFont Font = null!;
        public double Width;
        public double Ascent;
        public double Height;
        public bool IsSpace;
        public bool IsImage;
        public byte[]? ImageData;
        public double ImageWidth;
        public double ImageHeight;
        public bool IsTabFiller;
        public TabLeader TabLeader;
    }

    sealed class Line
    {
        public List<LineItem> Items { get; } = [];
        public double Width;
        public double Ascent;
        public double Height;
        public bool IsLast;
        public int SpaceCount;
    }
}
