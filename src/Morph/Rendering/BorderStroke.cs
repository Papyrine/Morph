/// <summary>
/// Turns a <see cref="BorderEdge"/> into the concrete lines a painter strokes, so Skia, ImageSharp
/// and PDF cannot drift apart on what "double" or "dotDash" means.
///
/// <para><b>Bands.</b> A multi-line style lays out as units of line and gap: <c>double</c> is 1-1-1
/// (line, gap, line), <c>triple</c> is 1-1-1-1-1, <c>thinThickSmallGap</c> is 1-1-2, and the Medium
/// and Large variants widen only the gap. What one unit MEASURES differs by family, and both halves
/// are Word-probed (see <c>_probe_bordersp</c> and the comment in <see cref="Bands"/>): for the
/// symmetric families a unit is the declared <c>w:sz</c> itself, so a 3pt <c>double</c> stacks to
/// 9pt, while the thin/thick family divides the declared width between its units. Offsets come back
/// relative to the edge's CENTRE, which is where the single-line styles already draw — so this
/// changes nothing for the plain <c>single</c> borders that make up almost the whole corpus.</para>
///
/// <para><b>Dashes.</b> Patterns are multiples of the declared width, which is how Word scales
/// them: a 3pt dashed border has visibly longer dashes than a 0.5pt one.</para>
///
/// <para><b>Bevels.</b> <c>threeDEngrave</c> and <c>threeDEmboss</c> are NOT a line/gap layout —
/// Word draws one contiguous block, part darkened and part at the declared colour, in opposite
/// order for the two. <c>inset</c> and <c>outset</c> carry no shading at all: at <c>sz=48</c> both
/// are solid at the declared grey. All of that was measured at 6pt (<c>_probe_bevel</c>), because
/// at the 0.75pt the fixture used originally every one of these collapses to 1-2px and
/// antialiasing is indistinguishable from a light line — reading `outset` at that size suggested a
/// highlight that does not exist.</para>
///
/// <para><b>Not modelled.</b> <c>wave</c> and <c>doubleWave</c> stroke straight (one and two lines)
/// — the sine path would need geometry in three painters and no corpus document uses either.</para>
/// </summary>
static class BorderStroke
{
    /// <summary>
    /// One stroked line within a border edge. <paramref name="Offset"/> is how far OUTWARD the
    /// line's centre sits from the border box, in points — always zero or positive, with the
    /// innermost band at zero so a plain <c>single</c> border draws exactly where it always did.
    /// <paramref name="Shade"/> multiplies the declared colour (1 = as declared, below 1 darker)
    /// for the bevel styles.
    ///
    /// <para>A painter must expand the line's SPAN by the same offset as well as displacing it, so
    /// the four edges' bands close into concentric rectangles. Drawing each band across the
    /// original box extent instead leaves the corners open — the verticals come out as stubs
    /// beside full-width horizontals — and stacking inward puts the innermost line through the
    /// text (a `triple` box rendered its label as "riple").</para>
    /// </summary>
    internal readonly record struct Band(double Offset, double Thickness, double Shade = 1);

    // The dark half of a three-D bevel. Word draws threeDEngrave/threeDEmboss as a CONTIGUOUS block
    // — not two separated lines — one part darkened and one part at the declared colour. Measured
    // off _probe_bevel at sz=48 (6pt), where antialiasing is negligible: an 808080 (grey 128) groove
    // comes out grey 53 across ~12px with a grey 127 strip of ~3px beside it, and a ridge is the
    // same two parts in the opposite order. 53/128 = 0.41.
    const double bevelShade = 0.41;

    // A declared width too small to resolve is floored per unit rather than divided into slivers,
    // which is what Word does — its own `double` at sz=6 spans ~5px against the 1.6px the declared
    // width allows. 0.75pt rather than the 0.5pt (1px at 150 DPI) Word uses, because Word draws
    // these pixel-aligned and unantialiased while Morph antialiases: at 0.5pt the gap is ~1px and
    // the neighbouring lines' antialiasing closes it, unpredictably by pixel phase (rendering the
    // fixture at 0.5pt merged the double box's TOP edge into one 4px run while its bottom edge
    // still split). Trades ~0.5px of gap for reproducing the structure a reader sees.
    const double minUnitPoints = 0.75;

