/// <summary>
/// Backend-agnostic orchestration shared by SkiaPageRenderer and ImageSharpPageRenderer.
/// Owns table rendering, pagination, and cell-content fan-out. Backends supply only the
/// drawing primitives via the abstract members.
/// </summary>
abstract class PageRendererBase(RenderContextBase context)
{
    protected RenderContextBase Context => context;

    /// <summary>The text-layout engine used for measurement and (in derived backends) rendering.</summary>
    protected abstract IParagraphMeasurer Measurer { get; }

    /// <summary>True when there's a current canvas / page to draw on.</summary>
    protected abstract bool HasOutput { get; }

    /// <summary>Closes the current page and (typically) flushes it to the caller.</summary>
    protected abstract void FinishCurrentPage();

    /// <summary>Starts a fresh page, resetting per-page state.</summary>
    protected abstract void StartNewPage();

    /// <summary>Fills a rectangle with the parsed background color.</summary>
    protected abstract void DrawCellBackground(float pixelX, float pixelY, float pixelWidth, float pixelHeight, string hexColor);

    /// <summary>Strokes the visible edges of <paramref name="borders"/> around the cell rectangle.</summary>
    protected abstract void DrawCellBorders(float pixelX, float pixelY, float pixelWidth, float pixelHeight, CellBorders borders);

    /// <summary>Strokes the visible cell diagonals (<c>w:tl2br</c> / <c>w:tr2bl</c>) corner-to-corner.</summary>
    protected abstract void DrawCellDiagonals(float pixelX, float pixelY, float pixelWidth, float pixelHeight, CellDiagonals diagonals);

    /// <summary>Renders a paragraph constrained to a bounded x/width region.</summary>
    protected abstract void RenderParagraphInBounds(ParagraphElement paragraph, float x, float maxWidth);

    /// <summary>Renders an image scaled to fit the available width within a cell.</summary>
    protected abstract void RenderImageInCell(ImageElement image, float x, float maxWidth);

    /// <summary>Renders the contents of a btLr / tbRl rotated cell.</summary>
    protected abstract void RenderVerticalCellContent(TableCell cell, float cellX, float cellY, float cellWidth, float cellHeight, CellSpacing padding);

    /// <summary>Renders a top-level paragraph (handling page breaks, spacing, etc.). Used by
    /// the lifted content-control text path to delegate rich-text rendering back to the leaf.
    /// <paramref name="nextElement"/> lets the leaf elide trailing space when followed by a
    /// heading or section break.</summary>
    protected abstract void RenderParagraph(ParagraphElement paragraph, DocumentElement? nextElement = null);

    /// <summary>Fills a rectangle with <paramref name="fillHex"/> and strokes its outline with
    /// <paramref name="borderHex"/>. Used by every form field and content control box.</summary>
    protected abstract void DrawFormFieldRect(float pixelX, float pixelY, float pixelWidth, float pixelHeight, string fillHex, string borderHex, float pixelBorderWidth);

    /// <summary>Draws short 10pt default-font text inside a form-field rectangle, padded a few
    /// pixels from the left edge. Backends position the baseline per their convention.</summary>
    protected abstract void DrawFormFieldText(string text, float pixelX, float pixelY, float pixelWidth, float pixelHeight, string textHex);

    /// <summary>Draws a checkmark glyph (<paramref name="xShape"/>=false) or an X glyph
    /// (<paramref name="xShape"/>=true) inside a square at the given pixel coordinates.</summary>
    protected abstract void DrawCheckMark(float pixelX, float pixelY, float pixelSize, string hexColor, float pixelStrokeWidth, bool xShape);

    /// <summary>Draws a small downward-pointing triangle near the right edge of a form-field
    /// rectangle (used for combo / dropdown / list controls).</summary>
    protected abstract void DrawDropDownArrow(float pixelX, float pixelY, float pixelHeight, string hexColor);

    /// <summary>Renders a behind-text floating shape (background ribbon, watermark rectangle, etc).</summary>
    protected abstract void RenderBackgroundShape(FloatingShapeElement shape);

