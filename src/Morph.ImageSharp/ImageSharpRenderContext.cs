/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class ImageSharpRenderContext : RenderContextBase, IDisposable
{
    /// <summary>
    /// Per-instance collection that grows as faces are resolved. Each
    /// <see cref="LoadFace"/> call pre-loads every sibling face under the matched
    /// candidate name so <c>FamilyHandle.Family.CreateFont(size, FontStyle.Italic)</c>
    /// finds the italic variant even when the resolver's score-pick happened to be
    /// the Regular face. Mirrors the old <c>LoadFilesIntoSharedCollection</c> behavior
    /// inside the unified <see cref="FontResolver{TFont}"/> shape.
    /// </summary>
    readonly FontCollection sharedFontCollection = new();

    /// <summary>Path-keyed dedupe for <see cref="sharedFontCollection"/> — files only get added once.</summary>
    readonly HashSet<string> loadedPaths = [with(StringComparer.OrdinalIgnoreCase)];

    readonly FontResolver<FontFamilyHandle> resolver;

    public ImageSharpRenderContext(
        PageSettings pageSettings,
        int dpi,
        CompatibilitySettings? compatibility = null,
        double fontWidthScale = 1.0,
        Func<string, string?>? fontFallback = null,
        string? fontDirectory = null,
        bool? deterministicRendering = null) :
        base(pageSettings, dpi, compatibility, fontWidthScale, fontFallback, fontDirectory, deterministicRendering) =>
        // FontCollection / FontFamily don't own unmanaged state — no per-cache disposal needed.
        resolver = new(
            loadFace: LoadFace,
            systemFallback: fontDirectory == null ? TryResolveFromSystem : null,
            releaseFont: null,
            fontDirectory: fontDirectory,
            fontFallback: fontFallback,
            seed: FontResolver<FontFamilyHandle>.BuildBundledSeed(LoadFromBytes));

    static FontFamilyHandle LoadFromBytes(byte[] bytes)
    {
        // Wrap the face in its own single-style FontCollection so the returned
        // FontFamily exposes exactly the style this byte[] encodes. The resolver
        // only returns a seeded entry on an exact (family, weight, italic) match,
        // so PickAvailableStyle's later style lookup always hits the only style
        // present.
        var collection = new FontCollection();
        using var stream = new MemoryStream(bytes, writable: false);
        var family = collection.Add(stream);
        return new(family);
    }

    /// <summary>
    /// Loads every face matching the candidate name into <see cref="sharedFontCollection"/>
    /// (idempotent via <see cref="loadedPaths"/>) so the family that ImageSharp returns
    /// has every available style — Regular, Bold, Italic, BoldItalic — even though the
    /// resolver only score-picked one of them. This matters because at <c>GetFont</c> time
    /// we hand the family to <see cref="PickAvailableStyle"/>, which routes
    /// <c>CreateFont(size, FontStyle.Italic)</c> to the actual italic face inside the
    /// family. Without sibling pre-loading the italic face would never appear there.
    /// </summary>
    FontFamilyHandle? LoadFace(FontFace bestFace, IReadOnlyList<FontFace> allCandidateFaces)
    {
        foreach (var face in allCandidateFaces)
        {
            if (!loadedPaths.Add(face.Path))
            {
                continue;
            }

            try
            {
                if (face.Path.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
                {
                    sharedFontCollection.AddCollection(face.Path);
                }
                else
                {
                    sharedFontCollection.Add(face.Path);
                }
            }
            catch
            {
                // Individual file load failures don't fail the resolution; the resolver's
                // retry loop tries the next-best face if this one's family can't be located.
            }
        }

        if (bestFace.Family == null)
        {
            return null;
        }

        return sharedFontCollection.TryGet(bestFace.Family, out var family) ? new(family) : null;
    }

    /// <summary>
    /// OS font manager fallback. SixLabors' <see cref="SystemFonts"/> indexes by family
    /// name; numeric weight isn't filterable here, so style validation is deferred to
    /// <see cref="PickAvailableStyle"/> at <c>CreateFont</c> time.
    /// </summary>
    static FontFamilyHandle? TryResolveFromSystem(FontNameCandidates candidates, int targetWeight, bool targetItalic)
    {
        foreach (var name in FontFileCache.EnumerateCandidateNames(candidates))
        {
            if (SystemFonts.TryGet(name, out var family))
            {
                return new(family);
            }
        }

        return null;
    }

    public FontFamily GetFontFamily(string fontFamily, bool bold, bool italic) =>
        resolver.Resolve(fontFamily, bold, italic).Family;

    // Font allocations are a hot-path cost in ImageSharp (`family.CreateFont`
    // performs internal style lookup + font instantiation). The result is fully
    // determined by (family, size, style) — cache for the lifetime of the context.
    readonly Dictionary<(FontFamily, float, FontStyle), Font> fontCache = [];

    // First-level memo keyed by the raw run request so the per-fragment path is one
    // dictionary hit — the resolver walk, ImpliesBold suffix scans and the
    // PickAvailableStyle TryGetMetrics probes only run for new combinations.
    readonly Dictionary<(string Family, bool Bold, bool Italic, float Size), Font> requestFontCache = [];

    public Font GetFont(RunProperties props)
    {
        var fontSize = (float) props.FontSizePoints;

        // Subscript and superscript use reduced font size (approximately 58% per OpenXML convention)
        if (props.VerticalAlignment != VerticalRunAlignment.Baseline)
        {
            fontSize *= 0.58f;
        }

        return GetFontForFamily(props.FontFamily, fontSize, props.Bold, props.Italic);
    }

    // No synthetic bold here, unlike Skia (SkiaRenderContext.ShouldSyntheticallyEmbolden). Three
    // stroke-the-fill versions were built and all REVERTED 2026-07-22, each differing only in when
    // to fire. Stroke width was calibrated against Skia's ink ratio (bold/regular 1.507 vs 1.465 on
    // 24pt Franklin Gothic Book), so width is not the problem:
    //
    //   condition                                    scenarios   net AE     over-applies to
    //   !font.IsBold                                    41       +0.1127    already-heavy faces
    //   resolved OS/2 weight < 700                      38       +0.1127    real bold siblings
    //   both of the above                               33       +0.0933    (still ~2x Skia)
    //   both, with Skia's tapered stroke width          33       +0.0952    (marginally worse)
    //
    // The fourth row is the taper Skia uses for fake bold: a fraction of the DEVICE-space text
    // size, 1/24 at 9px easing to 1/32 at 36px, clamped. It is the more faithful formula — a flat
    // 1/32 runs up to 24% light on small text — but it only differs BELOW about 16pt, because at
    // 150 DPI anything from 17pt up is already past the 36px clamp. So it adds weight to small text
    // only, and the corpus went slightly further negative.
    //
    // Skia's own version moves 18 scenarios for +0.0320. Every attempt stayed roughly double that
    // and net-negative, and crops showed real over-application rather than the new-ink offset
    // penalty that made Skia's worth keeping (labels/15's script went from too thin to far heavier
    // than Word; resumes/02's display type gained weight Word lacks).
    //
    // NOT the blocker, contrary to an earlier note here: ImageSharp already resolves faces through
    // the SAME weight-aware FontResolver as Skia, and the picked face's OS/2 weight is available on
    // FontFace.Weight inside LoadFace. Plumbing it through changed the number by nothing at all.
    //
    // The structural difference is LoadFace's sibling pre-loading: every candidate face is added to
    // sharedFontCollection so PickAvailableStyle can find the italic variant, which means the
    // family often has a real Bold style even when the score-pick was Regular. Skia cannot hit this
    // — an SKTypeface is the single picked file — so the two backends are answering questions about
    // different things, and no combination of the flag and the weight reconciled them.
    //
    // labels/15's delta was +0.0138 under ALL THREE conditions, bit-identical. That was briefly
    // recorded here as unexplained; it is not. The scenario has exactly one qualifying run — 32pt
    // "Cochocib Script Latin Pro", bold, resolved face weight 400, no bold face bundled — and it
    // satisfies every one of the three conditions, so identical output is what should happen.
    //
    // Skia has emboldened that same run all along: the family name carries no weight suffix, so
    // ResolveTargetWeight gives 700 against the 400 face and the pre-existing "gap >= 200" clause
    // already fired. That is why landing the Franklin Gothic Book fix changed labels/15's Skia
    // baseline by zero pixels — only " Book"-style names (suffix -> target 400, gap 0) were missed.
    //
    // That was then blamed on stroke width, and the taper was tried (row four above). Wrong again,
    // and measuring the script GLYPHS ALONE — an earlier crop box had spanned the address lines
    // beside them — says why:
    //
    //   Word                        509 ink
    //   Skia, already emboldened    660     +29.7%
    //   ImageSharp, no synthesis    234     -54.0%
    //   ImageSharp, stroked         747     +46.8%
    //
    // Skia is 30% OVER Word on that script and has been since before any of this work: the family
    // name carries no weight suffix, so the long-standing "gap >= 200" clause always fired there.
    // Mechanical dilation is simply not a designed bold. A real bold script is redrawn, not
    // fattened, and sits far closer to its regular than a stroked outline does — so both backends
    // overshoot, ImageSharp further because its rasterization is heavier to begin with.
    //
    // Detecting "does a designed bold exist that we do not bundle" was then investigated and is a
    // DEAD END from the font file: PANOSE would be the obvious signal, but Cochocib Script reports
    // bFamilyType 2 (Latin Text), identical to Franklin Gothic Book, with the rest of its PANOSE
    // left at generic defaults. Nothing in OS/2 says whether a sibling weight exists elsewhere.
    //
    // The useful finding is that detection is the wrong frame, because THE CALIBRATION REFERENCE
    // WAS WRONG. A Word probe of one word in Franklin Gothic Book (no bold face bundled) at
    // 8/12/16/24/32/48pt, measured against the same document through Skia:
    //
    //   pt    Word bold adds    Skia synthesis adds
    //    8        +48.7%              +54.1%
    //   12        +30.7%              +41.2%
    //   16        +24.4%              +56.3%
    //   24        +19.0%              +46.3%
    //   32        +34.5%              +46.3%
    //   48        +27.9%              +48.7%
    //
    // Word's bold adds ~26% ink on average; Skia's synthesis adds ~46%, so Skia runs roughly 1.8x
    // heavy from 16pt up and the two only agree at 8-12pt. Every ImageSharp attempt above was
    // calibrated against SKIA's ink ratio (1.507 vs 1.465), so all of them inherited that error.
    //
    // That multi-font calibration was then done: 10 bold-less families spanning text sans, text
    // serif, geometric, display, script and handwriting, at 8/12/16/24/32/48pt, with ink measured
    // as threshold-free coverage. Two results:
    //
    //   * The overshoot is SIZE-INDEPENDENT. Skia/Word ratio runs 1.58-2.05 across the whole range
    //     with no trend, so no taper is warranted and the earlier non-monotonic per-size fractions
    //     were noise. Mean ratio ~1.9.
    //   * The variation is PER-TYPEFACE: Tw Cen MT 1.00, Playfair 1.26, Bahnschrift 1.30, Impact
    //     1.37, Kristen 1.50, Franklin Gothic Book 1.62, Trade Gothic 1.83, Baskerville Old 1.89,
    //     Vladimir Script 2.25, Cochocib Script 2.53.
    //
    // A fifth attempt used the resulting flat size/59 stroke (Skia is effectively size/32) and
    // measured +0.0698 over 33 scenarios — the best of the five, and per-scenario (+0.0021)
    // essentially Skia's accepted +0.0018. Ink matched Word well: labels/15's script went from
    // -22.5% to +5.3%, resumes/07's text from -33.6% to -17.0%.
    //
    // It was still reverted, and the crops are why: at that width resumes/07's "Company, location"
    // DOES NOT READ AS BOLD. Word's is unmistakably bold, the stroked version only marginally
    // heavier than regular. Matching Word's ink is not the same as matching Word's bold — a
    // designed bold redraws the letterforms with wider stems and altered proportions, so at ink
    // parity the text still looks unbolded. The trade has no winning side: size/32 makes text look
    // bold but overshoots scripts (+0.0933), size/59 matches script ink but fails the very case
    // the feature exists for (+0.0698).
    //
    // That is the end of outline dilation as an approach here. Anything further needs real weight:
    // bundle the missing bold faces, or instance a variable font's wght axis where one exists.

    Font GetOrCreateCachedFont(FontFamily family, float fontSize, FontStyle style)
    {
        var key = (family, fontSize, style);
        if (fontCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var font = family.CreateFont(fontSize, style);
        fontCache[key] = font;
        return font;
    }

    /// <summary>
    /// Returns the closest available style from the given family. Prefers the requested
    /// style, then drops italic, then drops bold, finally returning Regular.
    /// </summary>
    static FontStyle PickAvailableStyle(FontFamily family, FontStyle requested)
    {
        if (family.TryGetMetrics(requested, out _))
        {
            return requested;
        }

        // Ordered fallback attempts per requested style. For pure Bold requests, prefer
        // Regular over BoldItalic — wrong slant is more visually disruptive than missing
        // weight (e.g. some bundled font directories carry Regular/Italic/BoldItalic but
        // not Bold; falling to BoldItalic there would render upright text as italic).
        IEnumerable<FontStyle> fallbackOrder = requested switch
        {
            FontStyle.BoldItalic => [FontStyle.Bold, FontStyle.Italic, FontStyle.Regular],
            FontStyle.Bold => [FontStyle.Regular, FontStyle.BoldItalic, FontStyle.Italic],
            FontStyle.Italic => [FontStyle.BoldItalic, FontStyle.Regular, FontStyle.Bold],
            _ => [FontStyle.Bold, FontStyle.Italic, FontStyle.BoldItalic]
        };
        foreach (var candidate in fallbackOrder)
        {
            if (family.TryGetMetrics(candidate, out _))
            {
                return candidate;
            }
        }

        return requested;
    }

    /// <summary>
    /// Creates a Font for a given font family name and size in points.
    /// </summary>
    public Font GetFontForFamily(string fontFamily, float sizePoints, bool bold, bool italic)
    {
        var requestKey = (fontFamily, bold, italic, sizePoints);
        if (requestFontCache.TryGetValue(requestKey, out var cached))
        {
            return cached;
        }

        var family = GetFontFamily(fontFamily, bold, italic);

        var effectiveBold = bold || FontHelpers.ImpliesBold(fontFamily);

        var style = FontStyle.Regular;
        if (effectiveBold && italic)
        {
            style = FontStyle.BoldItalic;
        }
        else if (effectiveBold)
        {
            style = FontStyle.Bold;
        }
        else if (italic)
        {
            style = FontStyle.Italic;
        }

        var font = GetOrCreateCachedFont(family, sizePoints, PickAvailableStyle(family, style));
        requestFontCache[requestKey] = font;
        return font;
    }

    // TextOptions carries only (font, dpi=72, kerning) here and is never mutated after
    // construction — reuse one instance per combination instead of allocating per measured
    // token. Whitespace tokenizes into single-space words, so a large share of measure calls
    // are the same space glyph in a handful of fonts; its advance is memoized outright.
    readonly Dictionary<(Font Font, KerningMode Kerning), TextOptions> textOptionsCache = [];
    readonly Dictionary<(Font Font, KerningMode Kerning), float> spaceWidthCache = [];

    /// <summary>
    /// Measures text width in points. Uses DPI=72 so pixels equal points.
    /// </summary>
    public float MeasureText(Font font, string text, KerningMode kerning = KerningMode.Standard)
    {
        var key = (font, kerning);
        var isSpace = text == " ";
        if (isSpace && spaceWidthCache.TryGetValue(key, out var spaceWidth))
        {
            return spaceWidth;
        }

        if (!textOptionsCache.TryGetValue(key, out var options))
        {
            options = new(font)
            {
                Dpi = 72,
                KerningMode = kerning
            };
            textOptionsCache[key] = options;
        }

        var advance = TextMeasurer.MeasureAdvance(text, options);
        if (isSpace)
        {
            spaceWidthCache[key] = advance.Width;
        }

        return advance.Width;
    }

    /// <summary>
    /// Gets font height and baseline metrics in points.
    /// </summary>
    public static (float Height, float Baseline) GetFontMetrics(Font font)
    {
        var metrics = font.FontMetrics;
        var unitsPerEm = metrics.UnitsPerEm;
        var pointSize = font.Size;

        // Ascender is positive in design units
        var ascent = metrics.HorizontalMetrics.Ascender * pointSize / unitsPerEm;

        // Descender is negative in design units, we want positive value
        var descent = Math.Abs(metrics.HorizontalMetrics.Descender) * pointSize / unitsPerEm;

        // Include the hhea line gap: Word's single-spacing line box is the full
        // ascent + descent + line gap (verified against Word XPS output), and the
        // Skia backend and PdfSharp's GetHeight() use the same three-term box.
        var lineGap = Math.Max(0f, metrics.HorizontalMetrics.LineGap * pointSize / unitsPerEm);

        var height = ascent + descent + lineGap;

        return (height, ascent);
    }

    /// <summary>The font's external leading (hhea line gap) in points; 0 when the font has none.</summary>
    public static float GetLineGap(Font font)
    {
        var metrics = font.FontMetrics;
        return Math.Max(0f, metrics.HorizontalMetrics.LineGap * font.Size / metrics.UnitsPerEm);
    }

    // SolidBrush and SolidPen are immutable in ImageSharp.Drawing — every fragment
    // render currently allocates a fresh instance. Documents typically use a small set
    // of colours/widths, so caching by key gives near-100% hit rate after warm-up.
    readonly Dictionary<Color, SolidBrush> brushCache = [];
    readonly Dictionary<(Color, float), SolidPen> penCache = [];

    public SolidBrush GetBrush(Color color)
    {
        if (brushCache.TryGetValue(color, out var cached))
        {
            return cached;
        }
        var brush = new SolidBrush(color);
        brushCache[color] = brush;
        return brush;
    }

    public SolidPen GetPen(Color color, float width)
    {
        var key = (color, width);
        if (penCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var pen = new SolidPen(color, width);
        penCache[key] = pen;
        return pen;
    }

    // ---- processed-image cache ----
    // Decode + bicubic resize dominate repeated image draws: a header/footer logo pays them on
    // every page, a repeated body image on every occurrence. The pipeline mutates the image in
    // place, so the cache keys the source array identity plus the full processing recipe. The
    // ImageBrush that DrawingCanvas.DrawImage queues holds the source image until the page's
    // canvas timeline renders, so cached images live for the whole document and are disposed
    // with the context (this replaced the old per-page RetainForPage sink). Failed decodes
    // cache null — the call sites deliberately swallow undecodable images.

    readonly Dictionary<(byte[] Data, int Width, int Height, ImageCrop? Crop, BlipColorEffect Effect, float Rotation, bool FlipHorizontal, bool FlipVertical, string? DuotoneColorHex, string? DuotoneLightColorHex), Image<Rgba32>?> processedImageCache = [];

    /// <summary>
    /// Decoded image with crop → resize → recolor → rotate applied, cached for the context's
    /// lifetime. Callers must not mutate or dispose the result.
    /// </summary>
    public Image<Rgba32>? GetProcessedImage(byte[] data, int width, int height, ImageCrop? crop, BlipColorEffect effect, float rotationDegrees, bool flipHorizontal = false, bool flipVertical = false, string? duotoneColorHex = null, string? duotoneLightColorHex = null)
    {
        var key = (data, width, height, crop, effect, rotationDegrees, flipHorizontal, flipVertical, duotoneColorHex, duotoneLightColorHex);
        if (processedImageCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Image<Rgba32>? image = null;
        try
        {
            image = Image.Load<Rgba32>(data);
            if (crop is {HasPadding: true})
            {
                // Padding (negative srcRect): the picture occupies Expand's sub-rectangle inside
                // the frame; compose it onto a transparent canvas of the frame size — Mutate.Crop
                // can't reach outside the bitmap.
                var (paddedX, paddedY, paddedWidth, paddedHeight) = crop.Expand(0, 0, width, height);
                image.Mutate(_ => _.Resize(Math.Max(1, (int) Math.Round(paddedWidth)), Math.Max(1, (int) Math.Round(paddedHeight))));
                var composed = new Image<Rgba32>(width, height);
                var placed = image;
                composed.Mutate(_ => _.DrawImage(placed, new Point((int) Math.Round(paddedX), (int) Math.Round(paddedY)), 1f));
                image.Dispose();
                image = composed;
            }
            else
            {
                if (crop is {IsCropped: true})
                {
                    var srcLeft = (int) (crop.Left * image.Width);
                    var srcTop = (int) (crop.Top * image.Height);
                    var srcWidth = Math.Max(1, image.Width - srcLeft - (int) (crop.Right * image.Width));
                    var srcHeight = Math.Max(1, image.Height - srcTop - (int) (crop.Bottom * image.Height));
                    image.Mutate(_ => _.Crop(new(srcLeft, srcTop, srcWidth, srcHeight)));
                }

                image.Mutate(_ => _.Resize(width, height));
            }

            // Word's "Recolor" gallery presets.
            switch (effect)
            {
                case BlipColorEffect.Duotone when duotoneColorHex != null || duotoneLightColorHex != null:
                    // a:duotone maps luminance onto a dark->light ramp: out_c = dark_c + L*(light_c - dark_c).
                    // Greyscale first collapses every channel to L; the matrix then scales each
                    // channel by (light_c - dark_c) and adds the dark_c bias. Word's Recolor
                    // gallery pairs a dark colour with white; letters/02 pairs black with a
                    // tinted accent instead.
                    var dark = ParseColor(duotoneColorHex ?? "000000").ToPixel<Rgba32>();
                    var light = ParseColor(duotoneLightColorHex ?? "FFFFFF").ToPixel<Rgba32>();
                    var darkRed = dark.R / 255f;
                    var darkGreen = dark.G / 255f;
                    var darkBlue = dark.B / 255f;
                    var duotoneMatrix = new ColorMatrix(
                        light.R / 255f - darkRed, 0, 0, 0,
                        0, light.G / 255f - darkGreen, 0, 0,
                        0, 0, light.B / 255f - darkBlue, 0,
                        0, 0, 0, 1,
                        darkRed, darkGreen, darkBlue, 0);
                    image.Mutate(_ => _.Grayscale().Filter(duotoneMatrix));
                    break;
                case BlipColorEffect.Grayscale:
                case BlipColorEffect.Duotone:
                    image.Mutate(_ => _.Grayscale());
                    break;
                case BlipColorEffect.Washout:
                    // Word's washout: brightness +70%, contrast -50%. ImageSharp's
                    // Brightness/Contrast operate in 0–N space — these constants line up
                    // visually with Skia's color-matrix branch.
                    image.Mutate(_ => _.Brightness(1.7f).Contrast(0.5f));
                    break;
            }

            // a:xfrm/@flipH/@flipV mirror before the rotation so the rotation spins the mirrored
            // picture, matching Word's transform order (and the canvas-transform backends).
            // ImageSharp's FlipMode names the axis of REFLECTION RESULT: Horizontal = left-right
            // mirror, Vertical = top-bottom mirror.
            if (flipHorizontal)
            {
                image.Mutate(_ => _.Flip(FlipMode.Horizontal));
            }

            if (flipVertical)
            {
                image.Mutate(_ => _.Flip(FlipMode.Vertical));
            }

            if (rotationDegrees != 0)
            {
                image.Mutate(_ => _.Rotate(rotationDegrees));
            }
        }
        catch
        {
            image?.Dispose();
            image = null;
        }

        processedImageCache[key] = image;
        return image;
    }

    readonly Dictionary<(byte[] Data, int Width, int Height, ImageCrop? Crop), Image<Rgba32>?> ellipseClippedCache = [];

    /// <summary>
    /// A copy of <see cref="GetProcessedImage"/>'s result masked to its inscribed ellipse — the
    /// circular photo, as a standalone bitmap with everything outside the ellipse transparent.
    /// The group renderer draws this via <c>DrawImage</c> (which honours a pushed canvas rotation)
    /// instead of the <c>DrawingCanvas.Apply</c> clip (which doesn't), so a rotated group turns its
    /// photo. Cached and disposed with the context; callers must not mutate or dispose the result.
    /// </summary>
    public Image<Rgba32>? GetEllipseClippedImage(byte[] data, int width, int height, ImageCrop? crop)
    {
        var key = (data, width, height, crop);
        if (ellipseClippedCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var source = GetProcessedImage(data, width, height, crop, BlipColorEffect.None, rotationDegrees: 0);
        Image<Rgba32>? clipped = null;
        if (source != null)
        {
            clipped = new(width, height, Color.Transparent.ToPixel<Rgba32>());
            var ellipse = new EllipsePolygon(width / 2f, height / 2f, width, height);
            clipped.Mutate(ctx => ctx.Paint(canvas => canvas.Apply(ellipse, inner => inner.DrawImage(source, new Point(0, 0), 1f))));
        }

        ellipseClippedCache[key] = clipped;
        return clipped;
    }

    public static Color ParseColor(string? hexColor)
    {
        if (string.IsNullOrEmpty(hexColor) || hexColor == "auto")
        {
            return Color.Black;
        }

        if (hexColor.Length == 6 &&
            uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var rgb))
        {
            return Color.FromPixel(new Rgb24(
                (byte) ((rgb >> 16) & 0xFF),
                (byte) ((rgb >> 8) & 0xFF),
                (byte) (rgb & 0xFF)));
        }

        if (hexColor.Length == 8 &&
            uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var argb))
        {
            return Color.FromPixel(new Rgba32(
                (byte) ((argb >> 16) & 0xFF),
                (byte) ((argb >> 8) & 0xFF),
                (byte) (argb & 0xFF),
                (byte) ((argb >> 24) & 0xFF)));
        }

        return Color.Black;
    }

    public void Dispose()
    {
        foreach (var image in processedImageCache.Values)
        {
            image?.Dispose();
        }
        processedImageCache.Clear();
        foreach (var image in ellipseClippedCache.Values)
        {
            image?.Dispose();
        }
        ellipseClippedCache.Clear();
        resolver.Dispose();
    }
}

/// <summary>
/// Adapter around <see cref="FontFamily"/> (a struct in SixLabors.Fonts) so it can flow
/// through the reference-typed <see cref="FontResolver{TFont}"/> cache.
/// </summary>
sealed class FontFamilyHandle(FontFamily family)
{
    public FontFamily Family { get; } = family;
}
