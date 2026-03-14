/// <summary>
/// Renders document pages to PNG images using SixLabors.ImageSharp.
/// </summary>
sealed class PageRenderer(RenderContext context) :
    IDisposable
{
    readonly TextRenderer textRenderer = new(context);
    readonly List<Image<Rgba32>> pages = [];

    Image<Rgba32>? currentPage;
    HeaderFooterContent? header;
    HeaderFooterContent? footer;
    float headerHeight;
    float footerHeight;

    bool hasSignificantContentOnCurrentPage;
    bool currentPageFromExplicitBreak;

    public IReadOnlyList<Image<Rgba32>> RenderDocument(ParsedDocument document)
    {
        header = document.Header;
        footer = document.Footer;

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

        FinishCurrentPage();
        RemoveBlankTrailingPage();

        return pages;
    }

    // ReSharper disable once UnusedParameter.Local
    static float MeasureHeaderFooterHeight(HeaderFooterContent? content) =>
        0;

    void RenderHeader()
    {
        if (header == null || currentPage == null)
        {
            return;
        }

        var savedY = context.CurrentY;
        context.CurrentY = (float) context.PageSettings.HeaderDistance;

        foreach (var element in header.Elements)
        {
            if (element is ParagraphElement para)
            {
                textRenderer.RenderParagraph(currentPage, para);
            }
        }

        context.CurrentY = savedY;
    }

    void RenderFooter()
    {
        if (footer == null || currentPage == null)
        {
            return;
        }

        var savedY = context.CurrentY;
        context.CurrentY = (float) (context.PageSettings.HeightPoints - context.PageSettings.FooterDistance - footerHeight);

        foreach (var element in footer.Elements)
        {
            if (element is ParagraphElement para)
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

    void RenderSectionBreak(SectionBreakElement sectionBreak)
    {
        switch (sectionBreak.BreakType)
        {
            case SectionBreakType.NextPage:
                FinishCurrentPage();
                ApplySectionSettings(sectionBreak.NewSectionSettings);
                StartNewPage();
                currentPageFromExplicitBreak = true;
                break;

            case SectionBreakType.Continuous:
                ApplySectionSettings(sectionBreak.NewSectionSettings);
                context.ResetColumn();
                break;

            case SectionBreakType.EvenPage:
                FinishCurrentPage();
                ApplySectionSettings(sectionBreak.NewSectionSettings);
                StartNewPage();
                currentPageFromExplicitBreak = true;
                if (context.CurrentPageNumber % 2 != 0)
                {
                    FinishCurrentPage();
                    StartNewPage();
                    currentPageFromExplicitBreak = true;
                }

                break;

            case SectionBreakType.OddPage:
                FinishCurrentPage();
                ApplySectionSettings(sectionBreak.NewSectionSettings);
                StartNewPage();
                currentPageFromExplicitBreak = true;
                if (context.CurrentPageNumber % 2 == 0)
                {
                    FinishCurrentPage();
                    StartNewPage();
                    currentPageFromExplicitBreak = true;
                }

                break;

            case SectionBreakType.NextColumn:
                ApplySectionSettings(sectionBreak.NewSectionSettings);
                if (!context.MoveToNextColumn())
                {
                    FinishCurrentPage();
                    StartNewPage();
                    currentPageFromExplicitBreak = true;
                }

                break;
        }
    }

    void ApplySectionSettings(PageSettings? settings)
    {
        if (settings != null)
        {
            context.UpdatePageSettings(settings);
            context.ResetLineNumbersForSection();
        }
    }

    void EnsureSpaceFor(float height)
    {
        if (height > context.ContentHeight)
        {
            return;
        }

        if (!context.HasSpaceFor(height) && context.CurrentY > context.ContentTop)
        {
            if (!context.MoveToNextColumn())
            {
                FinishCurrentPage();
                StartNewPage();
            }
        }
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

        if (paragraph.Properties.PageBreakBefore && !isCompletelyEmpty &&
            context.CurrentY > context.ContentTop)
        {
            FinishCurrentPage();
            StartNewPage();
            currentPageFromExplicitBreak = true;
        }

        var height = textRenderer.MeasureParagraphHeight(paragraph);

        if (paragraph.Properties.KeepNext && nextElement != null && !isCompletelyEmpty)
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

        if (paragraph.Properties.KeepLines && !isCompletelyEmpty)
        {
            if (!context.HasSpaceFor(height) &&
                height <= context.ContentHeight &&
                context.CurrentY > context.ContentTop)
            {
                FinishCurrentPage();
                StartNewPage();
            }
        }

        if (!isCompletelyEmpty)
        {
            EnsureSpaceFor(height);
        }

        if (currentPage != null)
        {
            textRenderer.RenderParagraph(currentPage, paragraph);
        }

        if (hasSignificantContent)
        {
            hasSignificantContentOnCurrentPage = true;
        }
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

        try
        {
            using var img = Image.Load<Rgba32>(image.ImageData);
            img.Mutate(ctx => ctx.Resize((int) width, (int) pixelHeight));
            currentPage.Mutate(ctx => ctx.DrawImage(img, new Point((int) x, (int) y), 1f));
        }
        catch
        {
            // Ignore image decode errors
        }

        context.CurrentY += height;
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

        var textSize = TextMeasurer.MeasureSize(wordArt.Text, new TextOptions(font) { Dpi = context.Dpi });

        var scaleX = textSize.Width > 0 ? width / textSize.Width : 1;
        var scaleY = textSize.Height > 0 ? pixelHeight / textSize.Height : 1;
        var scale = Math.Min(scaleX, scaleY);

        var scaledFont = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints * scale,
            wordArt.Bold,
            wordArt.Italic);

        var scaledSize = TextMeasurer.MeasureSize(wordArt.Text, new TextOptions(scaledFont) { Dpi = context.Dpi });

        var textX = x + (width - scaledSize.Width) / 2;
        var textY = y + (pixelHeight - scaledSize.Height) / 2;

        var fillColor = wordArt.FillColorHex != null
            ? RenderContext.ParseColor(wordArt.FillColorHex)
            : Color.Black;

        if (wordArt.HasShadow)
        {
            var shadowColor = Color.FromRgba(0, 0, 0, 80);
            currentPage.Mutate(ctx => ctx.DrawText(wordArt.Text, scaledFont, shadowColor, new PointF(textX + 3, textY + 3)));
        }

        if (wordArt is {OutlineColorHex: not null, OutlineWidthPoints: > 0})
        {
            var outlineColor = RenderContext.ParseColor(wordArt.OutlineColorHex);
            var outlinePen = Pens.Solid(outlineColor, context.PointsToPixels((float) wordArt.OutlineWidthPoints));
            currentPage.Mutate(ctx => ctx.DrawText(wordArt.Text, scaledFont, outlinePen, new PointF(textX, textY)));
        }

        currentPage.Mutate(ctx => ctx.DrawText(wordArt.Text, scaledFont, fillColor, new PointF(textX, textY)));

        context.CurrentY += height;
    }

    void RenderFloatingWordArt(FloatingWordArtElement wordArt)
    {
        if (currentPage == null)
        {
            return;
        }

        var x = CalculateFloatingWordArtX(wordArt);
        var y = CalculateFloatingWordArtY(wordArt);

        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var width = context.PointsToPixels((float) wordArt.WidthPoints);
        var pixelHeight = context.PointsToPixels((float) wordArt.HeightPoints);

        var font = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints,
            wordArt.Bold,
            wordArt.Italic);

        var textSize = TextMeasurer.MeasureSize(wordArt.Text, new TextOptions(font) { Dpi = context.Dpi });

        var scaleX = textSize.Width > 0 ? width / textSize.Width : 1;
        var scaleY = textSize.Height > 0 ? pixelHeight / textSize.Height : 1;
        var scale = Math.Min(scaleX, scaleY);

        var scaledFont = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints * scale,
            wordArt.Bold,
            wordArt.Italic);

        var scaledSize = TextMeasurer.MeasureSize(wordArt.Text, new TextOptions(scaledFont) { Dpi = context.Dpi });

        var textX = pixelX + (width - scaledSize.Width) / 2;
        var textY = pixelY + (pixelHeight - scaledSize.Height) / 2;

        var fillColor = wordArt.FillColorHex != null
            ? RenderContext.ParseColor(wordArt.FillColorHex)
            : Color.Black;

        if (wordArt.HasShadow)
        {
            var shadowColor = Color.FromRgba(0, 0, 0, 80);
            currentPage.Mutate(ctx => ctx.DrawText(wordArt.Text, scaledFont, shadowColor, new PointF(textX + 3, textY + 3)));
        }

        if (wordArt is {OutlineColorHex: not null, OutlineWidthPoints: > 0})
        {
            var outlineColor = RenderContext.ParseColor(wordArt.OutlineColorHex);
            var outlinePen = Pens.Solid(outlineColor, context.PointsToPixels((float) wordArt.OutlineWidthPoints));
            currentPage.Mutate(ctx => ctx.DrawText(wordArt.Text, scaledFont, outlinePen, new PointF(textX, textY)));
        }

        currentPage.Mutate(ctx => ctx.DrawText(wordArt.Text, scaledFont, fillColor, new PointF(textX, textY)));
    }

    float CalculateFloatingWordArtX(FloatingWordArtElement wordArt)
    {
        var baseX = wordArt.HorizontalAnchor switch
        {
            HorizontalAnchor.Page => 0,
            HorizontalAnchor.Margin => (float) context.PageSettings.MarginLeft,
            HorizontalAnchor.Column => context.ContentLeft,
            HorizontalAnchor.Character => context.ContentLeft,
            _ => 0
        };

        return baseX + (float) wordArt.HorizontalPositionPoints;
    }

    float CalculateFloatingWordArtY(FloatingWordArtElement wordArt)
    {
        var baseY = wordArt.VerticalAnchor switch
        {
            VerticalAnchor.Page => 0,
            VerticalAnchor.Margin => (float) context.PageSettings.MarginTop,
            VerticalAnchor.Paragraph => context.CurrentY,
            VerticalAnchor.Line => context.CurrentY,
            _ => 0
        };

        return baseY + (float) wordArt.VerticalPositionPoints;
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

            var color = RenderContext.ParseColor(stroke.ColorHex);

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
                points[i] = new PointF(
                    baseX + context.PointsToPixels((float) point.X),
                    baseY + context.PointsToPixels((float) point.Y));
            }

            currentPage.Mutate(ctx => ctx.DrawLine(pen, points));
        }

        context.CurrentY += height;
    }

    float MeasureTableHeight(TableElement table)
    {
        if (table.Rows.Count == 0)
        {
            return 0;
        }

        int colCount;
        if (table.Properties.GridColumnWidths?.Count > 0)
        {
            colCount = table.Properties.GridColumnWidths.Count;
        }
        else
        {
            colCount = table.Rows.Max(r => r.Cells.Sum(c => c.Properties.GridSpan));
        }

        var colWidths = CalculateColumnWidths(table, colCount);
        var rowHeights = CalculateRowHeights(table, colWidths);

        return rowHeights.Sum();
    }

    void RenderTable(TableElement table)
    {
        if (currentPage == null || table.Rows.Count == 0)
        {
            return;
        }

        int colCount;
        if (table.Properties.GridColumnWidths?.Count > 0)
        {
            colCount = table.Properties.GridColumnWidths.Count;
        }
        else
        {
            colCount = table.Rows.Max(r => r.Cells.Sum(c => c.Properties.GridSpan));
        }

        var colWidths = CalculateColumnWidths(table, colCount);
        var rowHeights = CalculateRowHeights(table, colWidths);
        var totalHeight = rowHeights.Sum();

        var tableTolerance = context.ContentHeight * 0.10f;
        var needsRowByRowRendering = totalHeight > context.ContentHeight + tableTolerance;

        if (!needsRowByRowRendering)
        {
            var tolerancePercent = context.Compatibility.CompatibilityMode >= 15 ? 0.02f : 0.01f;
            var tolerance = context.ContentHeight * tolerancePercent;
            var requiredHeight = totalHeight - tolerance;
            EnsureSpaceFor(requiredHeight);
            RenderTableRows(table, colCount, colWidths, rowHeights);
        }
        else
        {
            RenderTableRowByRow(table, colCount, colWidths, rowHeights);
        }
    }

    void RenderTableRows(TableElement table, int colCount, float[] colWidths, float[] rowHeights)
    {
        var tableX = context.ContentLeft;
        var startY = context.CurrentY;

        var hasVerticalMerge = table.Rows.Any(r => r.Cells.Any(c =>
            c.Properties.VerticalMerge is VerticalMergeType.Restart or VerticalMergeType.Continue));

        if (hasVerticalMerge)
        {
            var columnYPositions = new float[colCount];
            for (var i = 0; i < colCount; i++)
            {
                columnYPositions[i] = startY;
            }

            RenderTableWithColumnTracking(table, colCount, colWidths, rowHeights, tableX, columnYPositions);
            context.CurrentY = columnYPositions.Max();
        }
        else
        {
            var currentY = startY;
            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                RenderTableRow(table, rowIndex, colCount, colWidths, rowHeights, tableX, currentY);
                currentY += rowHeights[rowIndex];
            }

            context.CurrentY = currentY;
        }
    }

    void RenderTableWithColumnTracking(TableElement table, int colCount, float[] colWidths, float[] rowHeights, float tableX, float[] columnYPositions)
    {
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var currentX = tableX;
            var gridColIndex = 0;

            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = cell.Properties.GridSpan;

                float cellWidth = 0;
                for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                {
                    cellWidth += colWidths[gridColIndex + i];
                }

                if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
                {
                    currentX += cellWidth;
                    gridColIndex += span;
                    continue;
                }

                var cellY = columnYPositions[gridColIndex];

                float cellHeight;
                if (cell.Properties.VerticalMerge == VerticalMergeType.Restart)
                {
                    cellHeight = CalculateVerticalMergeHeight(table, rowIndex, gridColIndex, rowHeights);
                }
                else
                {
                    var padding = GetEffectivePadding(cell.Properties, table.Properties);
                    var contentWidth = cellWidth - (float) padding.Horizontal;
                    var contentHeight = MeasureCellHeight(cell, contentWidth, table.Properties);
                    cellHeight = contentHeight + (float) padding.Vertical;
                }

                RenderTableCell(cell, currentX, cellY, cellWidth, cellHeight, table.Properties, rowIndex, gridColIndex, table.Rows.Count, colCount);

                for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                {
                    columnYPositions[gridColIndex + i] = cellY + cellHeight;
                }

                currentX += cellWidth;
                gridColIndex += span;
            }
        }
    }

    void RenderTableRowByRow(TableElement table, int colCount, float[] colWidths, float[] rowHeights)
    {
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var rowHeight = rowHeights[rowIndex];
            EnsureSpaceFor(rowHeight);

            var tableX = context.ContentLeft;
            var currentY = context.CurrentY;

            RenderTableRow(table, rowIndex, colCount, colWidths, rowHeights, tableX, currentY);
            context.CurrentY += rowHeight;
        }
    }

    void RenderTableRow(TableElement table, int rowIndex, int colCount, float[] colWidths, float[] rowHeights, float tableX, float currentY)
    {
        var row = table.Rows[rowIndex];
        var rowHeight = rowHeights[rowIndex];
        var currentX = tableX;
        var gridColIndex = 0;

        for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
        {
            var cell = row.Cells[cellIndex];
            var span = cell.Properties.GridSpan;

            float cellWidth = 0;
            for (var i = 0; i < span && gridColIndex + i < colCount; i++)
            {
                cellWidth += colWidths[gridColIndex + i];
            }

            if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
            {
                currentX += cellWidth;
                gridColIndex += span;
                continue;
            }

            var cellHeight = rowHeight;
            if (cell.Properties.VerticalMerge == VerticalMergeType.Restart)
            {
                cellHeight = CalculateVerticalMergeHeight(table, rowIndex, gridColIndex, rowHeights);
            }

            RenderTableCell(cell, currentX, currentY, cellWidth, cellHeight, table.Properties, rowIndex, gridColIndex, table.Rows.Count, colCount);

            currentX += cellWidth;
            gridColIndex += span;
        }
    }

    static float CalculateVerticalMergeHeight(TableElement table, int startRowIndex, int gridColIndex, float[] rowHeights)
    {
        var totalHeight = rowHeights[startRowIndex];

        for (var rowIndex = startRowIndex + 1; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var currentGridCol = 0;
            TableCell? cellAtColumn = null;
            foreach (var cell in row.Cells)
            {
                if (currentGridCol == gridColIndex)
                {
                    cellAtColumn = cell;
                    break;
                }

                currentGridCol += cell.Properties.GridSpan;
                if (currentGridCol > gridColIndex)
                {
                    break;
                }
            }

            if (cellAtColumn?.Properties.VerticalMerge == VerticalMergeType.Continue)
            {
                totalHeight += rowHeights[rowIndex];
            }
            else
            {
                break;
            }
        }

        return totalHeight;
    }

    float[] CalculateColumnWidths(TableElement table, int colCount)
    {
        var widths = new float[colCount];
        var availableWidth = context.ContentWidth;
        var gridWidths = table.Properties.GridColumnWidths;

        var hasExplicitWidths = false;

        foreach (var row in table.Rows)
        {
            var gridColIndex = 0;
            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var props = cell.Properties;
                var span = props.GridSpan;

                if (span == 1 && props.WidthPoints.HasValue)
                {
                    widths[gridColIndex] = Math.Max(widths[gridColIndex], (float) props.WidthPoints.Value);
                    hasExplicitWidths = true;
                }

                gridColIndex += span;
            }
        }

        if (hasExplicitWidths)
        {
            var totalExplicitWidth = widths.Sum();
            var columnsWithoutWidth = widths.Count(w => w == 0);

            if (columnsWithoutWidth > 0 && totalExplicitWidth < availableWidth)
            {
                var remainingWidth = availableWidth - totalExplicitWidth;
                var perColumnWidth = remainingWidth / columnsWithoutWidth;
                for (var i = 0; i < colCount; i++)
                {
                    if (widths[i] == 0)
                    {
                        widths[i] = perColumnWidth;
                    }
                }
            }

            var totalWidth = widths.Sum();
            if (totalWidth > availableWidth)
            {
                var scale = availableWidth / totalWidth;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] *= scale;
                }
            }
        }
        else if (gridWidths is {Count: > 0})
        {
            for (var i = 0; i < colCount && i < gridWidths.Count; i++)
            {
                widths[i] = (float) gridWidths[i];
            }

            if (gridWidths.Count < colCount)
            {
                var avgWidth = (float) gridWidths.Average();
                for (var i = gridWidths.Count; i < colCount; i++)
                {
                    widths[i] = avgWidth;
                }
            }

            var totalWidth = widths.Sum();
            if (totalWidth > availableWidth && totalWidth > 0)
            {
                var scale = availableWidth / totalWidth;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] *= scale;
                }
            }
        }
        else
        {
            var cellWidth = availableWidth / colCount;
            for (var i = 0; i < colCount; i++)
            {
                widths[i] = cellWidth;
            }
        }

        return widths;
    }

    float[] CalculateRowHeights(TableElement table, float[] colWidths)
    {
        var heights = new float[table.Rows.Count];
        var colCount = colWidths.Length;

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            float maxHeight = 20;

            var gridColIndex = 0;
            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = cell.Properties.GridSpan;

                if (cell.Properties.VerticalMerge is VerticalMergeType.Continue or VerticalMergeType.Restart)
                {
                    gridColIndex += span;
                    continue;
                }

                float cellWidth = 0;
                for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                {
                    cellWidth += colWidths[gridColIndex + i];
                }

                var cellHeight = MeasureCellHeight(cell, cellWidth, table.Properties);
                maxHeight = Math.Max(maxHeight, cellHeight);

                gridColIndex += span;
            }

            heights[rowIndex] = maxHeight;
        }

        var hasVMerge = table.Rows.Any(r => r.Cells.Any(c =>
            c.Properties.VerticalMerge is VerticalMergeType.Restart or VerticalMergeType.Continue));
        var allRowsHaveExplicitHeight = table.Rows.All(r => r.HeightPoints.HasValue);
        var useStrictHeights = hasVMerge && allRowsHaveExplicitHeight;

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            if (row.HeightPoints.HasValue)
            {
                var explicitHeight = (float) row.HeightPoints.Value;
                if (row.IsExactHeight || useStrictHeights)
                {
                    heights[rowIndex] = explicitHeight;
                }
                else
                {
                    heights[rowIndex] = Math.Max(heights[rowIndex], explicitHeight);
                }
            }
        }

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var gridColIndex = 0;

            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = cell.Properties.GridSpan;

                if (cell.Properties.VerticalMerge == VerticalMergeType.Restart)
                {
                    var rowSpan = CalculateVerticalMergeRowSpan(table, rowIndex, gridColIndex);

                    float cellWidth = 0;
                    for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                    {
                        cellWidth += colWidths[gridColIndex + i];
                    }

                    var contentHeight = MeasureCellHeight(cell, cellWidth, table.Properties);

                    float currentTotalHeight = 0;
                    for (var r = rowIndex; r < rowIndex + rowSpan && r < table.Rows.Count; r++)
                    {
                        currentTotalHeight += heights[r];
                    }

                    if (contentHeight > currentTotalHeight)
                    {
                        var extraHeight = contentHeight - currentTotalHeight;
                        var extraPerRow = extraHeight / rowSpan;

                        for (var r = rowIndex; r < rowIndex + rowSpan && r < table.Rows.Count; r++)
                        {
                            heights[r] += extraPerRow;
                        }
                    }
                }

                gridColIndex += span;
            }
        }

        return heights;
    }

    static int CalculateVerticalMergeRowSpan(TableElement table, int startRowIndex, int gridColIndex)
    {
        var rowSpan = 1;

        for (var rowIndex = startRowIndex + 1; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var currentGridCol = 0;
            TableCell? cellAtColumn = null;
            foreach (var cell in row.Cells)
            {
                if (currentGridCol == gridColIndex)
                {
                    cellAtColumn = cell;
                    break;
                }

                currentGridCol += cell.Properties.GridSpan;
                if (currentGridCol > gridColIndex)
                {
                    break;
                }
            }

            if (cellAtColumn?.Properties.VerticalMerge == VerticalMergeType.Continue)
            {
                rowSpan++;
            }
            else
            {
                break;
            }
        }

        return rowSpan;
    }

    float MeasureCellHeight(TableCell cell, float cellWidth, TableProperties tableProps)
    {
        var padding = GetEffectivePadding(cell.Properties, tableProps);
        var margin = GetEffectiveMargin(cell.Properties, tableProps);
        var contentWidth = cellWidth - (float) (padding.Horizontal + margin.Horizontal);
        var height = (float) (padding.Vertical + margin.Vertical);

        var paragraphs = new List<(ParagraphElement para, float bulletIndent)>();
        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                float bulletIndent = para.Properties.Numbering != null ? 12 : 0;
                paragraphs.Add((para, bulletIndent));
            }
            else if (element is ContentControlElement contentControl)
            {
                ParagraphElement? measurePara = null;
                if (contentControl.Runs is {Count: > 0})
                {
                    measurePara = new()
                    {
                        Runs = contentControl.Runs,
                        Properties = new()
                    };
                }
                else if (!string.IsNullOrEmpty(contentControl.Content))
                {
                    measurePara = new()
                    {
                        Runs =
                        [
                            new()
                            {
                                Text = contentControl.Content,
                                Properties = new()
                            }
                        ],
                        Properties = new()
                    };
                }

                if (measurePara != null)
                {
                    paragraphs.Add((measurePara, 0));
                }
            }
            else if (element is TableElement {Properties.IsFloating: false})
            {
                height += 50;
            }
        }

        for (var i = 0; i < paragraphs.Count; i++)
        {
            var (para, bulletIndent) = paragraphs[i];
            var lines = textRenderer.LayoutParagraphForMeasurement(para, contentWidth - bulletIndent);
            var props = para.Properties;

            if (i == 0)
            {
                var extra = (float) props.SpacingBeforePoints - (float) padding.Top;
                if (extra > 0)
                {
                    height += extra;
                }
            }
            else
            {
                height += (float) props.SpacingBeforePoints;
            }

            foreach (var lineHeight in lines)
            {
                height += lineHeight;
            }

            if (i == paragraphs.Count - 1)
            {
                var extra = (float) props.SpacingAfterPoints - (float) padding.Bottom;
                if (extra > 0)
                {
                    height += extra;
                }
            }
            else
            {
                height += (float) props.SpacingAfterPoints;
            }
        }

        return height;
    }

    static CellSpacing GetEffectivePadding(TableCellProperties cellProps, TableProperties tableProps) =>
        cellProps.Padding ?? tableProps.DefaultCellPadding;

    static CellSpacing GetEffectiveMargin(TableCellProperties cellProps, TableProperties tableProps) =>
        cellProps.Margin ?? tableProps.DefaultCellMargin;

    static CellBorders? ResolveCellBorders(TableCellProperties cellProps, TableProperties tableProps, int rowIndex, int colIndex, int totalRows, int totalCols)
    {
        if (cellProps.Borders != null)
        {
            return cellProps.Borders;
        }

        var outer = tableProps.DefaultBorders;
        var insideH = tableProps.InsideHorizontalBorder;
        var insideV = tableProps.InsideVerticalBorder;

        if (outer == null && insideH == null && insideV == null)
        {
            return null;
        }

        var isFirstRow = rowIndex == 0;
        var isLastRow = rowIndex == totalRows - 1;
        var isFirstCol = colIndex == 0;
        var isLastCol = colIndex == totalCols - 1;

        return new CellBorders
        {
            Top = isFirstRow ? (outer?.Top ?? BorderEdge.None) : (insideH ?? BorderEdge.None),
            Bottom = isLastRow ? (outer?.Bottom ?? BorderEdge.None) : (insideH ?? BorderEdge.None),
            Left = isFirstCol ? (outer?.Left ?? BorderEdge.None) : (insideV ?? BorderEdge.None),
            Right = isLastCol ? (outer?.Right ?? BorderEdge.None) : (insideV ?? BorderEdge.None)
        };
    }

    void RenderTableCell(TableCell cell, float x, float y, float width, float height, TableProperties tableProps, int rowIndex, int colIndex, int totalRows, int totalCols)
    {
        if (currentPage == null)
        {
            return;
        }

        var padding = GetEffectivePadding(cell.Properties, tableProps);
        var margin = GetEffectiveMargin(cell.Properties, tableProps);

        var cellX = x + (float) margin.Left;
        var cellY = y + (float) margin.Top;
        var cellWidth = width - (float) margin.Horizontal;
        var cellHeight = height - (float) margin.Vertical;

        var pixelX = context.PointsToPixels(cellX);
        var pixelY = context.PointsToPixels(cellY);
        var pixelWidth = context.PointsToPixels(cellWidth);
        var pixelHeight = context.PointsToPixels(cellHeight);

        if (cell.Properties.BackgroundColorHex != null)
        {
            var bgColor = RenderContext.ParseColor(cell.Properties.BackgroundColorHex);
            currentPage.Mutate(ctx => ctx.Fill(bgColor, new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight)));
        }

        var borders = ResolveCellBorders(cell.Properties, tableProps, rowIndex, colIndex, totalRows, totalCols);
        if (borders != null)
        {
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

        var savedY = context.CurrentY;

        var contentX = cellX + (float) padding.Left;
        var contentWidth = cellWidth - (float) padding.Horizontal;
        var availableHeight = cellHeight - (float) padding.Vertical;

        float contentHeight = 0;
        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                float bulletIndent = para.Properties.Numbering != null ? 12 : 0;
                contentHeight += textRenderer.MeasureParagraphHeightWithWidth(para, contentWidth - bulletIndent);
            }
            else if (element is ContentControlElement contentControl)
            {
                var measurePara = new ParagraphElement
                {
                    Runs = contentControl.Runs!,
                    Properties = new()
                };
                contentHeight += textRenderer.MeasureParagraphHeightWithWidth(measurePara, contentWidth);
            }
            else if (element is ImageElement image)
            {
                var imageWidth = (float) image.WidthPoints;
                var imageHeight = (float) image.HeightPoints;
                if (imageWidth > contentWidth)
                {
                    var scale = contentWidth / imageWidth;
                    imageHeight *= scale;
                }

                contentHeight += imageHeight;
            }
        }

        var verticalOffset = cell.Properties.VerticalAlignment switch
        {
            CellVerticalAlignment.Center => Math.Max(0, (availableHeight - contentHeight) / 2),
            CellVerticalAlignment.Bottom => Math.Max(0, availableHeight - contentHeight),
            _ => 0
        };

        if (cell.Properties is {VerticalMerge: VerticalMergeType.Restart, VerticalAlignment: CellVerticalAlignment.Center})
        {
            const float maxCenterOffset = 12f;
            verticalOffset = Math.Min(verticalOffset, maxCenterOffset);
        }

        context.CurrentY = cellY + (float) padding.Top + verticalOffset;

        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                RenderParagraphInBounds(para, contentX, contentWidth);
            }
            else if (element is ContentControlElement contentControl)
            {
                RenderContentControlInCell(contentControl, contentX, contentWidth);
            }
            else if (element is ImageElement image)
            {
                RenderImageInCell(image, contentX, contentWidth);
            }
        }

        context.CurrentY = savedY;
    }

    void DrawBorderLine(float x1, float y1, float x2, float y2, BorderEdge edge)
    {
        if (currentPage == null)
        {
            return;
        }

        var color = RenderContext.ParseColor(edge.ColorHex ?? "000000");
        var strokeWidth = context.PointsToPixels((float) edge.WidthPoints);
        var pen = Pens.Solid(color, strokeWidth);

        currentPage.Mutate(ctx => ctx.DrawLine(pen, new PointF(x1, y1), new PointF(x2, y2)));
    }

    void RenderParagraphInBounds(ParagraphElement paragraph, float x, float maxWidth)
    {
        if (currentPage == null)
        {
            return;
        }

        textRenderer.RenderParagraphInBounds(currentPage, paragraph, x, maxWidth);
    }

    void RenderImageInCell(ImageElement image, float x, float maxWidth)
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
            img.Mutate(ctx => ctx.Resize((int) pixelWidth, (int) pixelHeight));
            currentPage.Mutate(ctx => ctx.DrawImage(img, new Point((int) pixelX, (int) pixelY), 1f));
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
        currentPage.Mutate(ctx => ctx.Fill(bgColor, new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight)));
        currentPage.Mutate(ctx => ctx.Draw(Pens.Solid(Color.Gray, 1 * context.Scale), new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight)));

        var displayText = string.IsNullOrEmpty(textField.Value) ? textField.DefaultText ?? "" : textField.Value;
        if (!string.IsNullOrEmpty(displayText))
        {
            var font = context.GetFontForFamily("Aptos", 10, false, false);
            var textColor = textField.Enabled ? Color.Black : Color.Gray;
            var textX = pixelX + 3 * context.Scale;
            var textY = pixelY + 2 * context.Scale;
            currentPage.Mutate(ctx => ctx.DrawText(displayText, font, textColor, new PointF(textX, textY)));
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
        currentPage.Mutate(ctx => ctx.Fill(bgColor, new RectangleF(pixelX, pixelY, pixelSize, pixelSize)));
        currentPage.Mutate(ctx => ctx.Draw(Pens.Solid(Color.Black, 1 * context.Scale), new RectangleF(pixelX, pixelY, pixelSize, pixelSize)));

        if (checkBox.Checked)
        {
            var checkPen = Pens.Solid(Color.Black, 2 * context.Scale);
            var pad = pixelSize * 0.2f;
            var left = pixelX + pad;
            var right = pixelX + pixelSize - pad;
            var top = pixelY + pad;
            var bottom = pixelY + pixelSize - pad;
            var midX = pixelX + pixelSize * 0.4f;

            currentPage.Mutate(ctx =>
            {
                ctx.DrawLine(checkPen, new PointF(left, top + (bottom - top) * 0.5f), new PointF(midX, bottom));
                ctx.DrawLine(checkPen, new PointF(midX, bottom), new PointF(right, top));
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
        currentPage.Mutate(ctx => ctx.Fill(bgColor, new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight)));
        currentPage.Mutate(ctx => ctx.Draw(Pens.Solid(Color.Gray, 1 * context.Scale), new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight)));

        var selectedValue = dropDown.SelectedIndex >= 0 && dropDown.SelectedIndex < dropDown.Items.Count
            ? dropDown.Items[dropDown.SelectedIndex]
            : "";

        if (!string.IsNullOrEmpty(selectedValue))
        {
            var font = context.GetFontForFamily("Aptos", 10, false, false);
            var textColor = dropDown.Enabled ? Color.Black : Color.Gray;
            currentPage.Mutate(ctx => ctx.DrawText(selectedValue, font, textColor, new PointF(pixelX + 3 * context.Scale, pixelY + 2 * context.Scale)));
        }

        var arrowSize = pixelHeight * 0.3f;
        var arrowX = pixelX + pixelWidth - 12 * context.Scale;
        var arrowY = pixelY + pixelHeight / 2;

        var arrowBuilder = new PathBuilder();
        arrowBuilder.AddLine(new PointF(arrowX, arrowY - arrowSize / 2), new PointF(arrowX + arrowSize, arrowY - arrowSize / 2));
        arrowBuilder.AddLine(new PointF(arrowX + arrowSize, arrowY - arrowSize / 2), new PointF(arrowX + arrowSize / 2, arrowY + arrowSize / 2));
        arrowBuilder.CloseFigure();
        currentPage.Mutate(ctx => ctx.Fill(Color.Black, arrowBuilder.Build()));

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

        currentPage.Mutate(ctx => ctx.Fill(Color.White, new RectangleF(pixelX, pixelY, pixelSize, pixelSize)));
        currentPage.Mutate(ctx => ctx.Draw(Pens.Solid(Color.Black, 1 * context.Scale), new RectangleF(pixelX, pixelY, pixelSize, pixelSize)));

        if (control.Checked == true)
        {
            var checkPen = Pens.Solid(Color.Black, 2 * context.Scale);
            var pad = pixelSize * 0.25f;

            currentPage.Mutate(ctx =>
            {
                ctx.DrawLine(checkPen, new PointF(pixelX + pad, pixelY + pad), new PointF(pixelX + pixelSize - pad, pixelY + pixelSize - pad));
                ctx.DrawLine(checkPen, new PointF(pixelX + pixelSize - pad, pixelY + pad), new PointF(pixelX + pad, pixelY + pixelSize - pad));
            });
        }

        context.CurrentY += boxSize + 4;
    }

    void RenderContentControlInCell(ContentControlElement control, float x, float maxWidth)
    {
        if (currentPage == null)
        {
            return;
        }

        ParagraphElement para;
        if (control.Runs is {Count: > 0})
        {
            para = new()
            {
                Runs = control.Runs,
                Properties = new()
            };
        }
        else if (!string.IsNullOrEmpty(control.Content))
        {
            para = new()
            {
                Runs =
                [
                    new()
                    {
                        Text = control.Content,
                        Properties = new()
                    }
                ],
                Properties = new()
            };
        }
        else
        {
            return;
        }

        RenderParagraphInBounds(para, x, maxWidth);
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
                RenderParagraph(new ParagraphElement { Runs = control.Runs, Properties = new() });
            }
            else
            {
                var displayText = string.IsNullOrEmpty(control.Content) ? control.PlaceholderText ?? "" : control.Content;
                if (!string.IsNullOrEmpty(displayText))
                {
                    RenderParagraph(new ParagraphElement
                    {
                        Runs = [new() { Text = displayText, Properties = new() }],
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

        currentPage.Mutate(ctx =>
        {
            ctx.Fill(Color.FromRgb(245, 245, 245), new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight));
            ctx.Draw(Pens.Solid(Color.FromRgb(200, 200, 200), 1 * context.Scale), new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight));
        });

        var text = string.IsNullOrEmpty(content) ? placeholder ?? "" : content;
        var isPlaceholder = string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(placeholder);

        if (!string.IsNullOrEmpty(text))
        {
            var font = context.GetFontForFamily("Aptos", 10, false, false);
            var textColor = isPlaceholder ? Color.Gray : Color.Black;
            currentPage.Mutate(ctx => ctx.DrawText(text, font, textColor, new PointF(pixelX + 3 * context.Scale, pixelY + 2 * context.Scale)));
        }

        if (drawDropdownArrow)
        {
            var arrowSize = pixelHeight * 0.3f;
            var arrowX = pixelX + pixelWidth - 12 * context.Scale;
            var arrowY = pixelY + pixelHeight / 2;

            var arrowBuilder = new PathBuilder();
            arrowBuilder.AddLine(new PointF(arrowX, arrowY - arrowSize / 2), new PointF(arrowX + arrowSize, arrowY - arrowSize / 2));
            arrowBuilder.AddLine(new PointF(arrowX + arrowSize, arrowY - arrowSize / 2), new PointF(arrowX + arrowSize / 2, arrowY + arrowSize / 2));
            arrowBuilder.CloseFigure();
            currentPage.Mutate(ctx => ctx.Fill(Color.Black, arrowBuilder.Build()));
        }

        context.CurrentY += fieldHeight + 4;
    }

    void StartNewPage()
    {
        currentPage = new Image<Rgba32>(context.PageWidthPixels, context.PageHeightPixels);

        var bgColor = context.PageSettings.BackgroundColorHex;
        var fillColor = !string.IsNullOrEmpty(bgColor) ? RenderContext.ParseColor(bgColor) : Color.White;
        currentPage.Mutate(ctx => ctx.Fill(fillColor, new RectangleF(0, 0, context.PageWidthPixels, context.PageHeightPixels)));

        if (pages.Count > 0)
        {
            context.StartNewPage();
            context.ResetLineNumbersForPage();
        }

        RenderHeader();

        hasSignificantContentOnCurrentPage = false;
        currentPageFromExplicitBreak = false;
    }

    void FinishCurrentPage()
    {
        if (currentPage != null)
        {
            RenderFooter();
            pages.Add(currentPage);
            currentPage = null;
        }
    }

    void RemoveBlankTrailingPage()
    {
        if (pages.Count > 1 && !hasSignificantContentOnCurrentPage && !currentPageFromExplicitBreak)
        {
            var lastPage = pages[^1];
            pages.RemoveAt(pages.Count - 1);
            lastPage.Dispose();
        }
    }

    void RenderBackgroundShape(FloatingShapeElement shape)
    {
        if (currentPage == null)
        {
            return;
        }

        var x = CalculateShapeX(shape);
        var y = CalculateShapeY(shape);
        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels((float) shape.WidthPoints);
        var pixelHeight = context.PointsToPixels((float) shape.HeightPoints);

        if (shape.ImageData != null)
        {
            try
            {
                using var img = Image.Load<Rgba32>(shape.ImageData);
                img.Mutate(ctx => ctx.Resize((int) pixelWidth, (int) pixelHeight));
                currentPage.Mutate(ctx => ctx.DrawImage(img, new Point((int) pixelX, (int) pixelY), 1f));
            }
            catch
            {
                // Ignore image decode errors
            }
        }
        else if (shape.FillColorHex != null)
        {
            var fillColor = RenderContext.ParseColor(shape.FillColorHex);
            currentPage.Mutate(ctx => ctx.Fill(fillColor, new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight)));
        }
    }

    float CalculateShapeX(FloatingShapeElement shape) =>
        (shape.HorizontalAnchor switch
        {
            HorizontalAnchor.Page => 0f,
            HorizontalAnchor.Margin => (float) context.PageSettings.MarginLeft,
            HorizontalAnchor.Column => (float) context.PageSettings.MarginLeft,
            _ => (float) context.PageSettings.MarginLeft
        }) + (float) shape.HorizontalPositionPoints;

    float CalculateShapeY(FloatingShapeElement shape) =>
        (shape.VerticalAnchor switch
        {
            VerticalAnchor.Page => 0f,
            VerticalAnchor.Margin => (float) context.PageSettings.MarginTop,
            VerticalAnchor.Paragraph => (float) context.PageSettings.MarginTop,
            VerticalAnchor.Line => (float) context.PageSettings.MarginTop,
            _ => (float) context.PageSettings.MarginTop
        }) + (float) shape.VerticalPositionPoints;

    void RenderFloatingImage(FloatingImageElement image)
    {
        if (currentPage == null || image.ContentType == "image/svg+xml")
        {
            return;
        }

        var pixelX = context.PointsToPixels(CalculateFloatingImageX(image));
        var pixelY = context.PointsToPixels(CalculateFloatingImageY(image));
        var pixelWidth = context.PointsToPixels((float) image.WidthPoints);
        var pixelHeight = context.PointsToPixels((float) image.HeightPoints);

        try
        {
            using var img = Image.Load<Rgba32>(image.ImageData);
            img.Mutate(ctx => ctx.Resize((int) pixelWidth, (int) pixelHeight));
            currentPage.Mutate(ctx => ctx.DrawImage(img, new Point((int) pixelX, (int) pixelY), 1f));
        }
        catch
        {
            // Ignore image decode errors
        }
    }

    float CalculateFloatingImageX(FloatingImageElement image) =>
        (image.HorizontalAnchor switch
        {
            HorizontalAnchor.Page => 0f,
            HorizontalAnchor.Margin => (float) context.PageSettings.MarginLeft,
            HorizontalAnchor.Column => context.ContentLeft,
            HorizontalAnchor.Character => context.ContentLeft,
            _ => 0f
        }) + (float) image.HorizontalPositionPoints;

    float CalculateFloatingImageY(FloatingImageElement image) =>
        (image.VerticalAnchor switch
        {
            VerticalAnchor.Page => 0f,
            VerticalAnchor.Margin => (float) context.PageSettings.MarginTop,
            VerticalAnchor.Paragraph => context.CurrentY,
            VerticalAnchor.Line => context.CurrentY,
            _ => 0f
        }) + (float) image.VerticalPositionPoints;

    void RenderFloatingTextBox(FloatingTextBoxElement textBox)
    {
        if (currentPage == null)
        {
            return;
        }

        var x = CalculateFloatingTextBoxX(textBox);
        var y = CalculateFloatingTextBoxY(textBox);
        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels((float) textBox.WidthPoints);
        var pixelHeight = context.PointsToPixels((float) textBox.HeightPoints);

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
                var bgColor = RenderContext.ParseColor(textBox.BackgroundColorHex);
                tempImage.Mutate(ctx => ctx.Fill(bgColor));
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

            tempImage.Mutate(ctx => ctx.Rotate((float) textBox.RotationDegrees));

            // Center the rotated image at the original text box center
            var centerX = pixelX + pixelWidth / 2;
            var centerY = pixelY + pixelHeight / 2;
            var drawX = (int) (centerX - tempImage.Width / 2f);
            var drawY = (int) (centerY - tempImage.Height / 2f);

            currentPage.Mutate(ctx => ctx.DrawImage(tempImage, new Point(drawX, drawY), 1f));
        }
        else
        {
            if (textBox.BackgroundColorHex != null)
            {
                var bgFillColor = RenderContext.ParseColor(textBox.BackgroundColorHex);
                currentPage.Mutate(ctx => ctx.Fill(bgFillColor, new RectangleF(pixelX, pixelY, pixelWidth, pixelHeight)));
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

    float CalculateFloatingTextBoxX(FloatingTextBoxElement textBox) =>
        (textBox.HorizontalAnchor switch
        {
            HorizontalAnchor.Page => 0f,
            HorizontalAnchor.Margin => (float) context.PageSettings.MarginLeft,
            HorizontalAnchor.Column => context.ContentLeft,
            HorizontalAnchor.Character => context.ContentLeft,
            _ => 0f
        }) + (float) textBox.HorizontalPositionPoints;

    float CalculateFloatingTextBoxY(FloatingTextBoxElement textBox) =>
        (textBox.VerticalAnchor switch
        {
            VerticalAnchor.Page => 0f,
            VerticalAnchor.Margin => (float) context.PageSettings.MarginTop,
            VerticalAnchor.Paragraph => context.CurrentY,
            VerticalAnchor.Line => context.CurrentY,
            _ => 0f
        }) + (float) textBox.VerticalPositionPoints;

    public void Dispose() =>
        currentPage?.Dispose();
}
