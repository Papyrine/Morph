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

    // Track whether the current page was started by a section break (a new page setup):
    // Word keeps a paragraph's spacing-before at the top of such pages in every mode.
    bool currentPageFromSectionBreak;
    PdfPage? currentPage;
    bool hasSignificantContentOnCurrentPage;
    bool currentPageFromExplicitBreak;

    /// <summary>
    /// When set, rendering stops once every page in the range is complete: layout is strictly
    /// forward, so once the page in progress is past <see cref="PageRange.End"/> everything
    /// that follows lands on pages <c>TrimPages</c> deletes anyway. Requesting page 1 of a
    /// 500-page document used to lay out and draw all 500.
    /// </summary>
    public PageRange? Pages { get; init; }

    /// <summary>Receives notices about elements the backend couldn't render. Set from
    /// <see cref="ExportOptions.OnWarning"/>.</summary>
    public Action<ExportWarning>? OnWarning { get; init; }

    /// <summary>
    /// When true, WordArt is embedded as an image rendered by an optional raster backend; when
    /// false (or no backend is loadable) it falls back to plain text. Mirrors
    /// <see cref="PdfExportOptions.RasterizeWordArt"/>.
    /// </summary>
    public bool RasterizeWordArt { get; init; }

    public PdfPageRenderer(PdfRenderContext context) : base(context)
    {
        this.context = context;
        textEngine = new(context)
        {
            RequestNewPage = () =>
            {
                // Flow into the next column of a multi-column section before spilling to a new
                // page — mirrors EnsureSpaceFor in the shared engine. For single-column sections
                // MoveToNextColumn returns false and this is an ordinary page break.
                if (!context.MoveToNextColumn())
                {
                    FinishCurrentPage();
                    StartNewPage();
                }
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
            // pagesAdded counts pages started, so once it passes the requested range's end the
            // page in progress (and everything after it) is guaranteed to be trimmed.
            if (Pages is { } range && pagesAdded > range.End)
            {
                break;
            }

            var element = elements[index];

            // Front-of-text shapes take the same page-advance as behind-text ones: their
            // anchor paragraph's content dictates the page, and when that content is about
            // to break to the next page the shape must follow it (resumes/10's accent
            // circle is anchored to a continuous-section paragraph that overflows).
            if (element is FloatingShapeElement anchoredShape)
            {
                AdvanceToBackgroundsTargetPage(elements, index);
                RenderBackgroundShape(anchoredShape);
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

        // Footnotes/endnotes as a document-end section, matching the raster backends: Word pins
        // footnotes to the bottom of the citing page, which needs page-level reservation in the
        // layout pass (not wired); listing them at document end keeps the content from being lost.
        RenderNotesAppendix(document);

        FinishCurrentPage();
        RemoveBlankTrailingPage();
        return pagesAdded;
    }

    void RenderNotesAppendix(ParsedDocument document)
    {
        // Footnote/endnote ids 0 and -1 are Word's "separator" / "continuation separator" stubs —
        // skip them so the appendix only contains user-authored notes.
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
                new()
                {
                    Text = heading,
                    Properties = new()
                    {
                        Bold = true,
                        FontSizePoints = 12
                    }
                }
            ],
            Properties = new()
            {
                SpacingBeforePoints = 12,
                SpacingAfterPoints = 6
            }
        };
        RenderParagraph(headingParagraph, nextElement: null);

        for (var noteIndex = 0; noteIndex < entries.Count; noteIndex++)
        {
            var (_, text) = entries[noteIndex];
            var noteParagraph = new ParagraphElement
            {
                Runs =
                [
                    // Sequential display number, matching the citation marks (footnotes.xml
                    // ids start at 2; Word shows 1, 2, 3...).
                    new()
                    {
                        Text = $"{noteIndex + 1}. ",
                        Properties = new()
                        {
                            Bold = true,
                            FontSizePoints = 10
                        }
                    },
                    new()
                    {
                        Text = text,
                        Properties = new()
                        {
                            FontSizePoints = 10
                        }
                    }
                ],
                Properties = new()
                {
                    SpacingAfterPoints = 4
                }
            };
            RenderParagraph(noteParagraph, nextElement: null);
        }
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
                    currentPageFromSectionBreak = true;
                }

                // Restart line numbering for the new section (w:restart="newSection"); the shared
                // SectionBreakHandler does this for the raster backends, but the PDF backend drives
                // sections itself. No-op unless the section's lnNumType restarts per section/page.
                context.ResetLineNumbersForSection();

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
            case PositionedFrameElement frame:
                RenderPositionedFrame(frame);
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
                RenderWordArtBlock(wordArt);
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
            case FloatingWordArtElement floatingWordArt:
                RenderFloatingWordArt(floatingWordArt);
                break;
            case FloatingShapeElement floatingShape:
                // FRONT-of-text shapes draw here over the content painted so far
                // (newsletters/08's cover photo is a front-anchored blip-filled freeform;
                // resumes/10's accent circle a front-anchored solid custGeom); behind-text
                // ones render from the pre-scan at page start.
                RenderBackgroundShape(floatingShape);
                break;
            case InkElement:
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

    // Inline WordArt is embedded as an image rendered by an optional raster backend (full glyph
    // warps/effects); when none is available it falls back to plain text (no warps). Either way the
    // block must still occupy the shape's declared height so pagination lines up with Word and the
    // raster backends — without this a tall section of WordArt collapses to a few text lines and the
    // pages Word spreads them across are lost.
    void RenderWordArtBlock(WordArtElement wordArt)
    {
        var height = (float) wordArt.HeightPoints;
        if (height > 0)
        {
            EnsureSpaceFor(height);
        }

        var startY = context.CurrentY;

        // An unwarped pseudo-WordArt is Word's inline text box: stroke its a:ln frame
        // under the text (business/06's LOGO box).
        if (wordArt is { BoxLineColorHex: { } boxLine, BoxLineWidthPoints: > 0 })
        {
            var boxRgb = PdfRenderContext.ParseColor(boxLine);
            var boxColor = XColor.FromArgb(
                (int) Math.Round(Math.Clamp(wordArt.BoxLineAlpha, 0, 1) * 255), boxRgb.R, boxRgb.G, boxRgb.B);
            Graphics.DrawRectangle(
                new XPen(boxColor, Math.Max(0.4, wordArt.BoxLineWidthPoints)),
                context.ContentLeft, startY, wordArt.WidthPoints, wordArt.HeightPoints);
        }

        if (!TryEmbedWordArt(wordArt, context.ContentLeft, startY, wordArt.WidthPoints, wordArt.HeightPoints))
        {
            RenderTextAsParagraph(wordArt.Text);
        }

        context.CurrentY = Math.Max(context.CurrentY, startY + height);
    }

    // Floating WordArt is positioned out of flow (no CurrentY advance). It renders only when a
    // raster backend produced the image; otherwise it's dropped with a warning — the previous
    // behaviour for every floating WordArt in the PDF backend.
    protected override void RenderFloatingWordArt(FloatingWordArtElement wordArt)
    {
        if (HasOutput)
        {
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

            if (TryEmbedWordArt(wordArt, bounds.X, bounds.Y, wordArt.WidthPoints, wordArt.HeightPoints))
            {
                hasSignificantContentOnCurrentPage = true;
                return;
            }
        }

        OnWarning?.Invoke(new(WarningKind.UnsupportedElement,
            $"{nameof(FloatingWordArtElement)} is not rendered by the PDF backend and was dropped."));
    }

    // The raster user space is 72 DPI (points == pixels); render WordArt higher so the embedded
    // image stays crisp when the PDF is zoomed. WordArt boxes are small, so the PNG stays tiny.
    const int wordArtRasterDpi = 300;

    IWordArtRasterizer? wordArtRasterizer;
    bool wordArtRasterizerResolved;
    WordArtRasterOptions? wordArtRasterOptions;

    // Resolves the optional raster backend once, honouring the RasterizeWordArt switch.
    IWordArtRasterizer? GetWordArtRasterizer()
    {
        if (!RasterizeWordArt)
        {
            return null;
        }

        if (!wordArtRasterizerResolved)
        {
            wordArtRasterizer = WordArtRasterizerFactory.TryGet();
            wordArtRasterizerResolved = true;
        }

        return wordArtRasterizer;
    }

    // Rasterizes the WordArt visual to a transparent PNG and draws it into the given box (points).
    // Returns false — so the caller can fall back to text — when no backend is available, the
    // backend produced nothing, or a draw failed.
    bool TryEmbedWordArt(IWordArtVisual visual, double xPoints, double yPoints, double widthPoints, double heightPoints)
    {
        if (!HasOutput)
        {
            return false;
        }

        var rasterizer = GetWordArtRasterizer();
        if (rasterizer == null)
        {
            return false;
        }

        wordArtRasterOptions ??= new()
        {
            Dpi = wordArtRasterDpi,
            FontWidthScale = context.FontWidthScale,
            FontFallback = context.FontFallback,
            FontDirectory = context.FontDirectory,
            Deterministic = true
        };

        byte[]? png;
        try
        {
            png = rasterizer.Render(visual, wordArtRasterOptions);
        }
        catch (Exception exception)
        {
            OnWarning?.Invoke(new(WarningKind.UnsupportedElement,
                $"WordArt could not be rasterized for the PDF and fell back to text: {exception.Message}"));
            return false;
        }

        if (png == null)
        {
            return false;
        }

        try
        {
            // The PNG is the box surrounded by WordArtRasterPage.Padding on every side (so warp
            // overflow isn't clipped). Shift the draw origin back by that padding and grow the
            // rectangle to match, so the box region still lands at (xPoints, yPoints) and the
            // overflow spills onto the surrounding page — mirroring the raster backends.
            var pad = WordArtRasterPage.Padding(visual);
            var image = context.GetImage(png);
            Graphics.DrawImage(image, xPoints - pad, yPoints - pad, widthPoints + 2 * pad, heightPoints + 2 * pad);
            return true;
        }
        catch (Exception exception)
        {
            OnWarning?.Invoke(new(WarningKind.ImageRenderingFailed,
                $"Rasterized WordArt could not be embedded in the PDF and was dropped: {exception.Message}"));
            return false;
        }
    }

    protected override void RenderFloatingTextBox(FloatingTextBoxElement textBox)
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

        // Rotate the whole box — chrome and text content — around its centre, matching the
        // raster backends' canvas rotation (labels/06's "ADMIT ONE" edge captions at 90°/270°).
        var rotationState = default(XGraphicsState);
        if (Math.Abs(textBox.RotationDegrees) > 0.01)
        {
            rotationState = Graphics.Save();
            Graphics.RotateAtTransform(
                textBox.RotationDegrees,
                new(bounds.X + bounds.PixelWidth / 2, bounds.Y + bounds.PixelHeight / 2));
        }

        // The shape's chrome behind the text: fill and a:ln outline, following the shape's
        // geometry when it is richer than a rectangle (roundRect ticket outlines, plaque frames).
        var geometryPath = BuildTextBoxPath(textBox, bounds.X, bounds.Y, bounds.PixelWidth, bounds.PixelHeight);
        if (textBox.BackgroundColorHex != null)
        {
            var brush = new XSolidBrush(PdfRenderContext.ParseColor(textBox.BackgroundColorHex));
            if (geometryPath != null)
            {
                Graphics.DrawPath(brush, geometryPath);
            }
            else
            {
                Graphics.DrawRectangle(brush, bounds.X, bounds.Y, bounds.PixelWidth, bounds.PixelHeight);
            }
        }

        if (textBox.LineColorHex != null && textBox.LineWidthPoints > 0)
        {
            var textBoxStrokeRgb = PdfRenderContext.ParseColor(textBox.LineColorHex);
            var pen = context.GetPen(
                XColor.FromArgb((int) Math.Round(Math.Clamp(textBox.LineAlpha, 0, 1) * 255), textBoxStrokeRgb.R, textBoxStrokeRgb.G, textBoxStrokeRgb.B),
                textBox.LineWidthPoints);
            if (geometryPath != null)
            {
                Graphics.DrawPath(pen, geometryPath);
            }
            else
            {
                Graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.PixelWidth, bounds.PixelHeight);
            }
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

        if (rotationState != null)
        {
            Graphics.Restore(rotationState);
        }
    }

    /// <summary>
    /// The text box's <see cref="FloatingTextBoxElement.Subpaths"/> contours scaled into its box,
    /// or null for plain rectangles. Alternate (even-odd) fill keeps ring geometry hollow.
    /// </summary>
    static XGraphicsPath? BuildTextBoxPath(FloatingTextBoxElement textBox, double x, double y, double width, double height)
    {
        if (textBox.Subpaths == null)
        {
            return null;
        }

        var path = new XGraphicsPath {FillMode = XFillMode.Alternate};
        foreach (var contour in textBox.Subpaths)
        {
            if (contour.Count < 3)
            {
                continue;
            }

            var points = new XPoint[contour.Count];
            for (var index = 0; index < contour.Count; index++)
            {
                var (pointX, pointY) = contour[index];
                points[index] = new(x + pointX * width, y + pointY * height);
            }

            path.StartFigure();
            path.AddPolygon(points);
            path.CloseFigure();
        }

        return path;
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
        currentPageFromSectionBreak = false;
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

    XPen EdgePen(BorderEdge edge) =>
        context.GetPen(PdfRenderContext.ParseColor(edge.ColorHex ?? "000000"), Math.Max(0.5, edge.WidthPoints));

    // ---- Drawing primitives required by PageRendererBase ----

    protected override void RenderParagraph(ParagraphElement paragraph, DocumentElement? nextElement = null)
    {
        // Substitute live page numbers for any PAGE/NUMPAGES/SECTIONPAGES field before measuring.
        paragraph = ResolveParagraphPageFields(paragraph);

        var hasContent = false;
        var isEmpty = paragraph.Runs.Count == 0;
        foreach (var run in paragraph.Runs)
        {
            if (run.InlineImageData != null || run.InlineShapeGroup != null || !string.IsNullOrWhiteSpace(run.Text))
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

        // Float wrap: a paragraph starting beside a wrap-enabled floating image lays out inside
        // the widest free band next to it; a wrapTopAndBottom float advances Y below itself.
        var (bandX, bandWidth, bandY, bandConstrained) = context.ResolveFlowBand(context.CurrentY);
        if (bandY > context.CurrentY)
        {
            context.CurrentY = bandY;
        }

        using (bandConstrained ? context.PushContentContainer(bandX, bandWidth) : null)
        {
            // KeepNext / KeepLines with Word's abandonment guards, mirroring the raster
            // renderers: no push when already at the top of the page (pushing again cannot
            // help) and no push when the kept content cannot fit a fresh column either.
            if (paragraph.Properties.KeepNext && nextElement != null && !isEmpty)
            {
                var nextHeight = MeasureElementHeight(nextElement, PdfTextEngine.SpacingAfter(paragraph));
                if (nextHeight > 0)
                {
                    var combinedHeight = textEngine.MeasureFlowHeight(paragraph, context.ContentWidth, context.LastParagraphSpacingAfterPoints) + nextHeight;
                    if (!context.HasSpaceFor(combinedHeight) &&
                        combinedHeight <= context.ContentHeight &&
                        context.CurrentY > context.ContentTop)
                    {
                        AdvanceToNextColumnOrPage();
                    }
                }
            }

            if (paragraph.Properties.KeepLines && !isEmpty)
            {
                var height = textEngine.MeasureFlowHeight(paragraph, context.ContentWidth, context.LastParagraphSpacingAfterPoints);
                if (!context.HasSpaceFor(height) &&
                    height <= context.ContentHeight &&
                    context.CurrentY > context.ContentTop)
                {
                    AdvanceToNextColumnOrPage();
                }
            }

            context.SuppressPageTopSpacingBefore = ShouldSuppressPageTopSpacingBefore();
            textEngine.Render(paragraph, nextElement);
        }

        if (hasContent)
        {
            hasSignificantContentOnCurrentPage = true;
        }
    }

    // Height of the element a KeepNext paragraph must share its page with, charged the way the
    // flow will charge it (a following paragraph collapses its spacing-before against this
    // paragraph's spacing-after). Tables return 0 (keep-before-table stays inert in the PDF
    // backend until it has a table pre-measure).
    protected override float MeasureHeaderFooterHeight(HeaderFooterContent content)
    {
        var total = 0f;
        foreach (var element in content.Elements)
        {
            // Nothing precedes the first element, so there is no prior spacing-after to collapse.
            total += MeasureElementHeight(element, 0);
        }

        return total;
    }

    float MeasureElementHeight(DocumentElement element, double previousSpacingAfter) =>
        element switch
        {
            ParagraphElement para => textEngine.MeasureFlowHeight(para, context.ContentWidth, previousSpacingAfter),
            ImageElement img => (float) img.HeightPoints,
            _ => 0
        };

    // Word does not apply a body paragraph's spacing-before at the top of a page reached by an
    // automatic break; compatibilityMode 15 also drops it after explicit page breaks, while a
    // section break (a new page setup) and the document's first page keep it. Column tops are
    // left unchanged (Word drops there too on automatic flow, but Morph's column handling is
    // measured separately). See page_counts.md, pass 4.
    bool ShouldSuppressPageTopSpacingBefore()
    {
        if (pagesAdded <= 1 ||
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
        if (!HasOutput)
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

        // Shapes are typically full-page backgrounds, so column/character anchors fall back to the
        // page margin (matches the Skia/ImageSharp backends).
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
        var x = bounds.PixelX;
        var y = bounds.PixelY;
        var shapeWidth = bounds.PixelWidth;
        var shapeHeight = bounds.PixelHeight;

        if (shape.ImageData != null)
        {
            // Word shows the picture through the shape's geometry (a circular profile photo is
            // an ellipse with a blip fill), not as a bare rectangle. Page-space clip, so rotated
            // ellipses stay unclipped; BuildShapePath already bakes rotation into contours.
            var clipGeometry = shape.Subpaths != null ||
                               (shape.Preset == PresetShape.Ellipse && shape.RotationDegrees == 0);
            if (clipGeometry)
            {
                var state = Graphics.Save();
                XGraphicsPath clipPath;
                if (shape.Subpaths != null)
                {
                    clipPath = BuildShapePath(shape, x, y, shapeWidth, shapeHeight);
                }
                else
                {
                    clipPath = new();
                    clipPath.AddEllipse(x, y, shapeWidth, shapeHeight);
                }

                Graphics.IntersectClip(clipPath);
                DrawRaster(shape.ImageData, shape.ImageContentType, null, null, x, y, shapeWidth, shapeHeight);
                Graphics.Restore(state);
            }
            else
            {
                DrawRaster(shape.ImageData, shape.ImageContentType, null, null, x, y, shapeWidth, shapeHeight);
            }
        }
        else if (shape.Gradient is { } gradient)
        {
            FillShape(shape, x, y, shapeWidth, shapeHeight, BuildGradientBrush(gradient, x, y, shapeWidth, shapeHeight));
        }
        else if (shape.FillColorHex != null)
        {
            var rgb = PdfRenderContext.ParseColor(shape.FillColorHex);
            var color = XColor.FromArgb((int) Math.Round(Math.Clamp(shape.FillAlpha, 0, 1) * 255), rgb.R, rgb.G, rgb.B);
            FillShape(shape, x, y, shapeWidth, shapeHeight, new XSolidBrush(color));
        }

        if (shape is {LineColorHex: { } lineColor, LineWidthPoints: { } lineWidth and > 0})
        {
            var strokeRgb = PdfRenderContext.ParseColor(lineColor);
            var strokeColor = XColor.FromArgb(
                (int) Math.Round(Math.Clamp(shape.LineAlpha, 0, 1) * 255), strokeRgb.R, strokeRgb.G, strokeRgb.B);
            var pen = new XPen(strokeColor, Math.Max(0.4, lineWidth));
            if (shape.LineDashPattern is { } dashPattern)
            {
                // XPen.DashPattern is in multiples of the pen width — the model's convention.
                pen.DashPattern = dashPattern.Select(_ => (double) _).ToArray();
            }

            StrokeShape(shape, x, y, shapeWidth, shapeHeight, pen);
        }
    }

    void FillShape(FloatingShapeElement shape, double x, double y, double width, double height, XBrush brush)
    {
        if (shape.Subpaths != null)
        {
            Graphics.DrawPath(brush, BuildShapePath(shape, x, y, width, height));
            return;
        }

        // Preset rects/ellipses rotate about their box centre like any other xfrm
        // (business-plans/08's accent rule is a 90°-rotated thin rect).
        var rotated = shape.RotationDegrees != 0;
        var state = rotated ? Graphics.Save() : null;
        if (rotated)
        {
            Graphics.RotateAtTransform(shape.RotationDegrees, new(x + width / 2, y + height / 2));
        }

        if (shape.Preset == PresetShape.Ellipse)
        {
            Graphics.DrawEllipse(brush, x, y, width, height);
        }
        else
        {
            Graphics.DrawRectangle(brush, x, y, width, height);
        }

        if (rotated)
        {
            Graphics.Restore(state!);
        }
    }

    void StrokeShape(FloatingShapeElement shape, double x, double y, double width, double height, XPen pen)
    {
        if (shape.Subpaths != null)
        {
            Graphics.DrawPath(pen, BuildShapePath(shape, x, y, width, height));
            return;
        }

        // Rotated preset outline: turn about the box centre (matches FillShape).
        var rotated = shape.RotationDegrees != 0;
        var state = rotated ? Graphics.Save() : null;
        if (rotated)
        {
            Graphics.RotateAtTransform(shape.RotationDegrees, new(x + width / 2, y + height / 2));
        }

        if (shape.Preset == PresetShape.Ellipse)
        {
            Graphics.DrawEllipse(pen, x, y, width, height);
        }
        else
        {
            Graphics.DrawRectangle(pen, x, y, width, height);
        }

        if (rotated)
        {
            Graphics.Restore(state!);
        }
    }

    // Builds a path from custom geometry: each sub-path is its own closed contour, filled with
    // nonzero winding so oppositely-wound nested contours read as holes (matching DrawingML's
    // default custGeom fill) rather than fusing into one polygon.
    static XGraphicsPath BuildShapePath(FloatingShapeElement shape, double x, double y, double width, double height)
    {
        var path = new XGraphicsPath
        {
            FillMode = XFillMode.Winding
        };

        // Flip in the unit square, scale into the bounding box, then rotate around its centre —
        // matching the Skia/ImageSharp path transform so rotated custom geometry lines up.
        var centerX = x + width / 2;
        var centerY = y + height / 2;
        var radians = shape.RotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        foreach (var contour in shape.Subpaths!)
        {
            var points = new XPoint[contour.Count];
            for (var i = 0; i < contour.Count; i++)
            {
                var (pointX, pointY) = contour[i];
                var unitX = shape.FlipHorizontal ? 1 - pointX : pointX;
                var unitY = shape.FlipVertical ? 1 - pointY : pointY;
                var absoluteX = x + unitX * width;
                var absoluteY = y + unitY * height;
                if (shape.RotationDegrees != 0)
                {
                    var deltaX = absoluteX - centerX;
                    var deltaY = absoluteY - centerY;
                    absoluteX = centerX + deltaX * cos - deltaY * sin;
                    absoluteY = centerY + deltaX * sin + deltaY * cos;
                }

                points[i] = new(absoluteX, absoluteY);
            }

            path.AddPolygon(points);
        }

        return path;
    }

    // Linear gradient mirroring the Skia/ImageSharp backends: angle 0° points along +X, clockwise
    // positive (OOXML a:lin/@ang), projected onto the bounding box as start/end points.
    static XLinearGradientBrush BuildGradientBrush(GradientFill gradient, double x, double y, double width, double height)
    {
        var radians = gradient.DirectionDegrees * Math.PI / 180.0;
        var directionX = Math.Cos(radians);
        var directionY = Math.Sin(radians);
        var centerX = x + width / 2;
        var centerY = y + height / 2;
        var halfDiagonal = Math.Sqrt(width * width + height * height) / 2;
        var start = new XPoint(centerX - directionX * halfDiagonal, centerY - directionY * halfDiagonal);
        var end = new XPoint(centerX + directionX * halfDiagonal, centerY + directionY * halfDiagonal);
        return new(start, end, PdfRenderContext.ParseColor(gradient.StartColorHex), PdfRenderContext.ParseColor(gradient.EndColorHex));
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

        // Wrap-enabled floats reserve their footprint so following flow text lays out beside
        // them instead of over them.
        context.RegisterFloatExclusion(image, bounds.X, bounds.Y, (float) width, (float) height);

        if (Math.Abs(image.RotationDegrees) > 0.01 || image.FlipHorizontal || image.FlipVertical)
        {
            // a:xfrm transforms around the image centre, matching DrawBlockImage and the raster
            // backends: @rot (e.g. letters/13's vertical banner at 90 degrees), then @flipH/@flipV
            // (e.g. agendas-minutes/02's mirrored wave artwork).
            var centerX = bounds.X + bounds.PixelWidth / 2;
            var centerY = bounds.Y + bounds.PixelHeight / 2;
            var state = Graphics.Save();
            if (Math.Abs(image.RotationDegrees) > 0.01)
            {
                Graphics.RotateAtTransform(image.RotationDegrees, new(centerX, centerY));
            }

            if (image.FlipHorizontal || image.FlipVertical)
            {
                Graphics.TranslateTransform(centerX, centerY);
                Graphics.ScaleTransform(image.FlipHorizontal ? -1 : 1, image.FlipVertical ? -1 : 1);
                Graphics.TranslateTransform(-centerX, -centerY);
            }

            DrawRaster(image.ImageData, image.ContentType, image.RasterFallbackData, image.RasterFallbackContentType, bounds.X, bounds.Y, bounds.PixelWidth, bounds.PixelHeight, image.Crop);
            Graphics.Restore(state);
        }
        else if (image.ClipToEllipse || image.ClipSubpaths != null)
        {
            // pic:spPr geometry crop (round photos, custGeom cuts), page-space clip.
            var state = Graphics.Save();
            var clipPath = new XGraphicsPath();
            if (image.ClipToEllipse)
            {
                clipPath.AddEllipse(bounds.X, bounds.Y, bounds.PixelWidth, bounds.PixelHeight);
            }
            else
            {
                foreach (var contour in image.ClipSubpaths!)
                {
                    var points = new XPoint[contour.Count];
                    for (var pointIndex = 0; pointIndex < contour.Count; pointIndex++)
                    {
                        var (unitX, unitY) = contour[pointIndex];
                        points[pointIndex] = new(
                            bounds.X + unitX * bounds.PixelWidth,
                            bounds.Y + unitY * bounds.PixelHeight);
                    }

                    clipPath.AddPolygon(points);
                }
            }

            Graphics.IntersectClip(clipPath);
            DrawRaster(image.ImageData, image.ContentType, image.RasterFallbackData, image.RasterFallbackContentType, bounds.X, bounds.Y, bounds.PixelWidth, bounds.PixelHeight, image.Crop);
            Graphics.Restore(state);
        }
        else
        {
            DrawRaster(image.ImageData, image.ContentType, image.RasterFallbackData, image.RasterFallbackContentType, bounds.X, bounds.Y, bounds.PixelWidth, bounds.PixelHeight, image.Crop);
        }
    }

    protected override void DrawBlockImage(byte[] imageData, string? contentType, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, bool flipHorizontal, bool flipVertical, ImageCrop? crop, BlipColorEffect colorEffect, string? duotoneColorHex, string? duotoneLightColorHex)
    {
        if (!HasOutput)
        {
            return;
        }

        if (Math.Abs(rotation) > 0.01 || flipHorizontal || flipVertical)
        {
            var centerX = pixelX + pixelWidth / 2;
            var centerY = pixelY + pixelHeight / 2;
            var state = Graphics.Save();
            if (Math.Abs(rotation) > 0.01)
            {
                Graphics.RotateAtTransform(rotation, new(centerX, centerY));
            }

            if (flipHorizontal || flipVertical)
            {
                // a:xfrm/@flipH/@flipV: mirror around the image centre inside the rotated frame.
                Graphics.TranslateTransform(centerX, centerY);
                Graphics.ScaleTransform(flipHorizontal ? -1 : 1, flipVertical ? -1 : 1);
                Graphics.TranslateTransform(-centerX, -centerY);
            }

            DrawRaster(imageData, contentType, null, null, pixelX, pixelY, pixelWidth, pixelHeight, crop);
            Graphics.Restore(state);
        }
        else
        {
            DrawRaster(imageData, contentType, null, null, pixelX, pixelY, pixelWidth, pixelHeight, crop);
        }
    }

    protected override bool CanRenderContentType(string? contentType) => contentType != "image/svg+xml";

    void DrawRaster(byte[] data, string? contentType, byte[]? fallbackData, string? fallbackContentType, double x, double y, double width, double height, ImageCrop? crop = null)
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

        try
        {
            var image = context.GetImage(data);
            if (crop is {IsCropped: true})
            {
                // a:srcRect crop: enlarge the whole image so its visible sub-rectangle covers the box,
                // then clip back to the box (PDFsharp has no source-rectangle API) — the same technique
                // as the shape-group path in PdfTextEngine.
                var (dx, dy, dw, dh) = crop.Expand(x, y, width, height);
                var state = Graphics.Save();
                Graphics.IntersectClip(new XRect(x, y, width, height));
                Graphics.DrawImage(image, dx, dy, dw, dh);
                Graphics.Restore(state);
            }
            else
            {
                Graphics.DrawImage(image, x, y, width, height);
            }
        }
        catch (Exception exception)
        {
            // PDFsharp's cross-platform build only decodes BMP/PNG/JPEG — a GIF (or other
            // unsupported format) used to crash the whole export here. Drop the image and
            // keep the document.
            OnWarning?.Invoke(new(WarningKind.ImageRenderingFailed,
                $"Image ({contentType ?? "unknown content type"}) could not be embedded in the PDF and was dropped: {exception.Message}"));
        }
    }
}
