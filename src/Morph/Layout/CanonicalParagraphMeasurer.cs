/// <summary>
/// The <see cref="IParagraphMeasurer"/> surface over the backend-independent
/// <see cref="CanonicalTextMeasurer"/> — step 1 of the layout engine
/// (<c>docs/layout-engine-proposal.md</c>). It wraps a paragraph's runs and reports line heights,
/// natural width and total height from the font's own OpenType metrics, so the shared table-height
/// math (and, later, the fragmenter) can measure without a backend font library. Font resolution is
/// injected as a delegate — the caller wires a <c>FontResolver&lt;FontMetrics&gt;</c> — keeping this
/// class purely about layout.
///
/// <para>Modelled: multi-run greedy wrap (adjacent non-space pieces across runs form one word, so a
/// mid-word format change never splits the word), per-line height = the tallest run's hhea box under
/// the paragraph's line-spacing rule, Word's before/after spacing, and the empty-paragraph mark's own
/// run properties (w:pPr/w:rPr) sizing the blank line — matching PdfTextEngine, so a spacer parked over
/// a differently-sized phantom run keeps its true height. Tabs and first-line/hanging indents are
/// modelled too: a tab piece's advance resolves position-dependently in BuildLineItems below,
/// and the wrap narrows the first line by the first-line or hanging indent (lists exempt — their
/// hanging indent is the marker gutter).</para>
/// </summary>
sealed class CanonicalParagraphMeasurer(Func<string, bool, bool, FontMetrics?> resolveFont, double fontWidthScale = 1.0) : IParagraphMeasurer
{
    public List<float> LayoutParagraphForMeasurement(ParagraphElement paragraph, float maxWidth)
    {
        var lines = LayoutLines(paragraph, maxWidth);
        var heights = new List<float>(lines.Count);
        foreach (var line in lines)
        {
            heights.Add(line.Height);
        }

        return heights;
    }

    /// <summary>
    /// The paragraph's wrapped lines with both width and laid-out height — the fragmenter's richer view
    /// over <see cref="LayoutParagraphForMeasurement"/> (which returns heights only for the shared
    /// table-height math).
    /// </summary>
    public IReadOnlyList<MeasuredLine> LayoutLines(ParagraphElement paragraph, float maxWidth)
    {
        var props = paragraph.Properties;
        var wrapped = Wrap(paragraph, maxWidth);
        var result = new MeasuredLine[wrapped.Count];
        for (var i = 0; i < wrapped.Count; i++)
        {
            var height = (float) CanonicalTextMeasurer.LineHeightPoints(
                wrapped[i].Pitch, props.LineSpacingRule, props.LineSpacingMultiplier, props.LineSpacingPoints);
            result[i] = new(wrapped[i].Width, height);
        }

        return result;
    }

