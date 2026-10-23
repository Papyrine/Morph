/// <summary>
/// Rendering state for a PDF conversion. Coordinates are kept in points (the PDF user unit) by
/// constructing the base with a 72-DPI scale, so <see cref="RenderContextBase.PointsToPixels"/> is
/// an identity and every value the shared layout engine computes maps straight onto the Krilla
/// <see cref="Krilla.Surface"/>, whose origin is the top-left corner with Y increasing downward.
/// </summary>
sealed class PdfRenderContext : RenderContextBase, IDisposable
{
    public KrillaDocument Document { get; } = new();

    /// <summary>The drawing surface of the page currently being emitted (null between pages).</summary>
    public Surface? Surface { get; set; }

    // Faces resolve through the same FontResolver the layout engine measures with (LayoutFonts):
    // the same directory, the same bundled seed, the same FontFallback delegate, so the painter
    // draws the face the measurer broke the line on.
    readonly FontResolver<PdfFace> fonts;

    public PdfRenderContext(
        PageSettings pageSettings,
        CompatibilitySettings? compatibility,
        double fontWidthScale,
        Func<string, string?>? fontFallback,
        string? fontDirectory) :
        base(pageSettings, dpi: 72, compatibility, fontWidthScale, fontFallback, fontDirectory)
    {
        fonts = new(
            loadFace: (face, _) => PdfFace.Load(face),
            systemFallback: null,
            releaseFont: _ => _.Dispose(),
            fontDirectory: fontDirectory,
            fontFallback: fontFallback,
            seed: FontResolver<PdfFace>.BuildBundledSeed(LoadSeeded));
        Document.SetMetadata(
            new()
            {
                Creator = "Morph"
            });
    }

    // The resolver leaves the faces it was seeded with to their owner — they are shared in the raster
    // backends — but these are this conversion's own, so they are disposed with it.
    readonly List<PdfFace> seededFaces = [];

    PdfFace LoadSeeded(byte[] bytes)
    {
        var face = PdfFace.Load(bytes);
        seededFaces.Add(face);
        return face;
    }

    /// <summary>
    /// The face a run draws in. A family nothing can serve falls back to
    /// <see cref="DefaultFontSettings.DefaultFont"/> — the last resort <see cref="LayoutFonts.ToDelegate"/>
    /// gives the measurer — so the line is drawn in the face it was measured in.
    /// </summary>
    public PdfFace GetFace(string family, bool bold, bool italic)
    {
        try
        {
            return fonts.Resolve(family, bold, italic);
        }
        catch (InvalidOperationException)
        {
            return fonts.Resolve(DefaultFontSettings.DefaultFont, bold, italic);
        }
    }

    public PdfFace GetFace(RunProperties properties) =>
        GetFace(properties.FontFamily, properties.Bold, properties.Italic);

    // Whether an italic run needs the painter's synthetic oblique: the face it resolved is upright.
    public bool NeedsSyntheticItalic(RunProperties properties) =>
        properties.Italic && !GetFace(properties).Italic;

    // A Paint allocated through Krilla lives until the document is disposed, so one is made per
    // colour rather than per drawn word, underline or border edge.
    readonly Dictionary<(byte R, byte G, byte B), Paint> paintCache = [];

    public Paint GetPaint(PdfColor color)
    {
        var key = (color.R, color.G, color.B);
        if (!paintCache.TryGetValue(key, out var paint))
        {
            paint = Paint.Solid(Color.Rgb(color.R, color.G, color.B));
            paintCache[key] = paint;
        }

        return paint;
    }

    public Fill GetFill(PdfColor color) =>
        new(GetPaint(color), (float) color.Alpha);

    public Fill GetFill(PdfColor color, FillRule rule) =>
        new(GetPaint(color), (float) color.Alpha, rule);

    public Stroke GetStroke(PdfColor color, double width, LineCap cap = LineCap.Butt, float[]? dash = null) =>
        new(GetPaint(color), (float) width, (float) color.Alpha, cap, DashArray: dash);

    // Krilla embeds an image once per PdfImage instance, so one is kept per source array (reference
    // identity: the parsed elements hold stable arrays) and treatment — a header logo on an N-page
    // document is stored once. A source that cannot be decoded is remembered as null and skipped.
    readonly Dictionary<(byte[] Data, ImageRecolor? Recolor, double Opacity), PdfImage?> imageCache = new(ImageKeyComparer.Instance);

    // The same source array can be drawn both plainly and with effects, so the key carries them
    // beside it: the array still compares by reference, the effects by value.
    sealed class ImageKeyComparer : IEqualityComparer<(byte[] Data, ImageRecolor? Recolor, double Opacity)>
    {
        public static readonly ImageKeyComparer Instance = new();