    // Word's bevel block is wider than the declared width: that 6pt groove spans 19px = 9.1pt,
    // about 1.5x. Split 1.2 dark to 0.3 light, matching the ~12px/~3px measured.
    const double bevelDarkUnits = 1.2;
    const double bevelLightUnits = 0.3;

    static readonly Band[] singleBand = [new(0, 1)];

    /// <summary>
    /// Which border a stroke belongs to. Word does not scale the two the same way — see the
    /// <c>perLine</c> comment in <see cref="Bands"/> — so the caller has to say.
    /// </summary>
    internal enum Scope
    {
        Paragraph,
        Cell
    }

    /// <summary>
    /// The lines making up this edge, thicknesses already scaled from <paramref name="totalWidth"/>.
    /// Returns empty for a style that draws nothing.
    /// </summary>
    internal static Band[] Bands(BorderLineStyle style, double totalWidth, Scope scope = Scope.Paragraph)
    {
        if (totalWidth <= 0 || style == BorderLineStyle.None)
        {
            return [];
        }

        // The three-D bevels are not a line/gap layout at all — see bevelShade. Two touching
        // sub-bands, dark then light for the engraved groove and light then dark for the embossed
        // ridge, with no gap between them.
        if (style is BorderLineStyle.ThreeDEngrave or BorderLineStyle.ThreeDEmboss)
        {
            var bevelUnit = Math.Max(totalWidth, minUnitPoints);
            var dark = bevelUnit * bevelDarkUnits;
            var light = bevelUnit * bevelLightUnits;
            // Touching sub-bands, innermost first. A groove is dark on the OUTSIDE (its top edge
            // reads dark then light going inward); a ridge is the other way round.
            return style == BorderLineStyle.ThreeDEngrave
                ? [new Band(0, light), new Band(light / 2 + dark / 2, dark, bevelShade)]
                : [new Band(0, dark, bevelShade), new Band(dark / 2 + light / 2, light)];
        }

        // Line/gap layout in units, outermost first. Odd entries are gaps.
        int[]? layout = style switch
        {
            BorderLineStyle.Double or BorderLineStyle.DoubleWave => [1, 1, 1],
            BorderLineStyle.Triple => [1, 1, 1, 1, 1],
            BorderLineStyle.ThinThickSmallGap => [1, 1, 2],
            BorderLineStyle.ThickThinSmallGap => [2, 1, 1],
            BorderLineStyle.ThinThickThinSmallGap => [1, 1, 2, 1, 1],
            BorderLineStyle.ThinThickMediumGap => [1, 2, 2],
            BorderLineStyle.ThickThinMediumGap => [2, 2, 1],
            BorderLineStyle.ThinThickThinMediumGap => [1, 2, 2, 2, 1],
            BorderLineStyle.ThinThickLargeGap => [1, 3, 2],
            BorderLineStyle.ThickThinLargeGap => [2, 3, 1],
            BorderLineStyle.ThinThickThinLargeGap => [1, 3, 2, 3, 1],
            _ => null
        };

        if (layout == null)
        {
            return [new(0, totalWidth)];
        }

        var units = 0;
        foreach (var segment in layout)
        {
            units += segment;
        }


        // For the symmetric families w:sz is the width of EACH LINE, not of the whole stack —
        // Word-probed with _probe_bordersp (an unbordered mark above and below each bordered
        // paragraph, so the mark-to-mark distance isolates exactly what the border reserves, A4,
        // 150 DPI). A `single` at sz=24 measured 109px mark-to-mark and a `double` at the same
        // sz=24 measured 134px: 25px = 12pt more, which is 6pt per edge on top of the 3pt a single
        // draws, i.e. two 3pt lines with a 3pt gap. Dividing sz between the lines instead gave 108px
        // and left that border 26px short of Word's. `double` at sz=6 measured 105px against
        // single's 100px, again the 1-1-1 stack at the declared width, and `triple` at sz=6 measured
        // 111px against a predicted 3.75pt stack.
        //
        // The thin/thick family does NOT follow that rule — thinThickLargeGap at sz=24 measured
        // 118px, a ~5pt stack rather than the 18pt six units of 3pt would give — so it keeps
        // dividing the declared width, which lands within 3px of Word. What Word actually does
        // there is unresolved; this is fitted to the measurement, not derived.
        //
        // Nor does a TABLE CELL border, which is why the scope is a parameter. Measured directly off
        // Word's render of table_default_style, whose table style declares `double` at sz=12: the
        // drawn rule is 3px at 150 DPI — 1.44pt, the declared width as a total — where per-line
        // would be 4.5pt / 9.4px. The paragraph measurements above are equally direct (the sz=24
        // double draws lines at y=386 and y=399 of border_style_variants p3, a 19px stack), so the
        // two scopes genuinely differ in Word rather than one reading being wrong. Applying the
        // paragraph rule to cells widened table_default_style's rules and cost it 0.0524 -> 0.0546 AE.
        var perLine = scope == Scope.Paragraph &&
            style is BorderLineStyle.Double or BorderLineStyle.Triple or BorderLineStyle.DoubleWave;

        var unit = Math.Max(perLine ? totalWidth : totalWidth / units, minUnitPoints);
        var bands = new Band[(layout.Length + 1) / 2];

        // The layout reads outermost-first, so walk it BACKWARDS: the innermost line sits on the
        // border box at offset 0 (which is where a single line draws, so single borders do not
        // move) and each further line stacks outward by half of itself, the gap, and half of its
        // neighbour. Growing outward is what keeps the stack off the text — Word anchors a
        // border's inner edge and thickens away from the content.
        var index = 0;
        var offset = 0d;
        var previousThickness = 0d;
        for (var i = layout.Length - 1; i >= 0; i -= 2)
        {
            var thickness = layout[i] * unit;
            if (index > 0)
            {
                var gap = layout[i + 1] * unit;
                offset += previousThickness / 2 + gap + thickness / 2;
            }

            bands[index++] = new(offset, thickness);
            previousThickness = thickness;
        }

        // A CELL border straddles its edge rather than growing outward from it: the edge is shared
        // with the neighbouring cell, so there is no "outward" side to thicken into. Word-measured
        // on labels/08, whose table declares `double` at 3pt — Word draws two thin rules ~10px
        // apart at 150 DPI, centred on the cell boundary, where growing outward moved the pair off
        // the boundary and added a third visible rule. Paragraph borders keep the outward stack,
        // which is what holds a wide one clear of the text.
        if (scope == Scope.Cell && bands.Length > 1)
        {
            var shift = bands[^1].Offset / 2;
            for (var i = 0; i < bands.Length; i++)
            {
                bands[i] = bands[i] with {Offset = bands[i].Offset - shift};
            }
        }

        return bands;
    }