    /// <summary>
    /// The paragraph's wrapped lines with the run segments to paint (text + font per source run) and the
    /// baseline offset — the fragmenter's view for building <see cref="PlacedLine"/>s. Same wrap as
    /// <see cref="LayoutLines"/>, so heights and page counts are unchanged; this adds only the content.
    /// </summary>
    public IReadOnlyList<LaidOutLine> LayoutLineContents(ParagraphElement paragraph, float maxWidth)
    {
        var props = paragraph.Properties;
        var wrapped = Wrap(paragraph, maxWidth);
        var result = new LaidOutLine[wrapped.Count];
        for (var i = 0; i < wrapped.Count; i++)
        {
            var textHeight = (float) CanonicalTextMeasurer.LineHeightPoints(
                wrapped[i].Pitch, props.LineSpacingRule, props.LineSpacingMultiplier, props.LineSpacingPoints);

            var segments = wrapped[i].Segments;
            var runs = new LaidOutRun[segments.Count];
            var textAscent = 0f;
            for (var segment = 0; segment < segments.Count; segment++)
            {
                runs[segment] = new(segments[segment].X, segments[segment].Width, segments[segment].Text, segments[segment].Properties, segments[segment].Leader);
                textAscent = Math.Max(textAscent, AscentPoints(segments[segment].Properties));
            }

            // An empty mark line has no runs; its baseline still needs the mark's ascent, and its height the
            // mark font's line pitch. Without the height, an image-only line — a heading-rule drawing in an
            // otherwise blank paragraph (resumes/18's section rules) — collapses to the tiny image height, so
            // the rule drops onto the following line instead of sitting in the paragraph's own font-height row.
            if (segments.Count == 0)
            {
                textAscent = AscentPoints(ParagraphFont(paragraph));
                textHeight = (float) CanonicalTextMeasurer.LineHeightPoints(
                    MarkPitch(paragraph), props.LineSpacingRule, props.LineSpacingMultiplier, props.LineSpacingPoints);
            }

            // An inline image sits with its bottom on the baseline, so it fills the whole ascent and can
            // grow the line: ascent and height take the max of the text metrics and the tallest image.
            var images = wrapped[i].Images;
            var maxImageHeight = 0f;
            foreach (var image in images)
            {
                maxImageHeight = Math.Max(maxImageHeight, image.Height);
            }

            result[i] = new(wrapped[i].Width, Math.Max(textHeight, maxImageHeight), Math.Max(textAscent, maxImageHeight), runs, images);
        }

        return result;
    }

    public float MeasureParagraphNaturalWidth(ParagraphElement paragraph, float maxWidth)
    {
        var widest = 0f;
        foreach (var line in Wrap(paragraph, maxWidth))
        {
            if (line.Width > widest)
            {
                widest = line.Width;
            }
        }

        return widest;
    }

    float AscentPoints(RunProperties fontProperties)
    {
        var metrics = resolveFont(fontProperties.FontFamily, fontProperties.Bold, fontProperties.Italic);
        return metrics == null ? 0 : (float) ((double) metrics.Ascender / metrics.UnitsPerEm * fontProperties.FontSizePoints);
    }

    /// <summary>
    /// The width in points of a short run of text in the given font — for placing a right-aligned list
    /// marker just before its text. Zero when the font does not resolve.
    /// </summary>
    public float MeasureRunWidth(string text, RunProperties properties)
    {
        var metrics = resolveFont(properties.FontFamily, properties.Bold, properties.Italic);
        return metrics == null ? 0 : (float) CanonicalTextMeasurer.MeasureWidthPoints(metrics, text, properties.FontSizePoints, fontWidthScale);
    }

    public float MeasureParagraphHeightWithWidth(ParagraphElement paragraph, float maxWidth)
    {
        var props = paragraph.Properties;
        var lines = LayoutLines(paragraph, maxWidth);
        var total = (float) props.SpacingBeforePoints;
        foreach (var line in lines)
        {
            total += line.Height;
        }

        // An empty paragraph is a visual spacer — Word emits its mark's line but not the after-spacing.
        if (lines.Count != 1 || lines[0].Width > 0)
        {
            total += (float) props.SpacingAfterPoints;
        }

        return total;
    }

    // Pixels is the piece's unrounded linear device-pixel advance; accumulating it per line and
    // quantizing once (PixelsToPoints) is pen-position rounding, which composes across fonts. Text and
    // Properties are carried so the wrap can hand a painter each line's run segments — they never enter
    // the width/pitch arithmetic.
    readonly record struct Piece(bool IsSpace, double Pixels, float Pitch, string Text, RunProperties Properties, LaidOutImage? Image, bool IsBreak, bool IsTab);

    readonly record struct WrapSegment(float X, float Width, string Text, RunProperties Properties, TabLeader Leader = TabLeader.None);

    readonly record struct WrapLine(float Width, float Pitch, IReadOnlyList<WrapSegment> Segments, IReadOnlyList<LaidOutImage> Images);

