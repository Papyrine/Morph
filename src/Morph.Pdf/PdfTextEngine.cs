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

            // List marker hangs to the left of the first line's text by the numbering's
            // hanging indent (Word's model, matching the Skia/ImageSharp backends), and takes
            // the colour of the paragraph's first run so a white-on-dark list keeps white
            // bullets. Falls back to a snug gap when no hanging indent is defined.
            if (!markerDrawn && paragraph.Properties.Numbering is {Text.Length: > 0} numbering)
            {
                markerDrawn = true;
                if (graphics != null)
                {
                    var firstProperties = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Properties : new();
                    var markerFont = context.GetFont(firstProperties.FontFamily, firstProperties.Bold, false, firstProperties.FontSizePoints);
                    var markerText = numbering.Text;
                    var markerBrush = new XSolidBrush(PdfRenderContext.ParseColor(firstProperties.ColorHex));
                    var markerX = numbering.HangingIndentPoints > 0.01
                        ? penX - numbering.HangingIndentPoints
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

    static void DrawItem(XGraphics graphics, LineItem item, double penX, double baseline)
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

        var brush = new XSolidBrush(PdfRenderContext.ParseColor(properties.ColorHex));
        graphics.DrawString(item.Text!, item.Font, brush, new XPoint(penX, drawBaseline), baselineFormat);

        if (properties.Underline)
        {
            var pen = new XPen(PdfRenderContext.ParseColor(properties.ColorHex), Math.Max(0.5, item.Font.Size / 16));
            var y = drawBaseline + item.Font.Size * 0.12;
            graphics.DrawLine(pen, penX, y, penX + item.Width, y);
        }

        if (properties.Strikethrough)
        {
            var pen = new XPen(PdfRenderContext.ParseColor(properties.ColorHex), Math.Max(0.5, item.Font.Size / 16));
            var y = drawBaseline - item.Ascent * 0.3;
            graphics.DrawLine(pen, penX, y, penX + item.Width, y);
        }
    }

    static void DrawImage(XGraphics? graphics, LineItem item, double penX, double baseline)
    {
        if (graphics == null || item.ImageData == null)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(item.ImageData);
            var image = XImage.FromStream(stream);
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

    static double Indent(ParagraphElement paragraph)
    {
        var numbering = paragraph.Properties.Numbering;
        if (numbering != null)
        {
            return numbering.IndentPoints;
        }

        return paragraph.Properties.LeftIndentPoints;
    }

    // Advance for a tab character. When the paragraph defines explicit Left tab stops and one lies
    // past the cursor, snap to it; otherwise keep the historical fixed 0.5" advance so paragraphs
    // without custom stops render unchanged. (Only Left stops are honoured — the merged text-frame
    // icon/label gap is the sole caller that relies on this; richer tab alignment stays with Skia.)
    static double ResolveTabAdvance(ParagraphProperties properties, double cursorFromLeft)
    {
        const double defaultTabWidth = 36d;
        foreach (var stop in properties.TabStops)
        {
            if (stop.Alignment == TabAlignment.Left &&
                stop.PositionPoints > cursorFromLeft)
            {
                return stop.PositionPoints - cursorFromLeft;
            }
        }

        return defaultTabWidth;
    }

    List<Line> Layout(ParagraphElement paragraph, double availableWidth)
    {
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
        var pendingSpaceFont = (XFont?) null;
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

        foreach (var run in paragraph.Runs)
        {
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
            var ascent = Ascent(font);
            var lineHeight = font.GetHeight() * multiplier;

            if (run.IsTab)
            {
                if (current.Items.Count > 0)
                {
                    // Current x measured from the line's left edge (= paragraph left indent).
                    var cursorFromLeft = leftIndent + current.Width + pendingSpaceWidth;
                    var tabWidth = ResolveTabAdvance(paragraph.Properties, cursorFromLeft);
                    pendingSpaceWidth += tabWidth;
                    pendingSpaceFont = font;
                    pendingSpaceProps = run.Properties;
                    lineHasTab = true;
                }

                continue;
            }

            var text = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            foreach (var token in Tokenize(text))
            {
                if (token.IsSpace)
                {
                    pendingSpaceWidth += measure.MeasureString(" ", font).Width * token.Text.Length;
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
                            Height = (pendingSpaceFont ?? font).GetHeight() * multiplier
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

        return lines;
    }

    static double Ascent(XFont font)
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