        public bool Equals((byte[] Data, ImageRecolor? Recolor, double Opacity) left, (byte[] Data, ImageRecolor? Recolor, double Opacity) right) =>
            ReferenceEquals(left.Data, right.Data) &&
            Equals(left.Recolor, right.Recolor) &&
            left.Opacity.Equals(right.Opacity);

        public int GetHashCode((byte[] Data, ImageRecolor? Recolor, double Opacity) key) =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(key.Data), key.Recolor, key.Opacity);
    }

    public PdfImage? GetImage(byte[] data, ImageRecolor? recolor = null, double opacity = 1)
    {
        var key = (data, recolor, opacity);
        if (!imageCache.TryGetValue(key, out var image))
        {
            // The blip's effects: Word's Recolor gallery (a:duotone / a:grayscl / a:lum) and the
            // a:alphaModFix transparency. Krilla embeds encoded bytes and has no pixel pipeline to
            // apply either with, so they are baked in here by a raster backend if one is deployed —
            // the arrangement WordArt already uses. The result is always a PNG.
            var source = (recolor != null || opacity < 1) &&
                         ImageEffectsFactory.TryGet()?.Bake(data, recolor, opacity) is { Length: > 0 } baked
                ? baked
                : data;

            image = LoadImage(source);
            imageCache[key] = image;
        }

        return image;
    }

    static PdfImage? LoadImage(byte[] data)
    {
        if (Sniff(data) is not { } format)
        {
            return null;
        }

        try
        {
            return PdfImage.Load(format, data);
        }
        catch (KrillaException)
        {
            return null;
        }
    }

    // The formats Krilla decodes, told apart by their signatures.
    static ImageFormat? Sniff(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>) [0x89, (byte) 'P', (byte) 'N', (byte) 'G']))
        {
            return ImageFormat.Png;
        }

        if (data.StartsWith((ReadOnlySpan<byte>) [0xFF, 0xD8]))
        {
            return ImageFormat.Jpeg;
        }

        if (data.StartsWith("GIF8"u8))
        {
            return ImageFormat.Gif;
        }

        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return ImageFormat.Webp;
        }

        return null;
    }

    // One graphic per drawing and treatment, as imageCache is one PdfImage per source array: a
    // graphic is stored once and every page that draws the picture refers to it.
    readonly Dictionary<(SvgDrawing Vector, ImageRecolor? Recolor, double Opacity), Graphic> vectorCache = [];

    /// <summary>
    /// A picture stored as SVG, as a graphic of path operators (<see cref="PdfVectorDrawing"/>) — the
    /// vector counterpart of <see cref="GetImage"/>, drawn in the drawing's own units. The recolour is
    /// applied to the drawing's colours. Transparency can only be folded into each path's own alpha,
    /// which is the picture faded as one image just when its paths keep clear of each other; the
    /// painter therefore asks for it only where the picture has no raster to bake it into.
    /// </summary>
    public Graphic GetVectorGraphic(SvgDrawing vector, ImageRecolor? recolor = null, double opacity = 1)
    {
        var key = (vector, recolor, opacity);
        if (!vectorCache.TryGetValue(key, out var graphic))
        {
            graphic = PdfVectorDrawing.Capture(this, Surface!, vector.Recolored(recolor), Math.Clamp(opacity, 0, 1));
            vectorCache[key] = graphic;
        }

        return graphic;
    }

    public void Dispose()
    {
        foreach (var image in imageCache.Values)
        {
            image?.Dispose();
        }

        imageCache.Clear();
        foreach (var graphic in vectorCache.Values)
        {
            graphic.Dispose();
        }

        vectorCache.Clear();
        foreach (var paint in paintCache.Values)
        {
            paint.Dispose();
        }

        paintCache.Clear();
        fonts.Dispose();
        foreach (var face in seededFaces)
        {
            face.Dispose();
        }

        Document.Dispose();
    }

    public static PdfColor ParseColor(string? hex)
    {
        if (hex.TryParseArgb(out var a, out var r, out var g, out var b))
        {
            return new(r, g, b, a / 255.0);
        }

        return PdfColor.Black;
    }
}

/// <summary>An RGB colour with its opacity, which Krilla takes apart from the colour as a fill's or stroke's.</summary>
readonly record struct PdfColor(byte R, byte G, byte B, double Alpha = 1)
{
    public static PdfColor Black => new(0, 0, 0);

    public PdfColor WithAlpha(double alpha) =>
        this with
        {
            Alpha = Math.Clamp(alpha, 0, 1)
        };

    // See SkiaPainter.Shaded.
    public PdfColor Shaded(double shade)
    {
        if (shade >= 1)
        {
            return this;
        }

        return this with
        {
            R = (byte) (R * shade),
            G = (byte) (G * shade),
            B = (byte) (B * shade)
        };
    }
}