    IReadOnlyList<WrapLine> Wrap(ParagraphElement paragraph, float maxWidth)
    {
        var pieces = Flatten(paragraph);
        var wrapWidth = maxWidth - (float) paragraph.Properties.LeftIndentPoints - (float) paragraph.Properties.RightIndentPoints;

        // Only the FIRST line is re-sized by paragraph indents: w:firstLine indents it right (narrower by
        // that much) and w:hanging outdents it left (wider by that much, extending toward the outer margin);
        // subsequent lines sit at the block's LeftIndent and keep the full wrapWidth. Both indents are zero
        // for a plain paragraph, so nothing changes. A LIST paragraph is exempt: its hanging indent only
        // positions the marker, the text stays at the LeftIndent on every line. The Fragmenter shifts the
        // first line's X by the same (FirstLineIndent − Hanging) so the wrap and the paint agree.
        var firstLineWidth = paragraph.Properties.Numbering is { Text.Length: > 0 }
            ? wrapWidth
            : wrapWidth
              - (float) paragraph.Properties.FirstLineIndentPoints
              + (float) paragraph.Properties.HangingIndentPoints;

        var lines = new List<WrapLine>();
        float LineWrapWidth() => lines.Count == 0 ? firstLineWidth : wrapWidth;
        double linePixels = 0, gapPixels = 0;
        float linePitch = 0, gapPitch = 0;
        var lineHasWord = false;
        var linePieces = new List<Piece>();
        var gapPieces = new List<Piece>();

        // A line that wrapped naturally (the next word did not fit) is justified when the paragraph is;
        // a line ended by a break or the paragraph's last line is not (Word leaves those at their natural
        // spacing). Justify distributes the leftover width evenly across the inter-word gaps.
        void CommitLine(bool justify)
        {
            var extraGapPixels = 0.0;
            if (justify && paragraph.Properties.Alignment == TextAlignment.Justify)
            {
                var gaps = 0;
                foreach (var piece in linePieces)
                {
                    if (piece.IsSpace)
                    {
                        gaps++;
                    }
                }

                if (gaps > 0)
                {
                    var slack = CanonicalTextMeasurer.PixelsFromPoints(LineWrapWidth()) - linePixels;
                    if (slack > 0)
                    {
                        extraGapPixels = slack / gaps;
                    }
                }
            }

            var (segments, images) = BuildLineItems(
                linePieces,
                extraGapPixels,
                paragraph.Properties.TabStops,
                paragraph.Properties.DefaultTabStopPoints,
                paragraph.Properties.LeftIndentPoints,
                maxWidth);
            lines.Add(new((float) CanonicalTextMeasurer.PixelsToPoints(linePixels), linePitch, segments, images));
        }

        var index = 0;
        while (index < pieces.Count)
        {
            if (pieces[index].IsBreak)
            {
                // A soft line break: whatever has accumulated becomes a line, then a fresh line starts. A
                // break with no preceding word emits a blank line at the break's pitch.
                if (!lineHasWord)
                {
                    linePitch = pieces[index].Pitch;
                }

                CommitLine(justify: false);
                linePixels = 0;
                linePitch = 0;
                lineHasWord = false;
                linePieces.Clear();
                gapPixels = 0;
                gapPitch = 0;
                gapPieces.Clear();
                index++;
                continue;
            }

            if (pieces[index].IsSpace)
            {
                gapPixels += pieces[index].Pixels;
                gapPitch = Math.Max(gapPitch, pieces[index].Pitch);
                gapPieces.Add(pieces[index]);
                index++;
                continue;
            }

            // Gather the whole word — every adjacent non-space piece, even across a run boundary (but not
            // across a forced break).
            double wordPixels = 0;
            float wordPitch = 0;
            var wordPieces = new List<Piece>();
            while (index < pieces.Count && !pieces[index].IsSpace && !pieces[index].IsBreak)
            {
                wordPixels += pieces[index].Pixels;
                wordPitch = Math.Max(wordPitch, pieces[index].Pitch);
                wordPieces.Add(pieces[index]);
                index++;
            }

            if (!lineHasWord)
            {
                linePixels = wordPixels;
                linePitch = wordPitch;
                linePieces.Clear();
                linePieces.AddRange(wordPieces);
                lineHasWord = true;
            }
            else if (CanonicalTextMeasurer.PixelsToPoints(linePixels + gapPixels + wordPixels) <= LineWrapWidth())
            {
                linePixels += gapPixels + wordPixels;
                linePitch = Math.Max(linePitch, Math.Max(gapPitch, wordPitch));
                linePieces.AddRange(gapPieces);
                linePieces.AddRange(wordPieces);
            }
            else
            {
                CommitLine(justify: true);
                linePixels = wordPixels;
                linePitch = wordPitch;
                linePieces.Clear();
                linePieces.AddRange(wordPieces);
            }

            gapPixels = 0;
            gapPitch = 0;
            gapPieces.Clear();
        }

        if (lineHasWord)
        {
            CommitLine(justify: false);
        }

        if (lines.Count == 0)
        {
            // An empty paragraph still occupies one line at its mark's pitch, with no runs to paint.
            lines.Add(new(0, MarkPitch(paragraph), [], []));
        }

        return lines;
    }