    /// <summary>Renders an absolutely-positioned floating image (anchored, not part of text flow).</summary>
    protected abstract void RenderFloatingImage(FloatingImageElement image);

    /// <summary>Renders a paragraph in the header / footer region (does not advance flow Y).</summary>
    protected abstract void RenderHeaderFooterParagraph(ParagraphElement paragraph);

    /// <summary>Strokes a horizontal rule line between the two x-pixel coordinates.</summary>
    protected abstract void DrawHorizontalRuleLine(float pixelX1, float pixelY, float pixelX2, string hexColor, float pixelStrokeWidth);

    /// <summary>Draws a block image at the given pixel rectangle, applying optional rotation, crop, and color effect.
    /// Backends decide how to handle <paramref name="contentType"/> (e.g. SVG vs raster).</summary>
    protected abstract void DrawBlockImage(byte[] imageData, string? contentType, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, ImageCrop? crop, BlipColorEffect colorEffect);

    // Header / footer state shared by both backends. Set from the leaf's RenderDocument
    // before page emission begins; consumed by the lifted RenderHeader / RenderFooter.
    protected HeaderFooterContent? header;
    protected HeaderFooterContent? footer;
    protected HeaderFooterContent? firstPageHeader;
    protected HeaderFooterContent? firstPageFooter;
    protected HeaderFooterContent? evenPageHeader;
    protected HeaderFooterContent? evenPageFooter;
    protected bool differentFirstPage;
    protected float footerHeight;

    // Form-field palette. Same constants used by every backend; centralising them here makes
    // the abstract draw primitives backend-agnostic without leaking ImageSharp/Skia color types.
    const string formFieldActiveBg = "FFFFFF";
    const string formFieldInactiveBg = "F0F0F0";
    const string formFieldBorder = "808080";
    const string contentControlBg = "F5F5F5";
    const string contentControlBorder = "C8C8C8";
    const string checkBoxBorder = "000000";
    const string textBlack = "000000";
    const string textGray = "808080";

    /// <summary>
    /// Renders a Word legacy text form field (a <c>FORMTEXT</c> field): a flat rectangle with
    /// 1-px gray border and the default value or current text inside.
    /// </summary>
    protected void RenderTextFormField(TextFormFieldElement textField)
    {
        if (!HasOutput)
        {
            return;
        }

        var fieldWidth = (float) textField.WidthPoints;
        const float fieldHeight = 18;
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
        var bgHex = textField.Enabled ? formFieldActiveBg : formFieldInactiveBg;
        DrawFormFieldRect(pixelX, pixelY, pixelWidth, pixelHeight, bgHex, formFieldBorder, context.Scale);

        var displayText = string.IsNullOrEmpty(textField.Value) ? textField.DefaultText ?? "" : textField.Value;
        if (!string.IsNullOrEmpty(displayText))
        {
            var textHex = textField.Enabled ? textBlack : textGray;
            DrawFormFieldText(displayText, pixelX, pixelY, pixelWidth, pixelHeight, textHex);
        }

        context.CurrentY += fieldHeight + 4;
    }

    /// <summary>
    /// Renders a Word legacy checkbox form field (<c>FORMCHECKBOX</c>): a square with 1-px black
    /// border and a checkmark inside when <see cref="CheckBoxFormFieldElement.Checked"/> is true.
    /// </summary>
    protected void RenderCheckBoxFormField(CheckBoxFormFieldElement checkBox)
    {
        if (!HasOutput)
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
        var bgHex = checkBox.Enabled ? formFieldActiveBg : formFieldInactiveBg;
        DrawFormFieldRect(pixelX, pixelY, pixelSize, pixelSize, bgHex, checkBoxBorder, context.Scale);

        if (checkBox.Checked)
        {
            DrawCheckMark(pixelX, pixelY, pixelSize, textBlack, 2 * context.Scale, xShape: false);
        }

        context.CurrentY += boxSize + 4;
    }

