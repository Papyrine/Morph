/// <summary>
/// The <see cref="IParagraphMeasurer"/> surface over the backend-independent
/// <see cref="CanonicalTextMeasurer"/> — step 1 of the layout engine
/// (<c>docs/layout-engine.md</c>). It wraps a paragraph's runs and reports line heights,
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
        if (TakesNoLine(paragraph))
        {
            return [];
        }

        var props = paragraph.Properties;
        var wrapped = Wrap(paragraph, maxWidth);
        var result = new MeasuredLine[wrapped.Count];
        for (var i = 0; i < wrapped.Count; i++)
        {
            var height = (float) CanonicalTextMeasurer.LineHeightPoints(
                wrapped[i].Pitch, props.LineSpacingRule, props.LineSpacingMultiplier, props.LineSpacingPoints)
                         + 2 * RunBorderPad(wrapped[i]);

            // An inline image grows the line exactly as LayoutLineContents places it — measure and
            // placement must agree or a cell's row height omits its image: newsletters/05's school
            // photo (213pt, alone in a cell paragraph) measured as a 12pt mark line, and every row
            // below overlapped it by the difference. The image sits on the baseline, so the line
            // keeps the text descent under it (ImageLineHeight).
            var tallestImage = 0f;
            foreach (var image in wrapped[i].Images)
            {
                tallestImage = Math.Max(tallestImage, image.Height);
            }

            if (tallestImage > 0)
            {
                var ascent = (float) CanonicalTextMeasurer.LineAscentPoints(
                    NaturalAscent(paragraph, wrapped[i]), props.LineSpacingRule, props.LineSpacingMultiplier, props.LineSpacingPoints, wrapped[i].Pitch);
                height = ImageLineHeight(height, ascent, tallestImage, props.LineSpacingRule, HasText(wrapped[i]));
            }

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
        if (TakesNoLine(paragraph))
        {
            return [];
        }

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
                runs[segment] = new(segments[segment].X, segments[segment].Width, segments[segment].Text, segments[segment].Properties, segments[segment].Leader, segments[segment].BaselineShift);
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

            // A compressed multiple takes its space off the ascent too, so the baseline rides up with the
            // box rather than staying at the font's natural ascent; exact boxes hard-set the baseline at
            // 80% of the declared height, and an at-least box that grows anchors its ink at the bottom —
            // see LineAscentPoints for the probes.
            var naturalPitch = segments.Count == 0 ? MarkPitch(paragraph) : wrapped[i].Pitch;
            textAscent = (float) CanonicalTextMeasurer.LineAscentPoints(
                textAscent, props.LineSpacingRule, props.LineSpacingMultiplier, props.LineSpacingPoints, naturalPitch);

            // A run border (w:bdr) reserves its stack plus w:space above and below the font's line box —
            // the line grows by twice the tallest such reserve and the baseline drops by it, exactly as
            // LayoutLines charged. See BorderStroke.RunBorderReserve for the Word measurements.
            var runBorderPad = RunBorderPad(wrapped[i]);
            textHeight += 2 * runBorderPad;
            textAscent += runBorderPad;

            // An inline image sits with its bottom on the baseline, so it fills the whole ascent and can
            // grow the line: the ascent takes the max of the text metrics and the tallest image, and the
            // line keeps the text DESCENT under the baseline (ImageLineHeight).
            var images = wrapped[i].Images;
            var maxImageHeight = 0f;
            foreach (var image in images)
            {
                maxImageHeight = Math.Max(maxImageHeight, image.Height);
            }

            var hasText = HasText(wrapped[i]);
            var lineHeight = maxImageHeight > 0 ? ImageLineHeight(textHeight, textAscent, maxImageHeight, props.LineSpacingRule, hasText) : textHeight;

            // A line holding only images puts its baseline on the line BOTTOM — the image sits on the
            // bottom of a line that is max(mark pitch, image) tall, not on the mark font's baseline.
            // XPS-read on _probe_r12 (2026-09-05): a 3.6pt inline rectangle alone in a 12pt Calibri
            // Light paragraph has its baseline 14.8pt under the line top (the full 14.65 pitch), where a
            // text line's sits 11.4; resumes/12's coral rule sat 9px high on the mark ascent.
            var ascent = maxImageHeight > 0 && !hasText ? lineHeight : Math.Max(textAscent, maxImageHeight);
            result[i] = new(wrapped[i].Width, lineHeight, ascent, runs, images, wrapped[i].FootnoteReferenceIds, wrapped[i].EndnoteReferenceIds);
        }

        return result;
    }

    /// <summary>
    /// The height of a line carrying an inline image or shape: the image's bottom sits on the
    /// baseline, its height becomes the line's ascent when it is the taller, and the text's descent
    /// stays under the baseline — XPS-read on <c>_probe_inline</c> (2026-09-05; 6/24/48pt pictures
    /// and 4/12/30pt inline rectangles in a 10pt Calibri line): the line above a 24pt picture sits
    /// 24pt + its own descent over the baseline (27.6 from a 12pt line, 3.2 + 24), and the line
    /// below keeps the plain 12.2 pitch (2.7 descent + 9.5 ascent) — so the picture line is 24 + 2.7,
    /// not 24. Taking the max of pitch and image height dropped that descent on every image-bearing
    /// line (icon_with_text's paragraph after its inline star sat 10-13px high). An exact rule pins
    /// the line whatever it holds.
    ///
    /// <para>The descent is the TEXT runs' descent, so a line holding only the image keeps no
    /// descent under it: the corpus refuted adding one — cards/16, postcards/02, labels/13 and
    /// resumes/11 each stack image-only paragraphs (card art, label icons, short inline rules), and
    /// charging a descent to every one drifted them further from Word than the plain image height
    /// did (skia +0.03 AE on cards/16), while icon_with_text's text-bearing line still needs it.</para>
    /// </summary>
    static float ImageLineHeight(float textHeight, float textAscent, float tallestImage, LineSpacingRule rule, bool hasText)
    {
        if (rule == LineSpacingRule.Exactly)
        {
            return textHeight;
        }

        var descent = hasText ? Math.Max(0f, textHeight - textAscent) : 0f;
        return Math.Max(textHeight, tallestImage + descent);
    }

    // Whether a wrapped line carries any text glyphs beside its images.
    static bool HasText(WrapLine line)
    {
        foreach (var segment in line.Segments)
        {
            if (!string.IsNullOrWhiteSpace(segment.Text))
            {
                return true;
            }
        }

        return false;
    }

    // The natural baseline ascent of a wrapped line: the tallest of its segments, or the mark font for a
    // line with no text (or measured widths-only, with no segments built).
    float NaturalAscent(ParagraphElement paragraph, WrapLine line)
    {
        var ascent = 0f;
        foreach (var segment in line.Segments)
        {
            ascent = Math.Max(ascent, AscentPoints(segment.Properties));
        }

        return ascent > 0 ? ascent : AscentPoints(MarkProperties(paragraph));
    }

    // The tallest run-border reserve on a line, in points — zero when no run on it carries a w:bdr.
    static float RunBorderPad(WrapLine line)
    {
        var pad = 0d;
        foreach (var segment in line.Segments)
        {
            // A run whose text begins with a space draws its box inside the line and reserves nothing
            // (BorderStroke.RunBorderReserves).
            if (segment.Properties.Border is { } border && BorderStroke.RunBorderReserves(segment.Text))
            {
                pad = Math.Max(pad, BorderStroke.RunBorderReserve(border));
            }
        }

        return (float) pad;
    }

    public float MeasureParagraphNaturalWidth(ParagraphElement paragraph, float maxWidth) =>
        WidestLine(Wrap(paragraph, maxWidth));

    // The autofit MINIMUM probe (TableLayout.MeasureCellContentWidth) asks for this at a 1pt measure, where
    // every word lands on its own line — so the wrap it runs builds line contents once per WORD, for every
    // cell paragraph of every autofit table, and discards all of them to keep one number. It runs
    // widths-only for that reason, and caches the number rather than the lines: routing it through the
    // wrap memo would retain one WrapLine per word for the whole conversion. The wrap itself is the same
    // one MeasureParagraphNaturalWidth would run at 1pt, so the answer is unchanged.
    readonly ConcurrentDictionary<ParagraphElement, float> longestTokenCache = [];

    public float MeasureLongestTokenWidth(ParagraphElement paragraph) =>
        longestTokenCache.GetOrAdd(
            paragraph,
            static (key, measurer) => WidestLine(measurer.BuildWrap(key, minimumMeasure, buildItems: false)),
            this);

    // 1pt: narrow enough that no word fits beside another, so every word breaks onto its own line.
    const float minimumMeasure = 1f;

    static float WidestLine(IReadOnlyList<WrapLine> lines)
    {
        var widest = 0f;
        foreach (var line in lines)
        {
            if (line.Width > widest)
            {
                widest = line.Width;
            }
        }

        return widest;
    }

    // The baseline sits (line box − descent) below the line-box top — the line gap stacks above the text,
    // the descent below it (FontMetrics.BaselineAscentUnits, Word-probed across 23 faces). Neither the hhea
    // ascender (0.2 em high on Calibri) nor usWinAscent (0.07 em low on Aptos, the corpus default) is
    // that quantity; both were tried. The line PITCH is the same box, so the two agree by construction.
    float AscentPoints(RunProperties fontProperties)
    {
        var metrics = resolveFont(fontProperties.FontFamily, fontProperties.Bold, fontProperties.Italic);
        return metrics == null ? 0 : (float) metrics.BaselineAscentPoints(fontProperties.FontSizePoints);
    }

    /// <summary>
    /// The font's strikethrough stroke at a run's size: how far its top sits ABOVE the baseline, and its
    /// thickness on the 120-dpi grid (at least one grid pixel). Word draws a footnote or endnote
    /// separator as exactly this stroke of the separator paragraph's font — XPS-read 2026-09-05 on
    /// <c>_probe_fn_l/_m/_o</c>: Calibri 20pt puts the rule 4.8pt above the baseline at 1.2pt thick, Calibri
    /// 40pt 10.2 / 2.4, Times New Roman 40pt 10.2 / 1.8 and Aptos 12pt 4.2 / 0.6, each within a grid
    /// pixel of <c>yStrikeoutPosition</c> / <c>yStrikeoutSize</c> (0.25 / 0.0654, 0.259 / 0.0488 and
    /// 0.357 / 0.0498 em). A font with no <c>OS/2</c> stroke falls back to a quarter em, one pixel thick.
    /// </summary>
    public (float Above, float Thickness) StrikeoutBand(RunProperties fontProperties)
    {
        var size = fontProperties.FontSizePoints;
        var pixel = 72.0 / CanonicalTextMeasurer.ReferenceDpi;
        var metrics = resolveFont(fontProperties.FontFamily, fontProperties.Bold, fontProperties.Italic);
        if (metrics is not { StrikeoutSize: > 0 })
        {
            return ((float) (size * 0.25), (float) pixel);
        }

        var scale = size / metrics.UnitsPerEm;
        var thickness = Math.Max(1, Math.Round(metrics.StrikeoutSize * scale / pixel, MidpointRounding.AwayFromZero)) * pixel;
        return ((float) (metrics.StrikeoutPosition * scale), (float) thickness);
    }

    /// <summary>
    /// The font a paragraph's mark line takes — a note separator paragraph carries no runs, and its rule
    /// is drawn against this font's baseline (<see cref="StrikeoutBand"/>).
    /// </summary>
    public static RunProperties MarkFont(ParagraphElement paragraph) => MarkProperties(paragraph);

    /// <summary>
    /// The width in points of a short run of text in the given font — for placing a right-aligned list
    /// marker just before its text. Zero when the font does not resolve.
    /// </summary>
    public float MeasureRunWidth(string text, RunProperties properties)
    {
        var metrics = resolveFont(properties.FontFamily, properties.Bold, properties.Italic);
        return metrics == null ? 0 : (float) CanonicalTextMeasurer.MeasureWidthPoints(metrics, text, properties.FontSizePoints, fontWidthScale, KerningEnabled(properties));
    }

    /// <summary>
    /// The font a list marker draws in — the paragraph's first-run properties, swapped to the
    /// bullet face for glyph markers. Shared by the fragmenter's marker placement and the
    /// first-line shift below so measure and paint agree.
    /// </summary>
    internal static RunProperties MarkerProperties(ParagraphElement paragraph, NumberingInfo numbering)
    {
        var firstProperties = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Properties : new();
        var useBulletFont = FontHelpers.UseBulletFont(numbering.Text, numbering.FontFamily);
        return new()
        {
            FontFamily = useBulletFont ? "Morph Bullets" : firstProperties.FontFamily,
            FontSizePoints = firstProperties.FontSizePoints,
            Bold = !useBulletFont && firstProperties.Bold,
            ColorHex = numbering.ColorHex ?? firstProperties.ColorHex
        };
    }

    /// <summary>
    /// How far the FIRST line's text starts right of the paragraph's LeftIndent when the list
    /// marker overruns it. Word's numbering suffix is a tab: the text lands at the first stop
    /// past the marker's end — the text indent itself when the marker ends before it (the
    /// ordinary case, zero shift), else the next DEFAULT-interval stop. Probed at 24pt
    /// (<c>_probe_numtab</c>): a "888." ending 96.5pt from the margin put its text at exactly
    /// 108pt, the next 36pt multiple. A right-aligned marker ends AT the number position and
    /// never overruns. Continuation lines stay at the LeftIndent.
    /// </summary>
    public float MarkerTextShift(ParagraphElement paragraph)
    {
        var properties = paragraph.Properties;
        if (properties.Numbering is not { Text.Length: > 0 } numbering ||
            numbering.MarkerRightAligned)
        {
            return 0;
        }

        var hanging = (float) properties.HangingIndentPoints;
        if (hanging <= 0.01f)
        {
            // The no-hanging branch draws the marker to the LEFT of the text position.
            return 0;
        }

        var markerWidth = MeasureRunWidth(numbering.Text, MarkerProperties(paragraph, numbering));
        var markerEnd = properties.LeftIndentPoints - hanging + markerWidth;
        if (markerEnd <= properties.LeftIndentPoints - 0.01)
        {
            return 0;
        }

        var interval = properties.DefaultTabStopPoints > 0.01 ? properties.DefaultTabStopPoints : 36.0;
        var stop = (Math.Floor(markerEnd / interval) + 1) * interval;
        return (float) (stop - properties.LeftIndentPoints);
    }

    // Word applies pair kerning to a run whose size reaches the resolved w:kern threshold; zero
    // (the spec default when docDefaults declare no kern) disables it. The threshold itself is
    // resolved by the parser, including the built-in-Normal default for a document with no
    // docDefaults - see DocumentParser (todo #43, _probe_kern_* fixtures).
    static bool KerningEnabled(RunProperties properties) =>
        properties.KerningMinFontSizePoints > 0 && properties.FontSizePoints >= properties.KerningMinFontSizePoints;

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
    // BreaksBefore marks a piece that may start a line even though the piece before it is not a space —
    // the hyphen break opportunity (see TokenizeText). The word-gathering loop stops there instead of
    // gluing every adjacent non-space piece into one unbreakable word.
    // BaselineShift: the run's super/subscript shift (VerticalRunPosition), carried onto its segment.
    // IsInset: a run border's horizontal reserve (BorderStroke.RunBorderGlyphInset) — advances the pen
    // like text, joins its neighbouring word for wrapping, but belongs to no segment.
    // A piece carries the id of the footnote / endnote its run cites (on the run's first text piece
    // only), so the line it lands on knows which notes it brought with it.
    readonly record struct Piece(bool IsSpace, double Pixels, float Pitch, string Text, RunProperties Properties, LaidOutImage? Image, bool IsBreak, bool IsTab, PositionalTab? Positional = null, bool BreaksBefore = false, float BaselineShift = 0, bool IsInset = false, string? FootnoteReferenceId = null, string? EndnoteReferenceId = null);

    readonly record struct WrapSegment(float X, float Width, string Text, RunProperties Properties, TabLeader Leader = TabLeader.None, float BaselineShift = 0);

    readonly record struct WrapLine(float Width, float Pitch, IReadOnlyList<WrapSegment> Segments, IReadOnlyList<LaidOutImage> Images, IReadOnlyList<string>? FootnoteReferenceIds = null, IReadOnlyList<string>? EndnoteReferenceIds = null);

    // Wrapping is the whole cost of a measurement — Flatten resolves a font per run and measures every
    // piece, then BuildLineItems positions them — and the same (paragraph, width) pair is asked for over
    // and over: a table cell's row height and then its placement ask at the identical content width, and a
    // header or footer paragraph is asked once per page (a footer's twice, once for the height loop that
    // anchors the band and once for the band layout). Counted over the 330-scenario corpus, the memo
    // answers 44% of all wrap requests — 29,573 requests, 16,448 builds.
    //
    // The result is a pure function of the paragraph and the width: ParagraphElement is init-only, and a
    // paragraph whose text varies per page (a PAGE field) is CLONED by the fragmenter rather than mutated
    // (Fragmenter.SubstitutePageFields), so reference identity is a sound key. Width has to stay in it —
    // a wrapping float narrows the measure mid-document, so one paragraph legitimately wraps at two
    // widths in one flow. Lifetime is the measurer instance: production builds one per conversion, the
    // same lifetime the deleted raster backends gave their pagedLayoutCache/boundedLayoutCache.
    //
    // Concurrent because an instance CAN be shared — the layout spec tests hold a static measurer
    // (LayoutTestFonts.Measurer) that TUnit drives from parallel tests, which corrupted a plain
    // Dictionary here. Racing callers may both build the same entry; the function is pure, so the loser's
    // copy is simply discarded.
    //
    // Every result is shared, never copied, so nothing downstream may mutate a WrapLine's segment or image
    // list. Nothing does: the fragmenter's LaidOutLine → PlacedLine build allocates its own arrays.
    readonly ConcurrentDictionary<(ParagraphElement Paragraph, float MaxWidth), IReadOnlyList<WrapLine>> wrapCache = [];

    // Whether a run's face measures with Word's own advances (a .wordadvances sidecar) — the
    // gate for the space-compression wedge. Cached per RunProperties reference: a paragraph's
    // pieces share their run's properties instance.
    readonly ConcurrentDictionary<RunProperties, bool> wordAdvancesByProperties = [];

    bool HasWordAdvances(RunProperties properties) =>
        wordAdvancesByProperties.GetOrAdd(
            properties,
            _ => resolveFont(_.FontFamily, _.Bold, _.Italic) is {WordAdvances: not null});

    IReadOnlyList<WrapLine> Wrap(ParagraphElement paragraph, float maxWidth) =>
        wrapCache.GetOrAdd(
            (paragraph, maxWidth),
            static (key, measurer) => measurer.BuildWrap(key.Paragraph, key.MaxWidth, buildItems: true),
            this);

    // buildItems false skips BuildLineItems and leaves every line's segments and images empty. A line's
    // WIDTH is the accumulated pen pixels, quantized in CommitLine below — BuildLineItems only positions
    // what the line already contains — so the widths a caller reads are identical either way.
    // The most a single inter-word space gives up before Word wraps instead: one pixel of its
    // 120-dpi layout grid, independent of font size — 10pt Calibri spaces drop 2.4pt → 1.8pt
    // (4px → 3px) and never further (_probe_wedge, and the em-9 deltas in _probe_bp15's XPS).
    const double wedgeQuantumPoints = 0.6;

    IReadOnlyList<WrapLine> BuildWrap(ParagraphElement paragraph, float maxWidth, bool buildItems)
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
            ? wrapWidth - MarkerTextShift(paragraph)
            : wrapWidth
              - (float) paragraph.Properties.FirstLineIndentPoints
              + (float) paragraph.Properties.HangingIndentPoints;

        var lines = new List<WrapLine>();
        float LineWrapWidth() => lines.Count == 0 ? firstLineWidth : wrapWidth;
        double linePixels = 0, gapPixels = 0;
        float linePitch = 0, gapPitch = 0;
        var lineHasWord = false;
        var lineCompressed = false;
        var linePieces = new List<Piece>();
        var gapPieces = new List<Piece>();

        // A line that wrapped naturally (the next word did not fit) is justified when the paragraph is;
        // a line ended by a break or the paragraph's last line is not (Word leaves those at their natural
        // spacing). Justify distributes the leftover width evenly across the inter-word gaps.
        void CommitLine(bool justify)
        {
            if (!buildItems)
            {
                lines.Add(new((float) CanonicalTextMeasurer.PixelsToPoints(linePixels), linePitch, [], []));
                return;
            }

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
                lineCompressed,
                paragraph.Properties.TabStops,
                paragraph.Properties.DefaultTabStopPoints,
                paragraph.Properties.LeftIndentPoints,
                maxWidth);

            // The notes this line cites, in order — the fragmenter opens their page-bottom area when the
            // line lands. Null for the ordinary line, so nothing allocates.
            List<string>? footnoteIds = null;
            List<string>? endnoteIds = null;
            foreach (var piece in linePieces)
            {
                if (piece.FootnoteReferenceId is { } footnoteId)
                {
                    (footnoteIds ??= []).Add(footnoteId);
                }

                if (piece.EndnoteReferenceId is { } endnoteId)
                {
                    (endnoteIds ??= []).Add(endnoteId);
                }
            }

            lines.Add(new((float) CanonicalTextMeasurer.PixelsToPoints(linePixels), linePitch, segments, images, footnoteIds, endnoteIds));
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
                lineCompressed = false;
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
            // across a forced break). A piece marked BreaksBefore ends the gather: it is the text after a
            // hyphen, which Word may move to the next line. The first piece is always taken, so a word
            // that begins at a break opportunity still advances.
            double wordPixels = 0;
            float wordPitch = 0;
            var wordPieces = new List<Piece>();
            while (index < pieces.Count && !pieces[index].IsSpace && !pieces[index].IsBreak &&
                   (wordPieces.Count == 0 || !pieces[index].BreaksBefore))
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
            else if (TryCompress(wordPixels))
            {
                // Word compresses inter-word spaces rather than wrap when that lets the word fit.
                // Word-probed twice: resumes/16's own XPS first showed full lines whose 10pt Calibri
                // spaces advance 1.8pt against 2.4pt everywhere else, and the measure sweep
                // (_probe_wedge: one sentence over twelve right-indents) then exposed the real shape —
                // compression is PER SPACE and just-enough (mixes like 9 narrowed + 8 natural,
                // 14 + 3, 16 + 1, exactly the count the overhang needs), each space losing at most
                // one 120-dpi layout pixel = 0.6pt regardless of font size, and paragraph-level
                // compat flags do not gate it. So: a word wedges when the overhang is at most 0.6pt
                // per available space; the reclaim spreads evenly here where Word quantises which
                // spaces shrink, which keeps the line's total width exact and each origin within
                // half a pixel. One wedge per line — a wedged line is full. Without this the last
                // word wraps, and one such wrap pushed resumes/16 onto a second page.
                linePixels += gapPixels + wordPixels;
                linePitch = Math.Max(linePitch, Math.Max(gapPitch, wordPitch));
                linePieces.AddRange(gapPieces);
                linePieces.AddRange(wordPieces);
                lineCompressed = true;
            }
            else
            {
                CommitLine(justify: true);
                linePixels = wordPixels;
                linePitch = wordPitch;
                linePieces.Clear();
                linePieces.AddRange(wordPieces);
                lineCompressed = false;
            }

            gapPixels = 0;
            gapPitch = 0;
            gapPieces.Clear();
        }

        // The wedge test and, when it passes, the in-place shrink of every space accumulated so far
        // (the line's own and the pending gap) to 75% of its advance.
        bool TryCompress(double wordPixels)
        {
            if (lineCompressed)
            {
                return false;
            }

            var lineSpaces = 0;
            foreach (var piece in linePieces)
            {
                if (piece.IsSpace)
                {
                    lineSpaces++;
                }
            }

            var spaces = lineSpaces + gapPieces.Count;
            if (spaces == 0)
            {
                return false;
            }

            // The rule is Word's, but firing it through approximate advances misfires: whether the
            // overhang is inside the give is decided at sub-pixel precision, exactly where a font
            // model that is not Word's own turns near-fit lines into coin flips — adjudicated on
            // the corpus, the ungated wedge moved agendas-minutes/15 (Franklin Gothic) +0.055 AE
            // and repacked business-plans/15 (Tahoma-for-Univers) a page short, both fonts Morph
            // measures by its own metrics. So the wedge fires only where the measure IS Word's:
            // every text piece on the line resolves to a face carrying a .wordadvances sidecar.
            foreach (var piece in linePieces)
            {
                if (!piece.IsSpace && piece.Text.Length > 0 && !HasWordAdvances(piece.Properties))
                {
                    return false;
                }
            }

            // Whole pixels on the measurer's (and Word's) 120-dpi pen grid: the overhang the fit
            // test saw, expressed as the number of one-pixel space shrinks that cover it.
            var overhangPoints = CanonicalTextMeasurer.PixelsToPoints(linePixels + gapPixels + wordPixels) - LineWrapWidth();
            if (overhangPoints <= 0)
            {
                return false;
            }

            var quanta = (int) Math.Ceiling(overhangPoints / wedgeQuantumPoints - 0.0001);
            if (quanta > spaces)
            {
                return false;
            }

            // Shrink the first `quanta` spaces by exactly one pixel each — Word narrows a per-need
            // COUNT of spaces and leaves the rest natural (the probe's 9+8 / 14+3 / 16+1 mixes);
            // which specific spaces it picks is unmodelled, so these take the earliest.
            var remaining = quanta;
            for (var pieceIndex = 0; pieceIndex < linePieces.Count && remaining > 0; pieceIndex++)
            {
                if (linePieces[pieceIndex].IsSpace)
                {
                    linePieces[pieceIndex] = linePieces[pieceIndex] with {Pixels = linePieces[pieceIndex].Pixels - 1};
                    linePixels -= 1;
                    remaining--;
                }
            }

            for (var pieceIndex = 0; pieceIndex < gapPieces.Count && remaining > 0; pieceIndex++)
            {
                gapPieces[pieceIndex] = gapPieces[pieceIndex] with {Pixels = gapPieces[pieceIndex].Pixels - 1};
                gapPixels -= 1;
                remaining--;
            }

            return true;
        }

        if (lineHasWord)
        {
            CommitLine(justify: false);
        }
        else if (pieces.Count > 0 && pieces[^1].IsBreak)
        {
            // A paragraph ENDING in an explicit line break still carries its paragraph mark on the
            // line after it — Word draws that box. A cell of seven <w:br/> runs is EIGHT line boxes
            // in Word's render, and dropping the last left nonstandard_main_part_name's "Notes:"
            // cell exactly one 26px line short (213px against Word's 238).
            lines.Add(new(0, MarkPitch(paragraph), [], []));
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
        bool spacesAsGaps,
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
        var segmentShift = 0f;

        void FlushSegment()
        {
            if (segmentFont == null)
            {
                return;
            }

            var x = (float) CanonicalTextMeasurer.PixelsToPoints(segmentStart);
            var width = (float) CanonicalTextMeasurer.PixelsToPoints(cursor) - x;
            segments.Add(new(x, width, segmentText.ToString(), segmentFont, BaselineShift: segmentShift));
            segmentText.Clear();
            segmentFont = null;
        }

        for (var pieceIndex = 0; pieceIndex < linePieces.Count; pieceIndex++)
        {
            var piece = linePieces[pieceIndex];

            // A run border's side reserve: the pen moves, no segment grows — the glyph run starts after
            // it and ends before it, and the painters stroke the box outward from the glyphs into it.
            if (piece.IsInset)
            {
                FlushSegment();
                cursor += piece.Pixels;
                continue;
            }

            // A tab advances the pen to its resolved stop — the shared resolver handles left / centre /
            // right / decimal by measuring the text up to the next tab (or line end), and clamps a stop
            // past the column to the column edge. Position-dependent, so it happens here, not in Flatten.
            if (piece.IsTab)
            {
                FlushSegment();
                var afterTab = pieceIndex + 1;
                var tabStart = cursor;
                var cursorFromMargin = leftIndentPoints + CanonicalTextMeasurer.PixelsToPoints(cursor);

                double destinationFromMargin;
                TabStop? matchedStop;
                if (piece.Positional is { } positional)
                {
                    // w:ptab snaps to no stop list — it jumps to a position taken from the text area
                    // and aligns the following text there. Word offers margin / indent / page bases;
                    // margin and page coincide with the measure the paragraph is laid out in here,
                    // while indent starts at the paragraph's own left indent.
                    var basePosition = positional.RelativeTo == PositionalTabBase.Indent ? leftIndentPoints : 0;
                    var edge = columnWidthPoints;
                    destinationFromMargin = positional.Alignment switch
                    {
                        TabAlignment.Right => edge - MeasureFollowing(linePieces, afterTab),
                        TabAlignment.Center => basePosition + (edge - basePosition - MeasureFollowing(linePieces, afterTab)) / 2,
                        _ => basePosition
                    };

                    // A ptab that would pull the text back behind the pen collapses, as a stop does.
                    destinationFromMargin = Math.Max(destinationFromMargin, cursorFromMargin);
                    matchedStop = positional.Leader == TabLeader.None
                        ? null
                        : new TabStop {PositionPoints = destinationFromMargin, Leader = positional.Leader};
                }
                else
                {
                    (destinationFromMargin, matchedStop, _) = TabStopResolver.Resolve(
                        cursorFromMargin,
                        () => MeasureFollowing(linePieces, afterTab),
                        tabStops,
                        defaultTabStopPoints,
                        leftIndentPoints,
                        availableEndX: columnWidthPoints);
                }
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
            // current word and advance past the space plus its even share of the leftover width. A
            // COMPRESSED line (see the wedge in BuildWrap) does the same with its narrowed spaces, so
            // the painters place each word at the measurer's origin instead of drawing the string with
            // their own natural space advances.
            if ((extraGapPixels > 0 || spacesAsGaps) && piece.IsSpace)
            {
                FlushSegment();
                cursor += piece.Pixels + extraGapPixels;
                continue;
            }

            if (segmentFont == null)
            {
                segmentFont = piece.Properties;
                segmentStart = cursor;
                segmentShift = piece.BaselineShift;
            }
            else if (!ReferenceEquals(piece.Properties, segmentFont))
            {
                FlushSegment();
                segmentFont = piece.Properties;
                segmentStart = cursor;
                segmentShift = piece.BaselineShift;
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
                    pieces.Add(new(false, CanonicalTextMeasurer.PixelsFromPoints(imageWidth), 0, "", run.Properties, new LaidOutImage(0, imageWidth, imageHeight, data, run.InlineImageRotationDegrees, run.InlineImageFlipHorizontal, run.InlineImageFlipVertical, run.InlineImageCrop, Recolor: ImageRecolor.For(run.InlineImageColorEffect, run.InlineImageDuotoneColorHex, run.InlineImageDuotoneLightColorHex), Opacity: run.InlineImageOpacity), false, false));
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
                pieces.Add(new(false, 0, tabPitch, "", run.Properties, null, false, true, run.PositionalTab));
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

            var noteFootnoteId = run.FootnoteReferenceId;
            var noteEndnoteId = run.EndnoteReferenceId;

            // A superscript or subscript measures at its reduced size and carries its shift; the line
            // pitch stays the run's declared size, since the reduced glyphs sit inside the full box.
            var size = VerticalRunPosition.RenderSizePoints(run.Properties);
            var pitch = (float) metrics.LinePitchPoints(run.Properties.FontSizePoints);
            var baselineShift = VerticalRunPosition.BaselineShiftPoints(
                run.Properties,
                (double) metrics.DescentUnits / metrics.UnitsPerEm * run.Properties.FontSizePoints);

            // A run border reserves its glyph inset on both sides of the run (BorderStroke.RunBorderGlyphInset).
            var insetPixels = run.Properties.Border is { } runBorder
                ? CanonicalTextMeasurer.PixelsFromPoints((float) BorderStroke.RunBorderGlyphInset(runBorder))
                : 0;
            if (insetPixels > 0)
            {
                pieces.Add(new(false, insetPixels, pitch, "", run.Properties, null, false, false, IsInset: true));
            }

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

                foreach (var (token, isSpace, breaksBefore) in TokenizeText(parts[partIndex]))
                {
                    // A no-break space glues its neighbours into one unbreakable token (TokenizeText
                    // only splits on ' ', so it rides inside the word — Word never wraps at one),
                    // but it MEASURES and PAINTS as an ordinary space: fonts that don't map U+00A0
                    // resolved it to .notdef's wide advance, so "to improve" drew with a
                    // double-width gap where Word shows a single space (business-plans/05).
                    var text = token.Contains('\u00A0') ? token.Replace('\u00A0', ' ') : token;
                    var advance = CanonicalTextMeasurer.LinearPixels(metrics, text, size, fontWidthScale, KerningEnabled(run.Properties));
                    if (trackingPerChar != 0)
                    {
                        advance += CanonicalTextMeasurer.PixelsFromPoints((float) (trackingPerChar * text.Length));
                    }

                    // A note reference rides on the run's first piece — the line that takes it opens the
                    // note's page-bottom area (Fragmenter.CommitFootnotes).
                    pieces.Add(new(isSpace, advance, pitch, text, run.Properties, null, false, false, null, breaksBefore, baselineShift, FootnoteReferenceId: noteFootnoteId, EndnoteReferenceId: noteEndnoteId));
                    noteFootnoteId = null;
                    noteEndnoteId = null;
                }
            }

            if (insetPixels > 0)
            {
                pieces.Add(new(false, insetPixels, pitch, "", run.Properties, null, false, false, IsInset: true));
            }
        }

        return pieces;
    }

    // A runless paragraph that exists only to carry a section break's sectPr contributes its spacing but
    // no line box — see ParagraphElement.IsSectionBreakMark for the Word probe. A paragraph with runs
    // keeps its line whatever the flag says, so this can never swallow visible text.
    //
    // Deliberately NOT keyed on IsAnchorOnlyMark, the neighbouring "mark without a line box" flag the
    // parser sets for a paragraph whose only content was behind-text decorative art. That flag is
    // currently inert — the deleted production renderers consumed it and the engine never did — and
    // honouring it here as well regressed 104 of 108 changed pages (aggregate mean |Word−render| 8.4 →
    // 56.8, menus/08 and brochures/01 to ~190 grey levels), because those paragraphs anchor art whose
    // placement depends on the line. Reviving it is its own investigation, not a side effect of this one.
    static bool TakesNoLine(ParagraphElement paragraph) =>
        paragraph is {IsSectionBreakMark: true, Runs.Count: 0};

    float MarkPitch(ParagraphElement paragraph)
    {
        var mark = MarkProperties(paragraph);
        var metrics = resolveFont(mark.FontFamily, mark.Bold, mark.Italic);
        if (metrics == null)
        {
            return 0;
        }

        return (float) metrics.LinePitchPoints(mark.FontSizePoints);
    }

    // The font that sizes a paragraph's mark line. Word sizes a blank line by the paragraph mark's own run
    // properties (w:rPr on w:pPr) — so the mark's rPr wins, mirroring PdfTextEngine.EmptyLineHeight. A
    // leading run stands in only when the mark carries no rPr: falling straight to a fresh RunProperties
    // (default font, 11pt) shrank empty spacer lines. The mark must win over a run to avoid a zero-length
    // leading run — a deleted-text artefact whose font differs from the mark (resumes/11 parks an empty
    // 11pt run over an 8pt mark) — over-sizing the spacer line by half a line each. With neither rPr nor a
    // run, a bare ParagraphMarkFontSizePoints still sizes the mark at the default face — again mirroring
    // PdfTextEngine (`ParagraphMarkFontSizePoints ?? 11`): cards/05's header holds an empty 2pt-mark
    // paragraph, and ignoring the size reserved a full 11pt header line, starting every card table 11pt low.
    static RunProperties MarkProperties(ParagraphElement paragraph)
    {
        if (paragraph.Properties.ParagraphMarkRunProperties is { } markProperties)
        {
            return markProperties;
        }

        if (paragraph.Runs.Count > 0)
        {
            return paragraph.Runs[0].Properties;
        }

        // Neither the mark's own rPr nor a leading run: fall back to whatever the mark's size and face
        // resolved to. The FACE matters as much as the size here, since an auto line box comes straight
        // from the font's metrics — sizing the mark correctly but measuring it against the record's
        // default face still gets the pitch wrong.
        var markSize = paragraph.Properties.ParagraphMarkFontSizePoints;
        var markFamily = paragraph.Properties.ParagraphMarkFontFamily;
        if (markSize is { } size)
        {
            return markFamily == null
                ? new() {FontSizePoints = size}
                : new() {FontSizePoints = size, FontFamily = markFamily};
        }

        return markFamily == null ? new() : new() {FontFamily = markFamily};
    }

    // Splits text into maximal runs of spaces vs non-spaces (U+0020 only — the inter-word break),
    // then splits each non-space run AFTER every internal hyphen, matching
    // CanonicalTextMeasurer.WrapLines. The hyphen pieces carry BreaksBefore so the wrap may start a
    // line at one; they are separate pieces rather than one token because a break opportunity inside a
    // word is exactly what the gathering loop needs to see.
    static IEnumerable<(string Text, bool IsSpace, bool BreaksBefore)> TokenizeText(string text)
    {
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i != text.Length && text[i] == ' ' == (text[start] == ' '))
            {
                continue;
            }

            var token = text[start..i];
            if (text[start] == ' ')
            {
                yield return (token, true, false);
            }
            else
            {
                var first = true;
                foreach (var piece in CanonicalTextMeasurer.SplitAfterDashes(token))
                {
                    yield return (piece, false, !first);
                    first = false;
                }
            }

            start = i;
        }
    }

}