    // Coalesces a line's pieces (in order) into one segment per contiguous source run, and anchors each
    // inline image at the pen position it reached. A segment's X is PixelsToPoints of the pixels before
    // it, its Width the pen distance to the next boundary — the same rounding as the line width, so a
    // painter draws each font (and each image) at the right offset and strokes decorations across the
    // right span. An image breaks any running text segment.
    static (IReadOnlyList<WrapSegment> Segments, IReadOnlyList<LaidOutImage> Images) BuildLineItems(
        List<Piece> linePieces,
        double extraGapPixels,
        IReadOnlyList<TabStop> tabStops,
        double defaultTabStopPoints,
        double leftIndentPoints,
        double columnWidthPoints)
    {
        var segments = new List<WrapSegment>();
        var images = new List<LaidOutImage>();
        double cursor = 0, segmentStart = 0;
        var segmentText = new StringBuilder();
        RunProperties? segmentFont = null;

        void FlushSegment()
        {
            if (segmentFont == null)
            {
                return;
            }

            var x = (float) CanonicalTextMeasurer.PixelsToPoints(segmentStart);
            var width = (float) CanonicalTextMeasurer.PixelsToPoints(cursor) - x;
            segments.Add(new(x, width, segmentText.ToString(), segmentFont));
            segmentText.Clear();
            segmentFont = null;
        }

        for (var pieceIndex = 0; pieceIndex < linePieces.Count; pieceIndex++)
        {
            var piece = linePieces[pieceIndex];

            // A tab advances the pen to its resolved stop — the shared resolver handles left / centre /
            // right / decimal by measuring the text up to the next tab (or line end), and clamps a stop
            // past the column to the column edge. Position-dependent, so it happens here, not in Flatten.
            if (piece.IsTab)
            {
                FlushSegment();
                var afterTab = pieceIndex + 1;
                var tabStart = cursor;
                var cursorFromMargin = leftIndentPoints + CanonicalTextMeasurer.PixelsToPoints(cursor);
                var (destinationFromMargin, matchedStop, _) = TabStopResolver.Resolve(
                    cursorFromMargin,
                    () => MeasureFollowing(linePieces, afterTab),
                    tabStops,
                    defaultTabStopPoints,
                    leftIndentPoints,
                    availableEndX: columnWidthPoints);
                var advanced = CanonicalTextMeasurer.PixelsFromPoints((float) (destinationFromMargin - leftIndentPoints));
                cursor = Math.Max(cursor, advanced);

                // A leadered stop (a TOC's dot leader, a signature underscore) leaves a filler across the gap
                // the tab opened; the painter tiles the glyph or strokes the rule. No leader on a default stop.
                if (matchedStop is { Leader: not TabLeader.None } && cursor > tabStart)
                {
                    var leaderX = (float) CanonicalTextMeasurer.PixelsToPoints(tabStart);
                    var leaderWidth = (float) CanonicalTextMeasurer.PixelsToPoints(cursor) - leaderX;
                    segments.Add(new(leaderX, leaderWidth, "", piece.Properties, matchedStop.Leader));
                }

                continue;
            }

            if (piece.Image is { } image)
            {
                FlushSegment();
                images.Add(image with { X = (float) CanonicalTextMeasurer.PixelsToPoints(cursor) });
                cursor += piece.Pixels;
                continue;
            }

            // When justifying, a space is a widened gap between words, not part of a segment: end the
            // current word and advance past the space plus its even share of the leftover width.
            if (extraGapPixels > 0 && piece.IsSpace)
            {
                FlushSegment();
                cursor += piece.Pixels + extraGapPixels;
                continue;
            }

            if (segmentFont == null)
            {
                segmentFont = piece.Properties;
                segmentStart = cursor;
            }
            else if (!ReferenceEquals(piece.Properties, segmentFont))
            {
                FlushSegment();
                segmentFont = piece.Properties;
                segmentStart = cursor;
            }

            segmentText.Append(piece.Text);
            cursor += piece.Pixels;
        }

        FlushSegment();
        return (segments, images);
    }

