using A = DocumentFormat.OpenXml.Drawing;
using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlPageBorders = DocumentFormat.OpenXml.Wordprocessing.PageBorders;

/// <summary>
/// What stands beside the body: page setup and section bands, headers and footers, notes, comments, bookmarks and watermarks.
/// </summary>
sealed partial class DocumentParser
{
    List<Watermark> ExtractWatermarks(MainDocumentPart mainPart)
    {
        var result = new List<Watermark>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var headerPart in mainPart.HeaderParts)
        {
            if (headerPart.Header == null)
            {
                continue;
            }

            foreach (var shape in headerPart.Header.Descendants<DocumentFormat.OpenXml.Vml.Shape>())
            {
                // Word emits the watermark shape with id="WordPictureWatermark..." or
                // id="WordTextWatermark...". Some templates also tag it via o:cls or class — match
                // any attribute carrying the marker so all variants are detected.
                var isWatermark = shape.GetAttributes()
                    .Any(_ =>
                        _.Value is { } v &&
                        (v.Contains("WordPictureWatermark", StringComparison.OrdinalIgnoreCase) ||
                         v.Contains("WordTextWatermark", StringComparison.OrdinalIgnoreCase)));
                if (!isWatermark)
                {
                    continue;
                }

                // De-dup across header parts: identical shapes can repeat in even/first-page headers.
                var dedupKey = shape.OuterXml;
                if (!seen.Add(dedupKey))
                {
                    continue;
                }

                var imageData = shape.GetFirstChild<DocumentFormat.OpenXml.Vml.ImageData>();
                if (imageData != null)
                {
                    var pictureWatermark = ParsePictureWatermark(imageData, headerPart);
                    if (pictureWatermark != null)
                    {
                        result.Add(pictureWatermark);
                    }

                    continue;
                }

                var textPath = shape.GetFirstChild<DocumentFormat.OpenXml.Vml.TextPath>();
                if (textPath?.String?.Value is {Length: > 0} textValue)
                {
                    result.Add(ParseTextWatermark(textPath, textValue, shape));
                }
            }
        }