    /// <summary>
    /// The dash pattern for this style as alternating on/off lengths in points, or null for a
    /// solid stroke.
    /// </summary>
    internal static float[]? DashPattern(BorderLineStyle style, double totalWidth)
    {
        if (totalWidth <= 0)
        {
            return null;
        }

        var w = (float) totalWidth;
        return style switch
        {
            BorderLineStyle.Dotted => [w, w],
            BorderLineStyle.Dashed => [w * 3, w * 2],
            BorderLineStyle.DashSmallGap => [w * 3, w],
            BorderLineStyle.DashDotStroked => [w * 3, w, w, w],
            BorderLineStyle.DotDash => [w * 3, w, w, w],
            BorderLineStyle.DotDotDash => [w * 3, w, w, w, w, w],
            _ => null
        };
    }

    /// <summary>
    /// How thick this edge actually DRAWS, which is what it should reserve in the flow — not the
    /// declared <c>w:sz</c>. They differ whenever the floor above kicks in: a `double` at sz=6
    /// declares 0.75pt but stacks to 2.25pt, and reserving only the declared width packs more
    /// paragraphs onto a page than Word fits.
    /// </summary>
    internal static double Extent(BorderLineStyle style, double totalWidth, Scope scope = Scope.Paragraph)
    {
        // The full thickness the stack occupies, from its innermost face to its outermost. Written
        // from the band offsets rather than assuming a direction, because a cell stack is centred
        // on its edge and so has bands on both sides of it.
        var bands = Bands(style, totalWidth, scope);
        if (bands.Length == 0)
        {
            return 0;
        }

        var inward = 0d;
        var outward = 0d;
        foreach (var band in bands)
        {
            inward = Math.Max(inward, band.Thickness / 2 - band.Offset);
            outward = Math.Max(outward, band.Offset + band.Thickness / 2);
        }

        return inward + outward;
    }

    /// <summary>Whether this edge draws anything at all.</summary>
    internal static bool Draws(BorderEdge edge) =>
        edge.IsVisible && edge.Style != BorderLineStyle.None && edge.WidthPoints > 0;

    internal static Band[] SingleBand => singleBand;
}