    // Points of text following a tab, up to the next tab or the line end — what a right / centre / decimal
    // stop aligns against.
    static double MeasureFollowing(List<Piece> linePieces, int startIndex)
    {
        double sum = 0;
        for (var index = startIndex; index < linePieces.Count && !linePieces[index].IsTab; index++)
        {
            sum += linePieces[index].Pixels;
        }

        return CanonicalTextMeasurer.PixelsToPoints(sum);
    }

    static RunProperties ParagraphFont(ParagraphElement paragraph) => MarkProperties(paragraph);

    List<Piece> Flatten(ParagraphElement paragraph)
    {
        var pieces = new List<Piece>();
        foreach (var run in paragraph.Runs)
        {
            // An inline image is an unbreakable box: it counts its display width toward the line width
            // (no text pitch — its height grows the line separately) and carries the drawable bytes.
            if (run.InlineImageData is { Length: > 0 } || run.InlineImageRasterFallbackData is { Length: > 0 })
            {
                var data = run.InlineImageContentType == "image/svg+xml"
                    ? run.InlineImageRasterFallbackData
                    : run.InlineImageData ?? run.InlineImageRasterFallbackData;
                if (data is { Length: > 0 })
                {
                    var imageWidth = (float) (run.InlineImageWidthPoints > 0 ? run.InlineImageWidthPoints : 12);
                    var imageHeight = (float) (run.InlineImageHeightPoints > 0 ? run.InlineImageHeightPoints : 12);
                    pieces.Add(new(false, CanonicalTextMeasurer.PixelsFromPoints(imageWidth), 0, "", run.Properties, new LaidOutImage(0, imageWidth, imageHeight, data, run.InlineImageRotationDegrees, run.InlineImageFlipHorizontal, run.InlineImageFlipVertical, run.InlineImageCrop), false, false));
                }

                continue;
            }

            // An inline shape group (a grouped drawing) is measured exactly like an inline image: an
            // unbreakable box whose display width counts toward the line and whose height grows the line,
            // carried on a LaidOutImage with no bytes and its ShapeGroup set. The display size is on the
            // run's inline-image extent, since the two are mutually exclusive.
            if (run.InlineShapeGroup is { } shapeGroup)
            {
                var groupWidth = (float) (run.InlineImageWidthPoints > 0 ? run.InlineImageWidthPoints : 12);
                var groupHeight = (float) (run.InlineImageHeightPoints > 0 ? run.InlineImageHeightPoints : 12);
                pieces.Add(new(false, CanonicalTextMeasurer.PixelsFromPoints(groupWidth), 0, "", run.Properties, new LaidOutImage(0, groupWidth, groupHeight, null, ShapeGroup: shapeGroup), false, false));
                continue;
            }

            // A tab advances the pen to the next tab stop; the advance is position-dependent, so it is
            // resolved in BuildLineItems, not here. Its pitch keeps a tab-only line the right height.
            if (run.IsTab)
            {
                var tabFont = resolveFont(run.Properties.FontFamily, run.Properties.Bold, run.Properties.Italic);
                var tabPitch = tabFont == null ? 0f : (float) tabFont.LinePitchPoints(run.Properties.FontSizePoints);
                pieces.Add(new(false, 0, tabPitch, "", run.Properties, null, false, true));
                continue;
            }

            if (run.Properties.Hidden || string.IsNullOrEmpty(run.Text))
            {
                continue;
            }

            var metrics = resolveFont(run.Properties.FontFamily, run.Properties.Bold, run.Properties.Italic);
            if (metrics is not { AdvanceWidths.Count: > 0 })
            {
                continue;
            }

            var size = run.Properties.FontSizePoints;
            var pitch = (float) metrics.LinePitchPoints(size);

            // w:spacing tracking widens every character's advance (letter-spacing), so it enters the wrap
            // and alignment maths through each token's pixel width — matching PdfTextEngine, which adds
            // CharacterSpacingPoints per character to a token's measured width.
            var trackingPerChar = run.Properties.CharacterSpacingPoints;

            // w:caps upper-cases the run; a soft line break (the parser emits it as "\n") splits the run
            // into parts with a forced break between them.
            var runText = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            var parts = runText.Split('\n');
            for (var partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                if (partIndex > 0)
                {
                    pieces.Add(new(false, 0, pitch, "", run.Properties, null, true, false));
                }

                foreach (var (text, isSpace) in TokenizeText(parts[partIndex]))
                {
                    var advance = CanonicalTextMeasurer.LinearPixels(metrics, text, size, fontWidthScale);
                    if (trackingPerChar != 0)
                    {
                        advance += CanonicalTextMeasurer.PixelsFromPoints((float) (trackingPerChar * text.Length));
                    }

                    pieces.Add(new(isSpace, advance, pitch, text, run.Properties, null, false, false));
                }
            }
        }

        return pieces;
    }

