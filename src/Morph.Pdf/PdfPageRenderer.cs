/// <summary>
/// Drives the shared <see cref="PageRendererBase"/> layout engine onto PdfSharp pages. All table,
/// pagination, form-field, content-control and header/footer logic is inherited; this class supplies
/// the PdfSharp drawing primitives and the document-level render loop (mirroring the Skia backend).
/// </summary>
sealed class PdfPageRenderer : PageRendererBase
{
    readonly PdfRenderContext context;
    readonly PdfTextEngine textEngine;

    int pagesAdded;
    PdfPage? currentPage;
    bool hasSignificantContentOnCurrentPage;
    bool currentPageFromExplicitBreak;

    /// <summary>Receives notices about elements the backend couldn't render. Set from
    /// <see cref="ExportOptions.OnWarning"/>.</summary>
    public Action<ExportWarning>? OnWarning { get; init; }

    public PdfPageRenderer(PdfRenderContext context) : base(context)
    {
        this.context = context;
        textEngine = new(context)
        {
            RequestNewPage = () =>
            {
                FinishCurrentPage();
                StartNewPage();
            }
        };
    }

    protected override IParagraphMeasurer Measurer => textEngine;
    protected override bool HasOutput => context.Graphics != null;

    XGraphics Graphics => context.Graphics!;

    /// <summary>Renders the document into the context's <see cref="PdfDocument"/> and returns the page count.</summary>
    public int RenderDocument(ParsedDocument document)
    {
        header = document.Header;
        footer = document.Footer;
        firstPageHeader = document.FirstPageHeader;
        firstPageFooter = document.FirstPageFooter;
        evenPageHeader = document.EvenPageHeader;
        evenPageFooter = document.EvenPageFooter;
        differentFirstPage = document.PageSettings.DifferentFirstPage;

        context.SetHeaderFooterSpace(0, 0);
        context.InitializeLineNumbers();

        StartNewPage();

        var elements = document.Elements;
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];

            if (element is FloatingShapeElement {BehindText: true})
            {
                // Background shapes are not rendered by this backend (vector fills/gradients are
                // out of scope); skip so they don't disturb flow.
                continue;
            }

            if (element is FloatingImageElement {BehindText: true} backgroundImage)
            {
                AdvanceToBackgroundsTargetPage(elements, index);
                RenderFloatingImage(backgroundImage);
                continue;
            }

            DocumentElement? nextElement = null;
            for (var lookAhead = index + 1; lookAhead < elements.Count; lookAhead++)
            {
                if (elements[lookAhead] is FloatingShapeElement {BehindText: true} or FloatingImageElement {BehindText: true})
                {
                    continue;
                }

                nextElement = elements[lookAhead];
                break;
            }

