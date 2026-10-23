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
///
/// <para><c>squeezeJustifiedLines</c> is the one place a document's compatibility mode enters the
/// wrap: in mode 15 Word narrows the spaces of a justified line to take a word that does not quite fit
/// (<see cref="CompatibilitySettings.SqueezesJustifiedLines"/>).</para>
/// </summary>
sealed class CanonicalParagraphMeasurer(Func<string, bool, bool, FontMetrics?> resolveFont, double fontWidthScale = 1.0, bool squeezeJustifiedLines = false) : IParagraphMeasurer
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
            var grid = OnGrid(props, wrapped[i].Pitch);
            var height = (grid?.Height ?? (float) CanonicalTextMeasurer.LineHeightPoints(
                             wrapped[i].Pitch, props.LineSpacingRule, props.LineSpacingMultiplier, props.LineSpacingPoints))
                         + 2 * RunBorderPad(wrapped[i]);

            // An inline image grows the line exactly as LayoutLineContents places it — measure and
            // placement must agree or a cell's row height omits its image: newsletters/05's school
            // photo (213pt, alone in a cell paragraph) measured as a 12pt mark line, and every row
            // below overlapped it by the difference. The image sits on the baseline, so the line
            // keeps the text descent under it (ImageLineHeight).
            var tallestImage = 0f;
            foreach (var image in wrapped[i].Images)
            {
                tallestImage = Math.Max(tallestImage, image.BoxHeight);
            }

            if (tallestImage > 0)
            {
                var ascent = (float) CanonicalTextMeasurer.LineAscentPoints(
                    NaturalAscent(paragraph, wrapped[i], first: i == 0), props.LineSpacingRule, props.LineSpacingMultiplier, props.LineSpacingPoints, wrapped[i].Pitch)
                             + (grid?.AscentShift ?? 0);
                height = ImageLineHeight(height, ascent, tallestImage, props.LineSpacingRule, HasText(wrapped[i]), wrapped[i].Pitch);

                // A picture alone on its line has no pitch of its own in the wrap, so the leading a
                // multiple adds under it (ImageLineHeight) is taken off the MARK's pitch here, as
                // LayoutLineContents takes it. The mark's own line is deliberately not made a floor
                // for the measured height the way it is for the placed one: in a cell the mark's
                // font is not resolved (ParseParagraphProperties omits it there), and floored on the
                // stand-in an icon's row came out taller than Word's (cover-letters/06, resumes/19).
                if (wrapped[i].Segments.Count == 0 &&
                    props.LineSpacingRule == LineSpacingRule.Auto)
                {
                    var markPitch = MarkPitch(paragraph);
                    height += (float) CanonicalTextMeasurer.LineHeightPoints(markPitch, props.LineSpacingRule, props.LineSpacingMultiplier, props.LineSpacingPoints) - markPitch;
                }
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
                runs[segment] = new(segments[segment].X, segments[segment].Width, segments[segment].Text, segments[segment].Properties, segments[segment].Leader, segments[segment].BaselineShift, segments[segment].BorderInside);
                textAscent = Math.Max(textAscent, AscentPoints(segments[segment].Properties));
            }

            AlignToLargest(runs, props.FontAlignment);

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
            else if (i == 0)
            {
                // The first line of a list paragraph rises to its label (RaiseForLabel).
                textAscent = Math.Max(textAscent, LabelAscentPoints(paragraph));
            }

            // A compressed multiple takes its space off the ascent too, so the baseline rides up with the
            // box rather than staying at the font's natural ascent; exact boxes hard-set the baseline at
            // 80% of the declared height, and an at-least box that grows anchors its ink at the bottom —
            // see LineAscentPoints for the probes.
            var naturalPitch = segments.Count == 0 ? MarkPitch(paragraph) : wrapped[i].Pitch;
            textAscent = (float) CanonicalTextMeasurer.LineAscentPoints(
                textAscent, props.LineSpacingRule, props.LineSpacingMultiplier, props.LineSpacingPoints, naturalPitch);

            // On the document grid the box is whole grid pitches and the text is centred in it.
            if (OnGrid(props, naturalPitch) is { } grid)
            {
                textHeight = grid.Height;
                textAscent += grid.AscentShift;
            }

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
                maxImageHeight = Math.Max(maxImageHeight, image.BoxHeight);
            }

            var hasText = HasText(wrapped[i]);
            var lineHeight = maxImageHeight > 0 ? ImageLineHeight(textHeight, textAscent, maxImageHeight, props.LineSpacingRule, hasText, naturalPitch) : textHeight;

            // A line holding only images puts its baseline on the line BOTTOM — the image sits on the
            // bottom of a line that is max(mark pitch, image) tall, not on the mark font's baseline.
            // XPS-read on _probe_r12 (2026-09-05): a 3.6pt inline rectangle alone in a 12pt Calibri
            // Light paragraph has its baseline 14.8pt under the line top (the full 14.65 pitch), where a
            // text line's sits 11.4; resumes/12's coral rule sat 9px high on the mark ascent.
            // The leading a multiple adds lies under that image, as it lies under a line of text.
            var imageOnlyLeading = props.LineSpacingRule == LineSpacingRule.Auto ? Math.Max(0f, textHeight - naturalPitch) : 0f;
            var ascent = maxImageHeight > 0 &&
                         !hasText ? lineHeight - imageOnlyLeading : Math.Max(textAscent, maxImageHeight);

            // The leading an expanding multiple adds under the baseline may hang past the bottom margin;
            // the natural box above it has to fit (see Fragmenter's fit loop for the probe). It never
            // exceeds what lies under the baseline, which an image-only line does not have.
            var overhang = 0f;
            if (props.LineSpacingRule == LineSpacingRule.Auto)
            {
                var leading = textHeight - 2 * runBorderPad - naturalPitch;
                overhang = Math.Clamp(leading, 0f, Math.Max(0f, lineHeight - ascent));
            }

            result[i] = new(wrapped[i].Width, lineHeight, ascent, runs, images, wrapped[i].FootnoteReferenceIds, wrapped[i].EndnoteReferenceIds, overhang);
        }

        return result;
    }

    // w:textAlignment: each run moves by a fixed fraction of how much smaller it is than the line's
    // largest — up for top and center, down for bottom (FontAlignment, Word-probed). The runs stay inside
    // the largest run's box, so the line keeps its height.
    static void AlignToLargest(LaidOutRun[] runs, FontAlignment alignment)
    {
        if (alignment == FontAlignment.Baseline ||
            runs.Length < 2)
        {
            return;
        }

        var factor = alignment switch
        {
            FontAlignment.Top => 0.895f,
            FontAlignment.Center => 0.34f,
            _ => -0.105f
        };
        var largest = runs.Max(_ => (float) _.Properties.FontSizePoints);
        for (var index = 0; index < runs.Length; index++)
        {
            var smaller = largest - (float) runs[index].Properties.FontSizePoints;
            if (smaller > 0)
            {
                runs[index] = runs[index] with {BaselineShift = runs[index].BaselineShift + factor * smaller};
            }
        }
    }

    // The height a line takes on the document grid and how far its baseline drops for it
    // (CanonicalTextMeasurer.GridLine); null when the paragraph is not on the grid or its line rule is
    // not "auto", which Word lays out off the grid.
    static (float Height, float AscentShift)? OnGrid(ParagraphProperties props, float naturalPitch)
    {
        if (props.GridLinePitchPoints <= 0 ||
            props.LineSpacingRule != LineSpacingRule.Auto)
        {
            return null;
        }

        var (height, shift) = CanonicalTextMeasurer.GridLine(naturalPitch, props.GridLinePitchPoints, props.LineSpacingMultiplier);
        return ((float) height, (float) shift);
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
    ///
    /// <para>A line-spacing MULTIPLE adds to a picture line what it adds to the mark's own line, not
    /// a share of the picture: the line is the single-spaced one plus <c>(multiple − 1)</c> of the
    /// mark font's pitch, under the picture. Word-probed 2026-10-11 (<c>_probe_picline</c>: a 72pt
    /// picture alone in a paragraph between exact-pitch references, baselines off the XPS): under a
    /// 10pt Arial mark (11.5pt pitch) the line is 71.9 single, 77.9 at 1.5 and 83.9 double; under a
    /// 30pt mark (34.5) it is 71.9, 89.3 and 106.7, and 65.3 at 0.8, the multiple taking its share
    /// off as readily as adding it. Beside two letters of text the same three read 74.3, 79.7 and
    /// 113.3, the descent kept as above. The image-only line took the larger of the multiplied mark
    /// line and the picture, so under any multiple a tall picture lost the leading: 5.3pt under
    /// business-plans/02's page-wide photos, whose Normal style is spaced at 1.4.</para>
    /// </summary>
    static float ImageLineHeight(float textHeight, float textAscent, float tallestImage, LineSpacingRule rule, bool hasText, float naturalPitch)
    {
        if (rule == LineSpacingRule.Exactly)
        {
            return textHeight;
        }

        if (!hasText &&
            rule == LineSpacingRule.Auto)
        {
            return Math.Max(naturalPitch, tallestImage) + (textHeight - naturalPitch);
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
    float NaturalAscent(ParagraphElement paragraph, WrapLine line, bool first)
    {
        var ascent = SegmentAscent(line);
        if (ascent > 0)
        {
            // The first line of a list paragraph rises to its label (RaiseForLabel).
            return first ? Math.Max(ascent, LabelAscentPoints(paragraph)) : ascent;
        }

        return AscentPoints(MarkProperties(paragraph));
    }

    // The tallest run-border reserve on a line, in points — zero when no run on it carries a w:bdr.
    static float RunBorderPad(WrapLine line)
    {
        var pad = 0d;
        foreach (var segment in line.Segments)
        {
            // A run whose text begins with a space draws its box inside the line and reserves nothing
            // (BorderStroke.RunBorderReserves).
            if (segment.Properties.Border is { } border &&
                !segment.BorderInside)
            {
                pad = Math.Max(pad, BorderStroke.RunBorderReserve(border));
            }
        }

        return (float) pad;
    }

    // The widest line's UNROUNDED width (WrapLine.LinearWidth), which is what a caller that sizes a box
    // to its text needs: the fit test compares the unrounded width against the measure, so a column
    // sized to the pen-rounded width would wrap its own text whenever the rounding went down.
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
            static (key, measurer) => WidestLine(measurer.BuildWrap(key, minimumMeasure, buildItems: false, splitOverlong: false)),
            this);

    // 1pt: narrow enough that no word fits beside another, so every word breaks onto its own line.
    const float minimumMeasure = 1f;

    static float WidestLine(IReadOnlyList<WrapLine> lines)
    {
        var widest = 0f;
        foreach (var line in lines)
        {
            if (line.LinearWidth > widest)
            {
                widest = line.LinearWidth;
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
        if (metrics == null)
        {
            return 0;
        }

        return (float) metrics.BaselineAscentPoints(fontProperties.FontSizePoints);
    }

    // The same ascent before its descent is put on the pixel grid: what a line's box is made of, as
    // against where its baseline is drawn.
    float UnsnappedAscentPoints(RunProperties fontProperties)
    {
        var metrics = resolveFont(fontProperties.FontFamily, fontProperties.Bold, fontProperties.Italic);
        if (metrics == null)
        {
            return 0;
        }

        return (float) ((double) metrics.BaselineAscentUnits / metrics.UnitsPerEm * fontProperties.FontSizePoints);
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
    // The horizontal scale a run's advances measure at: the conversion's own font width scale, then the
    // run's w:w (RunProperties.CharacterScalePercent).
    double WidthScale(RunProperties properties) =>
        fontWidthScale * properties.CharacterScalePercent / 100;

    public float MeasureRunWidth(string text, RunProperties properties)
    {
        var metrics = resolveFont(properties.FontFamily, properties.Bold, properties.Italic);
        if (metrics == null)
        {
            return 0;
        }

        return (float) CanonicalTextMeasurer.MeasureWidthPoints(metrics, text, properties.FontSizePoints, WidthScale(properties), KerningEnabled(properties));
    }

    /// <summary>
    /// The font a list marker draws in — the paragraph's first-run properties, swapped to the
    /// bullet face for glyph markers, at the level's own size when it declares one. Shared by the
    /// fragmenter's marker placement and the first-line shift below so measure and paint agree.
    /// </summary>
    internal static RunProperties MarkerProperties(ParagraphElement paragraph, NumberingInfo numbering)
    {
        var firstProperties = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Properties : new();
        var useBulletFont = FontHelpers.UseBulletFont(numbering.Text, numbering.FontFamily);
        return new()
        {
            FontFamily = useBulletFont ? "Morph Bullets" : firstProperties.FontFamily,
            FontSizePoints = numbering.FontSizePoints ?? firstProperties.FontSizePoints,
            Bold = !useBulletFont &&
                   firstProperties.Bold,
            ColorHex = numbering.ColorHex ?? firstProperties.ColorHex
        };
    }

    /// <summary>
    /// How far a list label rises above its line's baseline, in points: the ascent of the face the
    /// label is declared in (<c>w:lvl/w:rPr/w:rFonts</c>) at the label's size. Word sets the label
    /// on the first line as it would a run, except that only its ascent counts — the line's descent
    /// stays the text's. Word-probed 2026-10-11 (<c>_probe_bullet</c>, <c>_probe_bulletcal</c>: one-line
    /// list paragraphs of 24pt Arial and of 24pt Calibri, pitch read between their baselines):
    ///
    /// <list type="bullet">
    /// <item>A Symbol bullet at the text's size sets Arial lines 29.4pt apart against the text's own
    /// 27.6 — Symbol's ascent (1.0054 em) over Arial's descent — and Calibri lines 30.4 against
    /// 29.3. A continuation line of the same paragraph keeps the text's pitch.</item>
    /// <item>The same bullet declared 48pt, or a 48pt "1.", sets the lines 53.4pt and 50.2pt apart:
    /// the label's ascent at its own size over the text's descent, and nothing for the label's
    /// descent (which alone would have taken the Symbol line to 58.8).</item>
    /// <item>A label smaller than the text (8pt), one in a face with less ascent (Courier New,
    /// Wingdings) and one with no face of its own change nothing.</item>
    /// <item>A label in Microsoft YaHei carries that face's East Asian leading above it (35.2pt).</item>
    /// </list>
    ///
    /// Symbol and Wingdings are not bundled (their glyphs draw from the embedded bullet subset), so
    /// their ascents are the faces' own <c>usWinAscent</c> over 2048: 2059 and 1841. Zero when the
    /// paragraph has no label, or the label's face is the text's and its size is not declared.
    /// </summary>
    float LabelAscentPoints(ParagraphElement paragraph)
    {
        if (paragraph.Properties.Numbering is not { } numbering || string.IsNullOrEmpty(numbering.Text))
        {
            return 0;
        }

        var firstProperties = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Properties : MarkProperties(paragraph);
        var size = numbering.FontSizePoints ?? firstProperties.FontSizePoints;
        var family = numbering.FontFamily;
        if (family == null)
        {
            // The label is in the text's face: it only stands taller when it is declared larger.
            return numbering.FontSizePoints == null ? 0 : AscentPoints(firstProperties with {FontSizePoints = size});
        }

        if (family.Equals("Symbol", StringComparison.OrdinalIgnoreCase))
        {
            return (float) (size * 2059 / 2048.0);
        }

        if (family.Equals("Wingdings", StringComparison.OrdinalIgnoreCase))
        {
            return (float) (size * 1841 / 2048.0);
        }

        if (FontHelpers.IsProprietaryBulletFont(family))
        {
            return 0;
        }

        var metrics = resolveFont(family, false, false);
        return metrics == null ? 0 : (float) metrics.BaselineAscentPoints(size);
    }

    /// <summary>
    /// How far the FIRST line's text starts right of the paragraph's LeftIndent (negative: left of
    /// it), past the list marker. Continuation lines stay at the LeftIndent.
    /// </summary>
    public float MarkerTextShift(ParagraphElement paragraph) =>
        MarkerPlacement(paragraph).TextShift;

    /// <summary>
    /// Where a list paragraph's first line puts its marker and its text, both relative to the
    /// paragraph's LeftIndent.
    ///
    /// The marker starts at the first line's own indent (left + firstLine − hanging), or ends there
    /// when right-aligned. What follows it is the level's suffix (<c>w:lvl/w:suff</c>), Word-probed in
    /// <c>_probe_list_suffix</c> at 28pt over three hanging indents:
    /// * a tab runs to the first stop past the marker's end — the hanging indent's own stop at the
    ///   LeftIndent when the marker ends before it (the ordinary case), else the next DEFAULT-interval
    ///   stop. <c>_probe_numtab</c>: a "888." ending 96.5pt from the margin put its text at exactly 108pt,
    ///   the next 36pt multiple. With no hanging indent there is no stop at the LeftIndent, so the text
    ///   goes to the next default stop: "1." at 36pt with its text at 72pt.
    /// * a space sets the text one space (in the marker's font) after the marker's end;
    /// * nothing sets the text against it.
    /// Custom tab stops are not consulted.
    /// </summary>
    public (float MarkerOffset, float TextShift) MarkerPlacement(ParagraphElement paragraph)
    {
        var properties = paragraph.Properties;
        if (properties.Numbering is not { Text.Length: > 0 } numbering)
        {
            return (0, 0);
        }

        var markerProperties = MarkerProperties(paragraph, numbering);
        var markerWidth = MeasureRunWidth(numbering.Text, markerProperties);
        var hanging = (float) properties.HangingIndentPoints;
        var start = (float) properties.FirstLineIndentPoints - hanging;

        // w:lvlJc="right" anchors the marker's RIGHT edge at the number position, the numeral
        // growing leftward into the margin so the periods of I./VIII./XVIII. line up —
        // _probe_numtab's right edges landed within 3px of the position at two geometries.
        float markerOffset;
        float markerEnd;
        if (numbering.MarkerRightAligned)
        {
            markerOffset = start - markerWidth;
            markerEnd = start;
        }
        else
        {
            markerOffset = start;
            markerEnd = start + markerWidth;
        }

        switch (numbering.Suffix)
        {
            case ListMarkerSuffix.Nothing:
                return (markerOffset, markerEnd);
            case ListMarkerSuffix.Space:
                return (markerOffset, markerEnd + MeasureRunWidth(" ", markerProperties));
        }

        if (start < -0.01f &&
            markerEnd <= -0.01f)
        {
            return (markerOffset, 0);
        }

        var interval = properties.DefaultTabStopPoints > 0.01 ? properties.DefaultTabStopPoints : 36.0;
        var absoluteEnd = properties.LeftIndentPoints + markerEnd;
        var stop = (Math.Floor(absoluteEnd / interval) + 1) * interval;
        return (markerOffset, (float) (stop - properties.LeftIndentPoints));
    }

    // Word applies pair kerning to a run whose size reaches the resolved w:kern threshold; zero
    // (the spec default when docDefaults declare no kern) disables it. The threshold itself is
    // resolved by the parser, including the built-in-Normal default for a document with no
    // docDefaults - see DocumentParser (_probe_kern_* and _probe_kerncascade fixtures). The painters
    // ask the same question to kern what they draw (CanonicalTextMeasurer.KernShiftsPoints).
    internal static bool KerningEnabled(RunProperties properties) =>
        properties.KerningMinFontSizePoints > 0 &&
        properties.FontSizePoints >= properties.KerningMinFontSizePoints;

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
    // like text, joins its neighbouring word for wrapping, but belongs to no segment. Closes marks the
    // one after the run's text, as against the one before it.
    // FitPixels is what the piece costs the line OVER what it moves the pen: a bordered run is drawn
    // at its pixel-floored inset and charged the whole declared stack and space (BuildWrap).
    // BorderInside: the piece is text of a bordered run that begins with a space, whose border grows no
    // line (BorderStroke.RunBorderReserves). It is the RUN's first character that decides, so the flag
    // rides on every piece of the run: the part of " boxed " that wraps to the next line starts with a
    // letter there, and Word still gives that line no extra height.
    // A piece carries the id of the footnote / endnote its run cites (on the run's first text piece
    // only), so the line it lands on knows which notes it brought with it.
    readonly record struct Piece(bool IsSpace, double Pixels, float Pitch, string Text, RunProperties Properties, LaidOutImage? Image, bool IsBreak, bool IsTab, PositionalTab? Positional = null, bool BreaksBefore = false, float BaselineShift = 0, bool IsInset = false, string? FootnoteReferenceId = null, string? EndnoteReferenceId = null, double FitPixels = 0, bool Closes = false, bool BorderInside = false);

    readonly record struct WrapSegment(float X, float Width, string Text, RunProperties Properties, TabLeader Leader = TabLeader.None, float BaselineShift = 0, bool BorderInside = false);

    // Width is the pen position the line reached, rounded once onto the pixel grid — where a painter
    // and an alignment place things. LinearWidth is the same total unrounded, which is the quantity Word
    // tests against the measure and sizes an autofit column from.
    readonly record struct WrapLine(float Width, float LinearWidth, float Pitch, IReadOnlyList<WrapSegment> Segments, IReadOnlyList<LaidOutImage> Images, IReadOnlyList<string>? FootnoteReferenceIds = null, IReadOnlyList<string>? EndnoteReferenceIds = null);

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

    IReadOnlyList<WrapLine> Wrap(ParagraphElement paragraph, float maxWidth) =>
        wrapCache.GetOrAdd(
            (paragraph, maxWidth),
            static (key, measurer) => measurer.RaiseForLabel(key.Paragraph, measurer.BuildWrap(key.Paragraph, key.MaxWidth, buildItems: true)),
            this);

    // A list paragraph's first line is as tall as its label needs: the label's ascent over the text's
    // descent, where that is more than the text's own box (LabelAscentPoints).
    IReadOnlyList<WrapLine> RaiseForLabel(ParagraphElement paragraph, IReadOnlyList<WrapLine> lines)
    {
        if (lines.Count == 0 || lines[0].Segments.Count == 0)
        {
            return lines;
        }

        // Against the text's ascent as the box has it, unsnapped: the probe's pitches are the label's
        // ascent over the text's fractional descent (Calibri's 6.45pt at 24pt, not the 6.6 its baseline
        // is drawn at).
        var textAscent = 0f;
        foreach (var segment in lines[0].Segments)
        {
            textAscent = Math.Max(textAscent, UnsnappedAscentPoints(segment.Properties));
        }

        var rise = LabelAscentPoints(paragraph) - textAscent;
        if (rise <= 0 || textAscent <= 0)
        {
            return lines;
        }

        var raised = new WrapLine[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            raised[index] = lines[index];
        }

        raised[0] = lines[0] with {Pitch = lines[0].Pitch + rise};
        return raised;
    }

    // The tallest ascent among a wrapped line's own runs; zero when it has none.
    float SegmentAscent(WrapLine line)
    {
        var ascent = 0f;
        foreach (var segment in line.Segments)
        {
            ascent = Math.Max(ascent, AscentPoints(segment.Properties));
        }

        return ascent;
    }

    // How far a line may run past its measure and still fit: a three-hundredth of a layout pixel. The
    // measure a line is tested against is a float that has usually been through arithmetic on the way
    // here. An autofit column is the case that bites. It is sized to its content's natural width plus
    // the cell's chrome (TableLayout.CalculateContentBasedColumnWidths), the cell takes the chrome back
    // off, and the text is then wrapped at a measure a few millionths of a point short of the width
    // that was read from it. Compared exactly, the last word of a cell wrapped inside the column sized
    // to hold it: table_cell_margin_per_cell's "Top margin emphasis" and seven more table fixtures.
    // It has to stay well under what Word resolves, though: at 0.012pt, which stood here first, eight
    // of the bordered-run probe's lines fitted a measure Word wraps them at, 0.017px short of it.
    const double fitTolerancePoints = 0.002;

    // A line's width as Word tests it against the measure: the linear pixels, NOT rounded onto the
    // pixel grid. Probed 2026-10-02 by stepping a right indent a pixel at a time: the narrowest
    // measure a sentence still fits is ceil(linear width) on every one of the 54 logged thresholds,
    // and on the 31 whose fraction is under one half the pen-rounded width (PixelsToPoints) predicts a
    // pixel less and matches none — it kept a line that ran up to half a pixel over. What is sized
    // FROM a line (an autofit column, a frame) reads the same unrounded width off WrapLine.LinearWidth,
    // so the text fits the box made for it; Word's own columns run 0.3px over it in compatibility
    // mode 12 and 0.7px over in mode 15.
    static float LinearPoints(double pixels) =>
        (float) (pixels * 72.0 / CanonicalTextMeasurer.ReferenceDpi);

    // The two limits of the justified squeeze (BuildWrap.FitsSqueezed): how much of its width a space
    // gives up at most, and how large the squeeze per space may be against the stretch per space of
    // the line one word shorter. The tolerance is for a squeeze that lands on a limit exactly, as
    // Times New Roman's 5px space does, after the widths have been summed in floating point.
    const double squeezeLimit = 0.25;
    const double squeezeToStretch = 0.5;
    const double squeezeTolerancePixels = 1e-6;

    static bool HasTab(List<Piece> pieces)
    {
        foreach (var piece in pieces)
        {
            if (piece.IsTab)
            {
                return true;
            }
        }

        return false;
    }

    // buildItems false skips BuildLineItems and leaves every line's segments and images empty. A line's
    // WIDTH is the accumulated pen pixels, quantized in CommitLine below — BuildLineItems only positions
    // what the line already contains — so the widths a caller reads are identical either way.
    /// <summary>
    /// A word wider than the line breaks where it overflows, as Word breaks it: the corpus layout
    /// invariants (LayoutInvariantTests) caught business-plans/13's "Marketing/Advertising", 7.8pt wider
    /// than its 90pt cell, running on into the next column where Word sets "Marketing/Advertis" and
    /// "ing" on two lines. A piece wider than <paramref name="widthPoints"/> is cut into the longest
    /// prefixes that fit, each after the first marked as a break opportunity, so the wrap sets them on
    /// lines of their own. A piece no wider than the line is left alone, as is one that is not text.
    /// </summary>
    List<Piece> SplitOverlong(List<Piece> pieces, float widthPoints)
    {
        // The wrap's own fit test, tolerance and all: a column autofit sized to its longest word holds
        // that word only within the tolerance, and must not see it split.
        bool Fits(double pixels) => LinearPoints(pixels) <= widthPoints + fitTolerancePoints;

        bool Overlong(Piece piece) =>
            piece is
            {
                IsSpace: false,
                Image: null,
                IsTab: false,
                IsInset: false
            } &&
            piece.Text.Length > 1 &&
            !Fits(piece.Pixels + piece.FitPixels);

        if (widthPoints <= 0 || !pieces.Exists(Overlong))
        {
            return pieces;
        }

        var result = new List<Piece>(pieces.Count + 4);
        foreach (var piece in pieces)
        {
            var properties = piece.Properties;

            if (!Overlong(piece) ||
                resolveFont(properties.FontFamily, properties.Bold, properties.Italic) is not { } metrics)
            {
                result.Add(piece);
                continue;
            }

            var size = VerticalRunPosition.RenderSizePoints(properties);
            var scale = WidthScale(properties);
            var tracking = properties.CharacterSpacingPoints;
            double Width(string text) =>
                CanonicalTextMeasurer.LinearPixels(metrics, text, size, scale) +
                (tracking != 0 ? CanonicalTextMeasurer.PixelsFromPoints((float) (tracking * text.Length)) : 0);

            var text = piece.Text;
            var first = true;
            while (text.Length > 0)
            {
                // The longest prefix that fits, of at least one character so the line always advances.
                var take = text.Length;
                while (take > 1 &&
                       !Fits(Width(text[..take])))
                {
                    take--;
                }

                if (char.IsHighSurrogate(text[take - 1]) &&
                    take < text.Length)
                {
                    take++;
                }

                var chunk = text[..take];
                result.Add(piece with
                {
                    Text = chunk,
                    Pixels = take == text.Length &&
                             first ? piece.Pixels : Width(chunk),
                    BreaksBefore = first ? piece.BreaksBefore : true,
                    FootnoteReferenceId = first ? piece.FootnoteReferenceId : null,
                    EndnoteReferenceId = first ? piece.EndnoteReferenceId : null
                });
                text = text[take..];
                first = false;
            }
        }

        return result;
    }

    IReadOnlyList<WrapLine> BuildWrap(ParagraphElement paragraph, float maxWidth, bool buildItems, bool splitOverlong = true)
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

        // Not for the autofit minimum: Word sizes a column to its longest word whole, never to a piece of it.
        if (splitOverlong)
        {
            pieces = SplitOverlong(pieces, Math.Min(wrapWidth, firstLineWidth));
        }

        var lines = new List<WrapLine>();
        float LineWrapWidth()
        {
            if (lines.Count == 0)
            {
                return firstLineWidth;
            }

            return wrapWidth;
        }
        // The pen (linePixels, gapPixels) is where things are drawn; the fit totals beside it are what a
        // line is charged over and above that when it is tested against the measure (Piece.FitPixels).
        double linePixels = 0, gapPixels = 0;
        double lineFit = 0, gapFit = 0;
        float linePitch = 0, gapPitch = 0;
        var lineHasWord = false;
        var linePieces = new List<Piece>();
        var gapPieces = new List<Piece>();

        // What the squeeze test below needs to know about the line so far: how many spaces it holds,
        // how wide they are, whether a tab sits on it (a tab's advance is not known until the line is
        // built, so such a line is never squeezed), and whether its last word was taken squeezed.
        var squeezes = squeezeJustifiedLines &&
                       paragraph.Properties.Alignment == TextAlignment.Justify;
        double lineSpacePixels = 0;
        var lineSpaces = 0;
        var lineHasTab = false;
        var lineSqueezed = false;

        void StartLine(double wordPixels, double wordFit, float wordPitch, List<Piece> wordPieces)
        {
            linePixels = wordPixels;
            lineFit = wordFit;
            linePitch = wordPitch;
            linePieces.Clear();
            linePieces.AddRange(wordPieces);
            lineSpacePixels = 0;
            lineSpaces = 0;
            lineHasTab = HasTab(wordPieces);
            lineSqueezed = false;
        }

        void ClearGap()
        {
            gapPixels = 0;
            gapFit = 0;
            gapPitch = 0;
            gapPieces.Clear();
        }

        // The spaces waiting in the gap: how many, and how wide. A bordered run's opening reserve can
        // wait there too (see the loop), and is not one.
        (int Count, double Pixels) GapSpaces()
        {
            var count = 0;
            var pixels = 0.0;
            foreach (var piece in gapPieces)
            {
                if (piece.IsSpace)
                {
                    count += piece.Text.Length;
                    pixels += piece.Pixels;
                }
            }

            return (count, pixels);
        }

        // Opens a line with a word. What waited in the gap ahead of it is left behind on the line
        // before, except a bordered run's opening reserve, which belongs to the word: at a wrap the
        // reserve alone comes along (Word leaves the run's leading space hanging at the end of the line
        // above and opens the next with the box), and where nothing was wrapped — the paragraph's own
        // start, or after a break — the spaces that follow the reserve inside the run stay too.
        void OpenLine(double wordPixels, double wordFit, float wordPitch, List<Piece> wordPieces, bool wrapped)
        {
            var opening = new List<Piece>();
            var pixels = 0.0;
            var fit = 0.0;
            var pitch = wordPitch;
            var reserved = false;
            foreach (var piece in gapPieces)
            {
                reserved |= piece.IsInset;
                if (!reserved || (wrapped &&
                                  !piece.IsInset))
                {
                    continue;
                }

                opening.Add(piece);
                pixels += piece.Pixels;
                fit += piece.FitPixels;
                pitch = Math.Max(pitch, piece.Pitch);
            }

            opening.AddRange(wordPieces);
            StartLine(pixels + wordPixels, fit + wordFit, pitch, opening);
            foreach (var piece in opening)
            {
                if (piece.IsSpace)
                {
                    lineSpaces += piece.Text.Length;
                    lineSpacePixels += piece.Pixels;
                }
            }
        }

        // A justified line in compatibility mode 15 takes one more word than fits, with its spaces
        // narrowed to make room, when both of these hold (Word-probed 2026-10-11, _probe_just15 and
        // _probe_just15b: 30 sentences of repeated words in Calibri, Times New Roman and Arial at 10 to
        // 48pt, each set once per whole pixel of measure, plus the same text in mode 12):
        //
        //  - the squeeze is at most a QUARTER of each space. Where the shorter line would be very loose
        //    the threshold sits there on every case: accepted at 0.250 of the space and refused at
        //    0.251 (Times New Roman's 5.000px space takes exactly 1.250px).
        //  - the squeeze per space is at most HALF the stretch per space the line would need without
        //    that word. Fifteen thresholds of 5 to 33 words bracket the factor between 0.497 and
        //    0.503. It is per space and not a ratio of totals: as totals the same cases accept 0.634
        //    and refuse 0.587.
        //
        // A line with no space left once the word is taken off it has nothing to stretch, and only the
        // quarter applies (two long words: accepted at 0.229, refused at 0.451). The paragraph's last
        // line is squeezed by the same test although it is not justified: sixteen words that end the
        // paragraph stay on one line down to the very measure at which they stay on the first line of
        // a longer paragraph. In mode 12 all 16 thresholds sit at ceil(linear width), as left-aligned
        // text does in either mode.
        bool FitsSqueezed(double candidatePixels, List<Piece> wordPieces)
        {
            if (lineHasTab || HasTab(wordPieces))
            {
                return false;
            }

            var (gapSpaces, gapSpacePixels) = GapSpaces();
            var spaces = lineSpaces + gapSpaces;
            if (spaces == 0)
            {
                return false;
            }

            var measurePixels = CanonicalTextMeasurer.PixelsFromPoints(LineWrapWidth());
            var over = candidatePixels - measurePixels;
            if (over > squeezeLimit * (lineSpacePixels + gapSpacePixels) + squeezeTolerancePixels)
            {
                return false;
            }

            if (lineSpaces == 0)
            {
                return true;
            }

            var stretch = (measurePixels - linePixels - lineFit) / lineSpaces;
            return over / spaces <= squeezeToStretch * stretch + squeezeTolerancePixels;
        }

        // A line that wrapped naturally (the next word did not fit) is justified when the paragraph is;
        // a line ended by a break or the paragraph's last line is not (Word leaves those at their natural
        // spacing). Justify distributes the leftover width evenly across the inter-word gaps. A squeezed
        // line gives its overrun back the same way whether it is justified or not, and ends at the measure.
        void CommitLine(bool justify)
        {
            var measurePixels = CanonicalTextMeasurer.PixelsFromPoints(LineWrapWidth());
            var endPixels = lineSqueezed ? Math.Min(linePixels, measurePixels) : linePixels;

            // What a box sized to this line has to be for the line to fit it again: the pen and what the
            // line is charged beyond it.
            var linearWidth = lineSqueezed ? LinearPoints(endPixels) : LinearPoints(linePixels + lineFit);
            if (!buildItems)
            {
                lines.Add(new((float) CanonicalTextMeasurer.PixelsToPoints(endPixels), linearWidth, linePitch, [], []));
                return;
            }

            var extraGapPixels = 0.0;
            if (lineSqueezed || (justify &&
                                 paragraph.Properties.Alignment == TextAlignment.Justify))
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
                    var slack = measurePixels - linePixels;
                    if (slack > 0 || lineSqueezed)
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

            var width = (float) CanonicalTextMeasurer.PixelsToPoints(endPixels);
            if (extraGapPixels == 0 &&
                images.Count == 0 &&
                !HasTab(linePieces))
            {
                width += WidenEmboldened(segments, CanonicalTextMeasurer.PixelsToPoints(measurePixels - endPixels - lineFit));
            }

            lines.Add(new(width, linearWidth, linePitch, segments, images, footnoteIds, endnoteIds));
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
                StartLine(0, 0, 0, []);
                lineHasWord = false;
                ClearGap();
                index++;
                continue;
            }

            // A space waits in the gap. So does a bordered run's opening reserve when the run's own
            // text begins with a space (" boxed "): it belongs to the word after that space, and must
            // not be left on a line the word does not fit.
            if (pieces[index].IsSpace ||
                (pieces[index] is
                {
                    IsInset: true,
                    Closes: false
                } &&
                 index + 1 < pieces.Count &&
                 pieces[index + 1].IsSpace))
            {
                gapPixels += pieces[index].Pixels;
                gapFit += pieces[index].FitPixels;
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
            double wordFit = 0;
            float wordPitch = 0;
            var wordPieces = new List<Piece>();
            while (index < pieces.Count &&
                   !pieces[index].IsSpace &&
                   !pieces[index].IsBreak &&
                   (wordPieces.Count == 0 ||
                    !pieces[index].BreaksBefore))
            {
                wordPixels += pieces[index].Pixels;
                wordFit += pieces[index].FitPixels;
                wordPitch = Math.Max(wordPitch, pieces[index].Pitch);
                wordPieces.Add(pieces[index]);
                index++;
            }

            // A bordered run's closing reserve that its own trailing spaces part from the word: the
            // word has already been charged for it (below), so it follows the word onto the line
            // unasked, the spaces between them with it.
            var closesAfterSpaces = lineHasWord &&
                                    gapPieces.Count > 0 &&
                                    wordPieces is [{IsInset: true, Closes: true}];

            // The closing reserve this word has to fit WITH, when spaces stand between the two. Word
            // charges a bordered run both its sides before the run's trailing space is counted.
            var closingReserve = 0.0;
            var ahead = index;
            while (ahead < pieces.Count &&
                   pieces[ahead].IsSpace)
            {
                ahead++;
            }

            if (ahead > index &&
                ahead < pieces.Count &&
                pieces[ahead] is
                {
                    IsInset: true,
                    Closes: true
                } closing)
            {
                closingReserve = closing.Pixels + closing.FitPixels;
                ahead++;
                while (ahead < pieces.Count &&
                       pieces[ahead].IsSpace)
                {
                    ahead++;
                }
            }

            // And the next bordered run's, when that run begins with a space. The space will hang at
            // the end of this line whether or not the run's word follows it here, and Word charges the
            // line both sides of the piece of the box the space stands in: of the probe's two-run
            // lines, the FIRST run stays up only from the measure that also holds the second run's
            // reserve (triple at 2.25pt: from 412px, where it alone needs 371).
            if (ahead > index &&
                ahead + 1 < pieces.Count &&
                pieces[ahead] is
                {
                    IsInset: true,
                    Closes: false
                } opening &&
                pieces[ahead + 1].IsSpace)
            {
                closingReserve += 2 * (opening.Pixels + opening.FitPixels);
            }

            if (!lineHasWord)
            {
                OpenLine(wordPixels, wordFit, wordPitch, wordPieces, wrapped: false);
                lineHasWord = true;
            }
            else
            {
                var candidatePixels = linePixels + lineFit + gapPixels + gapFit + wordPixels + wordFit + closingReserve;
                var fits = closesAfterSpaces || LinearPoints(candidatePixels) <= LineWrapWidth() + fitTolerancePoints;
                var squeezed = !fits &&
                               squeezes &&
                               FitsSqueezed(candidatePixels, wordPieces);
                if (fits || squeezed)
                {
                    var (gapSpaces, gapSpacePixels) = GapSpaces();
                    linePixels += gapPixels + wordPixels;
                    lineFit += gapFit + wordFit;
                    linePitch = Math.Max(linePitch, Math.Max(gapPitch, wordPitch));
                    linePieces.AddRange(gapPieces);
                    linePieces.AddRange(wordPieces);
                    lineSpacePixels += gapSpacePixels;
                    lineSpaces += gapSpaces;
                    lineHasTab |= HasTab(wordPieces);
                    lineSqueezed |= squeezed;
                }
                else
                {
                    CommitLine(justify: true);
                    OpenLine(wordPixels, wordFit, wordPitch, wordPieces, wrapped: true);
                }
            }

            ClearGap();
        }

        if (lineHasWord)
        {
            CommitLine(justify: false);
        }
        else if (pieces.Count > 0 &&
                 pieces[^1].IsBreak)
        {
            // A paragraph ENDING in an explicit line break still carries its paragraph mark on the
            // line after it — Word draws that box. A cell of seven <w:br/> runs is EIGHT line boxes
            // in Word's render, and dropping the last left nonstandard_main_part_name's "Notes:"
            // cell exactly one 26px line short (213px against Word's 238).
            lines.Add(new(0, 0, MarkPitch(paragraph), [], []));
        }

        if (lines.Count == 0)
        {
            // An empty paragraph still occupies one line at its mark's pitch, with no runs to paint.
            lines.Add(new(0, 0, MarkPitch(paragraph), [], []));
        }

        return lines;
    }

    // Coalesces a line's pieces (in order) into one segment per contiguous source run, and anchors each
    // inline image at the pen position it reached. A segment's X is PixelsToPoints of the pixels before
    // it, its Width the pen distance to the next boundary — the same rounding as the line width, so a
    // painter draws each font (and each image) at the right offset and strokes decorations across the
    // right span. An image breaks any running text segment.
    static (List<WrapSegment> Segments, IReadOnlyList<LaidOutImage> Images) BuildLineItems(
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
        var segmentShift = 0f;
        var segmentInside = false;

        void FlushSegment()
        {
            if (segmentFont == null)
            {
                return;
            }

            var x = (float) CanonicalTextMeasurer.PixelsToPoints(segmentStart);
            var width = (float) CanonicalTextMeasurer.PixelsToPoints(cursor) - x;
            segments.Add(new(x, width, segmentText.ToString(), segmentFont, BaselineShift: segmentShift, BorderInside: segmentInside));
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
                    destinationFromMargin = positional.Alignment switch
                    {
                        TabAlignment.Right => columnWidthPoints - MeasureFollowing(linePieces, afterTab),
                        TabAlignment.Center => basePosition + (columnWidthPoints - basePosition - MeasureFollowing(linePieces, afterTab)) / 2,
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
                if (matchedStop is
                    {
                        Leader: not TabLeader.None
                    } &&
                    cursor > tabStart)
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
            // current word and advance past the space plus its even share of the leftover width. On a
            // squeezed line the share is negative and the gap is narrowed by it.
            if (extraGapPixels != 0 &&
                piece.IsSpace)
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
                segmentInside = piece.BorderInside;
            }
            else if (!ReferenceEquals(piece.Properties, segmentFont))
            {
                FlushSegment();
                segmentFont = piece.Properties;
                segmentStart = cursor;
                segmentShift = piece.BaselineShift;
                segmentInside = piece.BorderInside;
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

                // An SVG picture carries its drawing too, when the reader could take the whole document:
                // the painters draw that and leave the fallback alone. It is also what lets a picture
                // stored as SVG only — no fallback to decode — take its place on the line at all.
                var vector = SvgDrawing.For(run.InlineImageData, run.InlineImageContentType);
                if (data is { Length: > 0 } || vector != null)
                {
                    var imageWidth = (float) (run.InlineImageWidthPoints > 0 ? run.InlineImageWidthPoints : 12);
                    var imageHeight = (float) (run.InlineImageHeightPoints > 0 ? run.InlineImageHeightPoints : 12);

                    // The line reserves the picture's layout box — the extent plus its wp:effectExtent on
                    // every side (ImageEffectExtent) — and the picture draws inside it.
                    var image = new LaidOutImage(0, imageWidth, imageHeight, data, run.InlineImageRotationDegrees, run.InlineImageFlipHorizontal, run.InlineImageFlipVertical, run.InlineImageCrop, Recolor: ImageRecolor.For(run.InlineImageColorEffect, run.InlineImageDuotoneColorHex, run.InlineImageDuotoneLightColorHex), Opacity: run.InlineImageOpacity, Outline: run.InlineImageOutline, EffectExtent: run.InlineImageEffectExtent, Vector: vector);
                    pieces.Add(new(false, CanonicalTextMeasurer.PixelsFromPoints(image.BoxWidth), 0, "", run.Properties, image, false, false));
                }

                continue;
            }

            // An inline shape group (a grouped drawing) is measured exactly like an inline image: an
            // unbreakable box whose display width counts toward the line and whose height grows the line,
            // carried on a LaidOutImage with no bytes and its ShapeGroup set. The display size is on the
            // run's inline-image extent, since the two are mutually exclusive.
            // An inline chart takes the same box, composed into it at layout; a pie or doughnut also
            // carries its slices, which the chart outranks.
            if (run.InlineShapeGroup is { } || run.InlineChart is { })
            {
                // A straight connector is a box with no height (or no width) at all, and that is its
                // size on the line: it stands on the baseline and adds nothing to the line's height.
                // The 12pt stand-in is for a group that declares no size either way. Giving a rule
                // 12pt of height drew it at the top of that box, 7pt above the baseline Word rules
                // (resumes/11's three divider lines).
                var sized = run.InlineImageWidthPoints > 0 || run.InlineImageHeightPoints > 0;
                var groupWidth = (float) (sized ? run.InlineImageWidthPoints : 12);
                var groupHeight = (float) (sized ? run.InlineImageHeightPoints : 12);
                var group = new LaidOutImage(0, groupWidth, groupHeight, null, ShapeGroup: run.InlineShapeGroup, EffectExtent: run.InlineImageEffectExtent, Chart: run.InlineChart);
                pieces.Add(new(false, CanonicalTextMeasurer.PixelsFromPoints(group.BoxWidth), 0, "", run.Properties, group, false, false));
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

            // A bold run in a face that is not bold is emboldened by the painters (BoldSimulation). One
            // copy of the properties per run, so its pieces still coalesce into one segment.
            var emboldened = BoldSimulation.For(run.Properties.Bold, metrics);
            var drawn = emboldened == SyntheticBold.None
                ? run.Properties
                : run.Properties with {SyntheticBold = emboldened};

            // A superscript or subscript measures at its reduced size and carries its shift; the line
            // pitch stays the run's declared size, since the reduced glyphs sit inside the full box.
            var size = VerticalRunPosition.RenderSizePoints(run.Properties);
            var pitch = (float) metrics.LinePitchPoints(run.Properties.FontSizePoints);
            var baselineShift = VerticalRunPosition.BaselineShiftPoints(
                run.Properties,
                (double) metrics.DescentUnits / metrics.UnitsPerEm * run.Properties.FontSizePoints);

            // A run border reserves its glyph inset on both sides of the run (BorderStroke.RunBorderGlyphInset):
            // that is where Word DRAWS the run's first glyph and the run after it. What the border costs
            // the LINE is more — the whole declared stack and space on each side, unfloored
            // (BorderStroke.RunBorderReserve), the same quantity it reserves above and below. Word-probed
            // 2026-10-11 (_probe_runbdr5): a line of six words and one or two bordered runs, set once per
            // pixel of measure in ten borders — `single` at 0.5, 0.75, 2.25 and 6pt with 0 to 8pt of
            // space, `double`, `triple` and three thin-thick families at 2.25pt — wraps its last bordered
            // run 2 x (stack + space) past the text's own width on all forty: 3.25pt a side for a 2.25pt
            // `single` with 1pt of space where the pen moves 2.4, 12.25 for the `triple` where it moves
            // 9.6. The difference rides on the piece as FitPixels, charged by the fit test and not by the
            // pen. border_style_variants' row of four bordered names is the corpus case: drawn, the
            // fourth ends 16pt short of the measure, and Word wraps it.
            var insetPixels = run.Properties.Border is { } runBorder
                ? CanonicalTextMeasurer.PixelsFromPoints((float) BorderStroke.RunBorderGlyphInset(runBorder))
                : 0;
            var insetFitPixels = run.Properties.Border is { } chargedBorder
                ? CanonicalTextMeasurer.PixelsFromPoints((float) BorderStroke.RunBorderReserve(chargedBorder)) - insetPixels
                : 0;
            if (insetPixels > 0)
            {
                pieces.Add(new(false, insetPixels, pitch, "", drawn, null, false, false, IsInset: true, FitPixels: insetFitPixels));
            }

            // w:spacing tracking widens every character's advance (letter-spacing), so it enters the wrap
            // and alignment maths through each token's pixel width — matching PdfTextEngine, which adds
            // CharacterSpacingPoints per character to a token's measured width.
            var trackingPerChar = run.Properties.CharacterSpacingPoints;

            // w:caps upper-cases the run; a soft line break (the parser emits it as "\n") splits the run
            // into parts with a forced break between them.
            var runText = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            var borderInside = run.Properties.Border != null &&
                               !BorderStroke.RunBorderReserves(runText);
            var parts = runText.Split('\n');
            for (var partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                if (partIndex > 0)
                {
                    pieces.Add(new(false, 0, pitch, "", drawn, null, true, false));
                }

                var kerning = KerningEnabled(run.Properties);
                var previousToken = -1;
                foreach (var (token, isSpace, breaksBefore) in TokenizeText(parts[partIndex]))
                {
                    // A no-break space glues its neighbours into one unbreakable token (TokenizeText
                    // only splits on ' ', so it rides inside the word — Word never wraps at one),
                    // but it MEASURES and PAINTS as an ordinary space: fonts that don't map U+00A0
                    // resolved it to .notdef's wide advance, so "to improve" drew with a
                    // double-width gap where Word shows a single space (business-plans/05).
                    var text = token.Contains('\u00A0') ? token.Replace('\u00A0', ' ') : token;
                    var advance = CanonicalTextMeasurer.LinearPixels(metrics, text, size, WidthScale(run.Properties), kerning);
                    if (trackingPerChar != 0)
                    {
                        advance += CanonicalTextMeasurer.PixelsFromPoints((float) (trackingPerChar * text.Length));
                    }

                    // The pair that straddles two tokens goes to the earlier one, whose last glyph it
                    // adjusts. Measuring token by token would otherwise drop every pair against a space,
                    // and Word's layout counts those (CanonicalTextMeasurer.KernPixelsBetween).
                    if (kerning &&
                        previousToken >= 0)
                    {
                        var between = CanonicalTextMeasurer.KernPixelsBetween(metrics, pieces[previousToken].Text, text, size, WidthScale(run.Properties));
                        if (between != 0)
                        {
                            pieces[previousToken] = pieces[previousToken] with {Pixels = pieces[previousToken].Pixels + between};
                        }
                    }

                    previousToken = pieces.Count;

                    // A note reference rides on the run's first piece — the line that takes it opens the
                    // note's page-bottom area (Fragmenter.CommitFootnotes).
                    pieces.Add(new(isSpace, advance, pitch, text, drawn, null, false, false, null, breaksBefore, baselineShift, FootnoteReferenceId: noteFootnoteId, EndnoteReferenceId: noteEndnoteId, BorderInside: borderInside));
                    noteFootnoteId = null;
                    noteEndnoteId = null;
                }
            }

            if (insetPixels > 0)
            {
                pieces.Add(new(false, insetPixels, pitch, "", drawn, null, false, false, IsInset: true, FitPixels: insetFitPixels, Closes: true));
            }
        }

        return pieces;
    }

    /// <summary>
    /// The properties a run outside a paragraph's own text is drawn with (a list marker): flagged for
    /// the bold simulation when it asks for bold in a face that is not bold, as <see cref="Flatten"/>
    /// flags a paragraph's runs.
    /// </summary>
    internal RunProperties Drawn(RunProperties properties)
    {
        if (!properties.Bold || properties.SyntheticBold != SyntheticBold.None)
        {
            return properties;
        }

        var metrics = resolveFont(properties.FontFamily, properties.Bold, properties.Italic);
        if (metrics == null)
        {
            return properties;
        }

        var emboldened = BoldSimulation.For(properties.Bold, metrics);
        if (emboldened == SyntheticBold.None)
        {
            return properties;
        }

        return properties with {SyntheticBold = emboldened};
    }

    // Draws a line's emboldened runs as wide as Word draws them: each glyph BoldSimulation.AdvancePoints
    // wider, with everything after it on the line moved along. Only where the line has the room, and
    // then by no more than the room: Word breaks the line on the regular advances and narrows the spaces
    // of one that would overrun, so a line that already fills its measure is left as it was laid out.
    // A justified or squeezed line has had its room given to its spaces, and a line with a tab or an
    // inline picture places things at positions this would have to move with it; those stay as they are.
    // A variable font's bold is not the simulation and keeps the regular advances. Returns the points
    // the line grew by.
    static float WidenEmboldened(List<WrapSegment> segments, double roomPoints)
    {
        var glyphs = 0;
        foreach (var segment in segments)
        {
            if (segment.Leader != TabLeader.None)
            {
                return 0;
            }

            if (segment.Properties.SyntheticBold == SyntheticBold.Simulated)
            {
                glyphs += new StringInfo(segment.Text).LengthInTextElements;
            }
        }

        if (glyphs == 0 || roomPoints <= 0)
        {
            return 0;
        }

        var perGlyph = Math.Min(BoldSimulation.AdvancePoints, roomPoints / glyphs);
        if (perGlyph < 0.05)
        {
            return 0;
        }

        var shift = 0f;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment.Properties.SyntheticBold != SyntheticBold.Simulated)
            {
                segments[index] = segment with {X = segment.X + shift};
                continue;
            }

            var grown = (float) (perGlyph * new StringInfo(segment.Text).LengthInTextElements);
            segments[index] = segment with
            {
                X = segment.X + shift,
                Width = segment.Width + grown,
                Properties = segment.Properties with {CharacterSpacingPoints = segment.Properties.CharacterSpacingPoints + perGlyph}
            };
            shift += grown;
        }

        return shift;
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
            if (markFamily == null)
            {
                return new() {FontSizePoints = size};
            }

            return new() {FontSizePoints = size, FontFamily = markFamily};
        }

        if (markFamily == null)
        {
            return new();
        }

        return new() {FontFamily = markFamily};
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
            if (i != text.Length &&
                text[i] == ' ' == (text[start] == ' '))
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