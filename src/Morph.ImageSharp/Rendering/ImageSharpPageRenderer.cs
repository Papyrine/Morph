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
    HeaderFooterContent? header;
    HeaderFooterContent? footer;
    HeaderFooterContent? firstPageHeader;
    HeaderFooterContent? firstPageFooter;
    HeaderFooterContent? evenPageHeader;
    HeaderFooterContent? evenPageFooter;
    bool differentFirstPage;
    float headerHeight;
    float footerHeight;
    IReadOnlyList<Watermark> watermarks = [];

    bool hasSignificantContentOnCurrentPage;
    bool currentPageFromExplicitBreak;

    Dictionary<string, Color> colorCache = new();

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
                RenderBackgroundShape(shape);
                continue;
            }

            DocumentElement? nextElement = null;
            for (var j = i + 1; j < elements.Count; j++)
            {
                if (elements[j] is not FloatingShapeElement {BehindText: true})
                {
                    nextElement = elements[j];
                    break;
                }
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
            .Where(_ => _.Id != "0" && _.Id != "-1" && !string.IsNullOrWhiteSpace(_.Text))
            .ToList();
        var endnotes = document.Endnotes
            .Where(_ => _.Id != "0" && _.Id != "-1" && !string.IsNullOrWhiteSpace(_.Text))
            .ToList();

        if (footnotes.Count == 0 && endnotes.Count == 0)
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
                new() {Text = heading, Properties = new() {Bold = true, FontSizePoints = 12}}
            ],
            Properties = new() {SpacingBeforePoints = 12, SpacingAfterPoints = 6}
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

    void RenderHeader()
    {
        HeaderFooterContent? activeHeader;
        if (differentFirstPage &&
            context.CurrentPageNumber == 1)
        {
            activeHeader = firstPageHeader;
        }
        else if (evenPageHeader != null &&
                 context.CurrentPageNumber % 2 == 0)
        {
            activeHeader = evenPageHeader;
        }
        else
        {
            activeHeader = header;
        }

        if (activeHeader == null || currentPage == null)
        {
            return;
        }

        var savedY = context.CurrentY;
        context.CurrentY = (float) context.PageSettings.HeaderDistance;

        foreach (var element in activeHeader.Elements)
        {
            if (element is FloatingShapeElement {BehindText: true} shape)
            {
                RenderBackgroundShape(shape);
            }
            else if (element is FloatingImageElement floatingImage)
            {
                RenderFloatingImage(floatingImage);
            }
            else if (element is ParagraphElement para)
            {
                textRenderer.RenderParagraph(currentPage, para);
            }
        }

        context.CurrentY = savedY;
    }

    void RenderFooter()
    {
        HeaderFooterContent? activeFooter;
        if (differentFirstPage &&
            context.CurrentPageNumber == 1)
        {
            activeFooter = firstPageFooter;
        }
        else if (evenPageFooter != null &&
                 context.CurrentPageNumber % 2 == 0)
        {
            activeFooter = evenPageFooter;
        }
        else
        {
            activeFooter = footer;
        }

        if (activeFooter == null || currentPage == null)
        {
            return;
        }

        var savedY = context.CurrentY;
        context.CurrentY = (float) (context.PageSettings.HeightPoints - context.PageSettings.FooterDistance - footerHeight);

        foreach (var element in activeFooter.Elements)
        {
            if (element is FloatingShapeElement {BehindText: true} shape)
            {
                RenderBackgroundShape(shape);
            }
            else if (element is FloatingImageElement floatingImage)
            {
                RenderFloatingImage(floatingImage);
            }
            else if (element is ParagraphElement para)
            {
                textRenderer.RenderParagraph(currentPage, para);
            }
        }

        context.CurrentY = savedY;
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

    void RenderParagraph(ParagraphElement paragraph, DocumentElement? nextElement = null)
    {
        var hasSignificantContent = paragraph.Runs.Any(r => !string.IsNullOrWhiteSpace(r.Text));
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

        if (currentPage != null)
        {
            textRenderer.RenderParagraph(currentPage, paragraph, nextElement);
        }

        if (hasSignificantContent)
        {
            hasSignificantContentOnCurrentPage = true;
        }
    }

    void RenderHorizontalRule()
    {
        const float ruleHeight = 6;
        EnsureSpaceFor(ruleHeight);

        if (currentPage != null)
        {
            var y = context.PointsToPixels(context.CurrentY + 3);
            var x1 = context.PointsToPixels(context.ContentLeft);
            var x2 = context.PointsToPixels(context.ContentLeft + context.ContentWidth);
            var pen = Pens.Solid(Color.FromRgb(0xA0, 0xA0, 0xA0), context.PointsToPixels(0.75f));
            currentPage.Mutate(_ => _.DrawLine(pen, new PointF(x1, y), new PointF(x2, y)));
        }

        context.CurrentY += ruleHeight;
    }

    void RenderImage(ImageElement image)
    {
        var height = (float) image.HeightPoints;
        EnsureSpaceFor(height);

        if (currentPage == null)
        {
            return;
        }

        // SVG images are not supported in ImageSharp backend
        if (image.ContentType == "image/svg+xml")
        {
            context.CurrentY += height;
            return;
        }

        var x = context.PointsToPixels(context.ContentLeft);
        var y = context.PointsToPixels(context.CurrentY);
        var width = context.PointsToPixels((float) image.WidthPoints);
        var pixelHeight = context.PointsToPixels(height);

        DrawBlockImage(image.ImageData, x, y, width, pixelHeight, (float) image.RotationDegrees, image.Crop, image.ColorEffect);

        context.CurrentY += height;
    }

    void DrawBlockImage(byte[] imageData, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, ImageCrop? crop, BlipColorEffect colorEffect = BlipColorEffect.None)
    {
        if (currentPage == null)
        {
            return;
        }

        try
        {
            using var img = Image.Load<Rgba32>(imageData);

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
                currentPage.Mutate(_ => _.DrawImage(img, new Point((int) newX, (int) newY), 1f));
            }
            else
            {
                currentPage.Mutate(_ => _.DrawImage(img, new Point((int) pixelX, (int) pixelY), 1f));
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

        if (currentPage == null)
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

        var textSize = TextMeasurer.MeasureSize(
            wordArt.Text,
            new(font)
            {
                Dpi = context.Dpi
            });

        var scaleX = textSize.Width > 0 ? width / textSize.Width : 1;
        var scaleY = textSize.Height > 0 ? pixelHeight / textSize.Height : 1;
        var scale = Math.Min(scaleX, scaleY);

        var scaledFont = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints * scale,
            wordArt.Bold,
            wordArt.Italic);

        var scaledSize = TextMeasurer.MeasureSize(
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
            var shadowColor = Color.FromRgba(0, 0, 0, 80);
            currentPage.Mutate(_ => _.DrawText(wordArt.Text, scaledFont, shadowColor, new(textX + 3, textY + 3)));
        }

        if (wordArt is {OutlineColorHex: not null, OutlineWidthPoints: > 0})
        {
            var outlineColor = ParseColor(wordArt.OutlineColorHex);
            var outlinePen = Pens.Solid(outlineColor, context.PointsToPixels((float) wordArt.OutlineWidthPoints));
            currentPage.Mutate(_ => _.DrawText(wordArt.Text, scaledFont, outlinePen, new(textX, textY)));
        }

        currentPage.Mutate(_ => _.DrawText(wordArt.Text, scaledFont, fillColor, new(textX, textY)));

        context.CurrentY += height;
    }

    void RenderFloatingWordArt(FloatingWordArtElement wordArt)
    {
        if (currentPage == null)
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

        var textSize = TextMeasurer.MeasureSize(
            wordArt.Text,
            new(font)
            {
                Dpi = context.Dpi
            });

        var scaleX = textSize.Width > 0 ? width / textSize.Width : 1;
        var scaleY = textSize.Height > 0 ? pixelHeight / textSize.Height : 1;
        var scale = Math.Min(scaleX, scaleY);

        var scaledFont = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints * scale,
            wordArt.Bold,
            wordArt.Italic);

        var scaledSize = TextMeasurer.MeasureSize(
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
            var shadowColor = Color.FromRgba(0, 0, 0, 80);
            currentPage.Mutate(_ => _.DrawText(wordArt.Text, scaledFont, shadowColor, new(textX + 3, textY + 3)));
        }

        if (wordArt is {OutlineColorHex: not null, OutlineWidthPoints: > 0})
        {
            var outlineColor = ParseColor(wordArt.OutlineColorHex);
            var outlinePen = Pens.Solid(outlineColor, context.PointsToPixels((float) wordArt.OutlineWidthPoints));
            currentPage.Mutate(_ => _.DrawText(wordArt.Text, scaledFont, outlinePen, new(textX, textY)));
        }

        currentPage.Mutate(_ => _.DrawText(wordArt.Text, scaledFont, fillColor, new(textX, textY)));
    }

    void RenderInk(InkElement ink)
    {
        var height = (float) ink.HeightPoints;
        EnsureSpaceFor(height);

        if (currentPage == null)
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
                color = Color.FromRgba(pixel.R, pixel.G, pixel.B, alpha);
            }

            var strokeWidth = context.PointsToPixels((float) stroke.WidthPoints);
            var pen = Pens.Solid(color, strokeWidth);

            var points = new PointF[stroke.Points.Count];
            for (var i = 0; i < stroke.Points.Count; i++)
            {
                var point = stroke.Points[i];
                points[i] = new(
                    baseX + context.PointsToPixels((float) point.X),
                    baseY + context.PointsToPixels((float) point.Y));
            }

            currentPage.Mutate(_ => _.DrawLine(pen, points));
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

        var colWidths = TableLayout.CalculateColumnWidths(table, colCount, context.ContentWidth);
        var rowHeights = TableHeightCalculator.CalculateRowHeights(table, colWidths, textRenderer, TableLayout.HasVerticalMerge(table));

        return rowHeights.Sum();
    }


    protected override void RenderVerticalCellContent(TableCell cell, float cellX, float cellY, float cellWidth, float cellHeight, CellSpacing padding)
    {
        if (currentPage == null)
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

        using var tempImage = new Image<Rgba32>(tempW, tempH);

        var savedY = context.CurrentY;
        context.CurrentY = 0;

        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                textRenderer.RenderParagraphInBounds(tempImage, para, 0, availableHeight);
            }
            else if (element is ContentControlElement {Runs.Count: > 0} cc)
            {
                var ccPara = new ParagraphElement
                {
                    Runs = cc.Runs,
                    Properties = new()
                };
                textRenderer.RenderParagraphInBounds(tempImage, ccPara, 0, availableHeight);
            }
        }

        context.CurrentY = savedY;

        var bottomToTop = cell.Properties.TextDirection == CellTextDirection.BottomToTop;
        tempImage.Mutate(_ => _.Rotate(bottomToTop ? -90f : 90f));

        var drawX = (int) context.PointsToPixels(contentX);
        var drawY = (int) context.PointsToPixels(contentY);
        currentPage.Mutate(_ => _.DrawImage(tempImage, new Point(drawX, drawY), 1f));
    }


    protected override void DrawCellBackground(float pixelX, float pixelY, float pixelWidth, float pixelHeight, string hexColor)
    {
        if (currentPage == null)
        {
            return;
        }

        var bgColor = ParseColor(hexColor);
        currentPage.Mutate(_ => _.Fill(bgColor, new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight)));
    }

    protected override void DrawCellBorders(float pixelX, float pixelY, float pixelWidth, float pixelHeight, CellBorders borders)
    {
        if (currentPage == null)
        {
            return;
        }

        currentPage.Mutate(_ =>
        {
            if (borders.Top.IsVisible)
            {
                DrawBorderLine(_, pixelX, pixelY, pixelX + pixelWidth, pixelY, borders.Top);
            }

            if (borders.Right.IsVisible)
            {
                DrawBorderLine(_, pixelX + pixelWidth, pixelY, pixelX + pixelWidth, pixelY + pixelHeight, borders.Right);
            }

            if (borders.Bottom.IsVisible)
            {
                DrawBorderLine(_, pixelX, pixelY + pixelHeight, pixelX + pixelWidth, pixelY + pixelHeight, borders.Bottom);
            }

            if (borders.Left.IsVisible)
            {
                DrawBorderLine(_, pixelX, pixelY, pixelX, pixelY + pixelHeight, borders.Left);
            }
        });
    }

    void DrawBorderLine(float x1, float y1, float x2, float y2, BorderEdge edge)
    {
        if (currentPage == null)
        {
            return;
        }

        currentPage.Mutate(_ => DrawBorderLine(_, x1, y1, x2, y2, edge));
    }

    void DrawBorderLine(IImageProcessingContext ctx, float x1, float y1, float x2, float y2, BorderEdge edge)
    {
        var color = ParseColor(edge.ColorHex ?? "000000");
        var strokeWidth = context.PointsToPixels((float) edge.WidthPoints);
        var pen = Pens.Solid(color, strokeWidth);

        ctx.DrawLine(pen, new PointF(x1, y1), new PointF(x2, y2));
    }

    protected override void RenderParagraphInBounds(ParagraphElement paragraph, float x, float maxWidth)
    {
        if (currentPage == null)
        {
            return;
        }

        textRenderer.RenderParagraphInBounds(currentPage, paragraph, x, maxWidth);
    }

    protected override void RenderImageInCell(ImageElement image, float x, float maxWidth)
    {
        if (currentPage == null)
        {
            return;
        }

        if (image.ContentType == "image/svg+xml")
        {
            context.CurrentY += (float) image.HeightPoints;
            return;
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
            using var img = Image.Load<Rgba32>(image.ImageData);
            img.Mutate(_ => _.Resize((int) pixelWidth, (int) pixelHeight));
            currentPage.Mutate(_ => _.DrawImage(img, new Point((int) pixelX, (int) pixelY), 1f));
        }
        catch
        {
            // Ignore image decode errors
        }

        context.CurrentY += imageHeight;
    }

    void RenderTextFormField(TextFormFieldElement textField)
    {
        if (currentPage == null)
        {
            return;
        }

        var fieldWidth = (float) textField.WidthPoints;
        float fieldHeight = 18;
        var x = context.ContentLeft;
        var y = context.CurrentY;

        if (y + fieldHeight > context.ContentBottom)
        {
            FinishCurrentPage();
            StartNewPage();
            y = context.CurrentY;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels(fieldWidth);
        var pixelHeight = context.PointsToPixels(fieldHeight);

        var bgColor = textField.Enabled ? Color.White : Color.FromRgb(240, 240, 240);
        var rect = new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight);
        currentPage.Mutate(_ =>
        {
            _.Fill(bgColor, rect);
            _.Draw(Pens.Solid(Color.Gray, 1 * context.Scale), rect);
        });

        var displayText = string.IsNullOrEmpty(textField.Value) ? textField.DefaultText ?? "" : textField.Value;
        if (!string.IsNullOrEmpty(displayText))
        {
            var font = context.GetFontForFamily(DefaultFontSettings.DefaultFont, 10, false, false);
            var textColor = textField.Enabled ? Color.Black : Color.Gray;
            var textX = pixelX + 3 * context.Scale;
            var textY = pixelY + 2 * context.Scale;
            currentPage.Mutate(_ => _.DrawText(displayText, font, textColor, new(textX, textY)));
        }

        context.CurrentY += fieldHeight + 4;
    }

    void RenderCheckBoxFormField(CheckBoxFormFieldElement checkBox)
    {
        if (currentPage == null)
        {
            return;
        }

        var boxSize = checkBox.SizePoints > 0 ? (float) checkBox.SizePoints : 12;
        var x = context.ContentLeft;
        var y = context.CurrentY;

        if (y + boxSize > context.ContentBottom)
        {
            FinishCurrentPage();
            StartNewPage();
            y = context.CurrentY;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelSize = context.PointsToPixels(boxSize);

        var bgColor = checkBox.Enabled ? Color.White : Color.FromRgb(240, 240, 240);
        var rect = new RectangleF(pixelX, pixelY, pixelSize, pixelSize);
        currentPage.Mutate(_ =>
        {
            _.Fill(bgColor, rect);
            _.Draw(Pens.Solid(Color.Black, 1 * context.Scale), rect);
        });

        if (checkBox.Checked)
        {
            var checkPen = Pens.Solid(Color.Black, 2 * context.Scale);
            var pad = pixelSize * 0.2f;
            var left = pixelX + pad;
            var right = pixelX + pixelSize - pad;
            var top = pixelY + pad;
            var bottom = pixelY + pixelSize - pad;
            var midX = pixelX + pixelSize * 0.4f;

            currentPage.Mutate(_ =>
            {
                _.DrawLine(checkPen, new PointF(left, top + (bottom - top) * 0.5f), new PointF(midX, bottom));
                _.DrawLine(checkPen, new PointF(midX, bottom), new PointF(right, top));
            });
        }

        context.CurrentY += boxSize + 4;
    }

    void RenderDropDownFormField(DropDownFormFieldElement dropDown)
    {
        if (currentPage == null)
        {
            return;
        }

        var fieldWidth = (float) dropDown.WidthPoints;
        float fieldHeight = 18;
        var x = context.ContentLeft;
        var y = context.CurrentY;

        if (y + fieldHeight > context.ContentBottom)
        {
            FinishCurrentPage();
            StartNewPage();
            y = context.CurrentY;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels(fieldWidth);
        var pixelHeight = context.PointsToPixels(fieldHeight);

        var bgColor = dropDown.Enabled ? Color.White : Color.FromRgb(240, 240, 240);
        var rect = new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight);
        currentPage.Mutate(_ =>
        {
            _.Fill(bgColor, rect);
            _.Draw(Pens.Solid(Color.Gray, 1 * context.Scale), rect);
        });

        var selectedValue = dropDown.SelectedIndex >= 0 && dropDown.SelectedIndex < dropDown.Items.Count
            ? dropDown.Items[dropDown.SelectedIndex]
            : "";

        if (!string.IsNullOrEmpty(selectedValue))
        {
            var font = context.GetFontForFamily(DefaultFontSettings.DefaultFont, 10, false, false);
            var textColor = dropDown.Enabled ? Color.Black : Color.Gray;
            currentPage.Mutate(_ => _.DrawText(selectedValue, font, textColor, new(pixelX + 3 * context.Scale, pixelY + 2 * context.Scale)));
        }

        var arrowSize = pixelHeight * 0.3f;
        var arrowX = pixelX + pixelWidth - 12 * context.Scale;
        var arrowY = pixelY + pixelHeight / 2;

        var arrowBuilder = new PathBuilder();
        arrowBuilder.AddLine(new(arrowX, arrowY - arrowSize / 2), new(arrowX + arrowSize, arrowY - arrowSize / 2));
        arrowBuilder.AddLine(new(arrowX + arrowSize, arrowY - arrowSize / 2), new(arrowX + arrowSize / 2, arrowY + arrowSize / 2));
        arrowBuilder.CloseFigure();
        currentPage.Mutate(_ => _.Fill(Color.Black, arrowBuilder.Build()));

        context.CurrentY += fieldHeight + 4;
    }

    void RenderContentControl(ContentControlElement control)
    {
        if (currentPage == null)
        {
            return;
        }

        switch (control.ControlType)
        {
            case ContentControlType.CheckBox:
                RenderContentControlCheckBox(control);
                break;

            case ContentControlType.ComboBox:
            case ContentControlType.DropDownList:
                RenderContentControlDropDown(control);
                break;

            case ContentControlType.Date:
                RenderContentControlDate(control);
                break;

            default:
                RenderContentControlText(control);
                break;
        }
    }

    void RenderContentControlCheckBox(ContentControlElement control)
    {
        if (currentPage == null)
        {
            return;
        }

        float boxSize = 12;
        var x = context.ContentLeft;
        var y = context.CurrentY;

        if (y + boxSize > context.ContentBottom)
        {
            FinishCurrentPage();
            StartNewPage();
            y = context.CurrentY;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelSize = context.PointsToPixels(boxSize);

        var rect = new RectangleF(pixelX, pixelY, pixelSize, pixelSize);
        currentPage.Mutate(_ =>
        {
            _.Fill(Color.White, rect);
            _.Draw(Pens.Solid(Color.Black, 1 * context.Scale), rect);
        });

        if (control.Checked == true)
        {
            var checkPen = Pens.Solid(Color.Black, 2 * context.Scale);
            var pad = pixelSize * 0.25f;

            currentPage.Mutate(_ =>
            {
                _.DrawLine(checkPen, new PointF(pixelX + pad, pixelY + pad), new PointF(pixelX + pixelSize - pad, pixelY + pixelSize - pad));
                _.DrawLine(checkPen, new PointF(pixelX + pixelSize - pad, pixelY + pad), new PointF(pixelX + pad, pixelY + pixelSize - pad));
            });
        }

        context.CurrentY += boxSize + 4;
    }

    void RenderContentControlText(ContentControlElement control)
    {
        if (currentPage == null)
        {
            return;
        }

        if (control.ControlType is ContentControlType.RichText or ContentControlType.PlainText)
        {
            if (control.Runs is {Count: > 0})
            {
                RenderParagraph(
                    new()
                    {
                        Runs = control.Runs,
                        Properties = new()
                    });
            }
            else
            {
                var displayText = string.IsNullOrEmpty(control.Content) ? control.PlaceholderText ?? "" : control.Content;
                if (!string.IsNullOrEmpty(displayText))
                {
                    RenderParagraph(
                        new()
                        {
                            Runs =
                            [
                                new()
                                {
                                    Text = displayText,
                                    Properties = new()
                                }
                            ],
                            Properties = new()
                        });
                }
            }

            return;
        }

        RenderFormFieldBox(control.WidthPoints, control.Content, control.PlaceholderText);
    }

    void RenderContentControlDropDown(ContentControlElement control)
    {
        if (currentPage == null)
        {
            return;
        }

        string? first = null;
        foreach (var item in control.ListItems!)
        {
            first = item;
            break;
        }

        var displayText = string.IsNullOrEmpty(control.Content)
            ? first ?? control.PlaceholderText ?? ""
            : control.Content;

        RenderFormFieldBox(control.WidthPoints, displayText, null, drawDropdownArrow: true);
    }

    void RenderContentControlDate(ContentControlElement control)
    {
        if (currentPage == null)
        {
            return;
        }

        var displayText = control.DateValue.HasValue
            ? control.DateValue.Value.ToShortDateString()
            : !string.IsNullOrEmpty(control.Content) ? control.Content : control.PlaceholderText ?? "";

        RenderFormFieldBox(control.WidthPoints, displayText, null);
    }

    void RenderFormFieldBox(double fieldWidthPoints, string? content, string? placeholder, bool drawDropdownArrow = false)
    {
        if (currentPage == null)
        {
            return;
        }

        var fieldWidth = (float) fieldWidthPoints;
        float fieldHeight = 18;
        var x = context.ContentLeft;
        var y = context.CurrentY;

        if (y + fieldHeight > context.ContentBottom)
        {
            FinishCurrentPage();
            StartNewPage();
            y = context.CurrentY;
        }

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels(fieldWidth);
        var pixelHeight = context.PointsToPixels(fieldHeight);

        currentPage.Mutate(_ =>
        {
            _.Fill(Color.FromRgb(245, 245, 245), new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight));
            _.Draw(Pens.Solid(Color.FromRgb(200, 200, 200), 1 * context.Scale), new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight));
        });

        var text = string.IsNullOrEmpty(content) ? placeholder ?? "" : content;
        var isPlaceholder = string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(placeholder);

        if (!string.IsNullOrEmpty(text))
        {
            var font = context.GetFontForFamily(DefaultFontSettings.DefaultFont, 10, false, false);
            var textColor = isPlaceholder ? Color.Gray : Color.Black;
            currentPage.Mutate(_ => _.DrawText(text, font, textColor, new(pixelX + 3 * context.Scale, pixelY + 2 * context.Scale)));
        }

        if (drawDropdownArrow)
        {
            var arrowSize = pixelHeight * 0.3f;
            var arrowX = pixelX + pixelWidth - 12 * context.Scale;
            var arrowY = pixelY + pixelHeight / 2;

            var arrowBuilder = new PathBuilder();
            arrowBuilder.AddLine(new(arrowX, arrowY - arrowSize / 2), new(arrowX + arrowSize, arrowY - arrowSize / 2));
            arrowBuilder.AddLine(new(arrowX + arrowSize, arrowY - arrowSize / 2), new(arrowX + arrowSize / 2, arrowY + arrowSize / 2));
            arrowBuilder.CloseFigure();
            currentPage.Mutate(_ => _.Fill(Color.Black, arrowBuilder.Build()));
        }

        context.CurrentY += fieldHeight + 4;
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

        currentPage.Mutate(_ => _.Fill(fillColor, new RectangleF(0, 0, context.PageWidthPixels, context.PageHeightPixels)));

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
            pendingPage = currentPage;
            currentPage = null;
        }
    }

    void DrawWatermarks()
    {
        if (currentPage == null || watermarks.Count == 0)
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
            using var img = Image.Load<Rgba32>(watermark.ImageData!);

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
            currentPage!.Mutate(_ => _.DrawImage(img, new Point(x, y), 1f));
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

        using var temp = new Image<Rgba32>(tempW, tempH);
        var tempOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Origin = new PointF(tempW / 2f, tempH / 2f)
        };
        temp.Mutate(_ => _.DrawText(tempOptions, watermark.Text!, new SolidBrush(color)));
        temp.Mutate(_ => _.Rotate((float) watermark.RotationDegrees));

        var dstX = (context.PageWidthPixels - temp.Width) / 2;
        var dstY = (context.PageHeightPixels - temp.Height) / 2;
        currentPage!.Mutate(_ => _.DrawImage(temp, new Point(dstX, dstY), 1f));
    }

    void DrawPageBorders()
    {
        if (currentPage == null || context.PageSettings.PageBorders is not {HasAnyBorder: true} borders)
        {
            return;
        }

        var pageWidth = context.PageWidthPixels;
        var pageHeight = context.PageHeightPixels;
        var leftX = context.PointsToPixels((float) borders.LeftSpacePoints);
        var rightX = pageWidth - context.PointsToPixels((float) borders.RightSpacePoints);
        var topY = context.PointsToPixels((float) borders.TopSpacePoints);
        var bottomY = pageHeight - context.PointsToPixels((float) borders.BottomSpacePoints);

        var page = currentPage;

        if (borders.Top.IsVisible)
        {
            var pen = Pens.Solid(ParseColor(borders.Top.ColorHex), context.PointsToPixels((float) borders.Top.WidthPoints));
            page.Mutate(_ => _.DrawLine(pen, new PointF(leftX, topY), new PointF(rightX, topY)));
        }

        if (borders.Bottom.IsVisible)
        {
            var pen = Pens.Solid(ParseColor(borders.Bottom.ColorHex), context.PointsToPixels((float) borders.Bottom.WidthPoints));
            page.Mutate(_ => _.DrawLine(pen, new PointF(leftX, bottomY), new PointF(rightX, bottomY)));
        }

        if (borders.Left.IsVisible)
        {
            var pen = Pens.Solid(ParseColor(borders.Left.ColorHex), context.PointsToPixels((float) borders.Left.WidthPoints));
            page.Mutate(_ => _.DrawLine(pen, new PointF(leftX, topY), new PointF(leftX, bottomY)));
        }

        if (borders.Right.IsVisible)
        {
            var pen = Pens.Solid(ParseColor(borders.Right.ColorHex), context.PointsToPixels((float) borders.Right.WidthPoints));
            page.Mutate(_ => _.DrawLine(pen, new PointF(rightX, topY), new PointF(rightX, bottomY)));
        }
    }

    void RemoveBlankTrailingPage()
    {
        if (pageCount > 0 && !hasSignificantContentOnCurrentPage && !currentPageFromExplicitBreak)
        {
            pendingPage?.Dispose();
            pendingPage = null;
        }
        else
        {
            FlushPendingPage();
        }
    }

    void RenderBackgroundShape(FloatingShapeElement shape)
    {
        if (currentPage == null)
        {
            return;
        }

        var bounds = FloatingPosition.ResolveShapeBounds(
            context,
            shape.HorizontalAnchor,
            shape.VerticalAnchor,
            shape.HorizontalPositionPoints,
            shape.VerticalPositionPoints,
            shape.WidthPoints,
            shape.HeightPoints);
        var pixelX = bounds.PixelX;
        var pixelY = bounds.PixelY;
        var pixelWidth = bounds.PixelWidth;
        var pixelHeight = bounds.PixelHeight;

        if (shape.ImageData != null)
        {
            try
            {
                using var img = Image.Load<Rgba32>(shape.ImageData);
                img.Mutate(_ => _.Resize((int) pixelWidth, (int) pixelHeight));
                currentPage.Mutate(_ => _.DrawImage(img, new Point((int) pixelX, (int) pixelY), 1f));
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
            currentPage.Mutate(_ => _.Fill(brush, new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight)));
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
            currentPage.Mutate(_ => _.Fill(fillColor, new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight)));
        }
    }

    void RenderFloatingImage(FloatingImageElement image)
    {
        if (currentPage == null || image.ContentType == "image/svg+xml")
        {
            return;
        }

        var bounds = FloatingPosition.ResolveBounds(
            context,
            image.HorizontalAnchor,
            image.VerticalAnchor,
            image.HorizontalPositionPoints,
            image.VerticalPositionPoints,
            image.WidthPoints,
            image.HeightPoints);

        DrawBlockImage(image.ImageData, bounds.PixelX, bounds.PixelY, bounds.PixelWidth, bounds.PixelHeight, (float) image.RotationDegrees, image.Crop);
    }

    void RenderFloatingTextBox(FloatingTextBoxElement textBox)
    {
        if (currentPage == null)
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

            using var tempImage = new Image<Rgba32>(tempW, tempH);
            if (textBox.BackgroundColorHex != null)
            {
                var bgColor = ParseColor(textBox.BackgroundColorHex);
                tempImage.Mutate(_ => _.Fill(bgColor));
            }

            var savedY = context.CurrentY;
            context.CurrentY = 0;

            foreach (var element in textBox.Content)
            {
                if (element is ParagraphElement para)
                {
                    textRenderer.RenderParagraphInBounds(tempImage, para, 0, (float) textBox.WidthPoints);
                }
            }

            context.CurrentY = savedY;

            tempImage.Mutate(_ => _.Rotate((float) textBox.RotationDegrees));

            // Center the rotated image at the original text box center
            var centerX = pixelX + pixelWidth / 2;
            var centerY = pixelY + pixelHeight / 2;
            var drawX = (int) (centerX - tempImage.Width / 2f);
            var drawY = (int) (centerY - tempImage.Height / 2f);

            currentPage.Mutate(_ => _.DrawImage(tempImage, new Point(drawX, drawY), 1f));
        }
        else
        {
            if (textBox.BackgroundColorHex != null)
            {
                var bgFillColor = ParseColor(textBox.BackgroundColorHex);
                currentPage.Mutate(_ => _.Fill(bgFillColor, new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight)));
            }

            var savedY = context.CurrentY;
            context.CurrentY = y;

            foreach (var element in textBox.Content)
            {
                if (element is ParagraphElement para)
                {
                    textRenderer.RenderParagraphInBounds(currentPage, para, x, (float) textBox.WidthPoints);
                }
            }

            context.CurrentY = savedY;
        }
    }

    public void Dispose() =>
        currentPage?.Dispose();
}