        return result;
    }

    Watermark? ParsePictureWatermark(DocumentFormat.OpenXml.Vml.ImageData imageData, HeaderPart headerPart)
    {
        var relId = imageData.RelationshipId?.Value
                    ?? imageData.GetAttributes()
                        .FirstOrDefault(_ => _.LocalName == "id" &&
                                             _.NamespaceUri.EndsWith("/relationships")).Value;
        if (relId == null)
        {
            return null;
        }

        if (headerPart.PartOrNull(relId) is not ImagePart imagePart)
        {
            return null;
        }

        // Word stores gain/blacklevel as fixed-point /65536 (the trailing "f" is a hint for that).
        // 65536 → 1.0; default Gain = 1.0 (no contrast change), default BlackLevel = 0.
        var gain = ParseFixedPoint(imageData.Gain?.Value, defaultValue: 1.0);
        var blackLevel = ParseFixedPoint(imageData.BlackLevel?.Value, defaultValue: 0.0);

        return new()
        {
            ImageData = GetPartBytes(imagePart),
            ContentType = imagePart.ContentType,
            Gain = gain,
            BlackLevel = blackLevel
        };
    }

    static Watermark ParseTextWatermark(DocumentFormat.OpenXml.Vml.TextPath textPath, string text, DocumentFormat.OpenXml.Vml.Shape shape)
    {
        // textpath @style is a CSS-like string: "font:bold 36pt Calibri"
        var fontFamily = "Calibri";
        var fontSize = 36.0;
        var bold = false;

        if (textPath.Style?.Value is { } styleString)
        {
            var styleSpan = styleString.AsSpan();
            foreach (var propRange in styleSpan.Split(';'))
            {
                var prop = styleSpan[propRange];
                var colonIndex = prop.IndexOf(':');
                if (colonIndex < 0)
                {
                    continue;
                }

                var name = prop[..colonIndex].Trim();
                if (!name.Equals("font", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // CSS shorthand: "[style] [weight] size family" — last token is the family,
                // any "bold" token sets weight, any "Npt" token sets size.
                var fontValue = prop[(colonIndex + 1)..].Trim();
                ReadOnlySpan<char> lastToken = default;
                foreach (var tokenRange in fontValue.Split(' '))
                {
                    var token = fontValue[tokenRange];
                    if (token.IsEmpty)
                    {
                        continue;
                    }

                    lastToken = token;

                    if (token.Equals("bold", StringComparison.OrdinalIgnoreCase))
                    {
                        bold = true;
                    }
                    else if (token.EndsWith("pt", StringComparison.OrdinalIgnoreCase) &&
                             double.TryParse(token[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pt))
                    {
                        fontSize = pt;
                    }
                }

                if (!lastToken.IsEmpty)
                {
                    fontFamily = lastToken.ToString();
                }
            }
        }

        // shape.FillColor is a VML colour string (named or "#hex"); strip the "#".
        var color = "BFBFBF";
        if (shape.FillColor?.Value is {Length: > 0} fillColor)
        {
            color = fillColor.TrimStart('#').ToUpperInvariant();
        }

        return new()
        {
            Text = text,
            FontFamily = fontFamily,
            FontSizePoints = fontSize,
            Bold = bold,
            ColorHex = color
        };
    }

    // The footnotes part: each user note's body parsed as ordinary paragraphs — its w:footnoteRef run
    // becoming the citation number (noteReferenceText) — and the separator / continuation-separator
    // entries as paragraphs with their runs stripped. Runs after the body parse, so the citation
    // numbers are known. Null separators when the part is absent; a part WITH the entries but no
    // paragraph inside them (document_capture/01) still gets a paragraph, because Word draws the rule
    // either way.
    (IReadOnlyList<Footnote> Notes, ParagraphElement? Separator, ParagraphElement? Continuation) ExtractFootnotes(MainDocumentPart mainPart) =>
        ExtractNotes(
            mainPart,
            mainPart.FootnotesPart?.Footnotes?.Elements<DocumentFormat.OpenXml.Wordprocessing.Footnote>(),
            footnoteCitationNumbers,
            footnoteNumberFormat,
            (id, text, elements) => new Footnote
            {
                Id = id,
                Text = text,
                Elements = elements
            });

    (IReadOnlyList<Endnote> Notes, ParagraphElement? Separator, ParagraphElement? Continuation) ExtractEndnotes(MainDocumentPart mainPart) =>
        ExtractNotes(
            mainPart,
            mainPart.EndnotesPart?.Endnotes?.Elements<DocumentFormat.OpenXml.Wordprocessing.Endnote>(),
            endnoteCitationNumbers,
            endnoteNumberFormat,
            (id, text, elements) => new Endnote
            {
                Id = id,
                Text = text,
                Elements = elements
            });

    // Footnotes and endnotes differ only in their part and their model type; the walk over the
    // part's entries is one.
    (IReadOnlyList<TNote> Notes, ParagraphElement? Separator, ParagraphElement? Continuation) ExtractNotes<TNote>(
        MainDocumentPart mainPart,
        IEnumerable<FootnoteEndnoteType>? notes,
        Dictionary<string, int> citations,
        NumberFormatValues format,
        Func<string, string, List<DocumentElement>, TNote> make)
    {
        if (notes == null)
        {
            return ([], null, null);
        }

        var result = new List<TNote>();
        ParagraphElement? separator = null;
        ParagraphElement? continuation = null;
        foreach (var note in notes)
        {
            if (note.Type?.HasValue == true &&
                note.Type.Value != FootnoteEndnoteValues.Normal)
            {
                if (note.Type.Value == FootnoteEndnoteValues.Separator)
                {
                    separator = ParseSeparatorParagraph(note, mainPart);
                }
                else if (note.Type.Value == FootnoteEndnoteValues.ContinuationSeparator)
                {
                    continuation = ParseSeparatorParagraph(note, mainPart);
                }

                continue;
            }

            if (note.Id?.Value is not { } idLong)
            {
                continue;
            }

            var id = idLong.ToString();
            var text = string.Concat(note.Descendants<Text>().Select(_ => _.Text));
            result.Add(make(id, text, ParseNoteBody(note, mainPart, CitationNumber(citations, id), format)));
        }

        return (result, separator ?? ParseSeparatorParagraph(null, mainPart), continuation ?? ParseSeparatorParagraph(null, mainPart));
    }

    // The display number of a note: its citation ordinal, or the next one for a note the body never
    // cites (Word does not render such a note; the number only keeps the exporters' listing sane).
    static int CitationNumber(Dictionary<string, int> citations, string id)
    {
        if (!citations.TryGetValue(id, out var number))
        {
            number = citations.Count + 1;
            citations[id] = number;
        }

        return number;
    }

    // A note's paragraphs (and tables), parsed like a header's; the w:footnoteRef / w:endnoteRef run
    // inside draws the note's own number in the document's format (see ParseRun).
    List<DocumentElement> ParseNoteBody(OpenXmlCompositeElement note, MainDocumentPart mainPart, int number, NumberFormatValues format)
    {
        var elements = new List<DocumentElement>();
        noteReferenceText = NumberingParser.FormatNumber(number, format);
        try
        {
            AppendHeaderFooterElements(note, mainPart, elements);
        }
        finally
        {
            noteReferenceText = null;
        }

        return elements;
    }

    // A separator entry's paragraph with its runs stripped. Word sizes the separator line by the
    // paragraph's STYLE alone and ignores any run formatting the entry declares, on the run or on the
    // paragraph mark (_probe_fn_i/_j at 30pt and 10pt marks, _probe_fn_l/_m/_n across docDefaults and
    // Normal sizes, all XPS-read 2026-09-05: the rule and the note below it never moved with the run,
    // and moved exactly with the style). An entry without a paragraph — or no entry at all — takes
    // Word's own separator paragraph: empty, single-spaced, nothing after.
    ParagraphElement ParseSeparatorParagraph(OpenXmlCompositeElement? entry, MainDocumentPart mainPart)
    {
        var paragraph = entry?.GetFirstChild<Paragraph>()
                        ?? new Paragraph(new OoxmlParagraphProperties(new SpacingBetweenLines {After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto}));
        var parsed = ParseParagraph(paragraph, mainPart).OfType<ParagraphElement>().FirstOrDefault();
        return new()
        {
            Runs = [],
            Properties = parsed?.Properties ?? new()
        };
    }

    List<Comment> ExtractComments(MainDocumentPart mainPart, BodyScan scan)
    {
        var commentsPart = mainPart.WordprocessingCommentsPart;
        if (commentsPart?.Comments == null)
        {
            return [];
        }

        // Map each w:commentRangeStart's id → enclosing paragraph ordinal. The ordinal map is
        // shared with bookmark extraction and only materialized when a range start exists.
        Dictionary<Paragraph, int>? paragraphOrdinals = null;
        var anchorByCommentId = new Dictionary<string, int>(StringComparer.Ordinal);
        var numberByCommentId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rangeStart in scan.CommentStarts)
        {
            if (rangeStart.Id?.Value is not { } rangeId)
            {
                continue;
            }

            numberByCommentId.TryAdd(rangeId, numberByCommentId.Count + 1);

            paragraphOrdinals ??= scan.ParagraphOrdinals();
            for (var current = rangeStart.Parent; current != null; current = current.Parent)
            {
                if (current is Paragraph paragraph &&
                    paragraphOrdinals.TryGetValue(paragraph, out var idx))
                {
                    anchorByCommentId[rangeId] = idx;
                    break;
                }
            }
        }

        var result = new List<Comment>();
        foreach (var ooxmlComment in commentsPart.Comments.Elements<DocumentFormat.OpenXml.Wordprocessing.Comment>())
        {
            if (ooxmlComment.Id?.Value is not { } id)
            {
                continue;
            }

            var text = string.Concat(ooxmlComment.Descendants<Text>().Select(_ => _.Text));
            DateTimeOffset? date = null;
            if (ooxmlComment.Date?.Value is { } dateValue)
            {
                date = dateValue;
            }

            int? anchor = anchorByCommentId.TryGetValue(id, out var idx) ? idx : null;

            result.Add(
                new()
                {
                    Id = id,
                    Author = ooxmlComment.Author?.Value,
                    Initials = ooxmlComment.Initials?.Value,
                    FontFamily = CommentFont(ooxmlComment, mainPart),
                    Text = text,
                    Paragraphs = [.. ooxmlComment.Elements<Paragraph>().Select(_ => string.Concat(_.Descendants<Text>().Select(text => text.Text)))],
                    Number = numberByCommentId.TryGetValue(id, out var number) ? number : null,
                    Date = date,
                    AnchorParagraphIndex = anchor
                });
        }

        return result;
    }

    string? CommentFont(DocumentFormat.OpenXml.Wordprocessing.Comment comment, MainDocumentPart mainPart)
    {
        var run = comment.Descendants<OoxmlRun>().FirstOrDefault();
        if (run == null)
        {
            return null;
        }

        var styleId = run.Ancestors<Paragraph>().FirstOrDefault()?.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        return ParseRunProperties(run.RunProperties, mainPart, styleId).FontFamily;
    }

    List<Bookmark> ExtractBookmarks(BodyScan scan)
    {
        // Most documents have no bookmarks, so the paragraph-ordinal map is only built (shared with
        // comment extraction) once a bookmark is found.
        Dictionary<Paragraph, int>? paragraphOrdinals = null;

        var result = new List<Bookmark>();
        foreach (var start in scan.BookmarkStarts)
        {
            if (start.Id?.Value is not { } id ||
                start.Name?.Value is not { } name)
            {
                continue;
            }

            paragraphOrdinals ??= scan.ParagraphOrdinals();

            int? paragraphIndex = null;
            ParagraphElement? paragraphElement = null;
            for (var current = start.Parent; current != null; current = current.Parent)
            {
                if (current is Paragraph paragraph &&
                    paragraphOrdinals.TryGetValue(paragraph, out var idx))
                {
                    paragraphIndex = idx;
                    bookmarkParagraphs?.TryGetValue(paragraph, out paragraphElement);
                    break;
                }
            }

            result.Add(
                new()
                {
                    Id = id,
                    Name = name,
                    ParagraphIndex = paragraphIndex,
                    Paragraph = paragraphElement
                });
        }

        return result;
    }

    // w:docGrid/@w:linePitch in points when @w:type puts the section on a grid, else 0. Word writes a
    // bare <w:docGrid w:linePitch="360"/> with no @type into most documents as a stored default and
    // lays nothing out on it.
    static double GridLinePitchPoints(SectionProperties section)
    {
        var grid = section.GetFirstChild<DocGrid>();
        if (grid?.LinePitch?.HasValue == true &&
            grid.Type?.Value is { } type &&
            type != DocGridValues.Default &&
            grid.LinePitch.Value > 0)
        {
            return grid.LinePitch.Value / OoxmlUnits.TwipsPerPoint;
        }

        return 0;
    }

    // A page dimension (w:pgSz w:w/@w:h) is an unbounded twips measure in the file. Left raw it scales
    // straight into each backend's raster surface (an SKBitmap's width is WidthPoints x DPI/72 at
    // 4 bytes/px), so a crafted value would allocate — or integer-overflow — a bitmap of billions of
    // pixels and exhaust memory. Clamp each dimension to a ceiling far above any real paper (A0 and
    // ANSI E are ~3370pt; Word's own UI caps at 22in = 1584pt) and floor at 1pt so a zero/negative
    // size still lays out.
    const double maxPageDimensionPoints = 14400;

    static double ClampPageDimension(double points) => Math.Clamp(points, 1, maxPageDimensionPoints);

    // Two opposite margins shrunk in proportion, when they would leave the page less than an inch of
    // content (half the page, on one smaller than two inches). Real margins are never near it; only a
    // crafted pair is touched.
    static (double Near, double Far) FitMargins(double near, double far, double extent)
    {
        var room = extent - Math.Min(72, extent / 2);
        if (near + far <= room)
        {
            return (near, far);
        }

        var factor = room / (near + far);
        return (near * factor, far * factor);
    }

    PageSettings ExtractPageSettings(SectionProperties sectionProps)
    {
        var pageSize = sectionProps.GetFirstChild<PageSize>();
        var pageMargin = sectionProps.GetFirstChild<PageMargin>();

        // w:pgNumType - the section's page-number restart and display format.
        int? pageNumberStart = null;
        string? pageNumberFormat = null;
        int? chapterLevel = null;
        var chapterSeparator = "-";
        var pageNumberType = sectionProps.GetFirstChild<PageNumberType>();
        if (pageNumberType != null)
        {
            if (pageNumberType.Start?.Value is { } startValue)
            {
                pageNumberStart = startValue;
            }

            if (pageNumberType.Format?.Value is { } formatValue)
            {
                if (formatValue == NumberFormatValues.LowerRoman)
                {
                    pageNumberFormat = "roman";
                }
                else if (formatValue == NumberFormatValues.UpperRoman)
                {
                    pageNumberFormat = "Roman";
                }
                else if (formatValue == NumberFormatValues.LowerLetter)
                {
                    pageNumberFormat = "alphabetic";
                }
                else if (formatValue == NumberFormatValues.UpperLetter)
                {
                    pageNumberFormat = "Alphabetic";
                }
            }

            // w:chapStyle names the heading level whose number leads each page number, and
            // w:chapSep what stands between them (a hyphen when absent).
            if (pageNumberType.ChapterStyle?.Value is { } chapterStyle and >= 1 and <= 9)
            {
                chapterLevel = chapterStyle;
                chapterSeparator = pageNumberType.ChapterSeparator?.Value switch
                {
                    { } separator when separator == ChapterSeparatorValues.Period => ".",
                    { } separator when separator == ChapterSeparatorValues.Colon => ":",
                    { } separator when separator == ChapterSeparatorValues.EmDash => "—",
                    { } separator when separator == ChapterSeparatorValues.EnDash => "–",
                    _ => "-"
                };
            }
        }

        var (width, height) = DefaultPageSize.Resolve(useLetterPageSize);
        double marginTop = 72;
        var topMarginIsAbsolute = false;
        double marginBottom = 72;
        double marginLeft = 72;
        double marginRight = 72;
        double headerDistance = 36;
        double footerDistance = 36;
        var columnCount = 1;
        double columnSpacing = 36;

        if (pageSize != null)
        {
            width = pageSize.Width.TwipsToPoints() ?? width;

            height = pageSize.Height.TwipsToPoints() ?? height;

            (width, height) = SnapToPaperCode(pageSize.Code?.Value, width, height);
        }

        // A crafted w:pgSz would otherwise scale into a multi-gigabyte raster surface (see
        // ClampPageDimension). The defaults are already in range, so this only bites malformed input.
        width = ClampPageDimension(width);
        height = ClampPageDimension(height);

        double gutterPoints = 0;
        if (pageMargin != null)
        {
            if (pageMargin.Top?.HasValue == true)
            {
                // ECMA-376 §17.6.11: a negative w:top means |val| is the distance from the top of
                // the page to the top of the body, regardless of header height. A positive one is
                // a minimum the header can push past, which TopMarginIsAbsolute distinguishes.
                marginTop = Math.Abs(pageMargin.Top.Value) / OoxmlUnits.TwipsPerPoint;
                topMarginIsAbsolute = pageMargin.Top.Value < 0;
            }

            if (pageMargin.Bottom?.HasValue == true)
            {
                marginBottom = Math.Abs(pageMargin.Bottom.Value) / OoxmlUnits.TwipsPerPoint;
            }

            marginLeft = pageMargin.Left.TwipsToPoints() ?? marginLeft;

            marginRight = pageMargin.Right.TwipsToPoints() ?? marginRight;

            headerDistance = pageMargin.Header.TwipsToPoints() ?? headerDistance;

            footerDistance = pageMargin.Footer.TwipsToPoints() ?? footerDistance;

            gutterPoints = pageMargin.Gutter.TwipsToPoints() ?? gutterPoints;
        }

        // Gutter is added to the appropriate margin at parse time so the rest of the pipeline
        // doesn't need to know about it. Preserved separately on PageSettings for consumers.
        var gutterAtTop = gutterAtTopSetting;
        if (gutterPoints > 0)
        {
            if (gutterAtTop)
            {
                marginTop += gutterPoints;
            }
            else
            {
                marginLeft += gutterPoints;
            }
        }

        // A crafted w:pgMar can ask for margins wider than the page they stand on — the page is clamped
        // (ClampPageDimension), its margins are not — and a content box of no size, or less, sends the
        // fragmenter looking forever for a page anything fits on.
        (marginLeft, marginRight) = FitMargins(marginLeft, marginRight, width);
        (marginTop, marginBottom) = FitMargins(marginTop, marginBottom, height);

        var columns = sectionProps.GetFirstChild<Columns>();
        if (columns != null)
        {
            if (columns.ColumnCount?.HasValue == true)
            {
                columnCount = columns.ColumnCount.Value;
            }

            columnSpacing = columns.Space.TwipsToPoints() ?? columnSpacing;
        }

        var differentFirstPage = sectionProps.GetFirstChild<TitlePage>() != null;

        var lineNumbers = ParseLineNumberSettings(sectionProps);

        // A bare w:docGrid pitch with no @type is no grid (see GridLinePitchPoints): taken as a
        // per-line floor it set business-plans/13's 11pt body lines at 18pt.
        var documentGridLinePitchPoints = GridLinePitchPoints(sectionProps);

        var pageBorders = ParsePageBorders(sectionProps.GetFirstChild<OoxmlPageBorders>());

        var verticalAlignment = ParsePageVerticalAlignment(sectionProps.GetFirstChild<VerticalTextAlignmentOnPage>());

        return new()
        {
            WidthPoints = width,
            HeightPoints = height,
            MarginTop = marginTop,
            TopMarginIsAbsolute = topMarginIsAbsolute,
            MarginBottom = marginBottom,
            MarginLeft = marginLeft,
            MarginRight = marginRight,
            HeaderDistance = headerDistance,
            FooterDistance = footerDistance,
            ColumnCount = columnCount,
            ColumnSpacing = columnSpacing,
            LineNumbers = lineNumbers,
            FootnotesBeneathText = sectionProps.GetFirstChild<FootnoteProperties>()?
                .GetFirstChild<FootnotePosition>()?.Val?.Value == FootnotePositionValues.BeneathText,
            DocumentGridLinePitchPoints = documentGridLinePitchPoints,
            LastRenderedPageBreakCount = lastRenderedPageBreakCount,
            BackgroundColorHex = documentBackgroundColor,
            DifferentFirstPage = differentFirstPage,
            PageBorders = pageBorders,
            GutterPoints = gutterPoints,
            PageNumberStart = pageNumberStart,
            PageNumberFormat = pageNumberFormat,
            ChapterStyle = chapterLevel,
            ChapterSeparator = chapterSeparator,
            GutterAtTop = gutterAtTop,
            VerticalAlignment = verticalAlignment
        };
    }

    // w:sectPr/w:vAlign — the section's vertical alignment on the page. Absent, or top, is the default.
    static PageVerticalAlignment ParsePageVerticalAlignment(VerticalTextAlignmentOnPage? element)
    {
        var value = element?.Val?.Value;
        if (value == VerticalJustificationValues.Center)
        {
            return PageVerticalAlignment.Center;
        }

        if (value == VerticalJustificationValues.Both)
        {
            return PageVerticalAlignment.Justified;
        }

        if (value == VerticalJustificationValues.Bottom)
        {
            return PageVerticalAlignment.Bottom;
        }

        return PageVerticalAlignment.Top;
    }

    /// <summary>
    /// Standard <c>w:pgSz/@code</c> papers, keyed by the Windows <c>DMPAPER_*</c> constant the
    /// attribute carries, sized in points at their nominal portrait orientation.
    ///
    /// Only the metric papers can actually move a page: an inch-defined size is a whole number of
    /// points and so exactly representable in twips, while a millimetre-defined one never is (A4's
    /// 841.8898pt rounds to 16838 twips at best). The US entries are here so a file that rounds
    /// one oddly still lands on the exact paper.
    /// </summary>
    static readonly Dictionary<int, (double Width, double Height)> paperSizePoints = new()
    {
        // Letter, 8.5 x 11 in
        [1] = (612, 792),
        // Tabloid, 11 x 17 in
        [3] = (792, 1224),
        // Legal, 8.5 x 14 in
        [5] = (612, 1008),
        // Executive, 7.25 x 10.5 in
        [7] = (522, 756),
        // A3, 297 x 420 mm
        [8] = (841.8898, 1190.5512),
        // A4, 210 x 297 mm
        [9] = (595.2756, 841.8898),
        // A5, 148 x 210 mm
        [11] = (419.5276, 595.2756),
        // B5 (JIS), 182 x 257 mm
        [13] = (515.9055, 728.5039),
        // Envelope #14, 5 x 11.5 in
        [23] = (360, 828),
        // Envelope DL, 110 x 220 mm
        [27] = (311.8110, 623.6220)
    };

    /// <summary>
    /// Half a point — comfortably past the ~0.11pt worst case of a millimetre paper rounded to
    /// twips, and far short of any real difference between two standard papers.
    /// </summary>
    const double paperCodeTolerancePoints = 0.5;

    /// <summary>
    /// Resolves the page size against <c>w:pgSz/@code</c>, the Windows printer paper code. Word
    /// gives the code priority over the <c>w:w</c>/<c>w:h</c> twips beside it, because those are a
    /// rounded copy of a paper whose real size the code names exactly — `nonstandard_main_part_name`
    /// declares 16840 twips (842.0pt) with code 9, and Word renders A4's true 841.89pt, a page a
    /// pixel shorter at 150 DPI.
    ///
    /// The snap only applies when the declared size ALREADY matches that paper to within
    /// <see cref="paperCodeTolerancePoints"/>, in either orientation (Word writes w/h already
    /// swapped for landscape). A stale code is otherwise free to destroy the page: `cards/03` is a
    /// 7 x 5 in card still carrying code 23 — a 5 x 11.5 in envelope — from whatever it was branched
    /// off, and honouring that unconditionally would resize the card to an envelope. An unknown
    /// code falls through untouched, so this can only ever refine a declared size, never invent one.
    /// </summary>
    static (double Width, double Height) SnapToPaperCode(int? code, double width, double height)
    {
        if (code is not { } paperCode ||
            !paperSizePoints.TryGetValue(paperCode, out var paper))
        {
            return (width, height);
        }

        if (Matches(width, paper.Width) &&
            Matches(height, paper.Height))
        {
            return paper;
        }

        if (Matches(width, paper.Height) &&
            Matches(height, paper.Width))
        {
            return (paper.Height, paper.Width);
        }

        return (width, height);

        static bool Matches(double declared, double nominal) =>
            Math.Abs(declared - nominal) <= paperCodeTolerancePoints;
    }

    PageBorders? ParsePageBorders(OoxmlPageBorders? element)
    {
        if (element == null)
        {
            return null;
        }

        var top = ParseBorderEdge(element.GetFirstChild<TopBorder>());
        var right = ParseBorderEdge(element.GetFirstChild<RightBorder>());
        var bottom = ParseBorderEdge(element.GetFirstChild<BottomBorder>());
        var left = ParseBorderEdge(element.GetFirstChild<LeftBorder>());

        if (!top.IsVisible &&
            !right.IsVisible &&
            !bottom.IsVisible &&
            !left.IsVisible)
        {
            return null;
        }

        return new()
        {
            Top = top,
            Right = right,
            Bottom = bottom,
            Left = left,
            TopSpacePoints = ReadSpacePoints(element.GetFirstChild<TopBorder>()),
            RightSpacePoints = ReadSpacePoints(element.GetFirstChild<RightBorder>()),
            BottomSpacePoints = ReadSpacePoints(element.GetFirstChild<BottomBorder>()),
            LeftSpacePoints = ReadSpacePoints(element.GetFirstChild<LeftBorder>()),

            // offsetFrom="text" is the OOXML default; only an explicit "page" measures the
            // spaces from the page edge.
            MeasureFromText = element.OffsetFrom == null || element.OffsetFrom.Value != PageBorderOffsetValues.Page
        };

        static double ReadSpacePoints(BorderType? edge)
        {
            if (edge?.Space?.HasValue == true)
            {
                return edge.Space.Value;
            }

            return 24;
        }
    }

    static LineNumberSettings? ParseLineNumberSettings(SectionProperties sectionProps)
    {
        var lnNumType = sectionProps.GetFirstChild<LineNumberType>();
        if (lnNumType == null)
        {
            return null;
        }

        // w:start default 0: the attribute holds the value BEFORE the first counted line
        // (Word's UI "start at 1" writes w:start="0"), so an absent attribute numbers the
        // first line 1. See the Fragmenter's line-number emission for the reference evidence.
        var start = 0;
        var countBy = 1;
        // Default 0.25 inch
        double distancePoints = 18;
        var restart = LineNumberRestart.NewPage;

        if (lnNumType.Start?.HasValue == true)
        {
            start = lnNumType.Start.Value;
        }

        if (lnNumType.CountBy?.HasValue == true)
        {
            countBy = lnNumType.CountBy.Value;
        }

        distancePoints = lnNumType.Distance.TwipsToPoints() ?? distancePoints;

        if (lnNumType.Restart?.HasValue == true)
        {
            var restartValue = lnNumType.Restart.Value;
            if (restartValue == LineNumberRestartValues.Continuous)
            {
                restart = LineNumberRestart.Continuous;
            }
            else if (restartValue == LineNumberRestartValues.NewSection)
            {
                restart = LineNumberRestart.NewSection;
            }
            // else keep default (NewPage)
        }

        return new()
        {
            Start = start,
            CountBy = countBy,
            DistancePoints = distancePoints,
            Restart = restart
        };
    }

    // Each section's header/footer set with Word's inheritance applied (ECMA-376 §17.10.5 and
    // _probe_sect_a): a section that declares no reference of a type takes the previous section's part
    // of that type — declared or itself inherited — whatever else it declares, and the chain is per
    // TYPE: section 2 overriding only its default header left section 3 (w:titlePg, nothing declared)
    // showing section 1's first-page header and even header. A declared reference to an EMPTY part is
    // an explicit "nothing" (null) that later sections inherit. Even parts are dropped when the document
    // does not set w:evenAndOddHeaders — Word ignores them then (_probe_sect_c). A part shared by
    // several sections is parsed once, so the engine's paragraph-keyed caches see one instance.
    List<SectionBands> ExtractSectionBands(IReadOnlyList<SectionProperties> sectionPropsList, MainDocumentPart mainPart, bool evenAndOddHeaders)
    {
        var parsedParts = new Dictionary<string, HeaderFooterContent?>();

        HeaderFooterContent? Part(string relationshipId)
        {
            if (!parsedParts.TryGetValue(relationshipId, out var content))
            {
                OpenXmlPart? part;
                try
                {
                    part = mainPart.GetPartById(relationshipId);
                }
                catch (ArgumentOutOfRangeException)
                {
                    part = null;
                }

                content = ParseBandPart(part, mainPart);
                parsedParts[relationshipId] = content;
            }

            return content;
        }

        var result = new List<SectionBands>(sectionPropsList.Count);
        var previous = new SectionBands {EvenAndOddHeaders = evenAndOddHeaders};
        foreach (var sectionProps in sectionPropsList)
        {
            HeaderFooterContent? Header(HeaderFooterValues type, HeaderFooterContent? inherited)
            {
                var reference = sectionProps.Elements<HeaderReference>().FirstOrDefault(_ => _.Type?.Value == type);
                if (reference?.Id?.Value is { } id)
                {
                    return Part(id);
                }

                return inherited;
            }

            HeaderFooterContent? Footer(HeaderFooterValues type, HeaderFooterContent? inherited)
            {
                var reference = sectionProps.Elements<FooterReference>().FirstOrDefault(_ => _.Type?.Value == type);
                if (reference?.Id?.Value is { } id)
                {
                    return Part(id);
                }

                return inherited;
            }

            var bands = new SectionBands
            {
                Header = Header(HeaderFooterValues.Default, previous.Header),
                Footer = Footer(HeaderFooterValues.Default, previous.Footer),
                FirstPageHeader = Header(HeaderFooterValues.First, previous.FirstPageHeader),
                FirstPageFooter = Footer(HeaderFooterValues.First, previous.FirstPageFooter),
                EvenPageHeader = evenAndOddHeaders ? Header(HeaderFooterValues.Even, previous.EvenPageHeader) : null,
                EvenPageFooter = evenAndOddHeaders ? Footer(HeaderFooterValues.Even, previous.EvenPageFooter) : null,
                EvenAndOddHeaders = evenAndOddHeaders
            };
            result.Add(bands);
            previous = bands;
        }

        return result;
    }

    // A header or footer part's content, null when the part is missing or empty (a blank first-page
    // header is Word's way of showing none on a title page, and must stay "nothing").
    HeaderFooterContent? ParseBandPart(OpenXmlPart? part, MainDocumentPart mainPart)
    {
        OpenXmlCompositeElement? rootElement = part switch
        {
            HeaderPart hp => hp.Header,
            FooterPart fp => fp.Footer,
            _ => null
        };

        if (rootElement == null)
        {
            return null;
        }

        // A band's part is shared by the sections that show it; its percentage-sized art takes the
        // first section's page, as its aligned art does.
        if (anchorAlignmentPage != null)
        {
            rootElement.FoldRelativeSizes(anchorAlignmentPage, restore);
        }

        var elements = new List<DocumentElement>();
        AppendHeaderFooterElements(rootElement, mainPart, elements);

        // Header/footer floats need the same relativeHeight z-sort the body gets — letters/02's
        // header stacks an opaque white JPEG (z 251658240) UNDER its frame PNG (251658241), and
        // painting them in document order whited out the whole letterhead.
        SortFloatingBatchesByZ(elements);

        if (elements.Count > 0)
        {
            return new HeaderFooterContent
            {
                Elements = elements
            };
        }

        return null;
    }

    void AppendHeaderFooterElements(OpenXmlElement container, MainDocumentPart mainPart, List<DocumentElement> elements)
    {
        foreach (var element in container.ChildElements)
        {
            switch (element)
            {
                case Paragraph para:
                    elements.AddRange(ParseParagraph(para, mainPart));
                    break;
                case Table table:
                    var parsedTable = ParseTable(table, mainPart);
                    if (parsedTable != null)
                    {
                        elements.Add(parsedTable);
                    }

                    break;
                case SdtBlock sdt:
                    // Word's built-in galleries (e.g. "Page Numbers (Bottom of Page)") wrap the
                    // whole footer in a content control — unwrap it, or the footer parses empty.
                    var content = sdt.SdtContentBlock;
                    if (content != null)
                    {
                        AppendHeaderFooterElements(content, mainPart, elements);
                    }

                    break;
            }
        }
    }

    SectionBreakElement ParseSectionBreak(SectionProperties sectionProps)
    {
        // w:type describes how the section it belongs to is placed relative to the PREVIOUS section
        // (ECMA-376 §17.6.22). The break this inline sectPr marks starts the FOLLOWING section, so the
        // break's type comes from the following sectPr — the same lookahead the page settings use.
        // resumes/10: section 1 authors type=continuous (its own start), sections 2-3 author none →
        // nextPage. The ending sectPr stands in only when no following sectPr exists.
        var typeSource = sectionProps;
        if (nextSectionProps != null &&
            nextSectionProps.TryGetValue(sectionProps, out var followingProps) &&
            followingProps != null)
        {
            typeSource = followingProps;
        }

        // A section whose w:footnotePr / w:endnotePr restarts each section numbers its notes from 1 again
        // (_probe_en_sectrestart: section two's endnote reads "i" where it ran on to "iii" without).
        if (typeSource.GetFirstChild<FootnoteProperties>()?.GetFirstChild<NumberingRestart>()?.Val?.Value == RestartNumberValues.EachSection)
        {
            footnotesInSection = 0;
        }

        if (typeSource.GetFirstChild<EndnoteProperties>()?.GetFirstChild<NumberingRestart>()?.Val?.Value == RestartNumberValues.EachSection)
        {
            endnotesInSection = 0;
        }

        var typeElement = typeSource.GetFirstChild<SectionType>();
        // Default
        var breakType = SectionBreakType.NextPage;

        if (typeElement?.Val?.HasValue == true)
        {
            var val = typeElement.Val.Value;
            if (val == SectionMarkValues.Continuous)
            {
                breakType = SectionBreakType.Continuous;
            }
            else if (val == SectionMarkValues.EvenPage)
            {
                breakType = SectionBreakType.EvenPage;
            }
            else if (val == SectionMarkValues.OddPage)
            {
                breakType = SectionBreakType.OddPage;
            }
            else if (val == SectionMarkValues.NextColumn)
            {
                breakType = SectionBreakType.NextColumn;
            }
            // else NextPage (default)
        }

        // SectionProperties (sectPr) describes the section it belongs to.
        // For a section break, the following section's properties are stored in the next sectPr in the document.
        PageSettings? newSettings = null;
        if (nextSectionSettings != null &&
            nextSectionSettings.TryGetValue(sectionProps, out var nextSettings))
        {
            newSettings = nextSettings;
        }

        // Fallback: if we couldn't resolve the next section settings, parse from this sectPr.
        if (newSettings == null)
        {
            newSettings = ExtractPageSettings(sectionProps);
        }

        return new()
        {
            BreakType = breakType,
            NewSectionSettings = newSettings
        };
    }
}
