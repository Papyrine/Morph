using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Morph;

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