    /// <summary>
    /// Renders a Word legacy dropdown form field (<c>FORMDROPDOWN</c>): a flat rectangle with
    /// 1-px gray border, the currently selected list item inside, and a small ▼ on the right.
    /// </summary>
    protected void RenderDropDownFormField(DropDownFormFieldElement dropDown)
    {
        if (!HasOutput)
        {
            return;
        }

        var fieldWidth = (float) dropDown.WidthPoints;
        const float fieldHeight = 18;
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
        var bgHex = dropDown.Enabled ? formFieldActiveBg : formFieldInactiveBg;
        DrawFormFieldRect(pixelX, pixelY, pixelWidth, pixelHeight, bgHex, formFieldBorder, context.Scale);

        var selectedValue = dropDown.SelectedIndex >= 0 && dropDown.SelectedIndex < dropDown.Items.Count
            ? dropDown.Items[dropDown.SelectedIndex]
            : "";

        if (!string.IsNullOrEmpty(selectedValue))
        {
            var textHex = dropDown.Enabled ? textBlack : textGray;
            DrawFormFieldText(selectedValue, pixelX, pixelY, pixelWidth, pixelHeight, textHex);
        }

        DrawDropDownArrow(pixelX + pixelWidth, pixelY, pixelHeight, textBlack);

        context.CurrentY += fieldHeight + 4;
    }

    /// <summary>
    /// Dispatches an OOXML structured-document tag (<c>w:sdt</c>) to the renderer for its
    /// specific control type.
    /// </summary>
    protected void RenderContentControl(ContentControlElement control)
    {
        if (!HasOutput)
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
        const float boxSize = 12;
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
        DrawFormFieldRect(pixelX, pixelY, pixelSize, pixelSize, formFieldActiveBg, checkBoxBorder, context.Scale);

        if (control.Checked == true)
        {
            // Content-control checkbox uses an X (typed-form-field uses ✓), matching Word's
            // historical rendering of the two surfaces.
            DrawCheckMark(pixelX, pixelY, pixelSize, textBlack, 2 * context.Scale, xShape: true);
        }

        context.CurrentY += boxSize + 4;
    }

    void RenderContentControlText(ContentControlElement control)
    {
        // Rich-text and plain-text content controls are styled placeholders, not form fields —
        // route them through normal paragraph rendering so their styled runs come out correctly.
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
                return;
            }

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

