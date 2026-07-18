/// <summary>
/// Renders document pages to PNG images using SixLabors.ImageSharp.
/// </summary>
sealed class ImageSharpPageRenderer(ImageSharpRenderContext context) :
    PageRendererBase(context),
    IDisposable
{
    protected override IParagraphMeasurer Measurer => textRenderer;
    protected override bool HasOutput => currentPage != null;

    TextRenderer textRenderer = new(context);

    Action<Action<Stream>>? pageCallback;
    int pageCount;

    /// <summary>
    /// When true, pages are laid out and counted but not encoded to PNG or handed to the callback.
    /// Used by the gated counting pass that resolves NUMPAGES before the real render.
    /// </summary>
    public bool CountOnly { get; init; }

    Image<Rgba32>? pendingPage;
    Image<Rgba32>? currentPage;
    DrawingCanvas? currentCanvas;
    float headerHeight;
    IReadOnlyList<Watermark> watermarks = [];

    bool hasSignificantContentOnCurrentPage;
    bool currentPageFromExplicitBreak;

    // Track whether the current page was started by a section break (a new page setup):
    // Word keeps a paragraph's spacing-before at the top of such pages in every mode.
    bool currentPageFromSectionBreak;

    // Pages started so far (incremented in StartNewPage; pageCount only counts flushed pages,
    // which lags behind while a finished page is still pending).
    int pagesStarted;

    Dictionary<string, Color> colorCache = [];

    Color ParseColor(string? hexColor)
    {
        if (string.IsNullOrEmpty(hexColor))
        {
            return ImageSharpRenderContext.ParseColor(hexColor);
        }

        if (colorCache.TryGetValue(hexColor, out var cached))
        {
            return cached;
        }

        var color = ImageSharpRenderContext.ParseColor(hexColor);
        colorCache[hexColor] = color;
        return color;
    }

    public int RenderDocument(ParsedDocument document, Action<Action<Stream>> callback)
    {
        pageCallback = callback;

        header = document.Header;
        footer = document.Footer;
        firstPageHeader = document.FirstPageHeader;
        firstPageFooter = document.FirstPageFooter;
        evenPageHeader = document.EvenPageHeader;
        evenPageFooter = document.EvenPageFooter;
        watermarks = document.Watermarks;
        differentFirstPage = document.PageSettings.DifferentFirstPage;

        headerHeight = MeasureHeaderFooterHeight(header);
        footerHeight = MeasureHeaderFooterHeight(footer);

        context.SetHeaderFooterSpace(headerHeight, footerHeight);
        context.InitializeLineNumbers();

        StartNewPage();

        var elements = document.Elements;
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];

            if (element is FloatingShapeElement {BehindText: true} shape)
            {
                AdvanceToBackgroundsTargetPage(elements, i);
                RenderBackgroundShape(shape);
                continue;
            }

            // Behind-text floating images carry the same page-anchor semantics as background
            // shapes — see SkiaPageRenderer for the rationale.
            if (element is FloatingImageElement {BehindText: true} bgImage)
            {
                AdvanceToBackgroundsTargetPage(elements, i);
                RenderFloatingImage(bgImage);
                continue;
            }

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

        // Append footnotes/endnotes at document end (see Skia comment).
        RenderNotesAppendix(document);

        FinishCurrentPage();
        RemoveBlankTrailingPage();

        return pageCount;
    }

    // ReSharper disable once UnusedParameter.Local
    static float MeasureHeaderFooterHeight(HeaderFooterContent? content) =>
        0;

    void RenderNotesAppendix(ParsedDocument document)
    {
        var footnotes = document.Footnotes
            .Where(_ => _.Id != "0" &&
                        _.Id != "-1" &&
                        !string.IsNullOrWhiteSpace(_.Text))
            .ToList();
        var endnotes = document.Endnotes
            .Where(_ => _.Id != "0" &&
                        _.Id != "-1" &&
                        !string.IsNullOrWhiteSpace(_.Text))
            .ToList();

        if (footnotes.Count == 0 &&
            endnotes.Count == 0)
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
                        Bold = true, FontSizePoints = 12
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
                    new() {Text = $"{id}. ", Properties = new() {Bold = true, FontSizePoints = 10}},
                    new() {Text = text, Properties = new() {FontSizePoints = 10}}
                ],
                Properties = new() {SpacingAfterPoints = 4}
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
                RenderFloatingImage(floatingImage);
                hasSignificantContentOnCurrentPage = true;
                break;

            case FloatingTextBoxElement floatingTextBox:
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
                context.LastParagraphSpacingAfterPoints = 0;
                context.LastParagraphHadContextualSpacing = false;
                context.LastParagraphStyleId = null;
                break;

            case WordArtElement wordArt:
                RenderWordArt(wordArt);
                hasSignificantContentOnCurrentPage = true;
                break;

            case FloatingWordArtElement floatingWordArt:
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

    float MeasureElementHeight(DocumentElement element) =>
        element switch
        {
            ParagraphElement para => textRenderer.MeasureParagraphHeight(para),
            ImageElement img => (float) img.HeightPoints,
            TableElement table => MeasureTableHeight(table),
            _ => 0
        };

    protected override void RenderParagraph(ParagraphElement paragraph, DocumentElement? nextElement = null)
    {
        // Substitute live page numbers for any PAGE/NUMPAGES/SECTIONPAGES field before measuring.
        paragraph = ResolveParagraphPageFields(paragraph);

        var hasSignificantContent = paragraph.Runs.Any(_ => !string.IsNullOrWhiteSpace(_.Text));
        var isCompletelyEmpty = paragraph.Runs.Count == 0;

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

        if (paragraph.Properties.KeepNext &&
            nextElement != null &&
            !isCompletelyEmpty)
        {
            var nextHeight = MeasureElementHeight(nextElement);
            var combinedHeight = height + nextHeight;

            if (!context.HasSpaceFor(combinedHeight) &&
                combinedHeight <= context.ContentHeight &&
                context.CurrentY > context.ContentTop)
            {
                AdvanceToNextColumnOrPage();
            }
        }

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

        // WidowControl — push the whole paragraph forward when splitting it would leave a single
        // orphan/widow line. We can't break a paragraph mid-flow today, so this is the only enforceable case.
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
                if (fit == 1 || fit == lineHeights.Count - 1)
                {
                    AdvanceToNextColumnOrPage();
                }
            }
        }

        if (!isCompletelyEmpty)
        {
            EnsureSpaceFor(height);
        }

        if (currentCanvas != null)
        {
            context.SuppressPageTopSpacingBefore = ShouldSuppressPageTopSpacingBefore();
            textRenderer.RenderParagraph(currentCanvas, paragraph, nextElement);
        }

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

        var pen = context.GetPen(ParseColor(hexColor), pixelStrokeWidth);
        currentCanvas.DrawLine(pen, new PointF(pixelX1, pixelY), new PointF(pixelX2, pixelY));
    }

    protected override bool CanRenderContentType(string? contentType) =>
        contentType != "image/svg+xml";

    protected override void DrawBlockImage(byte[] imageData, string? contentType, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, bool flipHorizontal, bool flipVertical, ImageCrop? crop, BlipColorEffect colorEffect)
    {
        // SVG images are not supported in the ImageSharp backend
        if (contentType == "image/svg+xml")
        {
            return;
        }

        DrawBlockImage(imageData, pixelX, pixelY, pixelWidth, pixelHeight, rotation, crop, colorEffect, flipHorizontal, flipVertical);
    }

    void DrawBlockImage(byte[] imageData, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, ImageCrop? crop, BlipColorEffect colorEffect = BlipColorEffect.None, bool flipHorizontal = false, bool flipVertical = false)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Decode + crop + resize + recolor + flip + rotate are cached on the context, so a repeated
        // image (header logo, duplicated body icon) processes once per document.
        var img = context.GetProcessedImage(imageData, (int) pixelWidth, (int) pixelHeight, crop, colorEffect, rotation, flipHorizontal, flipVertical);
        if (img == null)
        {
            return;
        }

        if (rotation != 0)
        {
            // After rotation the image's bounding box grew; recentre over the original location.
            var newX = pixelX + pixelWidth / 2 - img.Width / 2f;
            var newY = pixelY + pixelHeight / 2 - img.Height / 2f;
            currentCanvas.DrawImage(img, new((int) newX, (int) newY));
        }
        else
        {
            currentCanvas.DrawImage(img, new((int) pixelX, (int) pixelY));
        }
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

        var font = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints,
            wordArt.Bold,
            wordArt.Italic);

        var textSize = TextMeasurer.MeasureAdvance(
            wordArt.Text,
            new(font)
            {
                Dpi = context.Dpi
            });

        // Only shrink to fit; never enlarge text past the explicit font size. The bounding
        // box for a WordArt shape (especially arc/circle warps) is much larger than the
        // rendered glyphs because Word lays the text along a curve inside the box.
        var scaleX = textSize.Width > 0 ? width / textSize.Width : 1;
        var scaleY = textSize.Height > 0 ? pixelHeight / textSize.Height : 1;
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1f);

        var scaledFont = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints * scale,
            wordArt.Bold,
            wordArt.Italic);

        if (TryRenderWordArtOnPath(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, wordArt.OutlineColorHex, wordArt.OutlineWidthPoints, x, y, width, pixelHeight, scaledFont))
        {
            context.CurrentY += height;
            return;
        }

        if (TryRenderWordArtPathWarp(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, x, y, width, pixelHeight, scaledFont))
        {
            context.CurrentY += height;
            return;
        }

        if (TryRenderWordArtEnvelope(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, x, y, width, pixelHeight, scaledFont))
        {
            context.CurrentY += height;
            return;
        }

        var scaledSize = TextMeasurer.MeasureAdvance(
            wordArt.Text,
            new(scaledFont)
            {
                Dpi = context.Dpi
            });

        var textX = x + (width - scaledSize.Width) / 2;
        var textY = y + (pixelHeight - scaledSize.Height) / 2;

        Color fillColor;
        if (wordArt.FillColorHex == null)
        {
            fillColor = Color.Black;
        }
        else
        {
            fillColor = ParseColor(wordArt.FillColorHex);
        }

        if (wordArt.HasShadow)
        {
            var shadowColor = Color.FromPixel(new Rgba32(0, 0, 0, 80));
            currentCanvas.DrawText(wordArt.Text, scaledFont, shadowColor, new(textX + 3, textY + 3));
        }

        if (wordArt is {OutlineColorHex: not null, OutlineWidthPoints: > 0})
        {
            var outlineColor = ParseColor(wordArt.OutlineColorHex);
            var outlinePen = context.GetPen(outlineColor, context.PointsToPixels((float) wordArt.OutlineWidthPoints));
            currentCanvas.DrawText(wordArt.Text, scaledFont, outlinePen, new(textX, textY));
        }

        currentCanvas.DrawText(wordArt.Text, scaledFont, fillColor, new(textX, textY));

        context.CurrentY += height;
    }

    /// <summary>
    /// Renders WordArt that follows a curved path (ArchUp / ArchDown / Circle) through
    /// <see cref="DrawingCanvas.DrawText(RichTextOptions, ReadOnlySpan{char}, IPath, Brush, Pen)"/>.
    /// Returns true when the warp was handled, false for warps that should fall back to
    /// flat-text rendering.
    /// </summary>
    /// <remarks>
    /// Word's <c>prstTxWarp</c> presets don't treat the WordArt bbox as the *full* arc
    /// bounding box — that produces a tight half-ellipse, much sharper than Word actually
    /// draws (and the typical bbox is 4:1 wide-and-flat, so the half-ellipse is also far
    /// off-centre vertically).
    /// <para>
    /// The correct geometry for <c>textArchUp</c>/<c>textArchDown</c> treats bbox W as the
    /// arc <em>chord</em> and bbox H as the <em>sagitta</em> (perpendicular distance from
    /// chord midpoint to arc midpoint). The circle radius is R = (W² + 4H²) / (8H), and the
    /// arc sweep is 2·asin(W/(2R)). Wide-and-flat bboxes give large R and small sweep —
    /// gentle, mostly-horizontal curves, matching Word.
    /// </para>
    /// <para>
    /// <c>textCircle</c> wraps text around the right side of an inscribed circle. Short
    /// text covers a small arc centred on 3 o'clock and reads downward — matches Word's
    /// behaviour where bbox width determines the circle diameter and text sits on the
    /// right hemisphere.
    /// </para>
    /// </remarks>
    bool TryRenderWordArtOnPath(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        string? outlineColorHex,
        double outlineWidthPoints,
        float x, float y, float width, float height,
        Font font)
    {
        if (currentCanvas == null)
        {
            return false;
        }

        // We size the path to exactly fit the text, centred on the desired anchor (peak/dip
        // for arches, 3 o'clock for circle). ImageSharp's HorizontalAlignment.Center doesn't
        // centre text along a path baseline the way Skia's SKTextAlign.Center does — it
        // places glyphs starting at the path origin regardless. Sizing the path to text
        // length sidesteps that and gives Word-shaped output.
        var textWidthPixels = TextMeasurer.MeasureAdvance(text, new(font) {Dpi = context.Dpi}).Width;
        if (textWidthPixels <= 0)
        {
            return false;
        }

        IPath path;
        switch (transform)
        {
            case WordArtTransform.ArchUp:
                path = BuildChordSagittaArc(x, y, width, height, textWidthPixels, archDown: false);
                break;
            case WordArtTransform.ArchDown:
                path = BuildChordSagittaArc(x, y, width, height, textWidthPixels, archDown: true);
                break;
            case WordArtTransform.Circle:
                {
                    var radius = Math.Min(width, height) / 2f;
                    var cx = x + width / 2f;
                    var cy = y + height / 2f;
                    // Text-length arc centred on 3 o'clock (right side of inscribed circle),
                    // running CW from upper-right to lower-right. Word's textCircle wraps
                    // short text on this hemisphere reading downward.
                    var halfAngleDegrees = (float) (textWidthPixels / radius * 90.0 / Math.PI);
                    var startAngle = 360f - halfAngleDegrees;
                    var sweepAngle = 2 * halfAngleDegrees;
                    path = BuildArc(new(cx - radius, cy - radius, 2 * radius, 2 * radius), startAngle, sweepAngle);
                    break;
                }
            case WordArtTransform.ChevronUp:
                // Word's textChevron renders as a single-peak smooth arch, not a sharp ^ —
                // the chord-sagitta arc gives the right visual without per-glyph overlap at
                // a discontinuous apex.
                path = BuildChordSagittaArc(x, y, width, height, textWidthPixels, archDown: false);
                break;
            case WordArtTransform.ChevronDown:
                path = BuildChordSagittaArc(x, y, width, height, textWidthPixels, archDown: true);
                break;
            case WordArtTransform.Wave:
                path = BuildWavePath(x, y, width, height, textWidthPixels);
                break;
            case WordArtTransform.SlantUp:
                path = BuildSlantPath(x, y, width, height, textWidthPixels, slantUp: true);
                break;
            case WordArtTransform.SlantDown:
                path = BuildSlantPath(x, y, width, height, textWidthPixels, slantUp: false);
                break;
            default:
                return false;
        }

        var fillColor = fillColorHex == null ? Color.Black : ParseColor(fillColorHex);
        var options = new RichTextOptions(font)
        {
            Dpi = context.Dpi
        };

        if (outlineColorHex != null && outlineWidthPoints > 0)
        {
            var outlineColor = ParseColor(outlineColorHex);
            var outlinePen = context.GetPen(outlineColor, context.PointsToPixels((float) outlineWidthPoints));
            currentCanvas.DrawText(options, text.AsSpan(), path, null, outlinePen);
        }

        currentCanvas.DrawText(options, text.AsSpan(), path, context.GetBrush(fillColor), null);
        return true;
    }

    /// <summary>
    /// Renders the box-filling envelope warps (Inflate / Deflate / CanUp / CanDown) by
    /// extracting glyph outline paths and remapping every point so each glyph's top and
    /// bottom edges follow the envelope curve. Word distorts each glyph as a true non-affine
    /// trapezoid — affine per-glyph scaling (the legacy fallback) only varies glyph height,
    /// keeping each glyph rectangular. Path-level remap captures the per-column height
    /// variation that makes the warp look right.
    ///
    /// Algorithm: render text glyphs at natural size to get outline paths in
    /// text-local coords (x ∈ [0, totalWidth], y ∈ ~[0, glyphHeight]). For every point,
    /// normalise (x, y) into (t, normY), look up the envelope's top/bottom Y at t, and
    /// remap y linearly between them. X is stretched to fill the bbox width.
    /// </summary>
    bool TryRenderWordArtPathWarp(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        float x, float y, float width, float height,
        Font font)
    {
        if (currentCanvas == null)
        {
            return false;
        }

        if (transform is not (WordArtTransform.Inflate or WordArtTransform.Deflate
            or WordArtTransform.CanUp or WordArtTransform.CanDown))
        {
            return false;
        }

        var measureOptions = new RichTextOptions(font) {Dpi = context.Dpi};
        var totalWidth = TextMeasurer.MeasureAdvance(text, measureOptions).Width;
        if (totalWidth <= 0)
        {
            return false;
        }

        var (_, baselineFromTop) = ImageSharpRenderContext.GetFontMetrics(font);
        var naturalAscent = baselineFromTop * context.Scale;

        // Render glyph outlines at text-local coords: baseline at y = ascent. X spans
        // 0..totalWidth. Use the resulting paths' actual bounds for Y normalisation rather
        // than font metrics — the visible glyph extent (cap-top to descender-bottom) is what
        // the envelope curve should map onto, and that's tighter than the font's metric box.
        var pathOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            Origin = new(0, naturalAscent)
        };
        var glyphPaths = TextBuilder.GeneratePaths(text, pathOptions);
        var pathsBounds = glyphPaths.Bounds;
        var glyphsTop = pathsBounds.Top;
        var glyphsHeight = pathsBounds.Height;
        if (glyphsHeight <= 0)
        {
            return false;
        }

        var pathBuilder = new PathBuilder();
        foreach (var glyphPath in glyphPaths)
        {
            foreach (var simplePath in glyphPath.Flatten())
            {
                var points = simplePath.Points.Span;
                if (points.Length == 0)
                {
                    continue;
                }

                pathBuilder.StartFigure();
                pathBuilder.MoveTo(WarpPoint(points[0], totalWidth, glyphsTop, glyphsHeight, x, y, width, height, transform));
                for (var i = 1; i < points.Length; i++)
                {
                    pathBuilder.LineTo(WarpPoint(points[i], totalWidth, glyphsTop, glyphsHeight, x, y, width, height, transform));
                }
                if (simplePath.IsClosed)
                {
                    pathBuilder.CloseFigure();
                }
            }
        }

        var fillColor = fillColorHex == null ? Color.Black : ParseColor(fillColorHex);
        currentCanvas.Fill(context.GetBrush(fillColor), pathBuilder.Build());
        return true;
    }

    static PointF WarpPoint(PointF point, float totalWidth, float glyphsTop, float glyphsHeight,
        float x, float y, float width, float height, WordArtTransform transform)
    {
        var t = Math.Clamp(point.X / totalWidth, 0f, 1f);
        var newX = x + t * width;
        var (top, bottom) = EnvelopeAt(t, transform, y, height);
        var normY = (point.Y - glyphsTop) / glyphsHeight;
        var newY = top + normY * (bottom - top);
        return new(newX, newY);
    }

    /// <summary>
    /// Returns the (top Y, bottom Y) envelope curve at normalised text position t ∈ [0, 1]
    /// for the given warp. Edge text height is <c>minRatio</c> of the bbox so glyphs at the
    /// ends of the word stay readable instead of collapsing to a line.
    /// </summary>
    static (float top, float bottom) EnvelopeAt(float t, WordArtTransform transform, float bboxTop, float bboxHeight)
    {
        var sinT = (float) Math.Sin(Math.PI * t);
        var bboxBottom = bboxTop + bboxHeight;
        var bboxCentre = bboxTop + bboxHeight / 2f;
        const float minRatio = 0.55f;

        switch (transform)
        {
            case WordArtTransform.Inflate:
            {
                var h = bboxHeight * (minRatio + (1 - minRatio) * sinT);
                return (bboxCentre - h / 2f, bboxCentre + h / 2f);
            }
            case WordArtTransform.Deflate:
            {
                var h = bboxHeight * (1f - (1 - minRatio) * sinT);
                return (bboxCentre - h / 2f, bboxCentre + h / 2f);
            }
            case WordArtTransform.CanUp:
            {
                var h = bboxHeight * (minRatio + (1 - minRatio) * sinT);
                return (bboxBottom - h, bboxBottom);
            }
            case WordArtTransform.CanDown:
            {
                var h = bboxHeight * (minRatio + (1 - minRatio) * sinT);
                return (bboxTop, bboxTop + h);
            }
            default:
                return (bboxTop, bboxBottom);
        }
    }

    /// <summary>
    /// Renders WordArt envelope warps that aren't text-on-path — Fade, Triangle, Inflate,
    /// Deflate, CanUp, CanDown — by drawing each glyph individually with a per-glyph
    /// vertical scale. The anchor depends on the warp: baseline for Fade/Triangle/CanUp
    /// (bottom stays put), glyph centre for Inflate/Deflate (both edges move symmetrically),
    /// glyph top for CanDown (top stays put). Returns true when handled. Per-glyph
    /// rendering is slower than a single DrawText so the path-based warps are tried first.
    /// </summary>
    bool TryRenderWordArtEnvelope(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        float x, float y, float width, float height,
        Font font)
    {
        if (currentCanvas == null)
        {
            return false;
        }

        // scaleY(t) returns the per-glyph vertical scale factor for normalised position
        // t ∈ [0, 1] along the text. anchor selects which line stays fixed under the scale:
        // baseline for Fade/Triangle/CanUp, centre for Inflate/Deflate, top for CanDown.
        // Inflate/CanUp/CanDown peak ~1.4× in the middle (sin curve, amplitude 0.4).
        // Deflate floors at 0.7 in the middle so glyphs stay readable.
        Func<float, float>? scaleY = transform switch
        {
            WordArtTransform.FadeRight => t => 1f - 0.65f * t,
            WordArtTransform.FadeLeft => t => 0.35f + 0.65f * t,
            WordArtTransform.Triangle => t => 0.35f + 0.65f * (1f - Math.Abs(2f * t - 1f)),
            WordArtTransform.Inflate => t => 1f + 0.5f * (float) Math.Sin(Math.PI * t),
            WordArtTransform.Deflate => t => 1f - 0.45f * (float) Math.Sin(Math.PI * t),
            WordArtTransform.CanUp => t => 1f + 0.5f * (float) Math.Sin(Math.PI * t),
            WordArtTransform.CanDown => t => 1f + 0.5f * (float) Math.Sin(Math.PI * t),
            _ => null
        };
        if (scaleY == null)
        {
            return false;
        }

        var anchor = transform switch
        {
            WordArtTransform.Inflate or WordArtTransform.Deflate => EnvelopeAnchor.Centre,
            WordArtTransform.CanDown => EnvelopeAnchor.Top,
            _ => EnvelopeAnchor.Baseline
        };

        var measureOptions = new RichTextOptions(font) {Dpi = context.Dpi};
        var totalWidth = TextMeasurer.MeasureAdvance(text, measureOptions).Width;
        if (totalWidth <= 0)
        {
            return false;
        }

        var (glyphHeight, baselineFromTop) = ImageSharpRenderContext.GetFontMetrics(font);
        var glyphHeightPixels = glyphHeight * context.Scale;
        var baselineOffsetPixels = baselineFromTop * context.Scale;

        var fillColor = fillColorHex == null ? Color.Black : ParseColor(fillColorHex);
        var brush = context.GetBrush(fillColor);

        // Inflate / Deflate / Can warps fill the bbox horizontally AND vertically — Word
        // stretches glyphs to span the box, then modulates each glyph's height by the warp
        // curve. Fade / Triangle leave the natural size (matches Word for those).
        // For the box-filling warps, scale so the PEAK (most-stretched) glyph fits the bbox
        // height: Inflate/Can peak at 1.4× the base, Deflate's largest is 1.0× (it shrinks
        // toward the centre rather than growing).
        var fillsBox = transform is WordArtTransform.Inflate or WordArtTransform.Deflate
            or WordArtTransform.CanUp or WordArtTransform.CanDown;
        var peakScale = transform switch
        {
            WordArtTransform.Inflate or WordArtTransform.CanUp or WordArtTransform.CanDown => 1.5f,
            // Deflate's biggest glyph is at the edges (sy=1.0) — it shrinks toward middle.
            _ => 1.0f
        };
        var sx = fillsBox ? width / totalWidth : 1f;
        var baseScaleY = fillsBox ? height / (peakScale * glyphHeightPixels) : 1f;
        var stretchedWidth = totalWidth * sx;

        // Position the natural-size glyph so its anchor point lands at the desired bbox
        // anchor. The Matrix3x2 then scales around that anchor without translating it, so
        // the chosen edge (top / centre / baseline) stays fixed and the opposite edge moves.
        // For non-box-filling warps (Fade / Triangle) keep the legacy layout — text centred
        // vertically in the bbox with baseline anchor — so existing baselines stay stable.
        var startX = x + (width - stretchedWidth) / 2f;
        var legacyTopY = y + (height - glyphHeightPixels) / 2f;
        var legacyBaselineY = legacyTopY + baselineOffsetPixels;
        float anchorY;
        float originY;
        if (!fillsBox)
        {
            originY = legacyTopY;
            anchorY = legacyBaselineY;
        }
        else
        {
            switch (anchor)
            {
                case EnvelopeAnchor.Top:
                    anchorY = y;
                    originY = anchorY;
                    break;
                case EnvelopeAnchor.Centre:
                    anchorY = y + height / 2f;
                    originY = anchorY - glyphHeightPixels / 2f;
                    break;
                default:
                    anchorY = y + height;
                    originY = anchorY - baselineOffsetPixels;
                    break;
            }
        }

        var charCount = text.Length;
        var cursorX = startX;
        for (var i = 0; i < charCount; i++)
        {
            var ch = text[i].ToString();
            var charAdvance = TextMeasurer.MeasureAdvance(ch, measureOptions).Width;
            // For 1-character labels in a box-filling warp, t=0 collapses sin(πt)=0 (no
            // warp). Use 0.5 so a single glyph still gets the centre amplitude. For Fade /
            // Triangle a single glyph at the start (t=0) is intentional.
            var t = charCount > 1 ? (float) i / (charCount - 1) : fillsBox ? 0.5f : 0f;
            var sy = scaleY(t) * baseScaleY;

            // Scale anchored at (cursorX, anchorY): the X scale stretches each glyph
            // horizontally from its left edge so cursorX increments stay in stretched space;
            // the Y scale anchors at the warp anchor line so the chosen edge stays put.
            var matrix = Matrix3x2.CreateScale(new Vector2(sx, sy), new(cursorX, anchorY));
            currentCanvas.Save(
                new()
                {
                    Transform = new(matrix)
                });

            var charOpts = new RichTextOptions(font)
            {
                Dpi = context.Dpi,
                Origin = new(cursorX, originY)
            };
            currentCanvas.DrawText(charOpts, ch.AsSpan(), brush, null);
            currentCanvas.Restore();

            cursorX += charAdvance * sx;
        }

        return true;
    }

    enum EnvelopeAnchor { Baseline, Centre, Top }

    /// <summary>
    /// Builds a text-length-fitting arc on the chord-sagitta circle (chord = bbox width,
    /// sagitta = bbox height). Path is centred on the bbox horizontal midline at the arc
    /// peak (archUp) or dip (archDown), with sweep limited to <paramref name="textWidth"/>
    /// arc length so glyphs sit at the page-centre of the WordArt without relying on
    /// path-text alignment.
    /// </summary>
    static IPath BuildChordSagittaArc(float x, float y, float width, float height, float textWidth, bool archDown)
    {
        // Sagitta-to-radius identity: R = (chord² + 4·sagitta²) / (8·sagitta).
        var radius = (width * width + 4 * height * height) / (8 * height);
        var textHalfAngleDegrees = (float) (textWidth / (2 * radius) * 180.0 / Math.PI);
        var centerX = x + width / 2f;

        float bboxTop;
        float startAngle;
        float sweepAngle;
        if (archDown)
        {
            // Arc dips through y+H; circle center above chord at y - (R-H).
            // Path centred on 90° (bottom of circle = arc dip), runs CCW for symmetric span.
            bboxTop = y + height - 2 * radius;
            startAngle = 90f + textHalfAngleDegrees;
            sweepAngle = -(2 * textHalfAngleDegrees);
        }
        else
        {
            // Arc peaks at y; circle center below chord at y + R.
            // Path centred on 270° (top of circle = arc peak), runs CW for symmetric span.
            bboxTop = y;
            startAngle = 270f - textHalfAngleDegrees;
            sweepAngle = 2 * textHalfAngleDegrees;
        }

        return BuildArc(new(centerX - radius, bboxTop, 2 * radius, 2 * radius), startAngle, sweepAngle);
    }

    static IPath BuildArc(RectangleF oval, float startAngle, float sweepAngle)
    {
        var builder = new PathBuilder();
        builder.AddArc(oval, rotation: 0, startAngle, sweepAngle);
        return builder.Build();
    }

    /// <summary>
    /// Builds a sine-wave polyline path centred on the bbox. Word's <c>textWave1</c> fits one
    /// full period across the text length: text starts high, dips through the middle, and
    /// ends high again — formula <c>y = midY - amplitude·cos(2π·t/textWidth)</c>. Amplitude
    /// is bbox H/2; period is the rendered text width (not the bbox width). 64 polyline
    /// segments per period — dense enough that per-glyph tangent jumps at segment joins are
    /// visually smooth.
    /// </summary>
    /// <summary>
    /// Straight diagonal path through the bbox centre with slope derived from bbox aspect
    /// (dy/dx = ±H/W). Path length matches <paramref name="textWidth"/> so glyphs sit on a
    /// slanted baseline. Text-on-path naturally rotates each glyph to the line angle —
    /// a small visual difference from Word (which keeps glyphs upright) but captures the
    /// slant effect with a single uniform path.
    /// </summary>
    static IPath BuildSlantPath(float x, float y, float width, float height, float textWidth, bool slantUp)
    {
        var slope = (slantUp ? -1f : 1f) * height / width;
        var halfTextLength = textWidth / 2f;
        var dx = halfTextLength / (float) Math.Sqrt(1 + slope * slope);
        var dy = dx * slope;
        var centerX = x + width / 2f;
        var centerY = y + height / 2f;

        var builder = new PathBuilder();
        builder.MoveTo(new(centerX - dx, centerY - dy));
        builder.LineTo(new(centerX + dx, centerY + dy));
        return builder.Build();
    }

    static IPath BuildWavePath(float x, float y, float width, float height, float textWidth)
    {
        // Amplitude is bbox H/4 (not H/2) — half the box reserves space for the glyph height
        // itself so text stays within the bbox visually, matching Word's textWave1 where the
        // wave excursion is gentle relative to glyph size.
        var amplitude = height / 4f;
        var midY = y + height / 2f;
        // Path covers exactly textWidth horizontal extent, centred on bbox horizontal centre.
        var pathStartX = x + width / 2f - textWidth / 2f;

        const int segmentsPerPeriod = 64;
        var dx = textWidth / segmentsPerPeriod;
        var phaseScale = 2.0 * Math.PI / textWidth;

        var builder = new PathBuilder();
        builder.MoveTo(new(pathStartX, midY - amplitude));
        for (var i = 1; i <= segmentsPerPeriod; i++)
        {
            var t = i * dx;
            var py = midY - amplitude * (float) Math.Cos(t * phaseScale);
            builder.LineTo(new(pathStartX + t, py));
        }
        return builder.Build();
    }

    void RenderFloatingWordArt(FloatingWordArtElement wordArt)
    {
        if (currentCanvas == null)
        {
            return;
        }

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
        var pixelX = bounds.PixelX;
        var pixelY = bounds.PixelY;
        var width = bounds.PixelWidth;
        var pixelHeight = bounds.PixelHeight;

        var font = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints,
            wordArt.Bold,
            wordArt.Italic);

        var textSize = TextMeasurer.MeasureAdvance(
            wordArt.Text,
            new(font)
            {
                Dpi = context.Dpi
            });

        // Only shrink to fit; never enlarge text past the explicit font size — see note in
        // RenderWordArt above.
        var scaleX = textSize.Width > 0 ? width / textSize.Width : 1;
        var scaleY = textSize.Height > 0 ? pixelHeight / textSize.Height : 1;
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1f);

        var scaledFont = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints * scale,
            wordArt.Bold,
            wordArt.Italic);

        if (TryRenderWordArtOnPath(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, wordArt.OutlineColorHex, wordArt.OutlineWidthPoints, pixelX, pixelY, width, pixelHeight, scaledFont))
        {
            return;
        }

        if (TryRenderWordArtPathWarp(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, pixelX, pixelY, width, pixelHeight, scaledFont))
        {
            return;
        }

        if (TryRenderWordArtEnvelope(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, pixelX, pixelY, width, pixelHeight, scaledFont))
        {
            return;
        }

        var scaledSize = TextMeasurer.MeasureAdvance(
            wordArt.Text,
            new(scaledFont)
            {
                Dpi = context.Dpi
            });

        var textX = pixelX + (width - scaledSize.Width) / 2;
        var textY = pixelY + (pixelHeight - scaledSize.Height) / 2;

        Color fillColor;
        if (wordArt.FillColorHex == null)
        {
            fillColor = Color.Black;
        }
        else
        {
            fillColor = ParseColor(wordArt.FillColorHex);
        }

        if (wordArt.HasShadow)
        {
            var shadowColor = Color.FromPixel(new Rgba32(0, 0, 0, 80));
            currentCanvas.DrawText(wordArt.Text, scaledFont, shadowColor, new(textX + 3, textY + 3));
        }

        if (wordArt is {OutlineColorHex: not null, OutlineWidthPoints: > 0})
        {
            var outlineColor = ParseColor(wordArt.OutlineColorHex);
            var outlinePen = context.GetPen(outlineColor, context.PointsToPixels((float) wordArt.OutlineWidthPoints));
            currentCanvas.DrawText(wordArt.Text, scaledFont, outlinePen, new(textX, textY));
        }

        currentCanvas.DrawText(wordArt.Text, scaledFont, fillColor, new(textX, textY));
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

            var color = ParseColor(stroke.ColorHex);

            if (stroke.Transparency > 0 || stroke.IsHighlighter)
            {
                var pixel = color.ToPixel<Rgba32>();
                var alpha = stroke.IsHighlighter
                    ? (byte) 128
                    : (byte) (255 - stroke.Transparency);
                color = Color.FromPixel(new Rgba32(pixel.R, pixel.G, pixel.B, alpha));
            }

            var strokeWidth = context.PointsToPixels((float) stroke.WidthPoints);
            var pen = context.GetPen(color, strokeWidth);

            var points = new PointF[stroke.Points.Count];
            for (var i = 0; i < stroke.Points.Count; i++)
            {
                var point = stroke.Points[i];
                points[i] = new(
                    baseX + context.PointsToPixels((float) point.X),
                    baseY + context.PointsToPixels((float) point.Y));
            }

            currentCanvas.DrawLine(pen, points);
        }

        context.CurrentY += height;
    }

    // Shares the memoized layout with PageRendererBase.RenderTable, so the follow-up render
    // doesn't recompute it.
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

        // Geometry-space ±90° rotation around the cell content centre. Layout the text into a
        // pre-rotation rectangle whose width is the cell's vertical extent (text wrap width)
        // and height is the cell's horizontal extent — once rotated, those dimensions swap to
        // match the rendered cell orientation. Drawing through the page canvas with a transform
        // pushed avoids the temp-image+blit round trip and rasterises glyphs once at the final
        // angle (no bicubic resample softening text).
        var bottomToTop = cell.Properties.TextDirection == CellTextDirection.BottomToTop;
        var pixelCx = context.PointsToPixels(contentX + contentWidth / 2);
        var pixelCy = context.PointsToPixels(contentY + availableHeight / 2);
        var angleRadians = bottomToTop ? -MathF.PI / 2f : MathF.PI / 2f;

        var preRotationLeft = contentX + contentWidth / 2 - availableHeight / 2;
        var preRotationTop = contentY + availableHeight / 2 - contentWidth / 2;

        currentCanvas.Save(BuildRotation(angleRadians, pixelCx, pixelCy));

        var savedY = context.CurrentY;
        context.CurrentY = preRotationTop;

        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                textRenderer.RenderParagraphInBounds(currentCanvas, para, preRotationLeft, availableHeight);
            }
            else if (element is ContentControlElement {Runs.Count: > 0} cc)
            {
                var ccPara = new ParagraphElement
                {
                    Runs = cc.Runs,
                    Properties = new()
                };
                textRenderer.RenderParagraphInBounds(currentCanvas, ccPara, preRotationLeft, availableHeight);
            }
        }

        context.CurrentY = savedY;
        currentCanvas.Restore();
    }

    /// <summary>
    /// Builds <see cref="DrawingOptions"/> with a 2D rotation transform around a screen-space pivot.
    /// Use with <see cref="DrawingCanvas.Save(DrawingOptions, IPath[])"/> /
    /// <see cref="DrawingCanvas.Restore"/> to render content rotated in geometry space, avoiding
    /// the temp-image + <c>Mutate(_.Rotate(...))</c> + composite round trip.
    /// </summary>
    static DrawingOptions BuildRotation(float radians, float pivotX, float pivotY) =>
        new()
        {
            Transform = new(Matrix3x2.CreateRotation(radians, new(pivotX, pivotY)))
        };


    protected override void DrawCellBackground(float pixelX, float pixelY, float pixelWidth, float pixelHeight, string hexColor)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var bgColor = ParseColor(hexColor);
        currentCanvas.Fill(context.GetBrush(bgColor), new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight));
    }

    protected override void DrawCellBorders(float pixelX, float pixelY, float pixelWidth, float pixelHeight, CellBorders borders)
    {
        if (currentCanvas == null)
        {
            return;
        }

        if (borders.Top.IsVisible)
        {
            DrawBorderLine(pixelX, pixelY, pixelX + pixelWidth, pixelY, borders.Top);
        }

        if (borders.Right.IsVisible)
        {
            DrawBorderLine(pixelX + pixelWidth, pixelY, pixelX + pixelWidth, pixelY + pixelHeight, borders.Right);
        }

        if (borders.Bottom.IsVisible)
        {
            DrawBorderLine(pixelX, pixelY + pixelHeight, pixelX + pixelWidth, pixelY + pixelHeight, borders.Bottom);
        }

        if (borders.Left.IsVisible)
        {
            DrawBorderLine(pixelX, pixelY, pixelX, pixelY + pixelHeight, borders.Left);
        }
    }

    protected override void DrawCellDiagonals(float pixelX, float pixelY, float pixelWidth, float pixelHeight, CellDiagonals diagonals)
    {
        if (currentCanvas == null)
        {
            return;
        }

        if (diagonals.Down.IsVisible)
        {
            DrawBorderLine(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight, diagonals.Down);
        }

        if (diagonals.Up.IsVisible)
        {
            DrawBorderLine(pixelX + pixelWidth, pixelY, pixelX, pixelY + pixelHeight, diagonals.Up);
        }
    }

    void DrawBorderLine(float x1, float y1, float x2, float y2, BorderEdge edge)
    {
        var canvas = currentCanvas;
        if (canvas == null)
        {
            return;
        }

        var color = ParseColor(edge.ColorHex ?? "000000");
        var strokeWidth = context.PointsToPixels((float) edge.WidthPoints);

        if (edge.Style == BorderLineStyle.Double)
        {
            // OOXML w:val="double": render as two parallel lines whose total span (line +
            // gap + line) matches the declared width. Each line gets ~1/3 of the width.
            var lineWidth = Math.Max(0.5f, strokeWidth / 3f);
            var offset = strokeWidth / 2f - lineWidth / 2f;
            var pen = context.GetPen(color, lineWidth);
            var horizontal = Math.Abs(y2 - y1) < Math.Abs(x2 - x1);
            if (horizontal)
            {
                canvas.DrawLine(pen, new PointF(x1, y1 - offset), new PointF(x2, y2 - offset));
                canvas.DrawLine(pen, new PointF(x1, y1 + offset), new PointF(x2, y2 + offset));
            }
            else
            {
                canvas.DrawLine(pen, new PointF(x1 - offset, y1), new PointF(x2 - offset, y2));
                canvas.DrawLine(pen, new PointF(x1 + offset, y1), new PointF(x2 + offset, y2));
            }
            return;
        }

        var solidPen = context.GetPen(color, strokeWidth);
        canvas.DrawLine(solidPen, new PointF(x1, y1), new PointF(x2, y2));
    }

    protected override void RenderParagraphInBounds(ParagraphElement paragraph, float x, float maxWidth)
    {
        if (currentCanvas == null)
        {
            return;
        }

        textRenderer.RenderParagraphInBounds(currentCanvas, paragraph, x, maxWidth);
    }

    protected override void RenderImageInCell(ImageElement image, float x, float maxWidth)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var data = image.ImageData;
        if (image.ContentType == "image/svg+xml")
        {
            if (image.RasterFallbackData == null)
            {
                context.CurrentY += (float) image.HeightPoints;
                return;
            }
            data = image.RasterFallbackData;
        }

        var imageWidth = (float) image.WidthPoints;
        var imageHeight = (float) image.HeightPoints;

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

        var img = context.GetProcessedImage(data, (int) pixelWidth, (int) pixelHeight, crop: null, BlipColorEffect.None, rotationDegrees: 0);
        if (img != null)
        {
            currentCanvas.DrawImage(img, new((int) pixelX, (int) pixelY));
        }

        context.CurrentY += imageHeight;
    }

    // === Form-field / content-control draw primitives (called from PageRendererBase) ===

    protected override void DrawFormFieldRect(float pixelX, float pixelY, float pixelWidth, float pixelHeight,
        string fillHex, string borderHex, float pixelBorderWidth)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var rect = new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight);
        var fill = ParseColor(fillHex);
        var border = ParseColor(borderHex);
        currentCanvas.Fill(context.GetBrush(fill), rect);
        currentCanvas.Draw(context.GetPen(border, pixelBorderWidth), rect);
    }

    protected override void DrawFormFieldText(string text, float pixelX, float pixelY, float pixelWidth, float pixelHeight, string textHex)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // ImageSharp's DrawText takes the top-of-text Y; sit a couple of scaled pixels below
        // the rect's top edge so caps clear the border.
        var font = context.GetFontForFamily(DefaultFontSettings.DefaultFont, 10, false, false);
        var color = ParseColor(textHex);
        currentCanvas.DrawText(text, font, color, new(pixelX + 3 * context.Scale, pixelY + 2 * context.Scale));
    }

    protected override void DrawCheckMark(float pixelX, float pixelY, float pixelSize, string hexColor, float pixelStrokeWidth, bool xShape)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var pen = context.GetPen(ParseColor(hexColor), pixelStrokeWidth);

        if (xShape)
        {
            // X glyph (used by content-control checkboxes).
            var pad = pixelSize * 0.25f;
            currentCanvas.DrawLine(pen, new PointF(pixelX + pad, pixelY + pad), new PointF(pixelX + pixelSize - pad, pixelY + pixelSize - pad));
            currentCanvas.DrawLine(pen, new PointF(pixelX + pixelSize - pad, pixelY + pad), new PointF(pixelX + pad, pixelY + pixelSize - pad));
            return;
        }

        // ✓ glyph (used by form-field checkboxes).
        var checkPad = pixelSize * 0.2f;
        var left = pixelX + checkPad;
        var right = pixelX + pixelSize - checkPad;
        var top = pixelY + checkPad;
        var bottom = pixelY + pixelSize - checkPad;
        var midX = pixelX + pixelSize * 0.4f;
        currentCanvas.DrawLine(pen, new PointF(left, top + (bottom - top) * 0.5f), new PointF(midX, bottom));
        currentCanvas.DrawLine(pen, new PointF(midX, bottom), new PointF(right, top));
    }

    protected override void DrawDropDownArrow(float pixelX, float pixelY, float pixelHeight, string hexColor)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var arrowSize = pixelHeight * 0.3f;
        var arrowX = pixelX - 12 * context.Scale;
        var arrowY = pixelY + pixelHeight / 2;

        var arrowBuilder = new PathBuilder();
        arrowBuilder.AddLine(new(arrowX, arrowY - arrowSize / 2), new(arrowX + arrowSize, arrowY - arrowSize / 2));
        arrowBuilder.AddLine(new(arrowX + arrowSize, arrowY - arrowSize / 2), new(arrowX + arrowSize / 2, arrowY + arrowSize / 2));
        arrowBuilder.CloseFigure();
        var color = ParseColor(hexColor);
        currentCanvas.Fill(color, arrowBuilder.Build());
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

            pageCallback!(page.SaveAsPng);
        }
    }

    protected override void StartNewPage()
    {
        FlushPendingPage();

        currentPage = new(context.PageWidthPixels, context.PageHeightPixels);
        // One canvas owns the page for its entire lifetime. Every Fill/Draw/DrawText that
        // used to be its own one-shot Mutate(_ => _.Paint(...)) round-trip is now a single
        // recorded command on this batcher; the backend renders the whole timeline once
        // on Dispose in FinishCurrentPage.
        currentCanvas = currentPage.Frames.RootFrame.CreateCanvas(Configuration.Default, new());

        // A fresh Image<Rgba32> is already transparent, so the WordArt rasterizer skips the fill;
        // otherwise clear to the background color if specified, else white.
        if (!context.TransparentBackground)
        {
            var bgColor = context.PageSettings.BackgroundColorHex;
            var fillColor = string.IsNullOrEmpty(bgColor) ? Color.White : ParseColor(bgColor);
            currentCanvas.Fill(context.GetBrush(fillColor), new RectanglePolygon(0, 0, context.PageWidthPixels, context.PageHeightPixels));
        }

        DrawWatermarks();

        DrawPageBorders();

        if (pageCount > 0)
        {
            context.StartNewPage();
            context.ResetLineNumbersForPage();
        }

        RenderHeader();

        hasSignificantContentOnCurrentPage = false;
        currentPageFromExplicitBreak = false;
        currentPageFromSectionBreak = false;
        pagesStarted++;
    }

    protected override void FinishCurrentPage()
    {
        if (currentPage != null)
        {
            RenderFooter();
            // Disposing the canvas flushes its recorded timeline through the backend.
            // Must happen before pendingPage hands the bitmap to the PNG callback —
            // otherwise the saved file would be missing every queued command on this page.
            currentCanvas?.Dispose();
            currentCanvas = null;
            pendingPage = currentPage;
            currentPage = null;
        }
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
        if (currentCanvas == null)
        {
            return;
        }

        var fontFamily = context.GetFontFamily(watermark.FontFamily, watermark.Bold, italic: false);
        var font = fontFamily.CreateFont((float) watermark.FontSizePoints * context.Scale, watermark.Bold ? FontStyle.Bold : FontStyle.Regular);
        var color = ParseColor(watermark.ColorHex);

        var pageCenterX = context.PageWidthPixels / 2f;
        var pageCenterY = context.PageHeightPixels / 2f;

        var textOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Origin = new(pageCenterX, pageCenterY)
        };

        // Geometry-space rotation around page centre — text glyphs rasterise once at the final
        // angle. Replaces the previous temp-image route (draw text to bbox-sized image, rotate
        // pixels, composite at page centre) which double-resampled the glyph edges.
        currentCanvas.Save(BuildRotation((float) (watermark.RotationDegrees * Math.PI / 180.0), pageCenterX, pageCenterY));
        currentCanvas.DrawText(textOptions, watermark.Text!, context.GetBrush(color));
        currentCanvas.Restore();
    }

    void DrawPageBorders()
    {
        if (currentCanvas == null || context.PageSettings.PageBorders is not {HasAnyBorder: true} borders)
        {
            return;
        }

        var pageWidth = context.PageWidthPixels;
        var pageHeight = context.PageHeightPixels;
        var leftX = context.PointsToPixels((float) borders.LeftSpacePoints);
        var rightX = pageWidth - context.PointsToPixels((float) borders.RightSpacePoints);
        var topY = context.PointsToPixels((float) borders.TopSpacePoints);
        var bottomY = pageHeight - context.PointsToPixels((float) borders.BottomSpacePoints);

        if (borders.Top.IsVisible)
        {
            var pen = context.GetPen(ParseColor(borders.Top.ColorHex), context.PointsToPixels((float) borders.Top.WidthPoints));
            currentCanvas.DrawLine(pen, new PointF(leftX, topY), new PointF(rightX, topY));
        }

        if (borders.Bottom.IsVisible)
        {
            var pen = context.GetPen(ParseColor(borders.Bottom.ColorHex), context.PointsToPixels((float) borders.Bottom.WidthPoints));
            currentCanvas.DrawLine(pen, new PointF(leftX, bottomY), new PointF(rightX, bottomY));
        }

        if (borders.Left.IsVisible)
        {
            var pen = context.GetPen(ParseColor(borders.Left.ColorHex), context.PointsToPixels((float) borders.Left.WidthPoints));
            currentCanvas.DrawLine(pen, new PointF(leftX, topY), new PointF(leftX, bottomY));
        }

        if (borders.Right.IsVisible)
        {
            var pen = context.GetPen(ParseColor(borders.Right.ColorHex), context.PointsToPixels((float) borders.Right.WidthPoints));
            currentCanvas.DrawLine(pen, new PointF(rightX, topY), new PointF(rightX, bottomY));
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

        if (shape.ImageData != null)
        {
            var img = context.GetProcessedImage(shape.ImageData, (int) pixelWidth, (int) pixelHeight, crop: null, BlipColorEffect.None, rotationDegrees: 0);
            if (img != null)
            {
                currentCanvas.DrawImage(img, new((int) pixelX, (int) pixelY));
            }
        }
        else if (shape.Gradient is { } gradient)
        {
            var rad = gradient.DirectionDegrees * Math.PI / 180.0;
            var dx = (float) Math.Cos(rad);
            var dy = (float) Math.Sin(rad);
            var cx = pixelX + pixelWidth / 2;
            var cy = pixelY + pixelHeight / 2;
            var halfDiag = (float) Math.Sqrt(pixelWidth * pixelWidth + pixelHeight * pixelHeight) / 2;

            var startPt = new PointF(cx - dx * halfDiag, cy - dy * halfDiag);
            var endPt = new PointF(cx + dx * halfDiag, cy + dy * halfDiag);

            var brush = new LinearGradientBrush(
                startPt, endPt,
                GradientRepetitionMode.None,
                new ColorStop(0f, ParseColor(gradient.StartColorHex)),
                new ColorStop(1f, ParseColor(gradient.EndColorHex)));
            FillShape(shape, pixelX, pixelY, pixelWidth, pixelHeight, brush);
        }
        else if (shape.FillColorHex != null)
        {
            var fillColor = ParseColor(shape.FillColorHex);
            var alpha = (float) Math.Clamp(shape.FillAlpha, 0, 1);
            if (alpha < 1f)
            {
                var pixel = fillColor.ToPixel<Rgba32>();
                pixel.A = (byte) Math.Round(alpha * 255);
                fillColor = Color.FromPixel(pixel);
            }
            FillShape(shape, pixelX, pixelY, pixelWidth, pixelHeight, context.GetBrush(fillColor));
        }

        if (shape is { LineColorHex: { } lineColor, LineWidthPoints: { } lineWidthPt and > 0 })
        {
            var strokeColor = ParseColor(lineColor);
            var strokePixels = context.PointsToPixels((float) lineWidthPt);
            var pen = context.GetPen(strokeColor, strokePixels);
            if (shape.Subpaths != null)
            {
                var path = BuildPath(shape, pixelX, pixelY, pixelWidth, pixelHeight);
                currentCanvas.Draw(pen, path);
            }
            else if (shape.Preset == PresetShape.Ellipse)
            {
                // EllipsePolygon's 4-arg ctor takes (centerX, centerY, fullWidth, fullHeight) —
                // the trailing two are bounding-box dimensions, not radii.
                var ellipse = new EllipsePolygon(
                    pixelX + pixelWidth / 2,
                    pixelY + pixelHeight / 2,
                    pixelWidth,
                    pixelHeight);
                currentCanvas.Draw(pen, ellipse);
            }
            else
            {
                // See note in SkiaPageRenderer.RenderBackgroundShape — clamp to canvas so the
                // centred stroke at the right/bottom of a full-page shape isn't lost to the
                // half-pixel gap between truncated PageWidthPixels and the shape's rect width.
                var strokeLeft = Math.Max(0, pixelX);
                var strokeTop = Math.Max(0, pixelY);
                var strokeRight = Math.Min(context.PageWidthPixels, pixelX + pixelWidth);
                var strokeBottom = Math.Min(context.PageHeightPixels, pixelY + pixelHeight);
                currentCanvas.Draw(pen, new RectangleF(strokeLeft, strokeTop, strokeRight - strokeLeft, strokeBottom - strokeTop));
            }
        }
    }

    void FillShape(FloatingShapeElement shape, float x, float y, float width, float height, Brush brush)
    {
        if (shape.Subpaths != null)
        {
            var path = BuildPath(shape, x, y, width, height);
            // DrawingCanvas.Fill takes no per-call options, so push the nonzero winding rule for
            // this fill via Save/Restore (the same mechanism used for rotated content).
            currentCanvas!.Save(nonzeroFill);
            currentCanvas.Fill(brush, path);
            currentCanvas.Restore();
            return;
        }

        if (shape.Preset == PresetShape.Ellipse)
        {
            var ellipse = new EllipsePolygon(x + width / 2, y + height / 2, width, height);
            currentCanvas!.Fill(brush, ellipse);
        }
        else
        {
            currentCanvas!.Fill(brush, new RectangleF(x, y, width, height));
        }
    }

    // custGeom fills use nonzero winding to match SkiaSharp's default and DrawingML — without
    // this ImageSharp's default even-odd rule would punch holes wherever contours overlap.
    static readonly DrawingOptions nonzeroFill = new()
    {
        ShapeOptions = new() { IntersectionRule = IntersectionRule.NonZero }
    };

    static IPath BuildPath(FloatingShapeElement shape, float x, float y, float width, float height)
    {
        var rotRad = (float) (shape.RotationDegrees * Math.PI / 180.0);
        var cos = (float) Math.Cos(rotRad);
        var sin = (float) Math.Sin(rotRad);
        var halfW = width / 2f;
        var halfH = height / 2f;

        var builder = new PathBuilder();
        foreach (var contour in shape.Subpaths!)
        {
            var transformed = new PointF[contour.Count];
            for (var i = 0; i < contour.Count; i++)
            {
                var (px, py) = contour[i];
                var ux = shape.FlipHorizontal ? 1 - px : px;
                var uy = shape.FlipVertical ? 1 - py : py;
                // Local coords with the bbox center at origin.
                var lx = (float) (ux * width) - halfW;
                var ly = (float) (uy * height) - halfH;
                // Rotate clockwise (image-space y-down): standard 2D rotation matrix.
                var rx = lx * cos - ly * sin;
                var ry = lx * sin + ly * cos;
                transformed[i] = new(x + halfW + rx, y + halfH + ry);
            }
            // Each contour is its own closed figure so disjoint pieces and holes stay separate
            // instead of being fused into one polygon by connector lines.
            builder.AddLines(transformed);
            builder.CloseFigure();
        }
        return builder.Build();
    }

    protected override void RenderFloatingImage(FloatingImageElement image)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // ImageSharp can't render SVG. If the docx supplied a raster fallback alongside the
        // SVG (Word does this for theme artwork), use it; otherwise skip.
        var data = image.ImageData;
        if (image.ContentType == "image/svg+xml")
        {
            if (image.RasterFallbackData == null)
            {
                return;
            }
            data = image.RasterFallbackData;
        }

        var (width, height) = FloatingPosition.ResolveEffectiveSize(
            context,
            image.WidthPoints,
            image.HeightPoints,
            image.WidthPercent,
            image.WidthRelativeFrom,
            image.HeightPercent,
            image.HeightRelativeFrom);

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

        DrawBlockImage(data, bounds.PixelX, bounds.PixelY, bounds.PixelWidth, bounds.PixelHeight, (float) image.RotationDegrees, image.Crop, flipHorizontal: image.FlipHorizontal, flipVertical: image.FlipVertical);
    }

    void RenderFloatingTextBox(FloatingTextBoxElement textBox)
    {
        if (currentCanvas == null)
        {
            return;
        }

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

        if (Math.Abs(textBox.RotationDegrees) > 0.01)
        {
            // Geometry-space rotation around the text box centre. Text and background paint
            // through the page canvas with the transform pushed; no temp image involved.
            var centerX = pixelX + pixelWidth / 2;
            var centerY = pixelY + pixelHeight / 2;
            currentCanvas.Save(BuildRotation((float) (textBox.RotationDegrees * Math.PI / 180.0), centerX, centerY));

            if (textBox.BackgroundColorHex != null)
            {
                var bgColor = ParseColor(textBox.BackgroundColorHex);
                currentCanvas.Fill(context.GetBrush(bgColor), new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight));
            }

            var savedY = context.CurrentY;
            context.CurrentY = y;

            foreach (var element in textBox.Content)
            {
                if (element is ParagraphElement para)
                {
                    textRenderer.RenderParagraphInBounds(currentCanvas, para, x, (float) textBox.WidthPoints);
                }
            }

            context.CurrentY = savedY;
            currentCanvas.Restore();
        }
        else
        {
            if (textBox.BackgroundColorHex != null)
            {
                var bgFillColor = ParseColor(textBox.BackgroundColorHex);
                currentCanvas.Fill(context.GetBrush(bgFillColor), new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight));
            }

            var savedY = context.CurrentY;
            context.CurrentY = y;

            foreach (var element in textBox.Content)
            {
                if (element is ParagraphElement para)
                {
                    textRenderer.RenderParagraphInBounds(currentCanvas, para, x, (float) textBox.WidthPoints);
                }
            }

            context.CurrentY = savedY;
        }
    }

    public void Dispose()
    {
        currentCanvas?.Dispose();
        currentPage?.Dispose();
    }
}
