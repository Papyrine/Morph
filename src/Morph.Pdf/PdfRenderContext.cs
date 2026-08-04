/// <summary>
/// Rendering state for a PDF conversion. Coordinates are kept in points (the PDF user unit) by
/// constructing the base with a 72-DPI scale, so <see cref="RenderContextBase.PointsToPixels"/> is
/// an identity and every value the shared layout engine computes maps straight onto
/// <see cref="XGraphics"/>.
/// </summary>
sealed class PdfRenderContext : RenderContextBase
{
    public PdfDocument Document { get; } = new();

    /// <summary>The graphics surface for the page currently being emitted (null between pages).</summary>
    public XGraphics? Graphics { get; set; }

    public PdfRenderContext(
        PageSettings pageSettings,
        CompatibilitySettings? compatibility,
        double fontWidthScale,
        Func<string, string?>? fontFallback,
        string? fontDirectory) :
        base(pageSettings, dpi: 72, compatibility, fontWidthScale, fontFallback, fontDirectory)
    {
        PdfFontResolver.Register(fontDirectory);
        Document.Info.Creator = "Morph";
    }

    static readonly XPdfFontOptions fontOptions = new(PdfFontEncoding.Unicode);
    readonly Dictionary<(string Family, bool Bold, bool Italic, double Size), XFont> fontCache = [];

    public XFont GetFont(RunProperties properties)
    {
        var size = properties.FontSizePoints;
        if (properties.VerticalAlignment != VerticalRunAlignment.Baseline)
        {
            size *= 0.58;
        }

        return GetFont(properties.FontFamily, properties.Bold, properties.Italic, size);
    }

    public XFont GetFont(string family, bool bold, bool italic, double sizePoints)
    {
        if (sizePoints <= 0)
        {
            sizePoints = 11;
        }

        family = ResolveFamily(family, bold, italic);

        var key = (family, bold, italic, sizePoints);
        if (fontCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var style = XFontStyleEx.Regular;
        if (bold)
        {
            style |= XFontStyleEx.Bold;
        }

        if (italic)
        {
            style |= XFontStyleEx.Italic;
        }

        var font = new XFont(family, sizePoints, style, fontOptions);
        fontCache[key] = font;
        return font;
    }

    // PdfSharp resolves faces through a process-global IFontResolver that cannot see per-conversion
    // state, so the FontFallback delegate is applied here rather than inside PdfFontResolver: the
    // substituted family is what reaches XFont, and the global resolver only ever sees a name it can
    // already serve. Cached per requested triple so the delegate runs once per distinct family.
    readonly Dictionary<(string Family, bool Bold, bool Italic), string> fallbackCache = [];

    string ResolveFamily(string family, bool bold, bool italic)
    {
        if (FontFallback == null)
        {
            return family;
        }

        var key = (family, bold, italic);
        if (fallbackCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // Mirrors the shared resolver's ordering: the delegate is consulted only once the indexed
        // faces and the curated alias map have missed, and a null/empty return falls through to
        // PdfFontResolver's own platform and default-font fallbacks.
        var resolved = family;
        if (!PdfFontResolver.Instance.CanResolve(family, bold, italic) &&
            FontFallback(family) is { Length: > 0 } substitute)
        {
            resolved = substitute;
        }

        fallbackCache[key] = resolved;
        return resolved;
    }

    // Brushes and pens were allocated per drawn word / underline / border edge. Documents use a
    // small set of colours and widths, so cache by value — PdfSharp reads them at draw time and
    // never mutates them.
    readonly Dictionary<XColor, XSolidBrush> brushCache = [];
    readonly Dictionary<(XColor Color, double Width), XPen> penCache = [];

    public XSolidBrush GetBrush(XColor color)
    {
        if (!brushCache.TryGetValue(color, out var brush))
        {
            brush = new(color);
            brushCache[color] = brush;
        }

        return brush;
    }

    public XPen GetPen(XColor color, double width)
    {
        var key = (color, width);
        if (!penCache.TryGetValue(key, out var pen))
        {
            pen = new(color, width);
            penCache[key] = pen;
        }

        return pen;
    }

    // PdfSharp dedupes embedded image XObjects per XImage *instance* — a fresh XImage from the
    // same bytes is decoded again and embedded again in the output PDF (a header logo on an
    // N-page document used to embed N copies). Cache per source array (reference identity: the
    // parsed elements hold stable arrays). Decode failures propagate to the caller, matching
    // the uncached XImage.FromStream behaviour — nothing is cached for a throwing source.
    readonly Dictionary<byte[], XImage> imageCache = new(ReferenceEqualityComparer.Instance);

    public XImage GetImage(byte[] data)
    {
        if (!imageCache.TryGetValue(data, out var image))
        {
            // Two formats need re-encoding before PDFsharp will take them, both keyed on the
            // ORIGINAL array so the cache stays reference-stable and the work happens at most once:
            //   * GIF — PDFsharp's cross-platform build cannot decode it at all, so the first frame
            //     is transcoded to an indexed PNG (GifToPng).
            //   * a sub-8-bit indexed PNG — PDFsharp emits an all-zero soft mask for those, so the
            //     picture lands in the PDF and draws as nothing (IndexedPngNormalizer).
            var decodable = GifToPng.IsGif(data)
                ? GifToPng.Convert(data) ?? data
                : IndexedPngNormalizer.Normalize(data);
            using var stream = new MemoryStream(decodable);
            image = XImage.FromStream(stream);
            imageCache[data] = image;
        }

        return image;
    }

    /// <summary>Disposes every cached image. Called once the document has been saved.</summary>
    public void DisposeImages()
    {
        foreach (var image in imageCache.Values)
        {
            image.Dispose();
        }

        imageCache.Clear();
    }

    public static XColor ParseColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex == "auto")
        {
            return XColors.Black;
        }

        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return XColor.FromArgb((byte) ((rgb >> 16) & 0xFF), (byte) ((rgb >> 8) & 0xFF), (byte) (rgb & 0xFF));
        }

        if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return XColor.FromArgb((byte) ((argb >> 24) & 0xFF), (byte) ((argb >> 16) & 0xFF), (byte) ((argb >> 8) & 0xFF), (byte) (argb & 0xFF));
        }

        return XColors.Black;
    }
}