            return;
        }

        var text = string.IsNullOrEmpty(control.Content) ? control.PlaceholderText ?? "" : control.Content;
        var isPlaceholder = string.IsNullOrEmpty(control.Content) && !string.IsNullOrEmpty(control.PlaceholderText);
        RenderContentControlBox(control.WidthPoints, text, isPlaceholder ? textGray : textBlack, drawDropdownArrow: false);
    }

    void RenderContentControlDropDown(ContentControlElement control)
    {
        string? first = null;
        foreach (var item in control.ListItems!)
        {
            first = item;
            break;
        }

        var displayText = string.IsNullOrEmpty(control.Content)
            ? first ?? control.PlaceholderText ?? ""
            : control.Content;

        RenderContentControlBox(control.WidthPoints, displayText, textBlack, drawDropdownArrow: true);
    }

    void RenderContentControlDate(ContentControlElement control)
    {
        var displayText = control.DateValue.HasValue
            ? control.DateValue.Value.ToShortDateString()
            : !string.IsNullOrEmpty(control.Content) ? control.Content : control.PlaceholderText ?? "";

        var hasValue = control.DateValue.HasValue || !string.IsNullOrEmpty(control.Content);
        RenderContentControlBox(control.WidthPoints, displayText, hasValue ? textBlack : textGray, drawDropdownArrow: false);
    }

    /// <summary>
    /// Shared layout for the three content-control variants that render as a flat rectangle
    /// (text, dropdown, date): handles page break, geometry, the background fill + border,
    /// optional text, and the dropdown arrow when requested.
    /// </summary>
    void RenderContentControlBox(double fieldWidthPoints, string displayText, string textHex, bool drawDropdownArrow)
    {
        var fieldWidth = (float) fieldWidthPoints;
        const float fieldHeight = 18;
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
        DrawFormFieldRect(pixelX, pixelY, pixelWidth, pixelHeight, contentControlBg, contentControlBorder, context.Scale);

        if (!string.IsNullOrEmpty(displayText))
        {
            DrawFormFieldText(displayText, pixelX, pixelY, pixelWidth, pixelHeight, textHex);
        }

        if (drawDropdownArrow)
        {
            DrawDropDownArrow(pixelX + pixelWidth, pixelY, pixelHeight, textBlack);
        }

        context.CurrentY += fieldHeight + 4;
    }

    /// <summary>
    /// Selects the active header for the current page — first-page header (when
    /// <see cref="differentFirstPage"/> is on and we're on page 1) → even-page header
    /// (on even pages) → default header — and renders its elements at <c>HeaderDistance</c>.
    /// </summary>
    protected void RenderHeader()
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

        if (activeHeader == null || !HasOutput)
        {
            return;
        }

        var savedY = context.CurrentY;
        context.CurrentY = (float) context.PageSettings.HeaderDistance;

        RenderHeaderFooterElements(activeHeader);

        context.CurrentY = savedY;
    }

    /// <summary>
    /// Selects the active footer for the current page (same precedence as
    /// <see cref="RenderHeader"/>) and renders its elements anchored to the page bottom.
    /// </summary>
    protected void RenderFooter()
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

        if (activeFooter == null || !HasOutput)
        {
            return;
        }

        var savedY = context.CurrentY;
        context.CurrentY = (float) (context.PageSettings.HeightPoints - context.PageSettings.FooterDistance - footerHeight);

        RenderHeaderFooterElements(activeFooter);

        context.CurrentY = savedY;
    }

    void RenderHeaderFooterElements(HeaderFooterContent content)
    {
        foreach (var element in content.Elements)
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
                RenderHeaderFooterParagraph(para);
            }
        }
    }

    /// <summary>Renders a horizontal-rule element: 0.75pt gray line across the content width.</summary>
    protected void RenderHorizontalRule()
    {
        const float ruleHeight = 6; // spacing above + line + spacing below
        EnsureSpaceFor(ruleHeight);

        if (HasOutput)
        {
            var y = context.PointsToPixels(context.CurrentY + 3);
            var x1 = context.PointsToPixels(context.ContentLeft);
            var x2 = context.PointsToPixels(context.ContentLeft + context.ContentWidth);
            DrawHorizontalRuleLine(x1, y, x2, "A0A0A0", context.PointsToPixels(0.75f));
        }

        context.CurrentY += ruleHeight;
    }

    /// <summary>Renders a block-level image, handling page-break, geometry, and Y advancement.</summary>
    protected void RenderImage(ImageElement image)
    {
        var height = (float) image.HeightPoints;
        EnsureSpaceFor(height);

        if (!HasOutput)
        {
            return;
        }

        var x = context.PointsToPixels(context.ContentLeft);
        var y = context.PointsToPixels(context.CurrentY);
        var width = context.PointsToPixels((float) image.WidthPoints);
        var pixelHeight = context.PointsToPixels(height);

        // Backends that can't render the primary content type (e.g. ImageSharp + SVG) substitute
        // the docx-supplied raster fallback when one is present.
        var data = image.ImageData;
        var contentType = image.ContentType;
        if (!CanRenderContentType(contentType) && image.RasterFallbackData != null)
        {
            data = image.RasterFallbackData;
            contentType = image.RasterFallbackContentType;
        }

        DrawBlockImage(data, contentType, x, y, width, pixelHeight, (float) image.RotationDegrees, image.Crop, image.ColorEffect);

        context.CurrentY += height;
    }

    /// <summary>
    /// Override in backends that can't render certain content types so the inline-image path
    /// can fall back to <see cref="ImageElement.RasterFallbackData"/>. Default renders all.
    /// </summary>
    protected virtual bool CanRenderContentType(string? contentType) => true;

    /// <summary>
    /// Ensures there's space for <paramref name="height"/> on the current page; otherwise
    /// moves to the next column or page. Content taller than a full page renders at the
    /// current position rather than triggering a useless break.
    /// </summary>
    protected void EnsureSpaceFor(float height)
    {
        if (height > context.ContentHeight)
        {
            return;
        }

        if (!context.HasSpaceFor(height) &&
            context.CurrentY > context.ContentTop)
        {
            if (!context.MoveToNextColumn())
            {
                FinishCurrentPage();
                StartNewPage();
            }
        }
    }

    /// <summary>
    /// Renders a content control inside a table cell by reusing
    /// <see cref="RenderParagraphInBounds"/> with a synthetic paragraph.
    /// </summary>
    protected void RenderContentControlInCell(ContentControlElement control, float x, float maxWidth)
    {
        if (!HasOutput)
        {
            return;
        }

        // Use styled runs if available, otherwise fall back to plain text.
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

    /// <summary>
    /// Top-level table render. Decides between single-page rendering and row-by-row
    /// pagination based on whether the table fits in the remaining content area.
    /// </summary>
    protected void RenderTable(TableElement table)
    {
        if (!HasOutput || table.Rows.Count == 0)
        {
            return;
        }

        // Floating tables (w:tblpPr) are rendered inline today; honour their tblpY as a
        // y-offset before the first row to approximate Word's anchored placement.
        if (table.Properties.IsFloating && table.Properties.FloatingYOffsetPoints > 0)
        {
            context.CurrentY += (float) table.Properties.FloatingYOffsetPoints;
        }

        var colCount = TableLayout.GetColumnCount(table);
        var colWidths = TableLayout.CalculateColumnWidths(table, colCount, context.ContentWidth, Measurer);
        var hasVerticalMerge = TableLayout.HasVerticalMerge(table);

        var rowHeights = TableHeightCalculator.CalculateRowHeights(table, colWidths, Measurer, hasVerticalMerge);
        var totalHeight = rowHeights.Sum();

        // Allow a 10% tolerance on the page-overflow check; row-height measurement is conservative.
        var tableTolerance = context.ContentHeight * 0.10f;
        var needsRowByRowRendering = totalHeight > context.ContentHeight + tableTolerance;

        if (needsRowByRowRendering)
        {
            RenderTableRowByRow(table, colCount, colWidths, rowHeights);
        }
        else
        {
            // Word's layout often allows tables to slightly overflow (line-height rounding etc.).
            // Word 2013+ (mode 15) has more consistent table handling, so use a slightly higher tolerance.
            var tolerancePercent = context.Compatibility.CompatibilityMode >= 15 ? 0.02f : 0.01f;
            var tolerance = context.ContentHeight * tolerancePercent;
            var requiredHeight = totalHeight - tolerance;
            EnsureSpaceFor(requiredHeight);
            RenderTableRows(table, colCount, colWidths, rowHeights, hasVerticalMerge);
        }
    }

    /// <summary>
    /// Draws the table's outer border as a single rectangle at the table boundary.
    /// Used only for the detached-border model (<c>w:tblCellSpacing</c> &gt; 0) where Word
    /// places this frame around the whole grid in addition to each cell's own borders.
    /// </summary>
    void DrawTableOuterFrame(float x, float y, float width, float height, CellBorders borders)
    {
        var pixelX = context.PointsToPixels(x);
        var pixelY = context.PointsToPixels(y);
        var pixelWidth = context.PointsToPixels(width);
        var pixelHeight = context.PointsToPixels(height);
        DrawCellBorders(pixelX, pixelY, pixelWidth, pixelHeight, borders);
    }

    protected float ComputeTableX(TableElement table, float[] colWidths)
    {
        var contentLeft = context.ContentLeft;
        var tableWidth = colWidths.Sum();
        var slack = context.ContentWidth - tableWidth;
        return table.Properties.Alignment switch
        {
            TextAlignment.Center => contentLeft + Math.Max(0, slack / 2),
            TextAlignment.Right => contentLeft + Math.Max(0, slack),
            _ => contentLeft
        };
    }

    /// <summary>
    /// Renders all table rows at the current position (used when the table fits on one page).
    /// Picks per-column Y tracking for vMerge tables and per-row tracking otherwise.
    /// </summary>
    void RenderTableRows(TableElement table, int colCount, float[] colWidths, float[] rowHeights, bool hasVerticalMerge)
    {
        var tableX = ComputeTableX(table, colWidths);
        var startY = context.CurrentY;

        // Detached-border model also needs Word's outer frame around the whole table —
        // each cell renders its own borders inset by cellSpacing, but Word draws an extra
        // rectangle at the table's outer boundary so the frame and the cell borders show
        // a visible gap between them.
        if (table.Properties is {CellSpacingPoints: > 0, DefaultBorders: { } outer})
        {
            DrawTableOuterFrame(tableX, startY, colWidths.Sum(), rowHeights.Sum(), outer);
        }

        if (hasVerticalMerge)
        {
            // Track Y per column so vertical merges line up properly.
            var columnYPositions = new float[colCount];
            for (var i = 0; i < colCount; i++)
            {
                columnYPositions[i] = startY;
            }

            RenderTableWithColumnTracking(table, colCount, colWidths, rowHeights, tableX, columnYPositions);

            // The cursor advances to the maximum column Y reached.
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

                // Sum column widths for horizontally merged cells.
                float cellWidth = 0;
                for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                {
                    cellWidth += colWidths[gridColIndex + i];
                }

                // Skip cells continuing a vertical merge; the Restart cell drew over their area.
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
                    // Use the full merged height so background fills the entire spanned area.
                    cellHeight = TableLayout.CalculateVerticalMergeHeight(table, rowIndex, gridColIndex, rowHeights);
                }
                else
                {
                    var padding = TableLayout.GetEffectivePadding(cell.Properties, table.Properties, row);
                    var contentWidth = cellWidth - (float) padding.Horizontal;
                    var contentHeight = TableHeightCalculator.MeasureCellHeight(cell, contentWidth, table.Properties, Measurer, row);
                    cellHeight = contentHeight + (float) padding.Vertical;
                }

                RenderTableCell(cell, currentX, cellY, cellWidth, cellHeight, table.Properties, row, rowIndex, gridColIndex, table.Rows.Count, colCount);

                for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                {
                    columnYPositions[gridColIndex + i] = cellY + cellHeight;
                }

                currentX += cellWidth;
                gridColIndex += span;
            }
        }
    }

    /// <summary>
    /// Renders rows one at a time, triggering page breaks as needed and re-emitting any
    /// header rows after each break. Used when the table is taller than a single page.
    /// </summary>
    void RenderTableRowByRow(TableElement table, int colCount, float[] colWidths, float[] rowHeights)
    {
        // Count contiguous header rows from the top (w:tblHeader). They get re-rendered after each page break.
        var headerCount = 0;
        while (headerCount < table.Rows.Count && table.Rows[headerCount].IsHeader)
        {
            headerCount++;
        }

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var rowHeight = rowHeights[rowIndex];

            var yBefore = context.CurrentY;
            EnsureSpaceFor(rowHeight);
            var pageBroke = context.CurrentY < yBefore;

            // After a page break, re-emit the header rows — but skip when the current row is itself
            // one of those headers (e.g. the very first row on the very first page).
            if (pageBroke && headerCount > 0 && rowIndex >= headerCount)
            {
                var tableXHeader = ComputeTableX(table, colWidths);
                for (var h = 0; h < headerCount; h++)
                {
                    var headerHeight = rowHeights[h];
                    var headerY = context.CurrentY;
                    RenderTableRow(table, h, colCount, colWidths, rowHeights, tableXHeader, headerY);
                    context.CurrentY += headerHeight;
                }
            }

            var tableX = ComputeTableX(table, colWidths);
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

            // Skip cells that continue a vertical merge above.
            if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
            {
                currentX += cellWidth;
                gridColIndex += span;
                continue;
            }

            var cellHeight = rowHeight;
            if (cell.Properties.VerticalMerge == VerticalMergeType.Restart)
            {
                // Use the full merged height so background fills the entire spanned area.
                cellHeight = TableLayout.CalculateVerticalMergeHeight(table, rowIndex, gridColIndex, rowHeights);
            }

            RenderTableCell(cell, currentX, currentY, cellWidth, cellHeight, table.Properties, row, rowIndex, gridColIndex, table.Rows.Count, colCount);

            currentX += cellWidth;
            gridColIndex += span;
        }
    }

    void RenderTableCell(TableCell cell, float x, float y, float width, float height, TableProperties tableProps, TableRow row, int rowIndex, int colIndex, int totalRows, int totalCols)
    {
        if (!HasOutput)
        {
            return;
        }

        var padding = TableLayout.GetEffectivePadding(cell.Properties, tableProps, row);
        var margin = TableLayout.GetEffectiveMargin(cell.Properties, tableProps);

        // w:tblCellSpacing puts a margin around each cell box, producing the detached-border
        // model where adjacent cells appear as separate rectangles with visible gaps. The
        // gap between two adjacent cells is 2 × CellSpacingPoints (one side from each cell).
        var spacing = (float) tableProps.CellSpacingPoints;

        var cellX = x + (float) margin.Left + spacing;
        var cellY = y + (float) margin.Top + spacing;
        var cellWidth = width - (float) margin.Horizontal - 2 * spacing;
        var cellHeight = height - (float) margin.Vertical - 2 * spacing;

        var pixelX = context.PointsToPixels(cellX);
        var pixelY = context.PointsToPixels(cellY);
        var pixelWidth = context.PointsToPixels(cellWidth);
        var pixelHeight = context.PointsToPixels(cellHeight);

        if (cell.Properties.BackgroundColorHex != null)
        {
            DrawCellBackground(pixelX, pixelY, pixelWidth, pixelHeight, cell.Properties.BackgroundColorHex);
        }

        var borders = TableLayout.ResolveCellBorders(cell.Properties, tableProps, rowIndex, colIndex, totalRows, totalCols, row);
        if (borders != null)
        {
            DrawCellBorders(pixelX, pixelY, pixelWidth, pixelHeight, borders);
        }

        // Diagonals draw additively on top of whatever sides the cell ended up with.
        if (cell.Properties.Diagonals is {HasAny: true} diagonals)
        {
            DrawCellDiagonals(pixelX, pixelY, pixelWidth, pixelHeight, diagonals);
        }

        if (cell.Properties.TextDirection != CellTextDirection.LeftToRight)
        {
            RenderVerticalCellContent(cell, cellX, cellY, cellWidth, cellHeight, padding);
            return;
        }

        var savedY = context.CurrentY;

        var contentX = cellX + (float) padding.Left;
        var contentWidth = cellWidth - (float) padding.Horizontal;
        var availableHeight = cellHeight - (float) padding.Vertical;

        // Measure content height for vertical alignment.
        float contentHeight = 0;
        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                // Account for bullet indent to match RenderParagraphInBounds behavior.
                float bulletIndent = para.Properties.Numbering != null ? 12 : 0;
                contentHeight += Measurer.MeasureParagraphHeightWithWidth(para, contentWidth - bulletIndent);
            }
            else if (element is ContentControlElement contentControl)
            {
                var measurePara = new ParagraphElement
                {
                    Runs = contentControl.Runs!,
                    Properties = new()
                };
                contentHeight += Measurer.MeasureParagraphHeightWithWidth(measurePara, contentWidth);
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
            _ => 0 // Top alignment
        };

        // For cells that start a vertical merge (vMerge="restart"), Word uses reduced centering
        // — content sits closer to the top — so cap the offset at ~0.17 inches.
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
}
