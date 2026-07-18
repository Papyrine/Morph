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
    protected abstract void DrawBlockImage(byte[] imageData, string? contentType, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, bool flipHorizontal, bool flipVertical, ImageCrop? crop, BlipColorEffect colorEffect);

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
        var displayText = DateControlText.Resolve(control);
        var hasValue = !string.IsNullOrEmpty(control.Content) || control.DateValue.HasValue;
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
        // Substitute live page numbers for any PAGE/NUMPAGES/SECTIONPAGES field on this page
        // (headers/footers are re-rendered per page, so CurrentPageNumber is current here).
        content = ResolveHeaderFooterPageFields(content);

        var previousInHeaderFooter = inHeaderFooter;
        inHeaderFooter = true;
        try
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
                else if (element is TableElement table)
                {
                    // Banner tables Word templates put in headers/footers render inline at the
                    // header/footer cursor; pagination is suppressed (see inHeaderFooter) so an
                    // overflowing footer table renders in place instead of recursing into RenderFooter.
                    RenderTable(table);
                }
            }
        }
        finally
        {
            inHeaderFooter = previousInHeaderFooter;
        }
    }

    // ---- page-number field substitution (PAGE / NUMPAGES / SECTIONPAGES) ----

    /// <summary>
    /// Returns a copy of <paramref name="paragraph"/> with every page-field run's cached text
    /// replaced by the live value for the current page, or the same instance when the paragraph
    /// has no page fields (or their value isn't known yet — the counting pass leaves NUMPAGES as
    /// its cached text). Backends call this at the top of their body-paragraph render so both
    /// measurement and drawing see the resolved text.
    /// </summary>
    protected ParagraphElement ResolveParagraphPageFields(ParagraphElement paragraph)
    {
        List<Run>? resolved = null;
        for (var i = 0; i < paragraph.Runs.Count; i++)
        {
            var run = paragraph.Runs[i];
            var value = run.PageField == PageFieldKind.None ? null : FormatPageField(run.PageField, run.PageFieldNumberFormat);
            if (value == null)
            {
                resolved?.Add(run);
                continue;
            }

            resolved ??= new(paragraph.Runs.Take(i));
            resolved.Add(run.WithText(value));
        }

        if (resolved == null)
        {
            return paragraph;
        }

        return new()
        {
            Runs = resolved,
            Properties = paragraph.Properties,
            IsAnchorOnlyMark = paragraph.IsAnchorOnlyMark,
            IsCollapsedCellMark = paragraph.IsCollapsedCellMark
        };
    }

    HeaderFooterContent ResolveHeaderFooterPageFields(HeaderFooterContent content)
    {
        List<DocumentElement>? resolved = null;
        for (var i = 0; i < content.Elements.Count; i++)
        {
            var element = content.Elements[i];
            var replacement = ResolveElementPageFields(element);
            if (!ReferenceEquals(replacement, element))
            {
                resolved ??= new(content.Elements.Take(i));
            }

            resolved?.Add(replacement);
        }

        return resolved == null ? content : new() {Elements = resolved};
    }

    DocumentElement ResolveElementPageFields(DocumentElement element) => element switch
    {
        ParagraphElement paragraph => ResolveParagraphPageFields(paragraph),
        TableElement table => ResolveTablePageFields(table),
        _ => element
    };

    TableElement ResolveTablePageFields(TableElement table)
    {
        List<TableRow>? rows = null;
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            List<TableCell>? cells = null;
            for (var c = 0; c < row.Cells.Count; c++)
            {
                var cell = row.Cells[c];
                List<DocumentElement>? cellContent = null;
                for (var k = 0; k < cell.Content.Count; k++)
                {
                    var replacement = ResolveElementPageFields(cell.Content[k]);
                    if (!ReferenceEquals(replacement, cell.Content[k]))
                    {
                        cellContent ??= new(cell.Content.Take(k));
                    }

                    cellContent?.Add(replacement);
                }

                var newCell = cellContent == null ? cell : new TableCell {Content = cellContent, Properties = cell.Properties};
                if (!ReferenceEquals(newCell, cell))
                {
                    cells ??= new(row.Cells.Take(c));
                }

                cells?.Add(newCell);
            }

            var newRow = cells == null
                ? row
                : new TableRow
                {
                    Cells = cells,
                    HeightPoints = row.HeightPoints,
                    IsExactHeight = row.IsExactHeight,
                    IsHeader = row.IsHeader,
                    OverrideBorders = row.OverrideBorders,
                    OverrideInsideHBorder = row.OverrideInsideHBorder,
                    OverrideInsideVBorder = row.OverrideInsideVBorder,
                    OverrideCellPadding = row.OverrideCellPadding
                };
            if (!ReferenceEquals(newRow, row))
            {
                rows ??= new(table.Rows.Take(r));
            }

            rows?.Add(newRow);
        }

        return rows == null ? table : new() {Rows = rows, Properties = table.Properties};
    }

    /// <summary>
    /// The live text for a page field on the current page, or null when it should keep its cached
    /// text — i.e. a NUMPAGES/SECTIONPAGES field during the counting pass, before the total is known.
    /// </summary>
    string? FormatPageField(PageFieldKind kind, string? numberFormat)
    {
        int value;
        switch (kind)
        {
            case PageFieldKind.Page:
                value = context.CurrentPageNumber;
                break;
            case PageFieldKind.NumberOfPages:
            case PageFieldKind.SectionPages:
                if (context.TotalPageCount <= 0)
                {
                    return null;
                }

                value = context.TotalPageCount;
                break;
            default:
                return null;
        }

        return FormatPageNumber(value, numberFormat);
    }

    static string FormatPageNumber(int value, string? numberFormat) => numberFormat switch
    {
        "roman" => ToRoman(value).ToLowerInvariant(),
        "Roman" or "ROMAN" => ToRoman(value),
        "alphabetic" => ToAlphabetic(value).ToLowerInvariant(),
        "Alphabetic" or "ALPHABETIC" => ToAlphabetic(value),
        _ => value.ToString(CultureInfo.InvariantCulture)
    };

    static string ToRoman(int value)
    {
        if (value is <= 0 or > 3999)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        int[] numbers = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var builder = new StringBuilder();
        for (var i = 0; i < numbers.Length && value > 0; i++)
        {
            while (value >= numbers[i])
            {
                builder.Append(symbols[i]);
                value -= numbers[i];
            }
        }

        return builder.ToString();
    }

    // Word's "\* ALPHABETIC" — A..Z, then AA..ZZ, ... (bijective base-26 on uppercase letters).
    static string ToAlphabetic(int value)
    {
        if (value <= 0)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        var builder = new StringBuilder();
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char) ('A' + value % 26));
            value /= 26;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders a floating text frame (Word's <c>w:framePr</c> positioning): measures the content to
    /// auto-size, resolves an absolute position from the frame's anchors + alignment (or explicit
    /// offset), then draws the inner paragraphs there without advancing the body cursor. Shared by
    /// all backends — the frame has no fill, so only <see cref="Measurer"/> and
    /// <see cref="RenderParagraphInBounds"/> are needed.
    /// </summary>
    protected void RenderPositionedFrame(PositionedFrameElement frame)
    {
        if (!HasOutput)
        {
            return;
        }

        // Auto-size: width is the widest content line, height the stacked content height. Explicit
        // w:w / w:h from the frame override the measurement.
        float measuredWidth = 0;
        float measuredHeight = 0;
        foreach (var element in frame.Content)
        {
            if (element is ParagraphElement paragraph)
            {
                measuredWidth = Math.Max(measuredWidth, Measurer.MeasureParagraphNaturalWidth(paragraph, context.ContentWidth));
                measuredHeight += Measurer.MeasureParagraphHeightWithWidth(paragraph, context.ContentWidth);
            }
        }

        // Pad the auto width by a hair: the measured natural width equals the exact line width, but
        // the per-line wrap check is a strict ">", so rendering at exactly that width can spill the
        // final word onto a second line. Two points is below visual threshold and avoids the wrap.
        const float autoWidthPaddingPoints = 2;
        var width = frame.WidthPoints is { } explicitWidth ? (float) explicitWidth : measuredWidth + autoWidthPaddingPoints;
        var height = frame.HeightPoints is { } explicitHeight ? (float) explicitHeight : measuredHeight;
        width = Math.Min(width, context.ContentWidth);

        var x = ResolveFrameX(frame, width);
        var y = ResolveFrameY(frame, height);

        var savedY = context.CurrentY;
        context.CurrentY = y;
        foreach (var element in frame.Content)
        {
            if (element is ParagraphElement paragraph)
            {
                RenderParagraphInBounds(paragraph, x, width);
            }
        }

        context.CurrentY = savedY;
    }

    float ResolveFrameX(PositionedFrameElement frame, float width)
    {
        // The frame's horizontal reference edge.
        var anchorLeft = frame.HorizontalAnchor switch
        {
            HorizontalAnchor.Page => 0f,
            HorizontalAnchor.Margin => (float) context.PageSettings.MarginLeft,
            _ => context.ContentLeft
        };
        var anchorWidth = frame.HorizontalAnchor switch
        {
            HorizontalAnchor.Page => (float) context.PageSettings.WidthPoints,
            HorizontalAnchor.Margin => (float) (context.PageSettings.WidthPoints - context.PageSettings.MarginLeft - context.PageSettings.MarginRight),
            _ => context.ContentWidth
        };

        // Word's legacy text-frame layout does NOT right-align these footer blocks flush to the text
        // margin — it leaves a wide band of empty space on the right. Measured from Word's
        // agendas-minutes/14 render (the block's right edge lands ~0.9" / ~12.2% of the content width
        // inside the right margin), and the inset isn't expressed anywhere in the frame markup
        // (no w:w / w:x / right-indent), so it can only be reproduced empirically. Without it the
        // block sits ~0.9" too far right.
        const float frameRightInsetFraction = 0.122f;
        var rightInset = anchorWidth * frameRightInsetFraction;

        return frame.HorizontalAlignment switch
        {
            FrameHorizontalAlignment.Right => anchorLeft + Math.Max(0, anchorWidth - width - rightInset),
            FrameHorizontalAlignment.Center => anchorLeft + Math.Max(0, (anchorWidth - width) / 2),
            FrameHorizontalAlignment.Left => anchorLeft,
            _ => anchorLeft + (float) frame.XPoints
        };
    }

    // A page/margin-anchored frame with a small explicit y (under half an inch) sitting out of flow
    // is Word's "footer info block" pattern (e.g. a right-aligned Location/Date/Time stack): Word
    // floats it just above the bottom margin rather than at the literal y from the top. A larger y
    // is an intentional upper-page placement (e.g. a centred sub-title) and is honoured from the top.
    const float frameBottomAnchorYThresholdPoints = 36;

    float ResolveFrameY(PositionedFrameElement frame, float height)
    {
        var anchorTop = frame.VerticalAnchor switch
        {
            VerticalAnchor.Page => 0f,
            VerticalAnchor.Margin => context.ContentTop,
            _ => context.CurrentY
        };
        var anchorBottom = frame.VerticalAnchor switch
        {
            VerticalAnchor.Page => (float) (context.PageSettings.HeightPoints - context.PageSettings.MarginBottom),
            _ => context.ContentBottom
        };

        switch (frame.VerticalAlignment)
        {
            case FrameVerticalAlignment.Top:
                return anchorTop + (float) frame.YPoints;
            case FrameVerticalAlignment.Center:
                return anchorTop + Math.Max(0, (anchorBottom - anchorTop - height) / 2);
            case FrameVerticalAlignment.Bottom:
                return anchorBottom - height;
            default:
                // Inline / None on a page/margin anchor: a sizeable explicit y means "place here
                // from the anchor top"; a tiny (or absent) y is the trailing footer block, floated
                // up so it sits just above the bottom margin and stays on-page.
                if (frame.VerticalAnchor is VerticalAnchor.Page or VerticalAnchor.Margin)
                {
                    return frame.YPoints >= frameBottomAnchorYThresholdPoints
                        ? anchorTop + (float) frame.YPoints
                        : anchorBottom - height;
                }

                return context.CurrentY + (float) frame.YPoints;
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

        DrawBlockImage(data, contentType, x, y, width, pixelHeight, (float) image.RotationDegrees, image.FlipHorizontal, image.FlipVertical, image.Crop, image.ColorEffect);

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
            AdvanceToNextColumnOrPage();
        }
    }

    /// <summary>
    /// Advances the flow to the next column of the current section, or — when the last column is
    /// already in use — finishes the page and starts a new one. Pagination decisions that must
    /// respect multi-column sections (keep-with-next, keep-lines, widow/orphan control) route
    /// through here instead of calling <see cref="FinishCurrentPage"/>/<see cref="StartNewPage"/>
    /// directly, so a forced break lands in the next column rather than skipping the remaining
    /// columns of the page.
    /// </summary>
    // Set while a header/footer is rendering. That content is anchored to a fixed band and must
    // never paginate: a footer table that "doesn't fit" must render inline, because advancing the
    // page re-enters RenderFooter (FinishCurrentPage draws the footer) and recurses until the stack
    // overflows. Guards every pagination point reachable from header/footer table rendering.
    bool inHeaderFooter;

    protected void AdvanceToNextColumnOrPage()
    {
        if (inHeaderFooter)
        {
            return;
        }

        if (!context.MoveToNextColumn())
        {
            FinishCurrentPage();
            StartNewPage();
        }
    }

    /// <summary>
    /// Background drawings are page-scoped: the next significant content typically dictates
    /// which page they belong to. Look ahead to the next non-background element and, if it
    /// would page-break before rendering, advance the cursor first so the background lands
    /// on the page where the actual content will be.
    /// </summary>
    // Suffix lookup for AdvanceToBackgroundsTargetPage: nextRequiredHeights[j] is the estimated
    // height of the first break-driving element at index >= j (0 when none remains). Built once
    // per document with a backward pass — the old forward scan re-walked the whole tail for
    // every behind-text drawing, and in table-free documents that meant scanning to the end of
    // the document per background.
    IReadOnlyList<DocumentElement>? nextRequiredHeightsSource;
    float[]? nextRequiredHeights;

    protected void AdvanceToBackgroundsTargetPage(IReadOnlyList<DocumentElement> elements, int backgroundIndex)
    {
        if (context.CurrentY <= context.ContentTop)
        {
            // Already at the top of a fresh page — background already on the right page.
            return;
        }

        // The next element with a known significant height (typically a table) dictates which
        // page the background belongs to. Paragraphs and other small elements estimate zero
        // (they rarely force the break themselves), and that makes later backgrounds skip
        // through to the same driving element too — when a sequence of behind-text drawings
        // precedes a table that needs the next page, all the drawings lift to that page
        // together. Since the estimate is position-independent, the whole forward scan
        // collapses to a precomputed suffix lookup.
        if (!ReferenceEquals(nextRequiredHeightsSource, elements))
        {
            nextRequiredHeightsSource = elements;
            nextRequiredHeights = new float[elements.Count + 1];
            for (var j = elements.Count - 1; j >= 0; j--)
            {
                var own = EstimatedNextElementHeight(elements[j]);
                nextRequiredHeights[j] = own > 0 ? own : nextRequiredHeights[j + 1];
            }
        }

        var required = nextRequiredHeights![backgroundIndex + 1];
        if (required <= 0)
        {
            return;
        }

        if (!context.HasSpaceFor(required) && required <= context.ContentHeight)
        {
            if (!context.MoveToNextColumn())
            {
                FinishCurrentPage();
                StartNewPage();
            }
        }
    }

    static float EstimatedNextElementHeight(DocumentElement element)
    {
        if (element is TableElement table)
        {
            // Sum every row's declared height; for rows without a w:trHeight, assume a
            // ~25pt single-line slot. The estimate only needs to be in the right ballpark
            // for the AdvanceToBackgroundsTargetPage check — when the table genuinely
            // won't fit, this catches it; when it does fit, the actual layout pass
            // confirms and renders inline. Previously this returned just the first row's
            // declared height, which under-estimated multi-row tables to zero.
            float sum = 0;
            foreach (var row in table.Rows)
            {
                sum += (float) (row.HeightPoints ?? 25);
            }
            return sum;
        }

        // Paragraphs / other small elements: a single line is unlikely to force a page break,
        // so conservatively claim no required height.
        return 0;
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

        // Use styled runs if available, otherwise fall back to plain text. The shared wrapper
        // means the layout computed during cell measurement is reused here by identity.
        if (control.CellParagraph is not { } para)
        {
            return;
        }

        RenderParagraphInBounds(para, x, maxWidth);
    }

    // Table layout is computed when a KeepNext lookahead measures the table's height and then
    // again by RenderTable; both run the full column-count + autofit column-width + vMerge +
    // row-height pipeline. The results are pure per (table, available width) — memoize per
    // renderer instance (one document render). The cached arrays are shared and never mutated.
    readonly Dictionary<(TableElement Table, float Width), (int ColCount, float[] ColWidths, bool HasVerticalMerge, float[] RowHeights)> tableLayoutCache = [];

    protected (int ColCount, float[] ColWidths, bool HasVerticalMerge, float[] RowHeights) GetTableLayout(TableElement table)
    {
        var key = (table, context.ContentWidth);
        if (!tableLayoutCache.TryGetValue(key, out var layout))
        {
            var colCount = TableLayout.GetColumnCount(table);
            var colWidths = TableLayout.CalculateColumnWidths(table, colCount, context.ContentWidth, Measurer);
            var hasVerticalMerge = TableLayout.HasVerticalMerge(table);
            var rowHeights = TableHeightCalculator.CalculateRowHeights(table, colWidths, Measurer, hasVerticalMerge);
            layout = (colCount, colWidths, hasVerticalMerge, rowHeights);
            tableLayoutCache[key] = layout;
        }

        return layout;
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

        // Floating tables position absolutely per their tblpY anchor and don't take inline
        // space — save the cursor, jump to the resolved Y, render, then restore so the
        // surrounding flow continues as if the table wasn't there.
        var savedY = context.CurrentY;
        if (table.Properties.IsFloating)
        {
            context.CurrentY = ResolveFloatingTableY(table);
        }

        var (colCount, colWidths, hasVerticalMerge, rowHeights) = GetTableLayout(table);
        var totalHeight = rowHeights.Sum();

        // Allow a 10% tolerance on the page-overflow check; row-height measurement is conservative.
        var tableTolerance = context.ContentHeight * 0.10f;
        var needsRowByRowRendering = totalHeight > context.ContentHeight + tableTolerance;

        // When a table fits on a fresh page but not in the remaining space, advance to the
        // next page before rendering it. Without this, letter-style layouts where one row
        // occupies most of a page (hRule="exact") get their fixed-height row clipped at the
        // bottom margin instead of being lifted onto the next page. Restricted to tables
        // that contain at least one hRule="exact" row so flow-style tables (the bulk of
        // scenarios) keep their existing behaviour where minor over-spill is tolerated.
        // Use a tighter tolerance here than the row-by-row check above — the goal is to
        // pre-empt overflow without splitting the table itself.
        if (!needsRowByRowRendering && !table.Properties.IsFloating && totalHeight > 0)
        {
            var remainingSpace = Math.Max(0f, context.ContentHeight - (context.CurrentY - context.ContentTop));
            // 5pt buffer: line-height rounding can put a single-paragraph leader (~37pt) just
            // a fraction over the remaining-space-minus-table-height delta. Anything inside
            // that margin renders fine without an extra page advance.
            if (totalHeight > remainingSpace + 5f && totalHeight <= context.ContentHeight + tableTolerance)
            {
                var hasExactRow = false;
                foreach (var row in table.Rows)
                {
                    if (row.IsExactHeight)
                    {
                        hasExactRow = true;
                        break;
                    }
                }
                // Only pre-advance when there is content above on the current page. At the top of
                // a fresh page the table already has the whole page to itself; a table taller than
                // one page gains nothing from another break and would strand an empty page in front
                // of it (e.g. a full-sheet label grid whose exact rows measure a hair over a page).
                if (hasExactRow && context.CurrentY > context.ContentTop && !inHeaderFooter)
                {
                    FinishCurrentPage();
                    StartNewPage();
                }
            }
        }


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
            if (!table.Properties.IsFloating)
            {
                EnsureSpaceFor(requiredHeight);
            }
            RenderTableRows(table, colCount, colWidths, rowHeights, hasVerticalMerge);
        }

        if (table.Properties.IsFloating)
        {
            // Page/margin anchored floating tables sit on top of the layout — they don't take
            // flow space, so restore the cursor. A text-anchored table flows with the surrounding
            // text: subsequent content should appear below it, so leave the cursor at the table's
            // bottom (or at savedY if the table rendered above the cursor).
            if (table.Properties.FloatingVerticalAnchor == FloatingTableVerticalAnchor.Text)
            {
                context.CurrentY = Math.Max(savedY, context.CurrentY);
            }
            else
            {
                context.CurrentY = savedY;
            }
        }
    }

    float ResolveFloatingTableY(TableElement table)
    {
        var offset = (float) table.Properties.FloatingYOffsetPoints;
        return table.Properties.FloatingVerticalAnchor switch
        {
            FloatingTableVerticalAnchor.Page => offset,
            FloatingTableVerticalAnchor.Margin => context.ContentTop + offset,
            // Text anchor: where the inline cursor currently sits + tblpY.
            _ => context.CurrentY + offset
        };
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

        // Floating tables (w:tblpPr) position via tblpX measured from horzAnchor,
        // overriding the regular alignment-based placement.
        if (table.Properties.IsFloating)
        {
            var offset = (float) table.Properties.FloatingXOffsetPoints;
            return table.Properties.FloatingHorizontalAnchor switch
            {
                FloatingTableHorizontalAnchor.Page => offset,
                FloatingTableHorizontalAnchor.Margin => (float) context.PageSettings.MarginLeft + offset,
                // Text/column anchor: from the current text column's left edge.
                _ => contentLeft + offset
            };
        }

        var tableWidth = colWidths.Sum();
        // w:tblInd shifts the table from the leading edge of the text column. It only applies
        // to left-aligned tables — for centred/right alignment the indent collapses into the
        // slack space, matching Word's behaviour.
        var indent = (float) table.Properties.IndentPoints;
        var slack = context.ContentWidth - tableWidth;
        return table.Properties.Alignment switch
        {
            TextAlignment.Center => contentLeft + Math.Max(0, slack / 2),
            TextAlignment.Right => contentLeft + Math.Max(0, slack),
            _ => contentLeft + indent
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
                    // Use the row's resolved height (already accounts for trHeight and natural
                    // content). Falling back to the cell's natural height here ignored trHeight
                    // and pushed bottom-aligned content to the top of the row.
                    cellHeight = rowHeights[rowIndex];
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

        // Trailing rows with nothing visible in them never force a page break: Word absorbs a
        // trailing empty spacer row (a letter template commonly ends its layout table with an
        // empty fixed-height row) into the bottom margin instead of starting a page that
        // renders nothing but would still count toward the page total.
        var lastVisibleRow = table.Rows.Count - 1;
        while (lastVisibleRow >= 0 && !RowHasVisibleContent(table.Rows[lastVisibleRow]))
        {
            lastVisibleRow--;
        }

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var rowHeight = rowHeights[rowIndex];

            var yBefore = context.CurrentY;
            if (rowIndex <= lastVisibleRow)
            {
                EnsureSpaceFor(rowHeight);
            }

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

    static bool RowHasVisibleContent(TableRow row)
    {
        foreach (var cell in row.Cells)
        {
            if (!string.IsNullOrEmpty(cell.Properties.BackgroundColorHex))
            {
                return true;
            }

            foreach (var element in cell.Content)
            {
                if (element is ParagraphElement paragraph)
                {
                    foreach (var run in paragraph.Runs)
                    {
                        if (run.InlineImageData != null || !string.IsNullOrWhiteSpace(run.Text))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    // Nested tables, images, content controls, form fields — all draw something.
                    return true;
                }
            }
        }

        return false;
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

        // Measure content height for vertical alignment. This must match the cell height that
        // TableHeightCalculator allocates: paragraph heights (which include their full
        // spacing-after) plus the contextual-spacing collapse below.
        // Top alignment — the default for the vast majority of cells — never reads the measured
        // height (its offset is always 0), so the whole measurement pass is skipped for it.
        float contentHeight = 0;
        if (cell.Properties.VerticalAlignment != CellVerticalAlignment.Top)
        {
            string? previousStyleId = null;
            var previousContextual = false;
            var previousAfter = 0f;
            foreach (var element in cell.Content)
            {
                var para = element switch
                {
                    ParagraphElement paragraph => paragraph,
                    ContentControlElement {CellParagraph: { } cellParagraph} => cellParagraph,
                    _ => null
                };

                if (para != null)
                {
                    // Measured at exactly the width RenderParagraphInBounds lays out at (it
                    // subtracts the paragraph's own indents internally and draws list markers
                    // into the hanging area — no extra marker inset applies).
                    contentHeight += Measurer.MeasureParagraphHeightWithWidth(para, contentWidth);

                    // Mirror RenderParagraphInBounds' contextual collapse: a contextual paragraph
                    // that follows a same-style contextual one removes the gap between them (its
                    // own spacing-before and the previous paragraph's spacing-after). The measured
                    // per-paragraph heights include both, so subtract them here — otherwise the
                    // centred/bottom offset treats the content as taller than it renders and sits
                    // too high.
                    var props = para.Properties;
                    if (props.ContextualSpacing && previousContextual &&
                        props.StyleId == previousStyleId)
                    {
                        contentHeight -= (float) props.SpacingBeforePoints + previousAfter;
                    }

                    previousStyleId = props.StyleId;
                    previousContextual = props.ContextualSpacing;
                    previousAfter = ParagraphHasVisibleContent(para) ? (float) props.SpacingAfterPoints : 0f;
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
                    previousStyleId = null;
                    previousContextual = false;
                    previousAfter = 0f;
                }
            }

            // The trailing after-spacing counts as content space in full — it stacks on the
            // cell's bottom margin (mirrors TableHeightCalculator, measured on
            // table_default_style), so nothing is subtracted here.
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

        // Each cell lays out independently: Word applies the first paragraph's spacing-before and
        // never collapses spacing across a cell boundary. Reset the cross-paragraph spacing state so
        // a previous cell's trailing paragraph can't collapse against this cell's first one.
        context.LastParagraphSpacingAfterPoints = 0;
        context.LastParagraphHadContextualSpacing = false;
        context.LastParagraphStyleId = null;

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
            else if (element is TableElement nested)
            {
                if (nested.Properties.IsFloating)
                {
                    // Floating nested tables (w:tblpPr) typically reach this branch only when
                    // the parser couldn't lift them to body level (e.g. inside header/footer
                    // content); the floating render saves and restores the cursor itself.
                    RenderTable(nested);
                }
                else
                {
                    // Non-floating nested tables: constrain the layout pipeline to the cell's
                    // content area so column widths size against the cell, not the page column.
                    using (context.PushContentContainer(contentX, contentWidth))
                    {
                        RenderTable(nested);
                    }
                }
            }
        }

        context.CurrentY = savedY;
    }

    // A paragraph contributes its spacing-after only when it lays out at least one real line —
    // mirrors the text engines' empty-paragraph handling, which skips spacing-after for marks
    // that carry no visible content.
    static bool ParagraphHasVisibleContent(ParagraphElement paragraph)
    {
        foreach (var run in paragraph.Runs)
        {
            if (run is {IsTab: false, InlineImageData: null} && !string.IsNullOrEmpty(run.Text))
            {
                return true;
            }

            if (run.InlineImageData != null)
            {
                return true;
            }
        }

        return false;
    }
}