            RenderElement(element, nextElement);
        }

        FinishCurrentPage();
        RemoveBlankTrailingPage();
        return pagesAdded;
    }

    void RenderElement(DocumentElement element, DocumentElement? nextElement)
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
                if (sectionBreak.NewSectionSettings != null)
                {
                    context.UpdatePageSettings(sectionBreak.NewSectionSettings);
                }

                if (context.CurrentY > context.ContentTop)
                {
                    FinishCurrentPage();
                    StartNewPage();
                    currentPageFromExplicitBreak = true;
                }

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
            case FloatingTextBoxElement textBox:
                RenderFloatingTextBox(textBox);
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
                RenderTextAsParagraph(wordArt.Text);
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
            case InkElement:
            case FloatingShapeElement:
            case FloatingWordArtElement:
                OnWarning?.Invoke(new(WarningKind.UnsupportedElement,
                    $"{element.GetType().Name} is not rendered by the PDF backend and was dropped."));
                break;
        }
    }

    void RenderTextAsParagraph(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        RenderParagraph(new()
        {
            Runs = [new() {Text = text, Properties = new()}],
            Properties = new()
        });
    }

    void RenderFloatingTextBox(FloatingTextBoxElement textBox)
    {
        if (!HasOutput)
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

        if (textBox.BackgroundColorHex != null)
        {
            Graphics.DrawRectangle(new XSolidBrush(PdfRenderContext.ParseColor(textBox.BackgroundColorHex)), bounds.X, bounds.Y, bounds.PixelWidth, bounds.PixelHeight);
        }

        var savedY = context.CurrentY;
        context.CurrentY = bounds.Y;
        foreach (var element in textBox.Content)
        {
            if (element is ParagraphElement paragraph)
            {
                RenderParagraphInBounds(paragraph, bounds.X, (float) textBox.WidthPoints);
            }
        }

        context.CurrentY = savedY;
    }

    // ---- Page lifecycle ----

    protected override void StartNewPage()
    {
        currentPage = context.Document.AddPage();
        currentPage.Width = XUnit.FromPoint(context.PageSettings.WidthPoints);
        currentPage.Height = XUnit.FromPoint(context.PageSettings.HeightPoints);

        context.Graphics = XGraphics.FromPdfPage(currentPage);

        var background = context.PageSettings.BackgroundColorHex;
        if (!string.IsNullOrEmpty(background))
        {
            Graphics.DrawRectangle(new XSolidBrush(PdfRenderContext.ParseColor(background)), 0, 0, currentPage.Width.Point, currentPage.Height.Point);
        }

        DrawPageBorders();

        if (pagesAdded > 0)
        {
            context.StartNewPage();
            context.ResetLineNumbersForPage();
        }

        pagesAdded++;

        RenderHeader();

        hasSignificantContentOnCurrentPage = false;
        currentPageFromExplicitBreak = false;
    }

    protected override void FinishCurrentPage()
    {
        if (currentPage == null)
        {
            return;
        }

        RenderFooter();

        context.Graphics?.Dispose();
        context.Graphics = null;
        currentPage = null;
    }

    void RemoveBlankTrailingPage()
    {
        if (pagesAdded > 1 &&
            !hasSignificantContentOnCurrentPage &&
            !currentPageFromExplicitBreak)
        {
            context.Document.Pages.RemoveAt(context.Document.PageCount - 1);
            pagesAdded--;
        }
    }

    void DrawPageBorders()
    {
        if (context.PageSettings.PageBorders is not {HasAnyBorder: true} borders)
        {
            return;
        }

        var width = currentPage!.Width.Point;
        var height = currentPage.Height.Point;
        var left = borders.LeftSpacePoints;
        var right = width - borders.RightSpacePoints;
        var top = borders.TopSpacePoints;
        var bottom = height - borders.BottomSpacePoints;

        if (borders.Top.IsVisible)
        {
            Graphics.DrawLine(EdgePen(borders.Top), left, top, right, top);
        }

        if (borders.Bottom.IsVisible)
        {
            Graphics.DrawLine(EdgePen(borders.Bottom), left, bottom, right, bottom);
        }

        if (borders.Left.IsVisible)
        {
            Graphics.DrawLine(EdgePen(borders.Left), left, top, left, bottom);
        }

        if (borders.Right.IsVisible)
        {
            Graphics.DrawLine(EdgePen(borders.Right), right, top, right, bottom);
        }
    }

    static XPen EdgePen(BorderEdge edge) =>
        new(PdfRenderContext.ParseColor(edge.ColorHex ?? "000000"), Math.Max(0.5, edge.WidthPoints));

    // ---- Drawing primitives required by PageRendererBase ----

    protected override void RenderParagraph(ParagraphElement paragraph, DocumentElement? nextElement = null)
    {
        var hasContent = false;
        var isEmpty = paragraph.Runs.Count == 0;
        foreach (var run in paragraph.Runs)
        {
            if (run.InlineImageData != null || !string.IsNullOrWhiteSpace(run.Text))
            {
                hasContent = true;
                break;
            }
        }

        if (paragraph.Properties.PageBreakBefore && !isEmpty && context.CurrentY > context.ContentTop)
        {
            FinishCurrentPage();
            StartNewPage();
            currentPageFromExplicitBreak = true;
        }

        textEngine.Render(paragraph);

        if (hasContent)
        {
            hasSignificantContentOnCurrentPage = true;
        }
    }

    protected override void RenderParagraphInBounds(ParagraphElement paragraph, float x, float maxWidth)
    {
        if (HasOutput)
        {
            textEngine.RenderInBounds(paragraph, x, maxWidth);
        }
    }

    protected override void RenderHeaderFooterParagraph(ParagraphElement paragraph)
    {
        if (HasOutput)
        {
            textEngine.RenderInBounds(paragraph, context.ContentLeft, context.ContentWidth);
        }
    }

    protected override void RenderImageInCell(ImageElement image, float x, float maxWidth)
    {
        if (!HasOutput)
        {
            return;
        }

        var imageWidth = (float) image.WidthPoints;
        var imageHeight = (float) image.HeightPoints;
        if (imageWidth > maxWidth)
        {
            imageHeight *= maxWidth / imageWidth;
            imageWidth = maxWidth;
        }

        DrawRaster(image.ImageData, image.ContentType, image.RasterFallbackData, image.RasterFallbackContentType, x, context.CurrentY, imageWidth, imageHeight);
        context.CurrentY += imageHeight;
    }

    protected override void RenderVerticalCellContent(TableCell cell, float cellX, float cellY, float cellWidth, float cellHeight, CellSpacing padding)
    {
        if (!HasOutput)
        {
            return;
        }

        var contentX = cellX + (float) padding.Left;
        var contentY = cellY + (float) padding.Top;
        var contentWidth = cellWidth - (float) padding.Horizontal;
        var availableHeight = cellHeight - (float) padding.Vertical;

        var bottomToTop = cell.Properties.TextDirection == CellTextDirection.BottomToTop;
        var state = Graphics.Save();
        if (bottomToTop)
        {
            Graphics.TranslateTransform(contentX, contentY + availableHeight);
            Graphics.RotateTransform(-90);
        }
        else
        {
            Graphics.TranslateTransform(contentX + contentWidth, contentY);
            Graphics.RotateTransform(90);
        }

        var savedY = context.CurrentY;
        context.CurrentY = 0;
        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement paragraph)
            {
                RenderParagraphInBounds(paragraph, 0, availableHeight);
            }
        }

        context.CurrentY = savedY;
        Graphics.Restore(state);
    }

    protected override void DrawCellBackground(float pixelX, float pixelY, float pixelWidth, float pixelHeight, string hexColor)
    {
        if (HasOutput)
        {
            Graphics.DrawRectangle(new XSolidBrush(PdfRenderContext.ParseColor(hexColor)), pixelX, pixelY, pixelWidth, pixelHeight);
        }
    }

    protected override void DrawCellBorders(float pixelX, float pixelY, float pixelWidth, float pixelHeight, CellBorders borders)
    {
        if (!HasOutput)
        {
            return;
        }

        if (borders.Top.IsVisible)
        {
            Graphics.DrawLine(EdgePen(borders.Top), pixelX, pixelY, pixelX + pixelWidth, pixelY);
        }

        if (borders.Right.IsVisible)
        {
            Graphics.DrawLine(EdgePen(borders.Right), pixelX + pixelWidth, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);
        }

        if (borders.Bottom.IsVisible)
        {
            Graphics.DrawLine(EdgePen(borders.Bottom), pixelX, pixelY + pixelHeight, pixelX + pixelWidth, pixelY + pixelHeight);
        }

        if (borders.Left.IsVisible)
        {
            Graphics.DrawLine(EdgePen(borders.Left), pixelX, pixelY, pixelX, pixelY + pixelHeight);
        }
    }

    protected override void DrawCellDiagonals(float pixelX, float pixelY, float pixelWidth, float pixelHeight, CellDiagonals diagonals)
    {
        if (!HasOutput)
        {
            return;
        }

        if (diagonals.Down.IsVisible)
        {
            Graphics.DrawLine(EdgePen(diagonals.Down), pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);
        }

        if (diagonals.Up.IsVisible)
        {
            Graphics.DrawLine(EdgePen(diagonals.Up), pixelX + pixelWidth, pixelY, pixelX, pixelY + pixelHeight);
        }
    }

    protected override void DrawFormFieldRect(float pixelX, float pixelY, float pixelWidth, float pixelHeight, string fillHex, string borderHex, float pixelBorderWidth)
    {
        if (!HasOutput)
        {
            return;
        }

        Graphics.DrawRectangle(new XSolidBrush(PdfRenderContext.ParseColor(fillHex)), pixelX, pixelY, pixelWidth, pixelHeight);
        Graphics.DrawRectangle(new XPen(PdfRenderContext.ParseColor(borderHex), Math.Max(0.5, pixelBorderWidth)), pixelX, pixelY, pixelWidth, pixelHeight);
    }

    protected override void DrawFormFieldText(string text, float pixelX, float pixelY, float pixelWidth, float pixelHeight, string textHex)
    {
        if (!HasOutput)
        {
            return;
        }

        var font = context.GetFont(DefaultFontSettings.DefaultFont, false, false, 10);
        var format = new XStringFormat {Alignment = XStringAlignment.Near, LineAlignment = XLineAlignment.Center};
        Graphics.DrawString(text, font, new XSolidBrush(PdfRenderContext.ParseColor(textHex)), new XRect(pixelX + 3, pixelY, pixelWidth - 6, pixelHeight), format);
    }

    protected override void DrawCheckMark(float pixelX, float pixelY, float pixelSize, string hexColor, float pixelStrokeWidth, bool xShape)
    {
        if (!HasOutput)
        {
            return;
        }

        var pen = new XPen(PdfRenderContext.ParseColor(hexColor), Math.Max(0.6, pixelStrokeWidth));
        if (xShape)
        {
            Graphics.DrawLine(pen, pixelX + pixelSize * 0.2, pixelY + pixelSize * 0.2, pixelX + pixelSize * 0.8, pixelY + pixelSize * 0.8);
            Graphics.DrawLine(pen, pixelX + pixelSize * 0.8, pixelY + pixelSize * 0.2, pixelX + pixelSize * 0.2, pixelY + pixelSize * 0.8);
        }
        else
        {
            Graphics.DrawLine(pen, pixelX + pixelSize * 0.2, pixelY + pixelSize * 0.55, pixelX + pixelSize * 0.42, pixelY + pixelSize * 0.78);
            Graphics.DrawLine(pen, pixelX + pixelSize * 0.42, pixelY + pixelSize * 0.78, pixelX + pixelSize * 0.82, pixelY + pixelSize * 0.25);
        }
    }

    protected override void DrawDropDownArrow(float pixelX, float pixelY, float pixelHeight, string hexColor)
    {
        if (!HasOutput)
        {
            return;
        }

        var size = pixelHeight * 0.3;
        var centerX = pixelX - size;
        var centerY = pixelY + pixelHeight / 2;
        XPoint[] triangle =
        [
            new(centerX - size / 2, centerY - size / 4),
            new(centerX + size / 2, centerY - size / 4),
            new(centerX, centerY + size / 2)
        ];
        Graphics.DrawPolygon(new XSolidBrush(PdfRenderContext.ParseColor(hexColor)), triangle, XFillMode.Winding);
    }

    protected override void DrawHorizontalRuleLine(float pixelX1, float pixelY, float pixelX2, string hexColor, float pixelStrokeWidth)
    {
        if (HasOutput)
        {
            Graphics.DrawLine(new(PdfRenderContext.ParseColor(hexColor), Math.Max(0.4, pixelStrokeWidth)), pixelX1, pixelY, pixelX2, pixelY);
        }
    }

    protected override void RenderBackgroundShape(FloatingShapeElement shape)
    {
        // Vector shapes/gradients are out of scope for the PDF backend.
    }

    protected override void RenderFloatingImage(FloatingImageElement image)
    {
        if (!HasOutput)
        {
            return;
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

        DrawRaster(image.ImageData, image.ContentType, image.RasterFallbackData, image.RasterFallbackContentType, bounds.X, bounds.Y, bounds.PixelWidth, bounds.PixelHeight);
    }

    protected override void DrawBlockImage(byte[] imageData, string? contentType, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, ImageCrop? crop, BlipColorEffect colorEffect)
    {
        if (!HasOutput)
        {
            return;
        }

        if (Math.Abs(rotation) > 0.01)
        {
            var state = Graphics.Save();
            Graphics.RotateAtTransform(rotation, new(pixelX + pixelWidth / 2, pixelY + pixelHeight / 2));
            DrawRaster(imageData, contentType, null, null, pixelX, pixelY, pixelWidth, pixelHeight);
            Graphics.Restore(state);
        }
        else
        {
            DrawRaster(imageData, contentType, null, null, pixelX, pixelY, pixelWidth, pixelHeight);
        }
    }

    protected override bool CanRenderContentType(string? contentType) => contentType != "image/svg+xml";

    void DrawRaster(byte[] data, string? contentType, byte[]? fallbackData, string? fallbackContentType, double x, double y, double width, double height)
    {
        // PDFsharp can't decode SVG (see CanRenderContentType); fall back to the raster blip behind
        // it, but only if that fallback is itself something we can decode.
        if (!CanRenderContentType(contentType))
        {
            if (fallbackData == null || !CanRenderContentType(fallbackContentType))
            {
                return;
            }

            data = fallbackData;
        }

        using var stream = new MemoryStream(data);
        var image = XImage.FromStream(stream);
        Graphics.DrawImage(image, x, y, width, height);
    }
}
