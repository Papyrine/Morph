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

    // The table-cell measurers take the cell's inner width and remove BOTH indents to get the wrap
    // width, exactly as RenderInBounds draws it and as the Skia/ImageSharp measurers do. Previously
    // they passed the raw width to Layout, so a left- or right-indented cell paragraph was measured
    // wider than it renders — the row was under-sized.
    public List<float> LayoutParagraphForMeasurement(ParagraphElement paragraph, float maxWidth)
    {
        var lines = Layout(paragraph, maxWidth - Indent(paragraph) - RightIndent(paragraph));
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

    // Autofit column widths use this for a cell paragraph's natural (unwrapped) and minimum (widest
    // word) content width, probed with sentinel widths. Returns the bare widest line width — the
    // shared TableLayout adds cell padding/margin itself. Adding the left indent here made the PDF's
    // autofit columns a left-indent wider than the Skia/ImageSharp measurers (which return bare
    // widest); the raster is the reference, so match it.
    public float MeasureParagraphNaturalWidth(ParagraphElement paragraph, float maxWidth)
    {
        var widest = 0d;
        foreach (var line in Layout(paragraph, maxWidth))
        {
            widest = Math.Max(widest, line.Width);
        }

        return (float) widest;
    }

    public float MeasureParagraphHeightWithWidth(ParagraphElement paragraph, float maxWidth) =>
        (float) MeasureHeight(paragraph, maxWidth - Indent(paragraph) - RightIndent(paragraph));

    /// <summary>Height the paragraph will consume in the page flow: <see cref="MeasureHeight"/>
    /// with the cross-paragraph spacing collapse that <see cref="Render"/> applies
    /// (max(after, before) between neighbours) folded in, so keep decisions test the height
    /// that drawing will actually use. <paramref name="previousSpacingAfter"/> is the
    /// spacing-after of whatever renders before this paragraph.
    /// <paramref name="maxWidth"/> is the full section content width; the paragraph's left and
    /// right indents are subtracted here so the measured wrap matches what <see cref="Render"/>
    /// draws (both now honour the right indent — issue #151 follow-up).</summary>
    public float MeasureFlowHeight(ParagraphElement paragraph, double maxWidth, double previousSpacingAfter)
    {
        var height = MeasureHeight(paragraph, maxWidth - Indent(paragraph) - RightIndent(paragraph));
        // On a same-style contextual collapse the render steps back over the whole previous
        // after-spacing (plus this paragraph's before, which it never adds); otherwise it's the
        // usual max(after, before) margin collapse folded in as the overlap.
        var reduction = Collapses(paragraph)
            ? SpacingBefore(paragraph) + previousSpacingAfter
            : Math.Min(SpacingBefore(paragraph), previousSpacingAfter);
        return (float) (height - reduction);
    }

    public double MeasureHeight(ParagraphElement paragraph, double maxWidth)
    {
        var lines = Layout(paragraph, maxWidth);
        var total = SpacingBefore(paragraph) + SpacingAfter(paragraph) + BorderSpaceExcess(paragraph.Properties);
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

    // Paragraph borders (w:pBdr) draw inside the spacing-before/after regions where they fit; only
    // the part of w:space that exceeds the adjacent spacing pushes neighbours apart and adds height.
    // Mirrors Morph.Skia's BorderSpaceExcess so pagination matches what Draw advances.
    static double BorderSpaceExcess(ParagraphProperties properties)
    {
        if (properties.Borders is not {HasAnyBorder: true} borders)
        {
            return 0;
        }

        var extra = 0d;
        if (borders.Top.IsVisible)
        {
            extra += Math.Max(0, properties.BorderTopSpacePoints - properties.SpacingBeforePoints);
        }

        if (borders.Bottom.IsVisible)
        {
            extra += Math.Max(0, properties.BorderBottomSpacePoints - properties.SpacingAfterPoints);
        }

        return extra;
    }

    double EmptyLineHeight(ParagraphElement paragraph)
    {
        // Word collapses the end-of-cell mark after a nested table to zero height. An anchor-only
        // mark (a paragraph left holding only out-of-flow drawings) still gets its own line at the
        // mark's font size, so the following block sits one line lower — matches the raster engines.
        if (paragraph.IsCollapsedCellMark)
        {
            return 0;
        }

        var properties = paragraph.Properties;
        // An empty paragraph's line takes the paragraph mark's resolved formatting
        // (w:pPr/w:rPr over the style chain) — not the default face at a fixed size — under
        // the paragraph's line-spacing rule (Auto multiplies, Exactly forces, AtLeast floors).
        var font = properties.ParagraphMarkRunProperties is { } markProps
            ? context.GetFont(markProps)
            : context.GetFont(DefaultFontSettings.DefaultFont, false, false, properties.ParagraphMarkFontSizePoints ?? 11);
        return properties.LineSpacingRule switch
        {
            LineSpacingRule.Exactly => properties.LineSpacingPoints,
            LineSpacingRule.AtLeast => Math.Max(font.GetHeight(), properties.LineSpacingPoints),
            _ => font.GetHeight() * properties.LineSpacingMultiplier
        };
    }

    // ---- Paragraph spacing (mirrors the Skia/ImageSharp contextual-spacing collapse) ----

    // True when this paragraph and the previous one share a style and both opt into
    // w:contextualSpacing — Word then removes the gap between them (e.g. a Details block of
    // Date/Time/Facilitator sits tight). The gap is closed by stepping back over the previous
    // paragraph's spacing-after, not by dropping either paragraph's own spacing, so both the
    // before and after values below stay raw.
    bool Collapses(ParagraphElement paragraph)
    {
        var properties = paragraph.Properties;
        var sameStyle = properties.StyleId == context.LastParagraphStyleId;
        return properties.ContextualSpacing && context.LastParagraphHadContextualSpacing && sameStyle;
    }

    // Raw spacing-before; a same-style contextual collapse is applied by the caller stepping back
    // over the previous paragraph's spacing-after, not folded in here.
    static double SpacingBefore(ParagraphElement paragraph) => paragraph.Properties.SpacingBeforePoints;

    // Raw spacing-after — contextual spacing does NOT drop it; the next same-style paragraph's
    // collapse removes the gap instead. Exposed so the page renderer's keep gates can collapse a
    // kept-next paragraph against this paragraph's after-spacing.
    internal static double SpacingAfter(ParagraphElement paragraph) =>
        paragraph.Properties.SpacingAfterPoints;

    void TrackContextualSpacing(ParagraphElement paragraph)
    {
        context.LastParagraphStyleId = paragraph.Properties.StyleId;
        context.LastParagraphHadContextualSpacing = paragraph.Properties.ContextualSpacing;
        // Tracked in both flow and bounded (cell) rendering: a following same-style contextual
        // paragraph steps back over this value to close the gap. Cells reset it per cell (see
        // PageRendererBase.RenderTableCell) so it can't leak across a cell boundary.
        context.LastParagraphSpacingAfterPoints = (float) SpacingAfter(paragraph);
    }

    // ---- Drawing ----

    /// <summary>Invoked when a line won't fit on the current page during flow rendering. The
    /// renderer finishes the current page and starts a new one (resetting CurrentY / Graphics).</summary>
    public Action? RequestNewPage { get; set; }

    /// <summary>Draws the paragraph at the current flow position, advancing <see cref="RenderContextBase.CurrentY"/>.</summary>
    public void Render(ParagraphElement paragraph, DocumentElement? nextElement = null)
    {
        var maxWidth = context.ContentWidth - Indent(paragraph) - RightIndent(paragraph);
        Draw(paragraph, context.ContentLeft + Indent(paragraph), maxWidth, allowPageBreak: true, nextElement);
    }

    /// <summary>Draws the paragraph constrained to a bounded region (table cell), no page breaks.
    /// <paramref name="maxWidth"/> is the region's inner width; both indents come off the wrap width
    /// (a bulleted / right-indented cell paragraph wraps within the indented region) while only the
    /// left indent shifts the start position.</summary>
    public void RenderInBounds(ParagraphElement paragraph, double x, double maxWidth)
    {
        var indent = Indent(paragraph);
        Draw(paragraph, x + indent, maxWidth - indent - RightIndent(paragraph), allowPageBreak: false);
    }

    void Draw(ParagraphElement paragraph, double left, double availableWidth, bool allowPageBreak, DocumentElement? nextElement = null)
    {
        var lines = Layout(paragraph, availableWidth);

        // Bar tabs (w:tab w:val="bar") draw a vertical rule over the paragraph's whole cell —
        // captured before spacing-before so consecutive bar-tab paragraphs' rules join into one
        // continuous separator, matching the Skia/ImageSharp backends. Flow path only (like borders).
        var barTabStartY = (double) context.CurrentY;

        // Word collapses adjacent paragraph spacing to max(after, before) — verified against
        // Word-generated XPS baselines — so in the page flow only the excess of this
        // paragraph's spacing-before over the previous paragraph's spacing-after consumes
        // height. Bounded (table-cell) rendering keeps the raw before (no flow margin collapse).
        double spacingBefore;
        if (Collapses(paragraph))
        {
            // Same-style contextual paragraphs sit tight: step back over the after-spacing the
            // previous paragraph already advanced past so the gap collapses to zero.
            spacingBefore = -context.LastParagraphSpacingAfterPoints;
        }
        else
        {
            spacingBefore = SpacingBefore(paragraph);
            if (allowPageBreak)
            {
                spacingBefore = Math.Max(0, spacingBefore - context.LastParagraphSpacingAfterPoints);
            }
        }

        // Word drops spacing-before at the top of an automatically broken page (one-shot, set by
        // the page renderer) — including any contextual step-back.
        if (allowPageBreak && context.SuppressPageTopSpacingBefore)
        {
            spacingBefore = 0;
        }

        context.SuppressPageTopSpacingBefore = false;
        context.CurrentY += (float) spacingBefore;

        if (lines.Count == 0)
        {
            context.CurrentY += (float) EmptyLineHeight(paragraph);
            context.CurrentY += (float) SpacingAfter(paragraph);
            if (allowPageBreak)
            {
                DrawBarTabs(paragraph.Properties, barTabStartY, context.CurrentY - barTabStartY);
            }
            TrackContextualSpacing(paragraph);
            return;
        }

        // An RTL paragraph flips the visual meaning of "leading-edge" alignment to the page's right
        // edge. Glyphs aren't reordered (no BiDi shaper), but the line at least lands on the right —
        // matching the Skia/ImageSharp engines.
        var alignment = paragraph.Properties is {IsRightToLeft: true, Alignment: TextAlignment.Left}
            ? TextAlignment.Right
            : paragraph.Properties.Alignment;
        var markerDrawn = false;

        // Word's widow/orphan control (w:widowControl, on by default — mapped to two lines on
        // each side of a split): a paragraph may not break leaving fewer than two lines at the
        // page bottom or carrying fewer than two lines forward. When fewer than two lines fit —
        // or trimming the split to carry two forward would leave fewer than two behind — the
        // whole paragraph moves; otherwise a split that would carry a single line forward breaks
        // one line earlier. Abandoned at the top of a page/column (moving cannot gain space),
        // the same family as the keep-rule guards.
        var widowControlled = allowPageBreak && paragraph.Properties.WidowControl && lines.Count >= 2;
        var forcedBreakIndex = -1;
        if (widowControlled && RequestNewPage != null)
        {
            var fit = CountLinesThatFit(lines, 0);
            if (fit < lines.Count)
            {
                var carried = lines.Count - fit;
                var moveWhole = fit < 2 || (carried == 1 && fit - 1 < 2);
                if (moveWhole && context.CurrentY > context.ContentTop)
                {
                    RequestNewPage();
                    left = context.ContentLeft + Indent(paragraph);
                }

                forcedBreakIndex = PlanWidowBreak(lines, 0);
            }
        }

        // Paragraph borders (w:pBdr) are a flow-path feature (allowPageBreak): Word does not border a
        // paragraph inside a table cell, matching the Skia/ImageSharp backends. Reserve the top
        // w:space that exceeds spacing-before so the top edge clears the previous paragraph; inside a
        // w:between chain reserve the full top space so the text clears the shared line above. Done
        // after any whole-paragraph widow move so the edges anchor to where the text actually starts.
        var borders = paragraph.Properties.Borders;
        var drawBorders = allowPageBreak && borders is {HasAnyBorder: true};
        var bottomSpaceExtra = 0d;
        if (drawBorders)
        {
            var properties = paragraph.Properties;
            var inBetweenChain = context.SuppressNextParagraphTopBorder;
            var topSpaceExtra = borders!.Top.IsVisible || inBetweenChain
                ? inBetweenChain
                    ? properties.BorderTopSpacePoints
                    : Math.Max(0, properties.BorderTopSpacePoints - properties.SpacingBeforePoints)
                : 0;
            bottomSpaceExtra = borders.Bottom.IsVisible
                ? Math.Max(0, properties.BorderBottomSpacePoints - properties.SpacingAfterPoints)
                : 0;
            context.CurrentY += (float) topSpaceExtra;
        }

        var paragraphStartY = (double) context.CurrentY;

        // Line numbers (w:lnNumType) render in the left-margin gutter on the flow path only, one per
        // line, skipping w:suppressLineNumbers paragraphs (which then also don't advance the shared
        // counter) — matching the Skia/ImageSharp backends. The counter is initialized and reset per
        // page/section by the page renderer.
        var lineNumberSettings = allowPageBreak ? context.PageSettings.LineNumbers : null;
        var showLineNumbers = lineNumberSettings != null && !paragraph.Properties.SuppressLineNumbers;

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            if (allowPageBreak &&
                context.CurrentY > context.ContentTop &&
                (context.CurrentY + line.Height > context.ContentBottom || lineIndex == forcedBreakIndex) &&
                RequestNewPage != null)
            {
                RequestNewPage();
                // RequestNewPage may advance to the next column instead of a new page; either way
                // the section's content-left can shift, so rebase this line's start onto the
                // current column. (availableWidth is the column width, unchanged by the move.)
                left = context.ContentLeft + Indent(paragraph);
                if (widowControlled)
                {
                    forcedBreakIndex = PlanWidowBreak(lines, lineIndex);
                }
            }

            var graphics = context.Graphics;
            var lineTop = (double) context.CurrentY;
            var baseline = lineTop + line.Ascent;

            // Advance the shared counter for every line (numbering counts wrapped lines too) and draw
            // the number in the gutter — RenderLineNumber only paints the every-CountBy line.
            if (showLineNumbers)
            {
                RenderLineNumber(context.GetNextLineNumber(), baseline, lineNumberSettings!);
            }

            // The first line's start shifts by its signed offset (see FirstLineOffset): right for a
            // first-line indent, LEFT (outdent) for a markerless hanging indent; its alignment box
            // resizes to match (Layout adjusted the wrap width the same way). Continuation lines sit
            // at the left indent unchanged.
            var firstLineOffset = lineIndex == 0 ? FirstLineOffset(paragraph) : 0;
            var lineWidth = availableWidth - firstLineOffset;
            var penX = left + firstLineOffset;
            var extraSpace = 0d;
            if (alignment == TextAlignment.Center)
            {
                penX += Math.Max(0, (lineWidth - line.Width) / 2);
            }
            else if (alignment == TextAlignment.Right)
            {
                penX += Math.Max(0, lineWidth - line.Width);
            }
            else if (alignment == TextAlignment.Justify && line is {IsLast: false, SpaceCount: > 0})
            {
                extraSpace = Math.Max(0, lineWidth - line.Width) / line.SpaceCount;
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
                if (item.ShapeGroup != null)
                {
                    DrawShapeGroup(graphics, item, penX, baseline);
                    penX += item.Width;
                    continue;
                }

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

        // Draw the border edges before spacing-after: the bottom edge sits at the text end plus the
        // full bottom w:space (any excess over spacing-after was reserved as bottomSpaceExtra).
        if (drawBorders && DrawParagraphBorders(paragraph, borders!, paragraphStartY, nextElement))
        {
            // Bottom edge collapsed into a shared w:between line with the next (identically bordered)
            // paragraph: that line already advanced CurrentY to the shared edge, so charge no
            // spacing-after and let the next paragraph's top space begin here.
            context.LastParagraphStyleId = paragraph.Properties.StyleId;
            context.LastParagraphHadContextualSpacing = paragraph.Properties.ContextualSpacing;
            context.LastParagraphSpacingAfterPoints = 0;
            return;
        }

        context.CurrentY += (float) bottomSpaceExtra;
        context.CurrentY += (float) SpacingAfter(paragraph);
        if (allowPageBreak)
        {
            DrawBarTabs(paragraph.Properties, barTabStartY, context.CurrentY - barTabStartY);
        }
        TrackContextualSpacing(paragraph);
    }

    // Draws the four w:pBdr edges and, when this paragraph's bottom collapses with an identically
    // bordered next paragraph, the shared w:between line. Returns true on that collapse, in which
    // case CurrentY has advanced past the between line and the caller charges no spacing-after.
    // Mirrors the Skia/ImageSharp border drawing; PDF coordinates are already points (== the PDF
    // user unit). The bottom edge is measured from CurrentY as it stands right after the last line.
    bool DrawParagraphBorders(ParagraphElement paragraph, CellBorders borders, double paragraphStartY, DocumentElement? nextElement)
    {
        var properties = paragraph.Properties;
        var graphics = context.Graphics;

        var borderLeft = context.ContentLeft + Indent(paragraph) - properties.BorderLeftSpacePoints;
        var borderRight = context.ContentLeft + context.ContentWidth - RightIndent(paragraph) + properties.BorderRightSpacePoints;
        var borderTop = paragraphStartY - properties.BorderTopSpacePoints;
        var borderBottom = context.CurrentY + properties.BorderBottomSpacePoints;

        var collapseBottom = nextElement is ParagraphElement next && properties.BordersCollapseWith(next.Properties);
        var suppressTop = context.SuppressNextParagraphTopBorder;
        context.SuppressNextParagraphTopBorder = false;

        if (graphics != null)
        {
            if (borders.Bottom.IsVisible && !collapseBottom)
            {
                graphics.DrawLine(EdgePen(borders.Bottom), borderLeft, borderBottom, borderRight, borderBottom);
            }

            if (borders.Top.IsVisible && !suppressTop)
            {
                graphics.DrawLine(EdgePen(borders.Top), borderLeft, borderTop, borderRight, borderTop);
            }

            if (borders.Left.IsVisible)
            {
                graphics.DrawLine(EdgePen(borders.Left), borderLeft, borderTop, borderLeft, borderBottom);
            }

            if (borders.Right.IsVisible)
            {
                graphics.DrawLine(EdgePen(borders.Right), borderRight, borderTop, borderRight, borderBottom);
            }

            if (collapseBottom)
            {
                graphics.DrawLine(EdgePen(properties.BorderBetween), borderLeft, borderBottom, borderRight, borderBottom);
            }
        }

        if (collapseBottom)
        {
            context.SuppressNextParagraphTopBorder = true;
            // Advance past the between line so the next paragraph's top space starts here.
            context.CurrentY += (float) properties.BorderBottomSpacePoints;
        }

        return collapseBottom;
    }

    XPen EdgePen(BorderEdge edge) =>
        context.GetPen(PdfRenderContext.ParseColor(edge.ColorHex ?? "000000"), Math.Max(0.5, edge.WidthPoints));

    // Bar-tab stops (w:tab w:val="bar") draw a vertical rule at each stop position spanning the
    // paragraph's full cell height (top..top+height), so consecutive bar-tab paragraphs' rules join
    // into one continuous separator — a faithful port of the Skia/ImageSharp DrawBarTabs. Tab-stop
    // positions are measured from the section content-left, not the paragraph indent (Word's model).
    void DrawBarTabs(ParagraphProperties properties, double top, double height)
    {
        var graphics = context.Graphics;
        if (graphics == null || properties.TabStops.Count == 0)
        {
            return;
        }

        XPen? pen = null;
        foreach (var stop in properties.TabStops)
        {
            if (stop.Alignment != TabAlignment.Bar)
            {
                continue;
            }

            pen ??= context.GetPen(PdfRenderContext.ParseColor("000000"), 0.5);
            var x = context.ContentLeft + stop.PositionPoints;
            graphics.DrawLine(pen, x, top, x, top + height);
        }
    }

    // Renders a section's line number (w:lnNumType) in the left-margin gutter, right-aligned so its
    // right edge sits DistancePoints left of the content, at the line's baseline. Only every-CountBy
    // line shows a number (the counter still advances every line). Port of Skia RenderLineNumber; the
    // gutter is drawn in the page margin so it never shifts the text. 10pt default face, black.
    void RenderLineNumber(int lineNumber, double baseline, LineNumberSettings settings)
    {
        if (lineNumber % settings.CountBy != 0)
        {
            return;
        }

        var graphics = context.Graphics;
        if (graphics == null)
        {
            return;
        }

        var font = context.GetFont(DefaultFontSettings.DefaultFont, false, false, 10);
        var text = lineNumber.ToString();
        var rightEdge = context.ContentLeft - settings.DistancePoints;
        var x = rightEdge - measure.MeasureString(text, font).Width;
        graphics.DrawString(text, font, context.GetBrush(PdfRenderContext.ParseColor("000000")), new XPoint(x, baseline), baselineFormat);
    }

    // Consecutive lines from startIndex that fit above the page bottom, measured from the
    // current flow position with the same float accumulation and comparison as the draw loop.
    int CountLinesThatFit(List<Line> lines, int startIndex)
    {
        var y = context.CurrentY;
        var count = 0;
        for (var index = startIndex; index < lines.Count; index++)
        {
            if (y + lines[index].Height > context.ContentBottom)
            {
                break;
            }

            y += (float) lines[index].Height;
            count++;
        }

        return count;
    }

    // Index of the line to force onto the next page so the split carries at least two lines
    // forward while leaving at least two behind, or -1 when the natural break already
    // satisfies the widow rule (or no earlier break can produce a valid split).
    int PlanWidowBreak(List<Line> lines, int startIndex)
    {
        var fit = CountLinesThatFit(lines, startIndex);
        var remaining = lines.Count - startIndex;
        if (fit >= remaining)
        {
            return -1;
        }

        if (remaining - fit == 1 && fit - 1 >= 2)
        {
            return startIndex + fit - 1;
        }

        return -1;
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
            var top = baseline - item.ImageHeight;
            var state = graphics.Save();
            if (item.ImageRotationDegrees != 0)
            {
                // a:xfrm/@rot: rotate around the image centre, matching DrawBlockImage / the raster.
                graphics.RotateAtTransform(item.ImageRotationDegrees, new(penX + item.ImageWidth / 2, top + item.ImageHeight / 2));
            }

            if (item.Crop is {IsCropped: true} crop)
            {
                // a:srcRect crop: enlarge the image so its visible sub-rectangle covers the box, then
                // clip back to the box (same technique as DrawRaster / the shape-group path).
                var (dx, dy, dw, dh) = crop.Expand(penX, top, item.ImageWidth, item.ImageHeight);
                graphics.IntersectClip(new XRect(penX, top, item.ImageWidth, item.ImageHeight));
                graphics.DrawImage(image, dx, dy, dw, dh);
            }
            else
            {
                graphics.DrawImage(image, penX, top, item.ImageWidth, item.ImageHeight);
            }

            graphics.Restore(state);
        }
        catch
        {
            // Unsupported inline image format (e.g. SVG): skip rather than fail the whole render.
        }
    }

    // EMU = English Metric Units. 1 point = 12700 EMU.
    const double emusPerPoint = 12700;

    /// <summary>
    /// Draws a <c>wpg:wgp</c> group that flows with the text — Word's icon and photo bubbles, and
    /// the connector-line arrow glyphs. Shape coordinates are in the group's child coordinate
    /// space; they scale into the fragment's rectangle, which sits on the baseline like an image.
    /// </summary>
    void DrawShapeGroup(XGraphics? graphics, LineItem item, double penX, double baseline)
    {
        if (graphics == null || item.ShapeGroup is not { } group)
        {
            return;
        }

        var top = baseline - item.ImageHeight;
        var scaleX = item.ImageWidth / group.ChildExtentX;
        var scaleY = item.ImageHeight / group.ChildExtentY;

        var state = graphics.Save();
        if (group.RotationDegrees != 0)
        {
            graphics.RotateAtTransform(group.RotationDegrees, new(penX + item.ImageWidth / 2, top + item.ImageHeight / 2));
        }

        foreach (var shape in group.Shapes)
        {
            var x = penX + shape.X * scaleX;
            var y = top + shape.Y * scaleY;
            var width = shape.Width * scaleX;
            var height = shape.Height * scaleY;

            if (shape.Geometry == GroupShapeGeometry.Line)
            {
                var startX = x;
                var startY = y;
                var endX = x + width;
                var endY = y + height;
                if (shape.FlipVertical)
                {
                    (startY, endY) = (endY, startY);
                }
                if (shape.FlipHorizontal)
                {
                    (startX, endX) = (endX, startX);
                }

                // Default to 0.75pt when the shape doesn't carry a width, as the raster backends do.
                var strokeWidth = shape.LineWidthEmu > 0 ? shape.LineWidthEmu / emusPerPoint : 0.75;
                graphics.DrawLine(StrokePen(shape, strokeWidth), startX, startY, endX, endY);
                continue;
            }

            var isEllipse = shape.Geometry == GroupShapeGeometry.Ellipse;

            // Contours (custGeom or a built preset like hexagon/roundRect) take precedence over
            // the Geometry primitive; a plain shape draws the rect/ellipse as before.
            var geometryPath = BuildGroupShapePath(shape, x, y, width, height);

            // The shadow is an offset copy of the shape's geometry, painted before the shape itself
            // so it lands behind it.
            if (shape.Shadow is { } shadow)
            {
                var shadowRgb = PdfRenderContext.ParseColor(shadow.ColorHex);
                var shadowBrush = new XSolidBrush(XColor.FromArgb(AlphaByte(shadow.Alpha), shadowRgb.R, shadowRgb.G, shadowRgb.B));
                var shadowX = x + shadow.OffsetX * scaleX;
                var shadowY = y + shadow.OffsetY * scaleY;
                if (geometryPath != null)
                {
                    graphics.DrawPath(shadowBrush, BuildGroupShapePath(shape, shadowX, shadowY, width, height)!);
                }
                else if (isEllipse)
                {
                    graphics.DrawEllipse(shadowBrush, shadowX, shadowY, width, height);
                }
                else
                {
                    graphics.DrawRectangle(shadowBrush, shadowX, shadowY, width, height);
                }
            }

            if (shape.ImageData != null)
            {
                DrawGroupPicture(graphics, shape, x, y, width, height, isEllipse);
            }
            else if (shape.FillColorHex is { } fillHex)
            {
                var rgb = PdfRenderContext.ParseColor(fillHex);
                var brush = new XSolidBrush(XColor.FromArgb(AlphaByte(shape.FillAlpha), rgb.R, rgb.G, rgb.B));
                if (geometryPath != null)
                {
                    graphics.DrawPath(brush, geometryPath);
                }
                else if (isEllipse)
                {
                    graphics.DrawEllipse(brush, x, y, width, height);
                }
                else
                {
                    graphics.DrawRectangle(brush, x, y, width, height);
                }
            }

            if (shape.LineWidthEmu > 0)
            {
                var pen = StrokePen(shape, shape.LineWidthEmu / emusPerPoint);
                if (geometryPath != null)
                {
                    graphics.DrawPath(pen, geometryPath);
                }
                else if (isEllipse)
                {
                    graphics.DrawEllipse(pen, x, y, width, height);
                }
                else
                {
                    graphics.DrawRectangle(pen, x, y, width, height);
                }
            }
        }

        graphics.Restore(state);
    }

    /// <summary>
    /// The shape's <see cref="GroupShape.Subpaths"/> contours scaled into the given box with the
    /// flip flags applied, or null for primitive-geometry shapes. Alternate (even-odd) fill keeps
    /// ring shapes (frame) hollow.
    /// </summary>
    static XGraphicsPath? BuildGroupShapePath(GroupShape shape, double x, double y, double width, double height)
    {
        if (shape.Subpaths == null)
        {
            return null;
        }

        var path = new XGraphicsPath {FillMode = XFillMode.Alternate};
        foreach (var contour in shape.Subpaths)
        {
            if (contour.Count < 3)
            {
                continue;
            }

            var points = new XPoint[contour.Count];
            for (var index = 0; index < contour.Count; index++)
            {
                var (pointX, pointY) = contour[index];
                var unitX = shape.FlipHorizontal ? 1 - pointX : pointX;
                var unitY = shape.FlipVertical ? 1 - pointY : pointY;
                points[index] = new(x + unitX * width, y + unitY * height);
            }

            path.StartFigure();
            path.AddPolygon(points);
            path.CloseFigure();
        }

        return path;
    }

    void DrawGroupPicture(XGraphics graphics, GroupShape shape, double x, double y, double width, double height, bool clipToEllipse)
    {
        // PDFsharp can't decode SVG, so an icon graphic falls back to the raster blip the parser
        // kept behind it — see PdfPageRenderer.CanRenderContentType.
        var data = shape.ImageContentType == "image/svg+xml"
            ? shape.ImageRasterFallbackData
            : shape.ImageData;
        if (data == null)
        {
            return;
        }

        // An a:srcRect crop is drawn by enlarging the picture so its visible sub-rectangle covers
        // the shape's box, then clipping back to that box. PDFsharp's source-rectangle overload
        // leaves the unit of the rectangle undocumented; this needs no such API.
        var image = shape.ImageCrop?.Expand(x, y, width, height) ?? (x, y, width, height);
        var cropped = image != (x, y, width, height);

        var state = graphics.Save();
        try
        {
            if (clipToEllipse)
            {
                // Word crops the picture to its pic:spPr geometry — the circular photos on menu
                // templates.
                var clip = new XGraphicsPath();
                clip.AddEllipse(x, y, width, height);
                graphics.IntersectClip(clip);
            }
            else if (cropped)
            {
                graphics.IntersectClip(new XRect(x, y, width, height));
            }

            graphics.DrawImage(context.GetImage(data), image.X, image.Y, image.Width, image.Height);
        }
        catch
        {
            // Undecodable raster (PDFsharp's cross-platform build only does BMP/PNG/JPEG):
            // drop the picture and keep the rest of the group.
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    static XPen StrokePen(GroupShape shape, double widthPoints)
    {
        var rgb = PdfRenderContext.ParseColor(shape.ColorHex);
        return new(XColor.FromArgb(AlphaByte(shape.LineAlpha), rgb.R, rgb.G, rgb.B), Math.Max(0.4, widthPoints));
    }

    static int AlphaByte(double alpha) =>
        (int) Math.Round(Math.Clamp(alpha, 0, 1) * 255);

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

    // Right indent narrows the wrap width the same way the left indent does — and a NEGATIVE right
    // indent (common in resume / multi-column templates, e.g. resumes/11's "Skills" style) WIDENS
    // it past the normal content edge. The Skia/ImageSharp backends subtract it from the wrap width;
    // the PDF backend used to drop it, so right-indented paragraphs wrapped at a Word-divergent width.
    static double RightIndent(ParagraphElement paragraph) =>
        paragraph.Properties.RightIndentPoints;

    // The first line's signed offset from the left indent. A positive first-line indent (w:firstLine)
    // pushes it right; a hanging indent (w:hanging, mutually exclusive with w:firstLine) OUTDENTS it
    // left to L - hanging — but only for a markerless paragraph. A numbered/bulleted list keeps its
    // first-line TEXT at the left indent and hangs the marker into the gap instead (drawn separately),
    // so the outdent must not apply there. Word draws a "bibliography" hanging paragraph's first line
    // at the margin (L - hanging) with continuation lines at L; the raster leaves the first line at L
    // (and over-indents continuation), so this deliberately diverges from the raster to match Word.
    static double FirstLineOffset(ParagraphElement paragraph) =>
        paragraph.Properties.FirstLineIndentPoints -
        (paragraph.Properties.Numbering == null ? paragraph.Properties.HangingIndentPoints : 0);

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

            if (run.InlineImageData is {Length: > 0} || run.InlineShapeGroup != null)
            {
                total += run.InlineImageWidthPoints > 0 ? run.InlineImageWidthPoints : 12;
                continue;
            }

            if (run.Text.Contains('\n') || run.Text.Contains('\r'))
            {
                break;
            }

            var text = RunText(run);
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
                run.InlineShapeGroup != null ||
                run.Text.Contains('\n') ||
                run.Text.Contains('\r'))
            {
                break;
            }

            var text = RunText(run);
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

    // When a Right/Centre/Decimal stop's following text would start at or past the visible
    // content edge, nothing of it can show (Word fills the leader to the edge and the text is
    // cut off — a TOC page number whose stop lies far past a narrow cell). Returns the last run
    // index to consume so the caller skips that text.
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

        // Word's line-spacing rules beyond Auto, mirrored from the raster CalculateLineHeight:
        // Exactly forces the specified pitch (smaller or larger than natural), AtLeast is a
        // floor. Applied per finished line so the tallest run still wins under AtLeast.
        double ApplyLineSpacingRule(double naturalHeight) =>
            paragraph.Properties.LineSpacingRule switch
            {
                LineSpacingRule.Exactly => paragraph.Properties.LineSpacingPoints,
                LineSpacingRule.AtLeast => Math.Max(naturalHeight, paragraph.Properties.LineSpacingPoints),
                _ => naturalHeight
            };

        var leftIndent = Indent(paragraph);

        // Each line wraps at EffectiveWidth() — the wrap width the caller derived by removing the
        // paragraph's left/right indents, adjusted on the FIRST line by its signed offset (see
        // FirstLineOffset): a first-line indent narrows it; a markerless hanging indent outdents the
        // line so it wraps that much WIDER. Draw shifts the first line's start to match. The PDF
        // backend used to ignore both, so an indented/outdented first line ran the full width.
        //
        // Continuation lines are NOT shifted for a hanging indent: the PDF draws them at the left
        // indent (where Word puts them), whereas the raster shifts them a further hanging-indent right
        // — a raster bug, so matching it would regress the PDF.
        //
        // (An earlier hack widened the limit by a whole right margin whenever a line held a tab; it
        // over-extended hanging-indent list first lines — issue #151 — and only appeared to help tab
        // columns by masking a dropped right indent, now honoured. Removed; no line spills the margin.)
        var firstLineIndent = FirstLineOffset(paragraph);
        // Raised for the remainder of a line when a tab matches an explicit Right/Center/Decimal
        // stop, so the wrap width covers the stop's true extent — Word honours such stops inside
        // the right-indent zone (TOC page numbers), so the post-tab text must not wrap away.
        // Scoped to explicit R/C/D stops only: the earlier blanket "widen on any tab" hack
        // over-extended hanging-indent list first lines (issue #151).
        var lineWidthExtension = 0d;
        double EffectiveWidth() => (lines.Count == 0 ? availableWidth - firstLineIndent : availableWidth) + lineWidthExtension;

        var current = new Line();
        var pendingSpaceWidth = 0d;
        XFont? pendingSpaceFont = null;
        RunProperties? pendingSpaceProps = null;

        void Flush()
        {
            if (current.Items.Count > 0)
            {
                current.Height = ApplyLineSpacingRule(current.Height);
                lines.Add(current);
            }

            current = new();
            pendingSpaceWidth = 0;
            pendingSpaceFont = null;
            pendingSpaceProps = null;
            lineWidthExtension = 0;
        }

        void Account(LineItem item)
        {
            current.Items.Add(item);
            current.Width += item.Width;
            current.Ascent = Math.Max(current.Ascent, item.Ascent);
            current.Height = Math.Max(current.Height, item.Height);
        }

        // Small caps (w:smallCaps): expand runs into full-size uppercase + 0.8x-size uppercased
        // lowercase segments before layout, matching the Skia/ImageSharp backends. No-op when no run
        // sets small caps. All following run lookups use the expanded list so indices stay aligned.
        var runs = SmallCapsExpander.Expand(paragraph.Runs);
        for (var runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];
            if (run.Properties.Hidden)
            {
                continue;
            }

            if (run.InlineShapeGroup is { } shapeGroup)
            {
                var groupWidth = run.InlineImageWidthPoints > 0 ? run.InlineImageWidthPoints : 12;
                var groupHeight = run.InlineImageHeightPoints > 0 ? run.InlineImageHeightPoints : 12;
                if (current.Items.Count > 0 && current.Width + pendingSpaceWidth + groupWidth > EffectiveWidth())
                {
                    Flush();
                }

                // A group is as tall as its extent, but Word still allocates the paragraph mark its
                // own line, so the line box never shrinks below EmptyLineHeight. That floor is what
                // keeps a hairline connector rule (a 0.5pt-tall group used as a section divider)
                // from collapsing its paragraph — before this branch existed the rule drew nothing
                // and the paragraph fell back to exactly that height. An icon bubble is taller than
                // the mark and keeps its own size.
                var lineFloor = EmptyLineHeight(paragraph);
                var itemHeight = Math.Max(groupHeight, lineFloor);
                var (_, groupAscent, _) = Metrics(context.GetFont(run.Properties));

                Account(
                    new()
                    {
                        ShapeGroup = shapeGroup,
                        ImageWidth = groupWidth,
                        ImageHeight = groupHeight,
                        Width = groupWidth,
                        // The group's bottom sits on the baseline, as an inline image does; a group
                        // shorter than the line rides the mark's baseline instead of the line top.
                        Ascent = Math.Max(groupHeight, Math.Min(groupAscent, itemHeight)),
                        Height = itemHeight
                    });
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
                if (current.Items.Count > 0 && current.Width + pendingSpaceWidth + width > EffectiveWidth())
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
                        ImageRotationDegrees = run.InlineImageRotationDegrees,
                        Crop = run.InlineImageCrop,
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
                        ? MeasureFollowingDecimalPrefix(runs, runIndex + 1)
                        : null;
                    var (destination, matchedStop, suppressFollowing) = TabStopResolver.Resolve(
                        cursorFromLeft,
                        () => MeasureFollowingWidth(runs, tabRunIndex + 1),
                        paragraph.Properties.TabStops,
                        paragraph.Properties.DefaultTabStopPoints,
                        leftIndent,
                        decimalPrefix,
                        availableEndX: leftIndent + availableWidth + RightIndent(paragraph));
                    if (matchedStop is {Alignment: TabAlignment.Right or TabAlignment.Center or TabAlignment.Decimal})
                    {
                        var extentEnd = suppressFollowing
                            ? destination
                            : matchedStop.Alignment == TabAlignment.Center
                                ? 2 * matchedStop.PositionPoints - destination
                                : matchedStop.PositionPoints;
                        var baseWrapWidth = EffectiveWidth() - lineWidthExtension;
                        lineWidthExtension = Math.Max(lineWidthExtension, extentEnd - leftIndent - baseWrapWidth);
                    }

                    var gap = destination - cursorFromLeft;
                    if (gap > 0 && current.Width + gap <= EffectiveWidth())
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
                    }

                    if (suppressFollowing)
                    {
                        runIndex = SkipFollowingTabContent(runs, runIndex);
                    }
                }

                continue;
            }

            var text = RunText(run);
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
                                lines.Add(new() {Ascent = ascent, Height = ApplyLineSpacingRule(lineHeight)});
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
                // A word straight after a tab filler stays put: the tab just resolved its
                // position, and rounding drift between the tab's following-width probe and this
                // word-by-word measure must not wrap it away (Word never does).
                if (current.Items.Count > 0 && current.Width + pendingSpaceWidth + wordWidth > EffectiveWidth() &&
                    !current.Items[^1].IsTabFiller)
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

    // Run text as it is measured and drawn. w:caps uppercases. The non-breaking hyphen U+2011 becomes
    // a plain '-': the bundled faces have no U+2011 glyph (it rendered as tofu), and Tokenize only
    // breaks on whitespace so the hyphen stays unbreakable either way. Soft hyphens U+00AD are
    // optional break hints Word never paints inline, so they are dropped. Matches the raster engines.
    const char nonBreakingHyphen = '\u2011'; // non-breaking hyphen
    const char softHyphen = '\u00AD'; // soft hyphen (optional break hint)
    const string softHyphenString = "\u00AD";

    static string RunText(Run run)
    {
        var text = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
        if (text.Contains(nonBreakingHyphen))
        {
            text = text.Replace(nonBreakingHyphen, '-');
        }

        if (text.Contains(softHyphen))
        {
            text = text.Replace(softHyphenString, "");
        }

        return text;
    }

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
        public double ImageRotationDegrees;
        public ImageCrop? Crop;
        public InlineShapeGroup? ShapeGroup;
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
