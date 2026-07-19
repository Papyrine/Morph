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
