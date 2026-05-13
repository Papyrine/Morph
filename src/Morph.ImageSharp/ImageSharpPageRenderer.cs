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
    Image<Rgba32>? pendingPage;
    Image<Rgba32>? currentPage;
    DrawingCanvas? currentCanvas;
    float headerHeight;
    IReadOnlyList<Watermark> watermarks = [];

    bool hasSignificantContentOnCurrentPage;
    bool currentPageFromExplicitBreak;

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
        // Source images held by ImageBrush instances on the canvas timeline are safe to dispose
        // once the canvas has rendered (canvas.Dispose above flushes the timeline).
        context.DisposePendingPageDisposables();
        currentPage?.Dispose();
        currentPage = null;
    }

    void StartNewExplicitPage()
    {
        StartNewPage();
        currentPageFromExplicitBreak = true;
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
                FinishCurrentPage();
                StartNewPage();
            }
        }

        if (paragraph.Properties.KeepLines &&
            !isCompletelyEmpty)
        {
            if (!context.HasSpaceFor(height) &&
                height <= context.ContentHeight &&
                context.CurrentY > context.ContentTop)
            {
                FinishCurrentPage();
                StartNewPage();
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
                    FinishCurrentPage();
                    StartNewPage();
                }
            }
        }

        if (!isCompletelyEmpty)
        {
            EnsureSpaceFor(height);
        }

        if (currentCanvas != null)
        {
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

    protected override void DrawBlockImage(byte[] imageData, string? contentType, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, ImageCrop? crop, BlipColorEffect colorEffect)
    {
        // SVG images are not supported in the ImageSharp backend
        if (contentType == "image/svg+xml")
        {
            return;
        }

        DrawBlockImage(imageData, pixelX, pixelY, pixelWidth, pixelHeight, rotation, crop, colorEffect);
    }

    void DrawBlockImage(byte[] imageData, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, ImageCrop? crop, BlipColorEffect colorEffect = BlipColorEffect.None)
    {
        if (currentCanvas == null)
        {
            return;
        }

        try
        {
            // Retain for the page's lifetime — canvas.DrawImage queues an ImageBrush that holds
            // this Image until the canvas timeline renders on FinishCurrentPage's Dispose.
            var img = Image.Load<Rgba32>(imageData);
            context.RetainForPage(img);

            if (crop is { IsCropped: true } c)
            {
                var srcLeft = (int) (c.Left * img.Width);
                var srcTop = (int) (c.Top * img.Height);
                var srcWidth = Math.Max(1, img.Width - srcLeft - (int) (c.Right * img.Width));
                var srcHeight = Math.Max(1, img.Height - srcTop - (int) (c.Bottom * img.Height));
                img.Mutate(_ => _.Crop(new(srcLeft, srcTop, srcWidth, srcHeight)));
            }

            img.Mutate(_ => _.Resize((int) pixelWidth, (int) pixelHeight));

            // Apply Word's "Recolor" gallery preset before drawing.
            if (colorEffect != BlipColorEffect.None)
            {
                ApplyBlipColorEffect(img, colorEffect);
            }

            if (rotation != 0)
            {
                img.Mutate(_ => _.Rotate(rotation));
                var newX = pixelX + pixelWidth / 2 - img.Width / 2f;
                var newY = pixelY + pixelHeight / 2 - img.Height / 2f;
                currentCanvas.DrawImage(img, new((int) newX, (int) newY));
            }
            else
            {
                currentCanvas.DrawImage(img, new((int) pixelX, (int) pixelY));
            }
        }
        catch
        {
            // Ignore image decode errors
        }
    }

    static void ApplyBlipColorEffect(Image<Rgba32> img, BlipColorEffect effect)
    {
        switch (effect)
        {
            case BlipColorEffect.Grayscale:
            case BlipColorEffect.Duotone:
                img.Mutate(_ => _.Grayscale());
                break;
            case BlipColorEffect.Washout:
                // Word's washout: brightness +70%, contrast -50%. ImageSharp's
                // Brightness/Contrast operate in 0–N space — these constants line up
                // visually with Skia's color-matrix branch.
                img.Mutate(_ => _.Brightness(1.7f).Contrast(0.5f));
                break;
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
            wordArt.HeightPoints);
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

    float MeasureTableHeight(TableElement table)
    {
        if (table.Rows.Count == 0)
        {
            return 0;
        }

        var colCount = TableLayout.GetColumnCount(table);

        var colWidths = TableLayout.CalculateColumnWidths(table, colCount, context.ContentWidth, textRenderer);
        var rowHeights = TableHeightCalculator.CalculateRowHeights(table, colWidths, textRenderer, TableLayout.HasVerticalMerge(table));

        return rowHeights.Sum();
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

        // Render unrotated into a temp image whose width is the rotated wrap-width
        // (real-space cell vertical extent) and whose height is the rotated cross-axis
        // (real-space cell horizontal extent). Then rotate ±90 and blit.
        var tempW = (int) Math.Ceiling(context.PointsToPixels(availableHeight));
        var tempH = (int) Math.Ceiling(context.PointsToPixels(contentWidth));
        if (tempW <= 0 || tempH <= 0)
        {
            return;
        }

        var tempImage = new Image<Rgba32>(tempW, tempH);
        // Retained: tempImage is the source of currentCanvas.DrawImage below, so it must survive
        // until the page's canvas timeline renders.
        context.RetainForPage(tempImage);
        // Scoped canvas — text rendering for this cell batches onto its own timeline,
        // disposed before the rotate+blit below so the rotation sees the pixels.
        using (var tempCanvas = tempImage.Frames.RootFrame.CreateCanvas(Configuration.Default, new()))
        {
            var savedY = context.CurrentY;
            context.CurrentY = 0;

            foreach (var element in cell.Content)
            {
                if (element is ParagraphElement para)
                {
                    textRenderer.RenderParagraphInBounds(tempCanvas, para, 0, availableHeight);
                }
                else if (element is ContentControlElement {Runs.Count: > 0} cc)
                {
                    var ccPara = new ParagraphElement
                    {
                        Runs = cc.Runs,
                        Properties = new()
                    };
                    textRenderer.RenderParagraphInBounds(tempCanvas, ccPara, 0, availableHeight);
                }
            }

            context.CurrentY = savedY;
        }

        var bottomToTop = cell.Properties.TextDirection == CellTextDirection.BottomToTop;
        tempImage.Mutate(_ => _.Rotate(bottomToTop ? -90f : 90f));

        var drawX = (int) context.PointsToPixels(contentX);
        var drawY = (int) context.PointsToPixels(contentY);
        currentCanvas.DrawImage(tempImage, new(drawX, drawY));
    }


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

        try
        {
            var img = Image.Load<Rgba32>(data);
            context.RetainForPage(img);
            img.Mutate(_ => _.Resize((int) pixelWidth, (int) pixelHeight));
            currentCanvas.DrawImage(img, new((int) pixelX, (int) pixelY));
        }
        catch
        {
            // Ignore image decode errors
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

        var bgColor = context.PageSettings.BackgroundColorHex;
        Color fillColor;
        if (string.IsNullOrEmpty(bgColor))
        {
            fillColor = Color.White;
        }
        else
        {
            fillColor = ParseColor(bgColor);
        }

        currentCanvas.Fill(context.GetBrush(fillColor), new RectanglePolygon(0, 0, context.PageWidthPixels, context.PageHeightPixels));

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
            // Source images retained for ImageBrush rendering are safe to free now.
            context.DisposePendingPageDisposables();
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
            if (watermark.ImageData != null)
            {
                DrawPictureWatermark(watermark);
            }
            else if (!string.IsNullOrEmpty(watermark.Text))
            {
                DrawTextWatermark(watermark);
            }
        }
    }

    void DrawPictureWatermark(Watermark watermark)
    {
        try
        {
            var img = Image.Load<Rgba32>(watermark.ImageData!);
            context.RetainForPage(img);

            // Word's gain/blacklevel washout: out = in * gain + blackLevel (per channel).
            // Bake the alpha-fade into the pixel alpha so DrawImage doesn't have to do it —
            // ImageSharp's DrawImage opacity parameter sometimes blends differently from
            // straight alpha multiplication.
            var gain = (float) watermark.Gain;
            var bias = (byte) Math.Clamp(watermark.BlackLevel * 255, 0, 255);
            // ImageSharp's blending is more conservative than Skia's — without a stronger alpha
            // cut the watermark stays too dark over the page. Half the gain matches Word's near-
            // invisible rendering of the standard washout preset (0.30 * 0.5 ≈ 0.15 alpha).
            var alphaScale = (float) Math.Clamp(watermark.Gain * 0.5, 0.1, 1.0);
            img.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var pixel = row[x];
                        row[x] = new(
                            (byte) Math.Clamp(pixel.R * gain + bias, 0, 255),
                            (byte) Math.Clamp(pixel.G * gain + bias, 0, 255),
                            (byte) Math.Clamp(pixel.B * gain + bias, 0, 255),
                            (byte) (pixel.A * alphaScale));
                    }
                }
            });

            // Scale to full page (matches Word's typical "width:612pt;height:11in" watermark sizing).
            var pageWidth = (float) context.PageWidthPixels;
            var pageHeight = (float) context.PageHeightPixels;
            var imageAspect = (float) img.Width / img.Height;
            var pageAspect = pageWidth / pageHeight;

            int drawW, drawH;
            if (imageAspect > pageAspect)
            {
                drawW = (int) pageWidth;
                drawH = (int) (drawW / imageAspect);
            }
            else
            {
                drawH = (int) pageHeight;
                drawW = (int) (drawH * imageAspect);
            }

            img.Mutate(_ => _.Resize(drawW, drawH));

            var x = (int) ((pageWidth - drawW) / 2);
            var y = (int) ((pageHeight - drawH) / 2);
            // Alpha already baked into pixels above; DrawImage opacity stays at 1.0.
            currentCanvas!.DrawImage(img, new(x, y));
        }
        catch
        {
            // Ignore image decode errors — watermark just won't appear, document still renders.
        }
    }

    void DrawTextWatermark(Watermark watermark)
    {
        var fontFamily = context.GetFontFamily(watermark.FontFamily, watermark.Bold, italic: false);
        var font = fontFamily.CreateFont((float) watermark.FontSizePoints * context.Scale, watermark.Bold ? FontStyle.Bold : FontStyle.Regular);
        var color = ParseColor(watermark.ColorHex);

        var textOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Origin = new PointF(context.PageWidthPixels / 2f, context.PageHeightPixels / 2f)
        };

        // ImageSharp's DrawText doesn't accept a transform per call, so we draw onto a temporary
        // image then rotate it before compositing. The temporary image only needs to cover the
        // text bounding box, not the whole page.
        var bounds = TextMeasurer.MeasureBounds(watermark.Text!, textOptions);
        var tempW = (int) Math.Ceiling(bounds.Width) + 4;
        var tempH = (int) Math.Ceiling(bounds.Height) + 4;
        if (tempW <= 0 || tempH <= 0)
        {
            return;
        }

        var temp = new Image<Rgba32>(tempW, tempH);
        context.RetainForPage(temp);
        var tempOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Origin = new PointF(tempW / 2f, tempH / 2f)
        };
        using (var tempCanvas = temp.Frames.RootFrame.CreateCanvas(Configuration.Default, new()))
        {
            tempCanvas.DrawText(tempOptions, watermark.Text!, context.GetBrush(color));
        }
        temp.Mutate(_ => _.Rotate((float) watermark.RotationDegrees));

        var dstX = (context.PageWidthPixels - temp.Width) / 2;
        var dstY = (context.PageHeightPixels - temp.Height) / 2;
        currentCanvas!.DrawImage(temp, new(dstX, dstY));
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
            height);
        var pixelX = bounds.PixelX;
        var pixelY = bounds.PixelY;
        var pixelWidth = bounds.PixelWidth;
        var pixelHeight = bounds.PixelHeight;

        if (shape.ImageData != null)
        {
            try
            {
                var img = Image.Load<Rgba32>(shape.ImageData);
                context.RetainForPage(img);
                img.Mutate(_ => _.Resize((int) pixelWidth, (int) pixelHeight));
                currentCanvas.DrawImage(img, new((int) pixelX, (int) pixelY));
            }
            catch
            {
                // Ignore image decode errors
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
            if (shape.PolygonPoints != null)
            {
                var polygon = BuildPolygon(shape, pixelX, pixelY, pixelWidth, pixelHeight);
                currentCanvas.Draw(pen, polygon);
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
        if (shape.PolygonPoints != null)
        {
            var polygon = BuildPolygon(shape, x, y, width, height);
            currentCanvas!.Fill(brush, polygon);
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

    static Polygon BuildPolygon(FloatingShapeElement shape, float x, float y, float width, float height)
    {
        var pts = shape.PolygonPoints!;
        var rotRad = (float) (shape.RotationDegrees * Math.PI / 180.0);
        var cos = (float) Math.Cos(rotRad);
        var sin = (float) Math.Sin(rotRad);
        var halfW = width / 2f;
        var halfH = height / 2f;

        var transformed = new PointF[pts.Count];
        for (var i = 0; i < pts.Count; i++)
        {
            var (px, py) = pts[i];
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
        return new(transformed);
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
            height);

        DrawBlockImage(data, bounds.PixelX, bounds.PixelY, bounds.PixelWidth, bounds.PixelHeight, (float) image.RotationDegrees, image.Crop);
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
            textBox.HeightPoints);
        var x = bounds.X;
        var y = bounds.Y;
        var pixelX = bounds.PixelX;
        var pixelY = bounds.PixelY;
        var pixelWidth = bounds.PixelWidth;
        var pixelHeight = bounds.PixelHeight;

        if (Math.Abs(textBox.RotationDegrees) > 0.01)
        {
            // Render to a temporary image, then rotate and compose onto the page
            var tempW = (int) Math.Ceiling(pixelWidth);
            var tempH = (int) Math.Ceiling(pixelHeight);
            if (tempW <= 0 || tempH <= 0)
            {
                return;
            }

            var tempImage = new Image<Rgba32>(tempW, tempH);
            context.RetainForPage(tempImage);
            using (var tempCanvas = tempImage.Frames.RootFrame.CreateCanvas(Configuration.Default, new()))
            {
                if (textBox.BackgroundColorHex != null)
                {
                    var bgColor = ParseColor(textBox.BackgroundColorHex);
                    tempCanvas.Fill(bgColor);
                }

                var savedY = context.CurrentY;
                context.CurrentY = 0;

                foreach (var element in textBox.Content)
                {
                    if (element is ParagraphElement para)
                    {
                        textRenderer.RenderParagraphInBounds(tempCanvas, para, 0, (float) textBox.WidthPoints);
                    }
                }

                context.CurrentY = savedY;
            }

            tempImage.Mutate(_ => _.Rotate((float) textBox.RotationDegrees));

            // Center the rotated image at the original text box center
            var centerX = pixelX + pixelWidth / 2;
            var centerY = pixelY + pixelHeight / 2;
            var drawX = (int) (centerX - tempImage.Width / 2f);
            var drawY = (int) (centerY - tempImage.Height / 2f);

            currentCanvas.DrawImage(tempImage, new(drawX, drawY));
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
