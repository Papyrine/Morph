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

    public SKFont CreateFont(RunProperties props)
    {
        var typeface = GetTypeface(props.FontFamily, props.Bold, props.Italic);
        var fontSize = (float) props.FontSizePoints;

        // Subscript and superscript use reduced font size (approximately 58% per OpenXML convention)
        if (props.VerticalAlignment != VerticalRunAlignment.Baseline)
        {
            fontSize *= 0.58f;
        }

        var font = new SKFont(typeface, fontSize * Scale);
        ApplyRenderingMode(font);
        return font;
    }

    public static SKPaint CreateTextPaint(RunProperties props) =>
        new()
        {
            IsAntialias = true,
            Color = ParseColor(props.ColorHex)
        };

    /// <summary>
    /// Creates an SKFont with consistent rendering properties from a typeface and font size.
    /// </summary>
    public SKFont CreateFontFromTypeface(SKTypeface typeface, float fontSizePoints)
    {
        var font = new SKFont(typeface, fontSizePoints * Scale);
        ApplyRenderingMode(font);
        return font;
    }

    /// <summary>
    /// Applies hinting / subpixel / edging settings. When deterministic rendering is enabled
    /// (via <see cref="ConversionOptions.DeterministicRendering"/> or the
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

    public void Dispose() => resolver.Dispose();
}