    float MarkPitch(ParagraphElement paragraph)
    {
        var mark = MarkProperties(paragraph);
        var metrics = resolveFont(mark.FontFamily, mark.Bold, mark.Italic);
        return metrics == null ? 0 : (float) metrics.LinePitchPoints(mark.FontSizePoints);
    }

    // The font that sizes a paragraph's mark line. Word sizes a blank line by the paragraph mark's own run
    // properties (w:rPr on w:pPr) — so the mark's rPr wins, mirroring PdfTextEngine.EmptyLineHeight. A
    // leading run stands in only when the mark carries no rPr: falling straight to a fresh RunProperties
    // (default font, 11pt) shrank empty spacer lines. The mark must win over a run to avoid a zero-length
    // leading run — a deleted-text artefact whose font differs from the mark (resumes/11 parks an empty
    // 11pt run over an 8pt mark) — over-sizing the spacer line by half a line each.
    static RunProperties MarkProperties(ParagraphElement paragraph) =>
        paragraph.Properties.ParagraphMarkRunProperties
        ?? (paragraph.Runs.Count > 0 ? paragraph.Runs[0].Properties : new RunProperties());

    // Splits text into maximal runs of spaces vs non-spaces (U+0020 only — the inter-word break),
    // matching CanonicalTextMeasurer.WrapLines.
    static IEnumerable<(string Text, bool IsSpace)> TokenizeText(string text)
    {
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || (text[i] == ' ') != (text[start] == ' '))
            {
                yield return (text[start..i], text[start] == ' ');
                start = i;
            }
        }
    }
}
