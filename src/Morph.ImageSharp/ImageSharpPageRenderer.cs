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

        // Header and footer space are both resolved per page from the header/footer actually
        // active there (see RenderHeader / RenderFooter), so nothing is reserved up front.
        context.SetHeaderFooterSpace(0, 0);
        context.InitializeLineNumbers();

        StartNewPage();

        var elements = document.Elements;
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];

            // Front-of-text shapes take the same page-advance as behind-text ones: their
            // anchor paragraph's content dictates the page (resumes/10's accent circle).
            if (element is FloatingShapeElement shape)
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
    protected override float MeasureHeaderFooterHeight(HeaderFooterContent content)
    {
        var total = 0f;
        foreach (var element in content.Elements)
        {
            total += MeasureElementHeight(element);
        }

        return total;
    }

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

        for (var noteIndex = 0; noteIndex < entries.Count; noteIndex++)
        {
            var (_, text) = entries[noteIndex];
            var noteParagraph = new ParagraphElement
            {
                Runs =
                [
                    // Sequential display number, matching the citation marks (footnotes.xml
                    // ids start at 2; Word shows 1, 2, 3...).
                    new() {Text = $"{noteIndex + 1}. ", Properties = new() {Bold = true, FontSizePoints = 10}},
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

            case FloatingShapeElement floatingShape:
                // Behind-text shapes render from the pre-scan at page start. FRONT-of-text
                // shapes have no other painter and draw here over the content painted so far
                // (newsletters/08's cover photo is a front-anchored blip-filled freeform;
                // resumes/10's accent circle a front-anchored solid custGeom).
                RenderBackgroundShape(floatingShape);
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

    protected override void DrawBlockImage(byte[] imageData, string? contentType, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, bool flipHorizontal, bool flipVertical, ImageCrop? crop, BlipColorEffect colorEffect, string? duotoneColorHex, string? duotoneLightColorHex)
    {
        // SVG images are not supported in the ImageSharp backend
        if (contentType == "image/svg+xml")
        {
            return;
        }

        DrawBlockImage(imageData, pixelX, pixelY, pixelWidth, pixelHeight, rotation, crop, colorEffect, flipHorizontal, flipVertical, duotoneColorHex, duotoneLightColorHex);
    }

    void DrawBlockImage(byte[] imageData, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, ImageCrop? crop, BlipColorEffect colorEffect = BlipColorEffect.None, bool flipHorizontal = false, bool flipVertical = false, string? duotoneColorHex = null, string? duotoneLightColorHex = null)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Decode + crop + resize + recolor + flip + rotate are cached on the context, so a repeated
        // image (header logo, duplicated body icon) processes once per document.
        var img = context.GetProcessedImage(imageData, (int) pixelWidth, (int) pixelHeight, crop, colorEffect, rotation, flipHorizontal, flipVertical, duotoneColorHex, duotoneLightColorHex);
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


    protected override void RenderWordArtInCell(WordArtElement wordArt) => RenderWordArt(wordArt, reserveSpace: false);

    void RenderWordArt(WordArtElement wordArt, bool reserveSpace = true)
    {
        var height = (float) wordArt.HeightPoints;
        if (reserveSpace)
        {
            EnsureSpaceFor(height);
        }

        if (currentCanvas == null)
        {
            return;
        }

        var x = context.PointsToPixels(context.ContentLeft) +
                ImageSharpWordArtDrawer.AlignWordArtOffset(
                    wordArt,
                    context.PointsToPixels(context.ContentWidth),
                    context.PointsToPixels((float) wordArt.WidthPoints));
        var y = context.PointsToPixels(context.CurrentY);
        var width = context.PointsToPixels((float) wordArt.WidthPoints);
        var pixelHeight = context.PointsToPixels(height);

        new ImageSharpWordArtDrawer(context, currentCanvas).DrawInline(wordArt, x, y, width, pixelHeight);

        context.CurrentY += height;
    }

    protected override void RenderFloatingWordArt(FloatingWordArtElement wordArt)
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

        new ImageSharpWordArtDrawer(context, currentCanvas).DrawFloating(wordArt, bounds.PixelX, bounds.PixelY, bounds.PixelWidth, bounds.PixelHeight);
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
    // Internal so SkiaPainter's ImageSharp counterpart (ImageSharpPainter) can reuse the same shape rotation.
    internal static DrawingOptions BuildRotation(float radians, float pivotX, float pivotY) =>
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
            // Word shows the picture through the shape's geometry (a circular profile photo is
            // an ellipse with a blip fill), not as a bare rectangle. Like the floating-image
            // pic:spPr crop, only the ellipse case masks here — custom-geometry contours draw
            // unmasked (rect-equivalent), and rotation keeps the plain path.
            if (shape.Preset == PresetShape.Ellipse &&
                shape.RotationDegrees == 0 &&
                context.GetEllipseClippedImage(shape.ImageData, (int) pixelWidth, (int) pixelHeight, crop: null) is { } ellipseClipped)
            {
                currentCanvas.DrawImage(ellipseClipped, new((int) pixelX, (int) pixelY));
            }
            else
            {
                var img = context.GetProcessedImage(shape.ImageData, (int) pixelWidth, (int) pixelHeight, crop: null, BlipColorEffect.None, rotationDegrees: 0);
                if (img != null)
                {
                    currentCanvas.DrawImage(img, new((int) pixelX, (int) pixelY));
                }
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
            var strokeColor = ImageSharpWordArtDrawer.WithAlpha(ParseColor(lineColor), shape.LineAlpha);
            var strokePixels = context.PointsToPixels((float) lineWidthPt);
            // PatternPen's pattern is in multiples of the stroke width — the same convention
            // the model stores. Dashed pens bypass the solid-pen cache.
            var pen = shape.LineDashPattern is { } dashPattern
                ? (Pen) new PatternPen(strokeColor, strokePixels, dashPattern.Select(_ => (float) _).ToArray())
                : context.GetPen(strokeColor, strokePixels);
            if (shape.Subpaths != null)
            {
                var path = BuildPath(shape, pixelX, pixelY, pixelWidth, pixelHeight);
                currentCanvas.Draw(pen, path);
            }
            else if (shape.RotationDegrees != 0)
            {
                // Rotated preset outline: turn about the box centre (matches FillShape).
                currentCanvas.Save(BuildRotation(
                    (float) (shape.RotationDegrees * Math.PI / 180.0), pixelX + pixelWidth / 2, pixelY + pixelHeight / 2));
                currentCanvas.Draw(pen, BuildPresetPath(shape, pixelX, pixelY, pixelWidth, pixelHeight));
                currentCanvas.Restore();
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
            currentCanvas!.Save(NonzeroFill);
            currentCanvas.Fill(brush, path);
            currentCanvas.Restore();
            return;
        }

        // Preset rects/ellipses rotate about their box centre like any other xfrm
        // (business-plans/08's accent rule is a 90°-rotated thin rect).
        var rotated = shape.RotationDegrees != 0;
        if (rotated)
        {
            currentCanvas!.Save(BuildRotation(
                (float) (shape.RotationDegrees * Math.PI / 180.0), x + width / 2, y + height / 2));
        }

        currentCanvas!.Fill(brush, BuildPresetPath(shape, x, y, width, height));
        if (rotated)
        {
            currentCanvas.Restore();
        }
    }

    /// <summary>The preset rect/ellipse as an unrotated path (rotation applies via
    /// <see cref="BuildRotation"/> around the box centre at the call sites).</summary>
    internal static IPath BuildPresetPath(FloatingShapeElement shape, float x, float y, float width, float height) =>
        shape.Preset == PresetShape.Ellipse
            ? new EllipsePolygon(x + width / 2, y + height / 2, width, height)
            : new RectanglePolygon(x, y, width, height);

    // custGeom fills use nonzero winding to match SkiaSharp's default and DrawingML — without
    // this ImageSharp's default even-odd rule would punch holes wherever contours overlap.
    internal static readonly DrawingOptions NonzeroFill = new()
    {
        ShapeOptions = new() { IntersectionRule = IntersectionRule.NonZero }
    };

    internal static IPath BuildPath(FloatingShapeElement shape, float x, float y, float width, float height)
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

        // pic:spPr ellipse crop (round photos): composite a pre-clipped bitmap — ImageSharp
        // has no canvas clip stack. custGeom crops draw unclipped here (Skia/PDF clip them).
        if (image.ClipToEllipse && Math.Abs(image.RotationDegrees) < 0.01 &&
            context.GetEllipseClippedImage(data, (int) bounds.PixelWidth, (int) bounds.PixelHeight, image.Crop) is { } ellipseClipped)
        {
            currentCanvas.DrawImage(ellipseClipped, new((int) bounds.PixelX, (int) bounds.PixelY));
            return;
        }

        DrawBlockImage(data, bounds.PixelX, bounds.PixelY, bounds.PixelWidth, bounds.PixelHeight, (float) image.RotationDegrees, image.Crop, image.ColorEffect, image.FlipHorizontal, image.FlipVertical, image.DuotoneColorHex, image.DuotoneLightColorHex);
    }

    protected override void RenderFloatingTextBox(FloatingTextBoxElement textBox)
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

            DrawTextBoxChrome(textBox, pixelX, pixelY, pixelWidth, pixelHeight);

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
            DrawTextBoxChrome(textBox, pixelX, pixelY, pixelWidth, pixelHeight);

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

    /// <summary>
    /// The shape's chrome behind a text box's content: fill and a:ln outline, following the
    /// shape's geometry when it is richer than a rectangle (roundRect ticket outlines, plaque
    /// frames). Even-odd contours keep ring geometry hollow.
    /// </summary>
    void DrawTextBoxChrome(FloatingTextBoxElement textBox, float pixelX, float pixelY, float pixelWidth, float pixelHeight)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var path = BuildTextBoxPath(textBox, pixelX, pixelY, pixelWidth, pixelHeight)
                   ?? (IPath) new RectanglePolygon(pixelX, pixelY, pixelWidth, pixelHeight);

        if (textBox.BackgroundColorHex != null)
        {
            currentCanvas.Fill(context.GetBrush(ParseColor(textBox.BackgroundColorHex)), path);
        }

        if (textBox.LineColorHex != null && textBox.LineWidthPoints > 0)
        {
            currentCanvas.Draw(context.GetPen(ImageSharpWordArtDrawer.WithAlpha(ParseColor(textBox.LineColorHex), textBox.LineAlpha), (float) textBox.LineWidthPoints * context.Scale), path);
        }
    }

    static IPath? BuildTextBoxPath(FloatingTextBoxElement textBox, float x, float y, float width, float height)
    {
        if (textBox.Subpaths == null)
        {
            return null;
        }

        var polygons = new List<IPath>();
        foreach (var contour in textBox.Subpaths)
        {
            if (contour.Count < 3)
            {
                continue;
            }

            var points = new PointF[contour.Count];
            for (var index = 0; index < contour.Count; index++)
            {
                var (pointX, pointY) = contour[index];
                points[index] = new(x + (float) pointX * width, y + (float) pointY * height);
            }

            polygons.Add(new Polygon(new LinearLineSegment(points)));
        }

        return polygons.Count switch
        {
            0 => null,
            1 => polygons[0],
            _ => new ComplexPolygon(polygons.ToArray())
        };
    }

    public void Dispose()
    {
        currentCanvas?.Dispose();
        currentPage?.Dispose();
    }
}
