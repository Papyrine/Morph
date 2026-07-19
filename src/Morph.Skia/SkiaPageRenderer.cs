/// <summary>
/// Renders document pages to PNG images.
/// </summary>
sealed class SkiaPageRenderer(SkiaRenderContext context) :
    PageRendererBase(context),
    IDisposable
{
    protected override IParagraphMeasurer Measurer => textRenderer;
    protected override bool HasOutput => currentCanvas != null;

    TextRenderer textRenderer = new(context);

    Action<Action<Stream>>? pageCallback;
    int pageCount;

    /// <summary>
    /// When true, pages are laid out and counted but not encoded to PNG or handed to the callback.
    /// Used by the gated counting pass that resolves NUMPAGES before the real render.
    /// </summary>
    public bool CountOnly { get; init; }

    SKBitmap? pendingPage;
    SKBitmap? currentPage;
    SKCanvas? currentCanvas;
    float headerHeight;
    IReadOnlyList<Watermark> watermarks = [];

    // Track whether meaningful content (text/images/tables) was rendered on current page
    // Used to detect and discard spurious blank trailing pages
    bool hasSignificantContentOnCurrentPage;

    // Track whether the current page was started due to an explicit break
    // (page break, section break) - such pages should not be discarded even if blank
    bool currentPageFromExplicitBreak;

    // Track whether the current page was started by a section break (a new page setup):
    // Word keeps a paragraph's spacing-before at the top of such pages in every mode.
    bool currentPageFromSectionBreak;

    // Pages started so far (incremented in StartNewPage; pageCount only counts flushed pages,
    // which lags behind while a finished page is still pending).
    int pagesStarted;

    /// <summary>
    /// Renders a parsed document, calling the callback for each page.
    /// Returns the total page count.
    /// </summary>
    public int RenderDocument(ParsedDocument document, Action<Action<Stream>> callback)
    {
        pageCallback = callback;

        header = document.Header;
        footer = document.Footer;
        firstPageHeader = document.FirstPageHeader;
        firstPageFooter = document.FirstPageFooter;
        evenPageHeader = document.EvenPageHeader;
        watermarks = document.Watermarks;
        evenPageFooter = document.EvenPageFooter;
        differentFirstPage = document.PageSettings.DifferentFirstPage;

        // Measure header and footer heights
        headerHeight = MeasureHeaderFooterHeight(header);
        footerHeight = MeasureHeaderFooterHeight(footer);

        // Adjust context for header/footer space
        context.SetHeaderFooterSpace(headerHeight, footerHeight);

        // Initialize line numbering
        context.InitializeLineNumbers();

        StartNewPage();

        var elements = document.Elements;
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];

            // Render background shapes on the current page only (not on every page).
            // Word anchors a behind-text drawing to the same page as its anchor paragraph.
            // If the next significant content won't fit on the current page (a page break is
            // imminent), the background should render on the next page — otherwise the page
            // ends up with two stacked backgrounds and the actual target page has none.
            if (element is FloatingShapeElement {BehindText: true} shape)
            {
                AdvanceToBackgroundsTargetPage(elements, i);
                RenderBackgroundShape(shape);
                continue;
            }

            // Behind-text floating images carry the same page-anchor semantics as background
            // shapes: when their parent paragraph contains no flow text and the next significant
            // element forces a page break, the image belongs on the next page (it's the
            // background for the *upcoming* content, not the current one).
            if (element is FloatingImageElement {BehindText: true} bgImage)
            {
                AdvanceToBackgroundsTargetPage(elements, i);
                RenderFloatingImage(bgImage);
                continue;
            }

            // Get next non-background element for KeepWithNext handling
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

        // Append footnotes and endnotes as a document-end section. Word draws footnotes at the
        // bottom of the page where the reference appears; that needs page-level reservation in
        // the layout pass (not currently wired). Until then we list them at document end so the
        // content isn't lost.
        RenderNotesAppendix(document);

        // Finish the last page
        FinishCurrentPage();

        // Remove any trailing blank page that was created by content overflow or section breaks.
        RemoveBlankTrailingPage();

        return pageCount;
    }

    // ReSharper disable once UnusedParameter.Local
    static float MeasureHeaderFooterHeight(HeaderFooterContent? content) =>
        // For now, return 0 to not adjust body content area based on header/footer.
        // Headers and footers render in their own areas (HeaderDistance/FooterDistance)
        // and shouldn't push body content in typical Word documents.
        // This matches Word's behavior where body content area is determined solely
        // by page margins, not by header/footer content size.
        0;

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
        RenderParagraph(headingParagraph);

        for (var noteIndex = 0; noteIndex < entries.Count; noteIndex++)
        {
            var (_, text) = entries[noteIndex];
            var noteParagraph = new ParagraphElement
            {
                Runs =
                [
                    new()
                    {
                        // Sequential display number, matching the citation marks (footnotes.xml
                        // ids start at 2; Word shows 1, 2, 3...).
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
                // Move to next column, or new page if no more columns
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
                // Render floating images immediately at their absolute positions
                // They don't affect text flow (no CurrentY advancement)
                RenderFloatingImage(floatingImage);
                hasSignificantContentOnCurrentPage = true;
                break;

            case FloatingTextBoxElement floatingTextBox:
                // Render floating text boxes at their absolute positions
                // They don't affect text flow (no CurrentY advancement)
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
                // Reset spacing tracking after table - tables don't participate in margin collapsing
                // so the next paragraph should get its full SpacingBefore
                context.LastParagraphSpacingAfterPoints = 0;
                context.LastParagraphHadContextualSpacing = false;
                context.LastParagraphStyleId = null;
                break;

            case WordArtElement wordArt:
                RenderWordArt(wordArt);
                hasSignificantContentOnCurrentPage = true;
                break;

            case FloatingWordArtElement floatingWordArt:
                // Render floating WordArt at absolute position
                // Doesn't affect text flow (no CurrentY advancement)
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
                // Behind-text shapes are handled in the RenderDocument pre-scan and rendered
                // at page start in StartNewPage. FRONT-of-text shapes have no other painter:
                // image-filled ones draw here over the content painted so far (newsletters/08's
                // cover photo is a front-anchored blip-filled freeform); solid front shapes
                // keep the long-standing drop — enabling them is a separate corpus-wide
                // experiment.
                if (floatingShape.ImageData != null)
                {
                    RenderBackgroundShape(floatingShape);
                }

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

    /// <summary>
    /// Measures the height of an element for pagination purposes.
    /// </summary>
    float MeasureElementHeight(DocumentElement element) =>
        element switch
        {
            ParagraphElement para => textRenderer.MeasureParagraphHeight(para),
            ImageElement img => (float) img.HeightPoints,
            TableElement table => MeasureTableHeight(table),
            _ => 0 // Other elements don't participate in KeepWithNext
        };

    protected override void RenderParagraph(ParagraphElement paragraph, DocumentElement? nextElement = null)
    {
        // Substitute live page numbers for any PAGE/NUMPAGES/SECTIONPAGES field before measuring,
        // so wrapping and drawing both use the resolved text.
        paragraph = ResolveParagraphPageFields(paragraph);

        // Check if this paragraph has significant content (actual text)
        var hasSignificantContent = paragraph.Runs.Any(_ => !string.IsNullOrWhiteSpace(_.Text));

        // Check if paragraph is completely empty (no runs at all)
        var isCompletelyEmpty = paragraph.Runs.Count == 0;

        // Handle PageBreakBefore - force a page break before this paragraph
        // But only if we're not already at the top of a page (to avoid blank pages)
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

        // Handle KeepWithNext (KeepNext) - keep this paragraph on the same page as the next element
        // This is commonly used for headings to prevent them from appearing alone at the bottom of a page
        if (paragraph.Properties.KeepNext &&
            nextElement != null &&
            !isCompletelyEmpty)
        {
            var nextHeight = MeasureElementHeight(nextElement);
            var combinedHeight = height + nextHeight;

            // If combined height won't fit in the current column, but both would fit in a fresh
            // one, advance to the next column (or page) before rendering this paragraph.
            if (!context.HasSpaceFor(combinedHeight) &&
                combinedHeight <= context.ContentHeight &&
                context.CurrentY > context.ContentTop)
            {
                AdvanceToNextColumnOrPage();
            }
        }

        // Handle KeepLines - keep all lines of this paragraph on the same page
        // If the paragraph doesn't fit on current page but would fit on a new page, move it
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

        // Handle WidowControl - prevent orphans (1 line stranded at bottom) and widows (1 line at top).
        // We can't currently break a paragraph in the middle, so the only enforceable case is the
        // orphan: when ≥1 line fits at the bottom of the current page but the rest would create a
        // single-line orphan there. Push the whole paragraph forward instead.
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

                // Orphan: 1 line fits at the bottom, ≥2 lines wrap to next page → push whole paragraph.
                // Widow: lines split such that only 1 wraps to next page → also push whole paragraph.
                if (fit == 1 || fit == lineHeights.Count - 1)
                {
                    AdvanceToNextColumnOrPage();
                }
            }
        }

        // For completely empty paragraphs (no runs), don't force page breaks
        // as these often appear at document end and cause spurious extra pages.
        // Paragraphs with whitespace-only runs are considered intentional spacing
        // and should still trigger page breaks normally to maintain layout.
        if (!isCompletelyEmpty)
        {
            EnsureSpaceFor(height);
        }

        // Render the paragraph
        if (currentCanvas != null)
        {
            context.SuppressPageTopSpacingBefore = ShouldSuppressPageTopSpacingBefore();
            textRenderer.RenderParagraph(currentCanvas, paragraph, nextElement);
        }

        // Track significant content for blank page removal
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

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SkiaRenderContext.ParseColor(hexColor),
            StrokeWidth = pixelStrokeWidth,
            IsAntialias = true
        };
        currentCanvas.DrawLine(pixelX1, pixelY, pixelX2, pixelY, paint);
    }

    protected override void DrawBlockImage(byte[] imageData, string? contentType, float pixelX, float pixelY, float pixelWidth, float pixelHeight, float rotation, bool flipHorizontal, bool flipVertical, ImageCrop? crop, BlipColorEffect colorEffect, string? duotoneColorHex, string? duotoneLightColorHex)
    {
        var destRect = new SKRect(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);
        DrawBlockImage(imageData, contentType, destRect, rotation, crop, colorEffect, flipHorizontal, flipVertical, duotoneColorHex, duotoneLightColorHex);
    }

    void DrawBlockImage(byte[] imageData, string? contentType, SKRect destRect, float rotation, ImageCrop? crop, BlipColorEffect colorEffect = BlipColorEffect.None, bool flipHorizontal = false, bool flipVertical = false, string? duotoneColorHex = null, string? duotoneLightColorHex = null)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var transformed = rotation != 0 || flipHorizontal || flipVertical;
        if (transformed)
        {
            currentCanvas.Save();
            if (rotation != 0)
            {
                currentCanvas.RotateDegrees(rotation, destRect.MidX, destRect.MidY);
            }

            if (flipHorizontal || flipVertical)
            {
                // a:xfrm/@flipH/@flipV: mirror around the image centre inside the rotated frame.
                currentCanvas.Scale(flipHorizontal ? -1 : 1, flipVertical ? -1 : 1, destRect.MidX, destRect.MidY);
            }
        }

        if (contentType == "image/svg+xml")
        {
            RenderSvgImage(imageData, destRect, crop);
        }
        else
        {
            var skImage = context.GetBitmap(imageData);
            if (skImage != null)
            {
                using var paint = BuildBlipColorEffectPaint(colorEffect, duotoneColorHex, duotoneLightColorHex);
                if (crop is {IsCropped: true})
                {
                    if (crop.HasPadding)
                    {
                        // Padding (negative srcRect): the image occupies Expand's sub-rectangle
                        // inside the frame — a source rect can't extend beyond the bitmap.
                        var (paddedX, paddedY, paddedWidth, paddedHeight) = crop.Expand(destRect.Left, destRect.Top, destRect.Width, destRect.Height);
                        currentCanvas.Save();
                        currentCanvas.ClipRect(destRect);
                        currentCanvas.DrawBitmap(skImage, new SKRect((float) paddedX, (float) paddedY, (float) (paddedX + paddedWidth), (float) (paddedY + paddedHeight)), paint);
                        currentCanvas.Restore();
                    }
                    else
                    {
                        var srcLeft = (float) (crop.Left * skImage.Width);
                        var srcTop = (float) (crop.Top * skImage.Height);
                        var srcRight = (float) ((1 - crop.Right) * skImage.Width);
                        var srcBottom = (float) ((1 - crop.Bottom) * skImage.Height);
                        currentCanvas.DrawBitmap(skImage, new(srcLeft, srcTop, srcRight, srcBottom), destRect, paint);
                    }
                }
                else
                {
                    currentCanvas.DrawBitmap(skImage, destRect, paint);
                }
            }
        }

        if (transformed)
        {
            currentCanvas.Restore();
        }
    }

    public static SKPaint? BuildBlipColorEffectPaint(BlipColorEffect effect, string? duotoneColorHex = null, string? duotoneLightColorHex = null)
    {
        // Standard ITU-R BT.601 luminance weights for grayscale conversion.
        const float lumR = 0.299f;
        const float lumG = 0.587f;
        const float lumB = 0.114f;

        // a:duotone maps luminance onto a dark→light ramp: out_c = dark_c + L·(light_c − dark_c).
        // As a colour matrix that is the luminance row scaled by (light_c − dark_c) plus a
        // dark_c bias (SkiaSharp's translate column is 0..1-normalized). Word's Recolor gallery
        // pairs a dark colour with white; letters/02 pairs black with a tinted accent instead.
        if (effect == BlipColorEffect.Duotone && (duotoneColorHex != null || duotoneLightColorHex != null))
        {
            var dark = SkiaRenderContext.ParseColor(duotoneColorHex ?? "000000");
            var light = SkiaRenderContext.ParseColor(duotoneLightColorHex ?? "FFFFFF");
            var darkRed = dark.Red / 255f;
            var darkGreen = dark.Green / 255f;
            var darkBlue = dark.Blue / 255f;
            var spanRed = light.Red / 255f - darkRed;
            var spanGreen = light.Green / 255f - darkGreen;
            var spanBlue = light.Blue / 255f - darkBlue;
            return new()
            {
                ColorFilter = SKColorFilter.CreateColorMatrix(
                [
                    lumR * spanRed, lumG * spanRed, lumB * spanRed, 0, darkRed,
                    lumR * spanGreen, lumG * spanGreen, lumB * spanGreen, 0, darkGreen,
                    lumR * spanBlue, lumG * spanBlue, lumB * spanBlue, 0, darkBlue,
                    0, 0, 0, 1, 0
                ])
            };
        }

        return effect switch
        {
            BlipColorEffect.Grayscale or BlipColorEffect.Duotone =>
                new()
                {
                    ColorFilter = SKColorFilter.CreateColorMatrix(
                    [
                        lumR, lumG, lumB, 0, 0,
                        lumR, lumG, lumB, 0, 0,
                        lumR, lumG, lumB, 0, 0,
                        0, 0, 0, 1, 0
                    ])
                },
            BlipColorEffect.Washout =>
                new()
                {
                    // Match Word's "Washout" picture preset: brightness +70%, contrast −50% — i.e.
                    // gain 0.50 + half-scale bias on each channel, producing a faded version of the
                    // image. The matrix translate column is 0..1-normalized in SkiaSharp, so the
                    // bias is 0.5 (a 255-scaled value saturates every channel to white).
                    ColorFilter = SKColorFilter.CreateColorMatrix(
                    [
                        0.5f, 0, 0, 0, 0.5f,
                        0, 0.5f, 0, 0, 0.5f,
                        0, 0, 0.5f, 0, 0.5f,
                        0, 0, 0, 1, 0
                    ])
                },
            _ => null
        };
    }

    void RenderSvgImage(byte[] svgData, SKRect destRect, ImageCrop? crop = null)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Preprocessing, parsing and rasterization are cached on the context — an SVG header
        // logo repeats on every page with a stable destination size. The raster draws to a
        // bitmap first because DrawPicture is unreliable on some canvases.
        var bitmap = context.GetSvgRaster(svgData, destRect.Width, destRect.Height, crop, originAdjusted: true);
        if (bitmap != null)
        {
            currentCanvas.DrawBitmap(bitmap, destRect.Left, destRect.Top);
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

        // Resolve through the bundled FontDirectory. SKTypeface.FromFamilyName only sees system
        // fonts, so a bundled WordArt face like "Impact" returned a non-rendering typeface in the
        // container (GetTextPath produced an empty outline) and the WordArt came out blank.
        var typeface = context.GetTypeface(wordArt.FontFamily, wordArt.Bold, wordArt.Italic);

        var pixelFontSize = context.PointsToPixels((float) wordArt.FontSizePoints);

        // Measure text to calculate scale
        using var measureFont = new SKFont(typeface, pixelFontSize);
        measureFont.MeasureText(wordArt.Text, out var textBounds);

        // Only shrink to fit; never enlarge past the explicit font size. The shape's
        // bounding box for arc/circle warps is sized for the curve, not the glyph cluster.
        var scaleX = textBounds.Width > 0 ? width / textBounds.Width : 1;
        var scaleY = textBounds.Height > 0 ? pixelHeight / textBounds.Height : 1;
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1f);

        if (TryRenderWordArtOnPath(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, wordArt.OutlineColorHex, wordArt.OutlineWidthPoints, x, y, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            context.CurrentY += height;
            return;
        }

        if (TryRenderWordArtPathWarp(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, x, y, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            context.CurrentY += height;
            return;
        }

        if (TryRenderWordArtEnvelope(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, x, y, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            context.CurrentY += height;
            return;
        }

        // Calculate centered position
        var scaledWidth = textBounds.Width * scale;
        var scaledHeight = textBounds.Height * scale;
        var textX = x + (width - scaledWidth) / 2;
        var textY = y + (pixelHeight + scaledHeight) / 2;

        // SkiaSharp 4 moved text size/typeface off SKPaint onto SKFont; one font, shared by
        // every draw below (all at the same scaled size). Matches the Antialias edging used
        // by the text-on-path WordArt helpers.
        using var font = new SKFont(typeface, pixelFontSize * scale)
        {
            Edging = SKFontEdging.Antialias
        };

        currentCanvas.Save();

        // Apply transform based on WordArt type
        ApplyWordArtTransform(wordArt.Transform, x, y, width, pixelHeight);

        // Draw shadow first if enabled
        if (wordArt.HasShadow)
        {
            using var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new(0, 0, 0, 80),
                Style = SKPaintStyle.Fill
            };
            currentCanvas.DrawText(wordArt.Text, textX + 3, textY + 3, font, shadowPaint);
        }

        // Draw glow if enabled
        if (wordArt.HasGlow)
        {
            using var glowPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new(255, 215, 0, 100), // Gold glow
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels(4),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3)
            };
            currentCanvas.DrawText(wordArt.Text, textX, textY, font, glowPaint);
        }

        // Draw outline if specified
        if (wordArt is {OutlineColorHex: not null, OutlineWidthPoints: > 0})
        {
            using var outlinePaint = new SKPaint
            {
                IsAntialias = true,
                Color = ParseColor(wordArt.OutlineColorHex),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels((float) wordArt.OutlineWidthPoints)
            };
            currentCanvas.DrawText(wordArt.Text, textX, textY, font, outlinePaint);
        }

        // Draw text fill
        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Color = wordArt.FillColorHex != null ? ParseColor(wordArt.FillColorHex) : SKColors.Black,
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawText(wordArt.Text, textX, textY, font, fillPaint);

        // Draw reflection if enabled
        if (wordArt.HasReflection)
        {
            currentCanvas.Save();
            currentCanvas.Scale(1, -0.5f, textX, textY + scaledHeight / 2);

            using var reflectionPaint = new SKPaint
            {
                IsAntialias = true,
                Color = fillPaint.Color.WithAlpha(60),
                Style = SKPaintStyle.Fill
            };
            currentCanvas.DrawText(wordArt.Text, textX, textY + scaledHeight * 2, font, reflectionPaint);
            currentCanvas.Restore();
        }

        currentCanvas.Restore();

        context.CurrentY += height;
    }

    protected override void RenderFloatingWordArt(FloatingWordArtElement wordArt)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Calculate absolute position based on anchor type
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

        // Resolve through the bundled FontDirectory. SKTypeface.FromFamilyName only sees system
        // fonts, so a bundled WordArt face like "Impact" returned a non-rendering typeface in the
        // container (GetTextPath produced an empty outline) and the WordArt came out blank.
        var typeface = context.GetTypeface(wordArt.FontFamily, wordArt.Bold, wordArt.Italic);

        var pixelFontSize = context.PointsToPixels((float) wordArt.FontSizePoints);

        // Measure text to calculate scale
        using var measureFont = new SKFont(typeface, pixelFontSize);
        measureFont.MeasureText(wordArt.Text, out var textBounds);

        // Only shrink to fit; never enlarge past the explicit font size — see note above.
        var scaleX = textBounds.Width > 0 ? width / textBounds.Width : 1;
        var scaleY = textBounds.Height > 0 ? pixelHeight / textBounds.Height : 1;
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1f);

        if (TryRenderWordArtOnPath(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, wordArt.OutlineColorHex, wordArt.OutlineWidthPoints, pixelX, pixelY, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            return;
        }

        if (TryRenderWordArtPathWarp(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, pixelX, pixelY, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            return;
        }

        if (TryRenderWordArtEnvelope(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, pixelX, pixelY, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            return;
        }

        // Calculate centered position
        var scaledWidth = textBounds.Width * scale;
        var scaledHeight = textBounds.Height * scale;
        var textX = pixelX + (width - scaledWidth) / 2;
        var textY = pixelY + (pixelHeight + scaledHeight) / 2;

        // SkiaSharp 4 moved text size/typeface off SKPaint onto SKFont; one font, shared by
        // every draw below (all at the same scaled size). Matches the Antialias edging used
        // by the text-on-path WordArt helpers.
        using var font = new SKFont(typeface, pixelFontSize * scale)
        {
            Edging = SKFontEdging.Antialias
        };

        currentCanvas.Save();

        // Apply transform based on WordArt type
        ApplyWordArtTransform(wordArt.Transform, pixelX, pixelY, width, pixelHeight);

        // Draw shadow first if enabled
        if (wordArt.HasShadow)
        {
            using var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new(0, 0, 0, 80),
                Style = SKPaintStyle.Fill
            };
            currentCanvas.DrawText(wordArt.Text, textX + 3, textY + 3, font, shadowPaint);
        }

        // Draw glow if enabled
        if (wordArt.HasGlow)
        {
            using var glowPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new(255, 215, 0, 100), // Gold glow
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels(4),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3)
            };
            currentCanvas.DrawText(wordArt.Text, textX, textY, font, glowPaint);
        }

        // Draw outline if specified
        if (wordArt is {OutlineColorHex: not null, OutlineWidthPoints: > 0})
        {
            using var outlinePaint = new SKPaint
            {
                IsAntialias = true,
                Color = ParseColor(wordArt.OutlineColorHex),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels((float) wordArt.OutlineWidthPoints)
            };
            currentCanvas.DrawText(wordArt.Text, textX, textY, font, outlinePaint);
        }

        // Draw text fill
        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Color = wordArt.FillColorHex != null ? ParseColor(wordArt.FillColorHex) : SKColors.Black,
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawText(wordArt.Text, textX, textY, font, fillPaint);

        // Draw reflection if enabled
        if (wordArt.HasReflection)
        {
            currentCanvas.Save();
            currentCanvas.Scale(1, -0.5f, textX, textY + scaledHeight / 2);

            using var reflectionPaint = new SKPaint
            {
                IsAntialias = true,
                Color = fillPaint.Color.WithAlpha(60),
                Style = SKPaintStyle.Fill
            };
            currentCanvas.DrawText(wordArt.Text, textX, textY + scaledHeight * 2, font, reflectionPaint);
            currentCanvas.Restore();
        }

        currentCanvas.Restore();
        // Note: No CurrentY advancement for floating elements
    }

    /// <summary>
    /// Renders WordArt that follows a curved path (ArchUp / ArchDown / Circle) via
    /// <see cref="SKCanvas.DrawTextOnPath(string, SKPath, SKPoint, SKTextAlign, SKFont, SKPaint)"/>.
    /// Returns true when the warp was handled, false for warps that should fall back to
    /// flat-text rendering.
    /// </summary>
    /// <remarks>
    /// Word's <c>prstTxWarp</c> presets don't treat the WordArt bbox as the *full* arc
    /// bounding box — that would produce a tight half-ellipse, much sharper than Word
    /// actually draws. The chord-sagitta interpretation gives the right shape: bbox W is
    /// the arc chord, bbox H is the sagitta, and the circle radius is
    /// R = (W² + 4H²) / (8H). For typical 4:1 wide-and-flat WordArt bboxes this gives a
    /// large radius and a gentle, mostly-horizontal curve — matching Word.
    /// <para>
    /// The path is sized to the rendered text width and centred on the arc peak/dip so
    /// short text sits at the bbox-centre rather than being stretched along the full
    /// chord. <c>textCircle</c> uses a text-width arc on the right side of an inscribed
    /// circle (3 o'clock anchor) — short text wraps the right hemisphere reading downward.
    /// </para>
    /// </remarks>
    bool TryRenderWordArtOnPath(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        string? outlineColorHex,
        double outlineWidthPoints,
        float x, float y, float width, float height,
        SKTypeface typeface, float fontSize)
    {
        if (currentCanvas == null)
        {
            return false;
        }

        using var measureFont = new SKFont(typeface, fontSize);
        var textWidth = measureFont.MeasureText(text);
        if (textWidth <= 0)
        {
            return false;
        }

        SKPath? path;
        switch (transform)
        {
            case WordArtTransform.ArchUp:
                path = BuildChordSagittaArc(x, y, width, height, textWidth, archDown: false);
                break;
            case WordArtTransform.ArchDown:
                path = BuildChordSagittaArc(x, y, width, height, textWidth, archDown: true);
                break;
            case WordArtTransform.Circle:
                {
                    var radius = Math.Min(width, height) / 2f;
                    var cx = x + width / 2f;
                    var cy = y + height / 2f;
                    var halfAngleDegrees = (float) (textWidth / radius * 90.0 / Math.PI);
                    var startAngle = 360f - halfAngleDegrees;
                    var sweepAngle = 2 * halfAngleDegrees;
                    path = BuildArc(new(cx - radius, cy - radius, cx + radius, cy + radius), startAngle, sweepAngle);
                    break;
                }
            case WordArtTransform.ChevronUp:
                // Word's textChevron renders as a single-peak smooth arch — same envelope as
                // ArchUp. Sharp-corner ^ paths cause per-glyph overlap at the apex.
                path = BuildChordSagittaArc(x, y, width, height, textWidth, archDown: false);
                break;
            case WordArtTransform.ChevronDown:
                path = BuildChordSagittaArc(x, y, width, height, textWidth, archDown: true);
                break;
            case WordArtTransform.Wave:
                path = BuildWavePath(x, y, width, height, textWidth);
                break;
            case WordArtTransform.SlantUp:
                path = BuildSlantPath(x, y, width, height, textWidth, slantUp: true);
                break;
            case WordArtTransform.SlantDown:
                path = BuildSlantPath(x, y, width, height, textWidth, slantUp: false);
                break;
            default:
                return false;
        }

        using (path)
        using (var font = new SKFont(typeface, fontSize)
               {
                   Edging = SKFontEdging.Antialias
               })
        using (var fillPaint = new SKPaint
               {
                   IsAntialias = true,
                   Color = fillColorHex != null ? ParseColor(fillColorHex) : SKColors.Black
               })
        {
            if (outlineColorHex != null &&
                outlineWidthPoints > 0)
            {
                using var outlinePaint = new SKPaint
                {
                    IsAntialias = true,
                    Color = ParseColor(outlineColorHex),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = context.PointsToPixels((float) outlineWidthPoints)
                };
                currentCanvas.DrawTextOnPath(text, path, new(0, 0), SKTextAlign.Left, font, outlinePaint);
            }

            currentCanvas.DrawTextOnPath(text, path, new(0, 0), SKTextAlign.Left, font, fillPaint);
        }

        return true;
    }

    /// <summary>
    /// Builds a text-length-fitting arc on the chord-sagitta circle (chord = bbox width,
    /// sagitta = bbox height). Path is centred on the arc peak (archUp) or dip (archDown),
    /// with sweep limited to <paramref name="textWidth"/> arc length so short text sits at
    /// the bbox-centre rather than being stretched along the full chord.
    /// </summary>
    static SKPath BuildChordSagittaArc(float x, float y, float width, float height, float textWidth, bool archDown)
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

        var ovalLeft = centerX - radius;
        return BuildArc(new(ovalLeft, bboxTop, ovalLeft + 2 * radius, bboxTop + 2 * radius), startAngle, sweepAngle);
    }

    static SKPath BuildArc(SKRect oval, float startAngle, float sweepAngle)
    {
        var path = new SKPath();
        path.AddArc(oval, startAngle, sweepAngle);
        return path;
    }

    /// <summary>
    /// Renders the box-filling envelope warps (Inflate / Deflate / CanUp / CanDown) by
    /// extracting the text outline as an SKPath, walking each subpath as a polyline (via
    /// SKPathMeasure), and remapping every sample point so each glyph's top and bottom
    /// edges follow the envelope curve. Word distorts each glyph as a true non-affine
    /// trapezoid; affine per-glyph scaling (the legacy fallback) only varies glyph height,
    /// keeping each glyph rectangular. Path-level remap captures the per-column height
    /// variation that makes the warp look right.
    /// </summary>
    bool TryRenderWordArtPathWarp(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        float x, float y, float width, float height,
        SKTypeface typeface, float fontSize)
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

        using var font = new SKFont(typeface, fontSize);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = fillColorHex != null ? ParseColor(fillColorHex) : SKColors.Black,
            Style = SKPaintStyle.Fill
        };

        var totalWidth = font.MeasureText(text);
        if (totalWidth <= 0)
        {
            return false;
        }

        var fontMetrics = font.Metrics;
        // Generate text outline at text-local coords: origin (0, -ascent) puts baseline at
        // y=0 with caps reaching up into negative Y. We use bounds-based normalisation, so
        // the exact origin doesn't matter — only consistency between path and bounds.
        using var textPath = font.GetTextPath(text, new SKPoint(0, -fontMetrics.Ascent));
        textPath.GetBounds(out var pathBounds);
        var glyphsTop = pathBounds.Top;
        var glyphsHeight = pathBounds.Height;
        if (glyphsHeight <= 0)
        {
            return false;
        }

        // Walk the outline verb by verb, flattening each quad/cubic into short line segments,
        // and warp every resulting point. (SKPathMeasure's contour sampling returns nothing for
        // these glyph outlines under SkiaSharp 4.x, which left the warp blank — iterating the raw
        // path is reliable and mirrors the ImageSharp backend's flatten-then-warp approach.)
        using var resultPath = new SKPath();
        const int curveSteps = 12;
        var iterator = textPath.CreateRawIterator();
        var points = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            SKPoint Warp(SKPoint sample) =>
                WarpPoint(sample, totalWidth, glyphsTop, glyphsHeight, x, y, width, height, transform);

            switch (verb)
            {
                case SKPathVerb.Move:
                    resultPath.MoveTo(Warp(points[0]));
                    break;
                case SKPathVerb.Line:
                    resultPath.LineTo(Warp(points[1]));
                    break;
                case SKPathVerb.Quad:
                case SKPathVerb.Conic:
                    // Conics (rare in glyph outlines) are approximated as quads; the warp only
                    // needs a dense polyline, not an exact conic.
                    for (var step = 1; step <= curveSteps; step++)
                    {
                        resultPath.LineTo(Warp(QuadPoint(points[0], points[1], points[2], (float) step / curveSteps)));
                    }

                    break;
                case SKPathVerb.Cubic:
                    for (var step = 1; step <= curveSteps; step++)
                    {
                        resultPath.LineTo(Warp(CubicPoint(points[0], points[1], points[2], points[3], (float) step / curveSteps)));
                    }

                    break;
                case SKPathVerb.Close:
                    resultPath.Close();
                    break;
            }
        }

        currentCanvas.DrawPath(resultPath, paint);
        return true;
    }

    static SKPoint QuadPoint(SKPoint p0, SKPoint p1, SKPoint p2, float t)
    {
        var mt = 1 - t;
        var a = mt * mt;
        var b = 2 * mt * t;
        var c = t * t;
        return new(a * p0.X + b * p1.X + c * p2.X, a * p0.Y + b * p1.Y + c * p2.Y);
    }

    static SKPoint CubicPoint(SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, float t)
    {
        var mt = 1 - t;
        var a = mt * mt * mt;
        var b = 3 * mt * mt * t;
        var c = 3 * mt * t * t;
        var d = t * t * t;
        return new(a * p0.X + b * p1.X + c * p2.X + d * p3.X, a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y);
    }

    static SKPoint WarpPoint(SKPoint point, float totalWidth, float glyphsTop, float glyphsHeight,
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
    /// Per-glyph rendering for envelope warps that aren't text-on-path (Fade, Triangle).
    /// Each glyph is drawn separately with a vertical scale anchored at the baseline so
    /// the bottoms align and the tops shrink toward the baseline. Returns true when
    /// handled.
    /// </summary>
    bool TryRenderWordArtEnvelope(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        float x, float y, float width, float height,
        SKTypeface typeface, float fontSize)
    {
        if (currentCanvas == null)
        {
            return false;
        }

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

        using var font = new SKFont(typeface, fontSize);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = fillColorHex != null ? ParseColor(fillColorHex) : SKColors.Black,
            Style = SKPaintStyle.Fill
        };

        var totalWidth = font.MeasureText(text);
        if (totalWidth <= 0)
        {
            return false;
        }

        var fontMetrics = font.Metrics;
        var glyphHeight = fontMetrics.Descent - fontMetrics.Ascent;

        // Inflate / Deflate / Can warps fill the bbox horizontally AND vertically — Word
        // stretches glyphs to span the box, then modulates each glyph's height by the warp
        // curve. Fade / Triangle leave the natural size (matches Word for those).
        // For the box-filling warps, scale so the PEAK (most-stretched) glyph fits the bbox
        // height. Inflate/Can peak at 1.5×, Deflate's largest is 1.0× (it shrinks).
        var fillsBox = transform is WordArtTransform.Inflate or WordArtTransform.Deflate
            or WordArtTransform.CanUp or WordArtTransform.CanDown;
        var peakScale = transform switch
        {
            WordArtTransform.Inflate or WordArtTransform.CanUp or WordArtTransform.CanDown => 1.5f,
            _ => 1.0f
        };
        var sx = fillsBox ? width / totalWidth : 1f;
        var baseScaleY = fillsBox ? height / (peakScale * glyphHeight) : 1f;
        var stretchedWidth = totalWidth * sx;

        // Position each glyph so its anchor edge (baseline / centre / top) sits at the
        // chosen bbox edge. The Save/Scale matrix then scales around that anchor without
        // translating it, so the chosen edge stays fixed and the opposite edge moves.
        // For non-box-filling warps (Fade / Triangle) keep the legacy layout — text centred
        // vertically in the bbox with baseline anchor — so existing baselines stay stable.
        var startX = x + (width - stretchedWidth) / 2f;
        var legacyTopY = y + (height - glyphHeight) / 2f;
        var legacyBaselineY = legacyTopY - fontMetrics.Ascent;
        float anchorY;
        float baselineY;
        if (!fillsBox)
        {
            anchorY = legacyBaselineY;
            baselineY = legacyBaselineY;
        }
        else
        {
            switch (anchor)
            {
                case EnvelopeAnchor.Top:
                    anchorY = y;
                    baselineY = anchorY - fontMetrics.Ascent;
                    break;
                case EnvelopeAnchor.Centre:
                    anchorY = y + height / 2f;
                    baselineY = anchorY - (fontMetrics.Ascent + fontMetrics.Descent) / 2f;
                    break;
                default:
                    anchorY = y + height;
                    baselineY = anchorY;
                    break;
            }
        }

        var charCount = text.Length;
        var cursorX = startX;
        for (var i = 0; i < charCount; i++)
        {
            var ch = text[i].ToString();
            var charAdvance = font.MeasureText(ch);
            // For 1-character labels in a box-filling warp, t=0 collapses sin(πt)=0 (no
            // warp). Use 0.5 so a single glyph still gets the centre amplitude. For Fade /
            // Triangle a single glyph at the start (t=0) is intentional.
            var t = charCount > 1 ? (float) i / (charCount - 1) : fillsBox ? 0.5f : 0f;
            var sy = scaleY(t) * baseScaleY;

            currentCanvas.Save();
            currentCanvas.Scale(sx, sy, cursorX, anchorY);
            currentCanvas.DrawText(ch, cursorX, baselineY, font, paint);
            currentCanvas.Restore();

            cursorX += charAdvance * sx;
        }
        return true;
    }

    enum EnvelopeAnchor { Baseline, Centre, Top }

    /// <summary>
    /// Straight diagonal path through the bbox centre with slope ±H/W. Path length matches
    /// <paramref name="textWidth"/> so glyphs sit on a slanted baseline.
    /// </summary>
    static SKPath BuildSlantPath(float x, float y, float width, float height, float textWidth, bool slantUp)
    {
        var slope = (slantUp ? -1f : 1f) * height / width;
        var halfTextLength = textWidth / 2f;
        var dx = halfTextLength / (float) Math.Sqrt(1 + slope * slope);
        var dy = dx * slope;
        var centerX = x + width / 2f;
        var centerY = y + height / 2f;

        var path = new SKPath();
        path.MoveTo(centerX - dx, centerY - dy);
        path.LineTo(centerX + dx, centerY + dy);
        return path;
    }

    /// <summary>
    /// Sine-wave polyline path (one full period across <paramref name="textWidth"/>) along the
    /// bbox horizontal midline. Amplitude is bbox H/4 so the wave excursion stays gentle
    /// relative to glyph height — matches Word's textWave1.
    /// </summary>
    static SKPath BuildWavePath(float x, float y, float width, float height, float textWidth)
    {
        var amplitude = height / 4f;
        var midY = y + height / 2f;
        var pathStartX = x + width / 2f - textWidth / 2f;

        const int segments = 64;
        var dx = textWidth / segments;
        var phaseScale = 2.0 * Math.PI / textWidth;

        var path = new SKPath();
        path.MoveTo(pathStartX, midY - amplitude);
        for (var i = 1; i <= segments; i++)
        {
            var t = i * dx;
            var py = midY - amplitude * (float) Math.Cos(t * phaseScale);
            path.LineTo(pathStartX + t, py);
        }
        return path;
    }

    void ApplyWordArtTransform(WordArtTransform transform, float x, float y, float width, float height)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var centerX = x + width / 2;
        var centerY = y + height / 2;

        switch (transform)
        {
            case WordArtTransform.ArchUp:
                // Simulate arch up with a slight rotation around center
                currentCanvas.Translate(centerX, centerY);
                currentCanvas.Scale(1, 0.8f);
                currentCanvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.ArchDown:
                // Simulate arch down
                currentCanvas.Translate(centerX, centerY);
                currentCanvas.Scale(1, 0.8f);
                currentCanvas.RotateDegrees(180);
                // Flip back to readable
                currentCanvas.Scale(1, -1);
                currentCanvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.Wave:
                // Simulate wave with slight skew
                currentCanvas.Translate(centerX, centerY);
                currentCanvas.Skew(0.1f, 0);
                currentCanvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.ChevronUp:
                // Simulate chevron up
                currentCanvas.Translate(centerX, centerY);
                currentCanvas.Scale(1, 0.7f);
                currentCanvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.ChevronDown:
                // Simulate chevron down
                currentCanvas.Translate(centerX, y + height);
                currentCanvas.Scale(1, 0.7f);
                currentCanvas.Translate(-centerX, -(y + height));
                break;

            case WordArtTransform.SlantUp:
                // Slant up with rotation
                currentCanvas.RotateDegrees(-10, centerX, centerY);
                break;

            case WordArtTransform.SlantDown:
                // Slant down with rotation
                currentCanvas.RotateDegrees(10, centerX, centerY);
                break;

            case WordArtTransform.Triangle:
                // Triangle shape - scale width at bottom
                currentCanvas.Translate(centerX, centerY);
                currentCanvas.Scale(0.8f, 1);
                currentCanvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.FadeRight:
                // Fade right - slight perspective
                currentCanvas.Translate(x, centerY);
                currentCanvas.Skew(0, 0.05f);
                currentCanvas.Translate(-x, -centerY);
                break;

            case WordArtTransform.FadeLeft:
                // Fade left - slight perspective
                currentCanvas.Translate(x + width, centerY);
                currentCanvas.Skew(0, -0.05f);
                currentCanvas.Translate(-(x + width), -centerY);
                break;

            case WordArtTransform.Circle:
                // Circle - approximate with scaling
                currentCanvas.Translate(centerX, centerY);
                currentCanvas.Scale(0.9f, 0.9f);
                currentCanvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.None:
            default:
                // No transform
                break;
        }
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

            // Create paint for this stroke
            var color = ParseColor(stroke.ColorHex);

            // Apply transparency
            if (stroke.Transparency > 0 || stroke.IsHighlighter)
            {
                var alpha = stroke.IsHighlighter
                    ? (byte) 128 // Highlighter is semi-transparent
                    : (byte) (255 - stroke.Transparency);
                color = color.WithAlpha(alpha);
            }

            using var paint = new SKPaint
            {
                Color = color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels((float) stroke.WidthPoints),
                IsAntialias = true,
                StrokeCap = stroke.PenTip == InkPenTip.Rectangle ? SKStrokeCap.Square : SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };

            // Highlighters use blend mode to simulate marker effect
            if (stroke.IsHighlighter)
            {
                paint.BlendMode = SKBlendMode.Multiply;
            }

            // Build path from points
            using var path = new SKPath();
            var firstPoint = stroke.Points[0];
            path.MoveTo(
                baseX + context.PointsToPixels((float) firstPoint.X),
                baseY + context.PointsToPixels((float) firstPoint.Y));

            for (var i = 1; i < stroke.Points.Count; i++)
            {
                var point = stroke.Points[i];
                path.LineTo(
                    baseX + context.PointsToPixels((float) point.X),
                    baseY + context.PointsToPixels((float) point.Y));
            }

            currentCanvas.DrawPath(path, paint);
        }

        context.CurrentY += height;
    }

    /// <summary>
    /// Measures the total height of a table for pagination purposes. Shares the memoized
    /// layout with <see cref="PageRendererBase.RenderTable"/>, so the follow-up render
    /// doesn't recompute it.
    /// </summary>
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

        // BottomToTop: rotate -90 around bottom-left of the content rect.
        // TopToBottom: rotate +90 around top-right of the content rect.
        var bottomToTop = cell.Properties.TextDirection == CellTextDirection.BottomToTop;
        var pivotXpx = context.PointsToPixels(bottomToTop ? contentX : contentX + contentWidth);
        var pivotYpx = context.PointsToPixels(bottomToTop ? contentY + availableHeight : contentY);

        currentCanvas.Save();
        currentCanvas.Translate(pivotXpx, pivotYpx);
        currentCanvas.RotateDegrees(bottomToTop ? -90 : 90);

        var savedY = context.CurrentY;
        context.CurrentY = 0;

        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                RenderParagraphInBounds(para, 0, availableHeight);
            }
            else if (element is ContentControlElement contentControl)
            {
                RenderContentControlInCell(contentControl, 0, availableHeight);
            }
        }

        context.CurrentY = savedY;
        currentCanvas.Restore();
    }


    protected override void RenderParagraphInBounds(ParagraphElement paragraph, float x, float maxWidth)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Render paragraph within specific bounds (for tables and text boxes)
        textRenderer.RenderParagraphInBounds(currentCanvas, paragraph, x, maxWidth);
    }

    protected override void RenderImageInCell(ImageElement image, float x, float maxWidth)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var imageWidth = (float) image.WidthPoints;
        var imageHeight = (float) image.HeightPoints;

        // Scale image to fit within cell width if needed
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

        var destRect = new SKRect(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);

        if (image.ContentType == "image/svg+xml")
        {
            RenderSvgImage(image.ImageData, destRect);
        }
        else
        {
            var skImage = context.GetBitmap(image.ImageData);
            if (skImage != null)
            {
                currentCanvas.DrawBitmap(skImage, destRect);
            }
        }

        context.CurrentY += imageHeight;
    }

    Dictionary<string, SKColor> colorCache = [];

    SKColor ParseColor(string hexColor)
    {
        if (string.IsNullOrEmpty(hexColor) || hexColor == "auto")
        {
            return SKColors.Black;
        }

        if (colorCache.TryGetValue(hexColor, out var cached))
        {
            return cached;
        }

        var color = ParseColorImpl(hexColor);
        colorCache[hexColor] = color;
        return color;
    }

    static SKColor ParseColorImpl(string hexColor)
    {
        if (hexColor.Length == 6 &&
            uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var rgb))
        {
            return new(
                (byte) ((rgb >> 16) & 0xFF),
                (byte) ((rgb >> 8) & 0xFF),
                (byte) (rgb & 0xFF)
            );
        }

        if (hexColor.Length == 8 &&
            uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var argb))
        {
            return new(
                (byte) ((argb >> 16) & 0xFF),
                (byte) ((argb >> 8) & 0xFF),
                (byte) (argb & 0xFF),
                (byte) ((argb >> 24) & 0xFF)
            );
        }

        return SKColors.Black;
    }

    protected override void DrawCellBackground(float pixelX, float pixelY, float pixelWidth, float pixelHeight, string hexColor)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var bgPaint = context.GetReusableFillPaint(ParseColor(hexColor), antialias: false);
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);
    }

    protected override void DrawCellBorders(float pixelX, float pixelY, float pixelWidth, float pixelHeight, CellBorders borders)
    {
        if (currentCanvas == null)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        if (borders.Top.IsVisible)
        {
            DrawBorderLine(paint, borders.Top, pixelX, pixelY, pixelX + pixelWidth, pixelY, true);
        }

        if (borders.Right.IsVisible)
        {
            DrawBorderLine(paint, borders.Right, pixelX + pixelWidth, pixelY, pixelX + pixelWidth, pixelY + pixelHeight, false);
        }

        if (borders.Bottom.IsVisible)
        {
            DrawBorderLine(paint, borders.Bottom, pixelX, pixelY + pixelHeight, pixelX + pixelWidth, pixelY + pixelHeight, true);
        }

        if (borders.Left.IsVisible)
        {
            DrawBorderLine(paint, borders.Left, pixelX, pixelY, pixelX, pixelY + pixelHeight, false);
        }
    }

    void DrawBorderLine(SKPaint paint, BorderEdge edge, float x1, float y1, float x2, float y2, bool horizontal)
    {
        ConfigureBorderPaint(paint, edge);
        if (edge.Style == BorderLineStyle.Double)
        {
            // OOXML w:val="double": render as two parallel lines whose total span (line +
            // gap + line) matches the declared width. Each line gets ~1/3 of the width.
            var totalWidth = paint.StrokeWidth;
            var lineWidth = Math.Max(0.5f, totalWidth / 3f);
            var offset = totalWidth / 2f - lineWidth / 2f;
            paint.StrokeWidth = lineWidth;
            if (horizontal)
            {
                currentCanvas!.DrawLine(x1, y1 - offset, x2, y2 - offset, paint);
                currentCanvas.DrawLine(x1, y1 + offset, x2, y2 + offset, paint);
            }
            else
            {
                currentCanvas!.DrawLine(x1 - offset, y1, x2 - offset, y2, paint);
                currentCanvas.DrawLine(x1 + offset, y1, x2 + offset, y2, paint);
            }
        }
        else
        {
            currentCanvas!.DrawLine(x1, y1, x2, y2, paint);
        }
    }

    protected override void DrawCellDiagonals(float pixelX, float pixelY, float pixelWidth, float pixelHeight, CellDiagonals diagonals)
    {
        if (currentCanvas == null)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        if (diagonals.Down.IsVisible)
        {
            ConfigureBorderPaint(paint, diagonals.Down);
            currentCanvas.DrawLine(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight, paint);
        }

        if (diagonals.Up.IsVisible)
        {
            ConfigureBorderPaint(paint, diagonals.Up);
            currentCanvas.DrawLine(pixelX + pixelWidth, pixelY, pixelX, pixelY + pixelHeight, paint);
        }
    }

    SKPaint CreateBorderPaint(BorderEdge edge) =>
        new()
        {
            Color = ParseColor(edge.ColorHex ?? "000000"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = context.PointsToPixels((float) edge.WidthPoints),
            IsAntialias = true
        };

    void ConfigureBorderPaint(SKPaint paint, BorderEdge edge)
    {
        paint.Color = ParseColor(edge.ColorHex ?? "000000");
        paint.StrokeWidth = context.PointsToPixels((float) edge.WidthPoints);
    }

    // === Form-field / content-control draw primitives (called from PageRendererBase) ===

    protected override void DrawFormFieldRect(float pixelX, float pixelY, float pixelWidth, float pixelHeight,
        string fillHex, string borderHex, float pixelBorderWidth)
    {
        if (currentCanvas == null)
        {
            return;
        }

        using var bgPaint = new SKPaint
        {
            Color = ParseColor(fillHex),
            Style = SKPaintStyle.Fill
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);

        using var borderPaint = new SKPaint
        {
            Color = ParseColor(borderHex),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = pixelBorderWidth,
            IsAntialias = true
        };
        currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, borderPaint);
    }

    protected override void DrawFormFieldText(string text, float pixelX, float pixelY, float pixelWidth, float pixelHeight, string textHex)
    {
        if (currentCanvas == null)
        {
            return;
        }

        var font = context.CreateFontFromTypeface(context.GetTypeface(DefaultFontSettings.DefaultFont, false, false), 10);
        using var textPaint = new SKPaint
        {
            Color = ParseColor(textHex),
            IsAntialias = true
        };

        // Skia DrawText takes baseline Y; position it 4 pixels above the rect's bottom edge so
        // the glyph caps sit a couple of pixels below the top of the rect.
        var textX = pixelX + 3 * context.Scale;
        var textY = pixelY + pixelHeight - 4 * context.Scale;
        currentCanvas.DrawText(text, textX, textY, SKTextAlign.Left, font, textPaint);
    }

    protected override void DrawCheckMark(float pixelX, float pixelY, float pixelSize, string hexColor, float pixelStrokeWidth, bool xShape)
    {
        if (currentCanvas == null)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Color = ParseColor(hexColor),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = pixelStrokeWidth,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };

        if (xShape)
        {
            // X glyph (used by content-control checkboxes). Wider padding (0.25) than the ✓ form
            // since the X stretches to all four corners.
            var pad = pixelSize * 0.25f;
            var left = pixelX + pad;
            var right = pixelX + pixelSize - pad;
            var top = pixelY + pad;
            var bottom = pixelY + pixelSize - pad;
            currentCanvas.DrawLine(left, top, right, bottom, paint);
            currentCanvas.DrawLine(right, top, left, bottom, paint);
            return;
        }

        // ✓ glyph (used by form-field checkboxes). Two strokes meeting at 40% from the left edge,
        // sitting on the bottom — geometrically what users expect.
        var checkPad = pixelSize * 0.2f;
        var checkLeft = pixelX + checkPad;
        var checkRight = pixelX + pixelSize - checkPad;
        var checkTop = pixelY + checkPad;
        var checkBottom = pixelY + pixelSize - checkPad;
        var midX = pixelX + pixelSize * 0.4f;
        currentCanvas.DrawLine(checkLeft, checkTop + (checkBottom - checkTop) * 0.5f, midX, checkBottom, paint);
        currentCanvas.DrawLine(midX, checkBottom, checkRight, checkTop, paint);
    }

    protected override void DrawDropDownArrow(float pixelX, float pixelY, float pixelHeight, string hexColor)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // pixelX is the right edge of the field; back off 12 scaled pixels to leave a margin
        // and centre the arrow vertically.
        var arrowSize = pixelHeight * 0.3f;
        var arrowX = pixelX - 12 * context.Scale;
        var arrowY = pixelY + pixelHeight / 2;

        using var arrowPaint = new SKPaint
        {
            Color = ParseColor(hexColor),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX, arrowY - arrowSize / 2);
        arrowPath.LineTo(arrowX + arrowSize, arrowY - arrowSize / 2);
        arrowPath.LineTo(arrowX + arrowSize / 2, arrowY + arrowSize / 2);
        arrowPath.Close();
        currentCanvas.DrawPath(arrowPath, arrowPaint);
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

            // Encode straight from the bitmap's pixels — SKImage.FromBitmap would snapshot a
            // full copy of the page (~33 MB at Letter/300 DPI) only to feed the same encoder.
            using var pixmap = page.PeekPixels();
            using var data = pixmap.Encode(SKEncodedImageFormat.Png, 100)!;
            pageCallback!(data.SaveTo);
        }
    }

    protected override void StartNewPage()
    {
        FlushPendingPage();

        currentPage = new(
            context.PageWidthPixels,
            context.PageHeightPixels,
            SKColorType.Rgba8888,
            SKAlphaType.Premul
        );

        currentCanvas = new(currentPage);

        // Clear to transparency (WordArt rasterizer), the background color if specified, else white.
        var bgColor = context.PageSettings.BackgroundColorHex;
        if (context.TransparentBackground)
        {
            currentCanvas.Clear(SKColors.Transparent);
        }
        else if (string.IsNullOrEmpty(bgColor))
        {
            currentCanvas.Clear(SKColors.White);
        }
        else
        {
            currentCanvas.Clear(ParseColor(bgColor));
        }

        // Watermarks live behind everything: drawn after the page background clear so they
        // don't show through it, but before page borders/header/body so those land on top.
        DrawWatermarks();

        DrawPageBorders();

        if (pageCount > 0)
        {
            context.StartNewPage();
            // Reset line numbers for new page (if restart mode is NewPage)
            context.ResetLineNumbersForPage();
        }

        // Render header on new page
        RenderHeader();

        // Reset tracking for the new page
        hasSignificantContentOnCurrentPage = false;
        currentPageFromExplicitBreak = false;
        currentPageFromSectionBreak = false;
        pagesStarted++;
    }

    protected override void FinishCurrentPage()
    {
        if (currentPage == null)
        {
            return;
        }

        // Render footer before finishing
        RenderFooter();

        pendingPage = currentPage;
        currentCanvas?.Dispose();
        currentCanvas = null;
        currentPage = null;
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
        var typeface = context.GetTypeface(watermark.FontFamily, watermark.Bold, italic: false);
        var fontSize = (float) watermark.FontSizePoints * context.Scale;
        var font = context.CreateFontFromTypeface(typeface, fontSize);
        using var paint = new SKPaint
        {
            Color = ParseColor(watermark.ColorHex),
            IsAntialias = true
        };

        var textWidth = font.MeasureText(watermark.Text);

        currentCanvas!.Save();
        currentCanvas.Translate(context.PageWidthPixels / 2f, context.PageHeightPixels / 2f);
        currentCanvas.RotateDegrees((float) watermark.RotationDegrees);
        currentCanvas.DrawText(watermark.Text, -textWidth / 2f, fontSize / 3f, SKTextAlign.Left, font, paint);
        currentCanvas.Restore();
    }

    void DrawPageBorders()
    {
        if (currentCanvas == null ||
            context.PageSettings.PageBorders is not {HasAnyBorder: true} borders)
        {
            return;
        }

        var pageWidth = context.PageWidthPixels;
        var pageHeight = context.PageHeightPixels;
        var leftX = context.PointsToPixels((float) borders.LeftSpacePoints);
        var rightX = pageWidth - context.PointsToPixels((float) borders.RightSpacePoints);
        var topY = context.PointsToPixels((float) borders.TopSpacePoints);
        var bottomY = pageHeight - context.PointsToPixels((float) borders.BottomSpacePoints);

        static SKPaint CreatePaint(BorderEdge edge, float strokeWidth) => new()
        {
            Color = SKColor.Parse("#" + (edge.ColorHex ?? "000000")),
            StrokeWidth = strokeWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        if (borders.Top.IsVisible)
        {
            using var paint = CreatePaint(borders.Top, context.PointsToPixels((float) borders.Top.WidthPoints));
            currentCanvas.DrawLine(leftX, topY, rightX, topY, paint);
        }

        if (borders.Bottom.IsVisible)
        {
            using var paint = CreatePaint(borders.Bottom, context.PointsToPixels((float) borders.Bottom.WidthPoints));
            currentCanvas.DrawLine(leftX, bottomY, rightX, bottomY, paint);
        }

        if (borders.Left.IsVisible)
        {
            using var paint = CreatePaint(borders.Left, context.PointsToPixels((float) borders.Left.WidthPoints));
            currentCanvas.DrawLine(leftX, topY, leftX, bottomY, paint);
        }

        if (borders.Right.IsVisible)
        {
            using var paint = CreatePaint(borders.Right, context.PointsToPixels((float) borders.Right.WidthPoints));
            currentCanvas.DrawLine(rightX, topY, rightX, bottomY, paint);
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

        // Calculate position based on anchor type
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

        // Check for image fill first
        if (shape.ImageData != null)
        {
            var image = context.GetImage(shape.ImageData);
            if (image != null)
            {
                var destRect = new SKRect(pixelX, pixelY, pixelX + pixelWidth, pixelY + pixelHeight);

                // Word shows the picture through the shape's geometry (a circular profile
                // photo is an ellipse with a blip fill), not as a bare rectangle. Rotated
                // geometry is handled inside BuildPolygonPath; the ellipse clip is page-space
                // like the pic:spPr crop, so rotated ellipses stay unclipped.
                var clipGeometry = shape.Subpaths != null ||
                                   (shape.Preset == PresetShape.Ellipse && shape.RotationDegrees == 0);
                if (clipGeometry)
                {
                    currentCanvas.Save();
                    if (shape.Subpaths != null)
                    {
                        using var clipPath = BuildPolygonPath(shape, pixelX, pixelY, pixelWidth, pixelHeight);
                        currentCanvas.ClipPath(clipPath, antialias: true);
                    }
                    else
                    {
                        using var clipPath = new SKPath();
                        clipPath.AddOval(destRect);
                        currentCanvas.ClipPath(clipPath, antialias: true);
                    }
                }

                using var paint = new SKPaint
                {
                    IsAntialias = true
                };
                currentCanvas.DrawImage(image, destRect, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);

                if (clipGeometry)
                {
                    currentCanvas.Restore();
                }
            }
        }
        else if (shape.Gradient is { } gradient)
        {
            // Linear gradient: angle 0° = horizontal-X-axis pointing right, clockwise positive
            // (matches OOXML a:lin/@ang). Convert to start/end points along the bounding box.
            var rad = gradient.DirectionDegrees * Math.PI / 180.0;
            var dx = (float) Math.Cos(rad);
            var dy = (float) Math.Sin(rad);
            var cx = pixelX + pixelWidth / 2;
            var cy = pixelY + pixelHeight / 2;
            var halfDiag = (float) Math.Sqrt(pixelWidth * pixelWidth + pixelHeight * pixelHeight) / 2;

            var startPt = new SKPoint(cx - dx * halfDiag, cy - dy * halfDiag);
            var endPt = new SKPoint(cx + dx * halfDiag, cy + dy * halfDiag);

            using var shader = SKShader.CreateLinearGradient(
                startPt, endPt,
                [SKColor.Parse(gradient.StartColorHex), SKColor.Parse(gradient.EndColorHex)],
                SKShaderTileMode.Clamp);
            using var paint = new SKPaint
            {
                Shader = shader,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            FillShape(shape, pixelX, pixelY, pixelWidth, pixelHeight, paint);
        }
        else if (shape.FillColorHex != null)
        {
            // Solid fill
            var color = SKColor.Parse(shape.FillColorHex)
                .WithAlpha((byte) Math.Round(Math.Clamp(shape.FillAlpha, 0, 1) * 255));
            using var paint = new SKPaint
            {
                Color = color,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            FillShape(shape, pixelX, pixelY, pixelWidth, pixelHeight, paint);
        }

        if (shape is { LineColorHex: { } lineColor, LineWidthPoints: { } lineWidthPt and > 0 })
        {
            using var strokePaint = new SKPaint
            {
                Color = SKColor.Parse(lineColor)
                    .WithAlpha((byte) Math.Round(Math.Clamp(shape.LineAlpha, 0, 1) * 255)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels((float) lineWidthPt),
                IsAntialias = true
            };
            if (shape.Subpaths != null)
            {
                using var path = BuildPolygonPath(shape, pixelX, pixelY, pixelWidth, pixelHeight);
                currentCanvas.DrawPath(path, strokePaint);
            }
            else if (shape.Preset == PresetShape.Ellipse)
            {
                currentCanvas.DrawOval(
                    pixelX + pixelWidth / 2,
                    pixelY + pixelHeight / 2,
                    pixelWidth / 2,
                    pixelHeight / 2,
                    strokePaint);
            }
            else
            {
                // PageWidthPixels truncates `pageWidth * Scale` to int, leaving the canvas one
                // sub-pixel narrower than a full-page shape rect. Without clamping, a centered
                // stroke at the right/bottom edge sits ~0.99px outside the canvas and disappears.
                // Clamp the stroke path to the canvas so the centred line lands on the last visible
                // column / row.
                var strokeLeft = Math.Max(0, pixelX);
                var strokeTop = Math.Max(0, pixelY);
                var strokeRight = Math.Min(context.PageWidthPixels, pixelX + pixelWidth);
                var strokeBottom = Math.Min(context.PageHeightPixels, pixelY + pixelHeight);
                currentCanvas.DrawRect(
                    new(strokeLeft, strokeTop, strokeRight, strokeBottom),
                    strokePaint);
            }
        }
    }

    void FillShape(FloatingShapeElement shape, float x, float y, float width, float height, SKPaint paint)
    {
        if (shape.Subpaths != null)
        {
            using var path = BuildPolygonPath(shape, x, y, width, height);
            currentCanvas!.DrawPath(path, paint);
            return;
        }

        if (shape.Preset == PresetShape.Ellipse)
        {
            currentCanvas!.DrawOval(x + width / 2, y + height / 2, width / 2, height / 2, paint);
        }
        else
        {
            currentCanvas!.DrawRect(x, y, width, height, paint);
        }
    }

    static SKPath BuildPolygonPath(FloatingShapeElement shape, float x, float y, float width, float height)
    {
        var path = new SKPath();
        // Each sub-path is its own closed contour. SKPath's default Winding (nonzero) fill type
        // matches DrawingML's default custGeom fill, so oppositely-wound nested contours read as
        // holes instead of being fused into one polygon by connector lines.
        foreach (var contour in shape.Subpaths!)
        {
            for (var i = 0; i < contour.Count; i++)
            {
                var (px, py) = contour[i];
                // Apply flips around the unit-square center, then scale into the bounding box.
                var ux = shape.FlipHorizontal ? 1 - px : px;
                var uy = shape.FlipVertical ? 1 - py : py;
                var localX = (float) (ux * width);
                var localY = (float) (uy * height);
                if (i == 0)
                {
                    path.MoveTo(localX, localY);
                }
                else
                {
                    path.LineTo(localX, localY);
                }
            }
            path.Close();
        }

        // Translate so (0,0) sits at the bbox top-left, then rotate around the bbox center.
        var matrix = SKMatrix.CreateTranslation(x, y);
        if (shape.RotationDegrees != 0)
        {
            matrix = SKMatrix.Concat(
                matrix,
                SKMatrix.CreateRotationDegrees((float) shape.RotationDegrees, width / 2, height / 2));
        }
        path.Transform(matrix);
        return path;
    }

    protected override void RenderFloatingImage(FloatingImageElement image)
    {
        if (currentCanvas == null)
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

        // Calculate absolute position based on anchor type
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

        var destRect = new SKRect(bounds.PixelX, bounds.PixelY, bounds.PixelX + bounds.PixelWidth, bounds.PixelY + bounds.PixelHeight);

        // pic:spPr geometry crop (round photos, custGeom cuts). The clip sits in page space,
        // so rotated pictures (whose bitmap turns inside DrawBlockImage) are left unclipped.
        var clipGeometry = (image.ClipToEllipse || image.ClipSubpaths != null) &&
                           Math.Abs(image.RotationDegrees) < 0.01;
        if (clipGeometry)
        {
            currentCanvas.Save();
            using var clipPath = new SKPath();
            if (image.ClipToEllipse)
            {
                clipPath.AddOval(destRect);
            }
            else
            {
                foreach (var contour in image.ClipSubpaths!)
                {
                    for (var pointIndex = 0; pointIndex < contour.Count; pointIndex++)
                    {
                        var (unitX, unitY) = contour[pointIndex];
                        var pointX = destRect.Left + (float) unitX * destRect.Width;
                        var pointY = destRect.Top + (float) unitY * destRect.Height;
                        if (pointIndex == 0)
                        {
                            clipPath.MoveTo(pointX, pointY);
                        }
                        else
                        {
                            clipPath.LineTo(pointX, pointY);
                        }
                    }

                    clipPath.Close();
                }
            }

            currentCanvas.ClipPath(clipPath, antialias: true);
        }

        DrawBlockImage(image.ImageData, image.ContentType, destRect, (float) image.RotationDegrees, image.Crop, image.ColorEffect, image.FlipHorizontal, image.FlipVertical, image.DuotoneColorHex, image.DuotoneLightColorHex);

        if (clipGeometry)
        {
            currentCanvas.Restore();
        }
    }

    protected override void RenderFloatingTextBox(FloatingTextBoxElement textBox)
    {
        if (currentCanvas == null)
        {
            return;
        }

        // Calculate absolute position
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

        // Save canvas state before rotation
        currentCanvas.Save();

        // Apply rotation if specified
        if (Math.Abs(textBox.RotationDegrees) > 0.01)
        {
            // Calculate center point for rotation
            var centerX = pixelX + pixelWidth / 2;
            var centerY = pixelY + pixelHeight / 2;

            // Rotate around center
            currentCanvas.RotateDegrees((float) textBox.RotationDegrees, centerX, centerY);
        }

        // The shape's chrome behind the text: fill and a:ln outline, following the shape's
        // geometry when it is richer than a rectangle (roundRect ticket outlines, plaque frames).
        using var geometryPath = BuildTextBoxPath(textBox, pixelX, pixelY, pixelWidth, pixelHeight);
        if (textBox.BackgroundColorHex != null)
        {
            using var bgPaint = new SKPaint
            {
                Color = ParseColor(textBox.BackgroundColorHex),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            if (geometryPath != null)
            {
                currentCanvas.DrawPath(geometryPath, bgPaint);
            }
            else
            {
                currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, bgPaint);
            }
        }

        if (textBox.LineColorHex != null && textBox.LineWidthPoints > 0)
        {
            using var strokePaint = new SKPaint
            {
                Color = ParseColor(textBox.LineColorHex)
                    .WithAlpha((byte) Math.Round(Math.Clamp(textBox.LineAlpha, 0, 1) * 255)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float) textBox.LineWidthPoints * context.Scale,
                IsAntialias = true
            };
            if (geometryPath != null)
            {
                currentCanvas.DrawPath(geometryPath, strokePaint);
            }
            else
            {
                currentCanvas.DrawRect(pixelX, pixelY, pixelWidth, pixelHeight, strokePaint);
            }
        }

        // Render content at the absolute position
        // Save current position and set to text box position
        var savedY = context.CurrentY;

        // Temporarily adjust context for text box rendering
        context.CurrentY = y;

        // Render each content element
        foreach (var element in textBox.Content)
        {
            if (element is ParagraphElement para)
            {
                textRenderer.RenderParagraphInBounds(currentCanvas, para, x, (float) textBox.WidthPoints);
            }
        }

        // Restore context
        context.CurrentY = savedY;

        // Restore canvas state (removes rotation)
        currentCanvas.Restore();
    }

    /// <summary>
    /// The text box's <see cref="FloatingTextBoxElement.Subpaths"/> contours scaled into its box,
    /// or null for plain rectangles. Even-odd fill keeps ring geometry hollow.
    /// </summary>
    static SKPath? BuildTextBoxPath(FloatingTextBoxElement textBox, float x, float y, float width, float height)
    {
        if (textBox.Subpaths == null)
        {
            return null;
        }

        var path = new SKPath {FillType = SKPathFillType.EvenOdd};
        foreach (var contour in textBox.Subpaths)
        {
            if (contour.Count < 3)
            {
                continue;
            }

            for (var index = 0; index < contour.Count; index++)
            {
                var (pointX, pointY) = contour[index];
                var localX = x + (float) pointX * width;
                var localY = y + (float) pointY * height;
                if (index == 0)
                {
                    path.MoveTo(localX, localY);
                }
                else
                {
                    path.LineTo(localX, localY);
                }
            }

            path.Close();
        }

        return path;
    }

    public void Dispose()
    {
        currentCanvas?.Dispose();
        currentPage?.Dispose();
        // Note: Don't dispose _pages here - caller owns them
    }
}
