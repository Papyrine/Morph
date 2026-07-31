using Morph;

/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class SkiaRenderContext(
    PageSettings pageSettings,
    int dpi,
    CompatibilitySettings? compatibility = null,
    double fontWidthScale = 1.0,
    Func<string, string?>? fontFallback = null,
    string? fontDirectory = null,
    bool? deterministicRendering = null) :
    RenderContextBase(
        pageSettings,
        dpi,
        compatibility,
        fontWidthScale,
        fontFallback,
        fontDirectory,
        deterministicRendering),
    IDisposable
{
    readonly FontResolver<SKTypeface> resolver = new(
        loadFace: LoadFace,
        systemFallback: fontDirectory == null ? TryResolveFromSystem : null,
        releaseFont: ReleaseTypeface,
        fontDirectory: fontDirectory,
        fontFallback: fontFallback,
        seed: FontResolver<SKTypeface>.BuildBundledSeed(LoadFromBytes));

    static SKTypeface LoadFromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return SKTypeface.FromStream(stream);
    }

    /// <summary>
    /// Loads a single picked face. Returns <c>null</c> on failure (e.g. WOFF2, which is
    /// indexable by <see cref="OpenTypeReader"/> but not loadable by Skia) so the resolver
    /// can advance to the next-best face. The sibling-faces argument is ignored — an
    /// <c>SKTypeface</c> wraps a single file, so there's no benefit to pre-loading style
    /// variants the way ImageSharp does.
    /// </summary>
    static SKTypeface? LoadFace(FontFace face, IReadOnlyList<FontFace> _)
    {
        try
        {
            return face.Index == 0
                ? SKTypeface.FromFile(face.Path)
                : SKTypeface.FromFile(face.Path, face.Index);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// OS font manager fallback. Only used in default mode (not directory mode), so
    /// behaves exactly like the previous inline implementation.
    /// </summary>
    static SKTypeface? TryResolveFromSystem(FontNameCandidates candidates, int targetWeight, bool targetItalic)
    {
        var slant = targetItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var style = new SKFontStyle(targetWeight, (int) SKFontStyleWidth.Normal, slant);

        foreach (var name in FontFileCache.EnumerateCandidateNames(candidates))
        {
            var typeface = SKTypeface.FromFamilyName(name, style);
            if (typeface == null)
            {
                continue;
            }

            // SKTypeface.FromFamilyName never returns null but may collapse the requested
            // family into a parent (e.g. "Segoe UI Semilight" → "Segoe UI"). Accept only
            // if the returned FamilyName matches what we asked for, AND the returned face
            // honors the requested italic — otherwise we'd miss an italic variant in a
            // later cache.
            if (typeface.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                typeface.FamilyName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                var gotItalic = typeface.FontStyle.Slant != SKFontStyleSlant.Upright;
                if (gotItalic == targetItalic)
                {
                    return typeface;
                }
            }

            typeface.Dispose();
        }

        return null;
    }

    static void ReleaseTypeface(SKTypeface typeface) =>
        typeface.Dispose();

    public SKTypeface GetTypeface(string fontFamily, bool bold, bool italic) =>
        resolver.Resolve(fontFamily, bold, italic);

    // SKFont is allocated per fragment in the hot path, but is fully determined by
    // (typeface, scaledSize, embolden) — rendering mode is fixed per context. Cache
    // for the lifetime of the context (one document render); disposed in Dispose.
    readonly Dictionary<(SKTypeface, float, bool), SKFont> fontCache = [];

    // First-level memo keyed by the raw run request, so the per-fragment path is one
    // dictionary hit — the typeface resolve and the synthetic-embolden weight check
    // (suffix scans over the family name) only run when a new combination appears.
    // Values are the same instances the typeface-keyed cache owns and disposes.
    readonly Dictionary<(string Family, bool Bold, bool Italic, double Size, bool Reduced), SKFont> requestFontCache = [];

    public SKFont CreateFont(RunProperties props)
    {
        var reduced = props.VerticalAlignment != VerticalRunAlignment.Baseline;
        var requestKey = (props.FontFamily, props.Bold, props.Italic, props.FontSizePoints, reduced);
        if (requestFontCache.TryGetValue(requestKey, out var cached))
        {
            return cached;
        }

        var typeface = GetTypeface(props.FontFamily, props.Bold, props.Italic);
        var fontSize = (float) props.FontSizePoints;

        // Subscript and superscript use reduced font size (approximately 58% per OpenXML convention)
        if (reduced)
        {
            fontSize *= 0.58f;
        }

        var scaledSize = fontSize * Scale;
        var embolden = ShouldSyntheticallyEmbolden(typeface, props);
        var font = GetOrCreateCachedFont(typeface, scaledSize, embolden);
        requestFontCache[requestKey] = font;
        return font;
    }

    /// <summary>
    /// Creates an SKFont with consistent rendering properties from a typeface and font size.
    /// </summary>
    public SKFont CreateFontFromTypeface(SKTypeface typeface, float fontSizePoints) =>
        GetOrCreateCachedFont(typeface, fontSizePoints * Scale, embolden: false);

    SKFont GetOrCreateCachedFont(SKTypeface typeface, float scaledSize, bool embolden)
    {
        var key = (typeface, scaledSize, embolden);
        if (fontCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var font = new SKFont(typeface, scaledSize);
        ApplyRenderingMode(font);
        if (embolden)
        {
            font.Embolden = true;
        }
        fontCache[key] = font;
        return font;
    }

    // When the resolved face is materially lighter than what was asked for (e.g. the
    // doc requests "Arial Black" weight 900, but only Arial Bold 700 is bundled), apply
    // synthetic emboldening so the rendered glyph has visibly heavier strokes. Without
    // this the font cache picks the closest-weight face but the user sees a normal-Bold
    // weight where Word would have rendered the heavier installed Arial Black.
    static bool ShouldSyntheticallyEmbolden(SKTypeface typeface, RunProperties props)
    {
        var faceWeight = typeface.FontStyle.Weight;

        // A bold run that resolved to a face which is not itself bold. Two ways in: the family
        // has no bold member bundled ("Franklin Gothic Book" ships only the 400 face), or the
        // family NAME carries a lighter weight, which deliberately outranks the bold flag for
        // face selection (see FontHelpers.ResolveTargetWeight) and so left the target at that
        // lighter weight. Either way Word draws the run bold, and without this it renders at
        // normal weight — resumes/07's "Company, location" and SKILLS labels, which inherit
        // Normal's w:b, came out regular against Word's bold.
        if (props.Bold && faceWeight < FontHelpers.BoldWeight)
        {
            return true;
        }

        return FontHelpers.ResolveTargetWeight(props.FontFamily, props.Bold) - faceWeight >= 200;
    }

    public static SKPaint CreateTextPaint(RunProperties props) =>
        new()
        {
            IsAntialias = true,
            Color = ParseColor(props.ColorHex)
        };

    // Per-fragment SKPaint allocation in the hot text path. Skia reads paint state at
    // draw-call time and doesn't retain references, so a single mutable instance can be
    // reused across consecutive RenderFragment calls. Only the dominant fill paint is
    // shared — effects/borders/strikes that need different Style or StrokeWidth still
    // allocate their own to avoid mutation leaks across the call.
    readonly SKPaint reusableTextPaint = new() { IsAntialias = true };

    public SKPaint GetReusableTextPaint(RunProperties props)
    {
        reusableTextPaint.Color = ParseColor(props.ColorHex);
        return reusableTextPaint;
    }

    // Reusable decoration paints for the other per-fragment/per-cell draws (run background
    // fills, underline/strikethrough rules, cell shading) — same contract as
    // GetReusableTextPaint: single-threaded rendering, Skia reads paint state at draw time,
    // and no draw call uses two instances of the same shape-class at once.
    readonly SKPaint reusableFillPaint = new() { Style = SKPaintStyle.Fill };
    // Stroke, not the SKPaint default of Fill: this paint strokes rules, text decorations and shape
    // outlines. DrawLine ignores the style, but DrawPath/DrawRect/DrawOval honour it — with the default
    // Fill a shape's outline flooded the whole shape with the line colour (menus/09's green border filled
    // the card over its grey fill).
    readonly SKPaint reusableRulePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };

    public SKPaint GetReusableFillPaint(SKColor color, bool antialias)
    {
        reusableFillPaint.Color = color;
        reusableFillPaint.IsAntialias = antialias;
        return reusableFillPaint;
    }

    public SKPaint GetReusableRulePaint(SKColor color, float strokeWidth)
    {
        reusableRulePaint.Color = color;
        reusableRulePaint.StrokeWidth = strokeWidth;
        return reusableRulePaint;
    }

    /// <summary>
    /// Applies hinting / subpixel / edging settings. When deterministic rendering is enabled
    /// (via <see cref="ImageExportOptions.DeterministicRendering"/> or the
    /// <see cref="DefaultFontSettings.DeterministicRendering"/> static fallback), falls back to
    /// integer-positioned greyscale anti-aliasing so output is identical across machines;
    /// otherwise uses the platform's full-fidelity subpixel LCD rendering.
    /// </summary>
    void ApplyRenderingMode(SKFont font)
    {
        if (DeterministicRendering)
        {
            font.Subpixel = false;
            font.Edging = SKFontEdging.Antialias;
            font.Hinting = SKFontHinting.None;
        }
        else
        {
            font.Subpixel = true;
            font.Edging = SKFontEdging.SubpixelAntialias;
            font.Hinting = SKFontHinting.Normal;
        }
    }

    // ---- decoded-image caches ----
    // A header/footer logo or picture watermark draws once per page, and a repeated body image
    // once per occurrence; without caching every draw re-decodes the source bytes. Keyed by
    // byte[] reference identity: header/watermark elements are the same parsed object on every
    // page, so their data array is stable for the lifetime of the render. Null results are
    // cached too so undecodable data isn't re-probed. Callers must not dispose the results —
    // everything is released in Dispose.

    readonly Dictionary<byte[], SKBitmap?> bitmapCache = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<byte[], SKImage?> imageCache = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<byte[], SKSvg?> svgCache = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<(byte[] Data, float Width, float Height, ImageCrop? Crop, bool OriginAdjusted), SKBitmap?> svgRasterCache = [];

    /// <summary>Decoded bitmap for the given image bytes. Null for undecodable data.</summary>
    public SKBitmap? GetBitmap(byte[] data)
    {
        if (!bitmapCache.TryGetValue(data, out var bitmap))
        {
            bitmap = Decode(data);
            bitmapCache[data] = bitmap;
        }

        return bitmap;
    }

    static SKBitmap? Decode(byte[] data)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData);
        return codec != null ? SKBitmap.Decode(codec) : null;
    }

    /// <summary>
    /// Drawable <see cref="SKImage"/> for the given bytes (decode + pixel snapshot, both paid
    /// once) — for the sites that draw via <c>DrawImage</c> with sampling options: the picture
    /// watermark and image-filled background shapes.
    /// </summary>
    public SKImage? GetImage(byte[] data)
    {
        if (!imageCache.TryGetValue(data, out var image))
        {
            var bitmap = GetBitmap(data);
            image = bitmap == null ? null : SKImage.FromBitmap(bitmap);
            imageCache[data] = image;
        }

        return image;
    }

    /// <summary>
    /// SVG rasterized at the requested pixel size. The preprocessor regexes, XML parse, scene
    /// build and rasterization all run once per (source, size, crop) — an SVG header logo would
    /// otherwise pay all of them on every page. <paramref name="originAdjusted"/> preserves the
    /// two historical call-site behaviours: block images translate the picture's CullRect origin
    /// to the bitmap origin (and honour crops), inline images draw the picture untranslated.
    /// </summary>
    public SKBitmap? GetSvgRaster(byte[] svgData, float width, float height, ImageCrop? crop, bool originAdjusted)
    {
        var key = (svgData, width, height, crop, originAdjusted);
        if (!svgRasterCache.TryGetValue(key, out var raster))
        {
            raster = RasterizeSvg(svgData, width, height, crop, originAdjusted);
            svgRasterCache[key] = raster;
        }

        return raster;
    }

    SKPicture? GetSvgPicture(byte[] svgData)
    {
        if (!svgCache.TryGetValue(svgData, out var svg))
        {
            svg = new();
            using var stream = new MemoryStream(SvgPreprocessor.StripStyleAndClass(svgData));
            if (svg.Load(stream) == null)
            {
                svg.Dispose();
                svg = null;
            }

            svgCache[svgData] = svg;
        }

        return svg?.Picture;
    }

    // The scale factors come from the un-truncated float destination size (the bitmap itself is
    // int-sized) — matching what the draw sites historically computed from their SKRect.
    SKBitmap? RasterizeSvg(byte[] svgData, float width, float height, ImageCrop? crop, bool originAdjusted)
    {
        var picture = GetSvgPicture(svgData);
        if (picture == null)
        {
            return null;
        }

        var svgBounds = picture.CullRect;
        if (svgBounds is not {Width: > 0, Height: > 0})
        {
            return null;
        }

        // a:srcRect crop: stretch only the requested sub-rect of the SVG into the target.
        // l/t/r/b are fractions of the source extent (Right/Bottom are insets from the edge).
        var srcLeft = svgBounds.Left;
        var srcTop = svgBounds.Top;
        var srcWidth = svgBounds.Width;
        var srcHeight = svgBounds.Height;
        if (crop is {IsCropped: true})
        {
            srcLeft = svgBounds.Left + (float) crop.Left * svgBounds.Width;
            srcTop = svgBounds.Top + (float) crop.Top * svgBounds.Height;
            srcWidth = (float) (1 - crop.Left - crop.Right) * svgBounds.Width;
            srcHeight = (float) (1 - crop.Top - crop.Bottom) * svgBounds.Height;
            if (srcWidth <= 0 || srcHeight <= 0)
            {
                return null;
            }
        }

        var scaleX = width / srcWidth;
        var scaleY = height / srcHeight;

        var bitmap = new SKBitmap((int) width, (int) height);
        using var tempCanvas = new SKCanvas(bitmap);
        tempCanvas.Clear(SKColors.Transparent);
        tempCanvas.Scale(scaleX, scaleY);
        if (originAdjusted)
        {
            tempCanvas.Translate(-srcLeft, -srcTop);
        }

        tempCanvas.DrawPicture(picture);
        return bitmap;
    }

    public static SKColor ParseColor(string? hexColor)
    {
        if (string.IsNullOrEmpty(hexColor) ||
            hexColor == "auto")
        {
            return SKColors.Black;
        }

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

    public void Dispose()
    {
        foreach (var font in fontCache.Values)
        {
            font.Dispose();
        }
        fontCache.Clear();
        foreach (var image in imageCache.Values)
        {
            image?.Dispose();
        }
        imageCache.Clear();
        foreach (var bitmap in bitmapCache.Values)
        {
            bitmap?.Dispose();
        }
        bitmapCache.Clear();
        foreach (var raster in svgRasterCache.Values)
        {
            raster?.Dispose();
        }
        svgRasterCache.Clear();
        foreach (var svg in svgCache.Values)
        {
            svg?.Dispose();
        }
        svgCache.Clear();
        reusableTextPaint.Dispose();
        reusableFillPaint.Dispose();
        reusableRulePaint.Dispose();
        resolver.Dispose();
    }
}
