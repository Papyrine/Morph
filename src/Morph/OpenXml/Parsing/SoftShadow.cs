using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// A blurred DrawingML outer shadow (<c>a:outerShdw</c> with a <c>blurRad</c>) under a rectangular
/// drawing, built as rectangles of the shadow's colour stacked from the blur's outer reach inwards,
/// each translucent enough that together they climb to the shadow's alpha the way a Gaussian blur of
/// the drawing's box does. Plain filled rectangles are something every painter draws, which a blur is
/// not, so the shadow needs nothing of the layout or the painters.
///
/// Measured on Excel's print of inventory-list-accessibility-guide (2026-11-28): its screenshot
/// picture's shadow, <c>#333333</c> at 65%, <c>blurRad</c> 23pt and <c>dist</c> 11pt at 45°, drawn at a
/// sheet scale of 0.632, is the picture's box moved by <c>dist</c> along <c>dir</c> and blurred with a
/// Gaussian whose sigma is a third of <c>blurRad</c>: at 96 DPI the darkening across its right and bottom
/// edges fits a sigma of 6.0 to 6.8px where a third of the blur is 6.5px, it stands at half the shadow's
/// alpha on the moved box's edge, and the 9% it leaves along the picture's top and left edges is the
/// tail of the same curve, 6.6px (the offset) outside the box.
/// </summary>
static class SoftShadow
{
    // Steps from nothing to the shadow's alpha, each 1.2px deep at the measured shadow's 96 DPI: at 16
    // they were 2.4px, and showed as bands of pixels pairing off in shade
    const int steps = 32;

    /// <summary>
    /// The layers of a box's shadow, outermost first, or none when the shadow is not blurred (a hard
    /// shadow is the box's copy, which callers draw themselves) or has no colour. The box and the
    /// result are in points at <paramref name="scale"/>, and stand where <paramref name="horizontalAnchor"/>
    /// and <paramref name="verticalAnchor"/> say, as the drawing does.
    /// </summary>
    public static List<FloatingShapeElement> Layers(
        A.OuterShadow? shadow,
        ThemeColors? themeColors,
        (double X, double Y, double Width, double Height) box,
        double scale,
        HorizontalAnchor horizontalAnchor,
        VerticalAnchor verticalAnchor,
        uint relativeHeight,
        bool notPrinted)
    {
        var layers = new List<FloatingShapeElement>();
        if (shadow?.BlurRadius?.Value is not { } blur ||
            blur <= 0 ||
            box.Width <= 0 ||
            box.Height <= 0 ||
            ShapeParser.ExtractSolidFillColor(shadow, themeColors) is not { } color)
        {
            return layers;
        }

        var alpha = shadow.Descendants<A.Alpha>().FirstOrDefault()?.Val?.Value / 100000.0 ?? 1;
        var sigma = blur / OoxmlUnits.EmusPerPoint * scale / 3;

        // The shadow's box: the drawing's grown by sx/sy about the point algn names, then moved dist
        // along dir (as SlideShapeParser.HardShadow moves a hard one)
        var width = box.Width * (shadow.HorizontalRatio?.Value ?? 100000) / 100000.0;
        var height = box.Height * (shadow.VerticalRatio?.Value ?? 100000) / 100000.0;
        var align = shadow.Alignment?.Value is { } value ? ((IEnumValue) value).Value : "b";
        var fixedX = align is "tl" or "l" or "bl" ? 0 : align is "tr" or "r" or "br" ? 1 : 0.5;
        var fixedY = align is "tl" or "t" or "tr" ? 0 : align is "bl" or "b" or "br" ? 1 : 0.5;
        var radians = (shadow.Direction?.Value ?? 0) / 60000.0 * Math.PI / 180;
        var distance = (shadow.Distance?.Value ?? 0) / OoxmlUnits.EmusPerPoint * scale;
        var left = box.X + (box.Width - width) * fixedX + distance * Math.Cos(radians);
        var top = box.Y + (box.Height - height) * fixedY + distance * Math.Sin(radians);

        // Each layer's edge stands a step inside the last, from three sigmas outside the shadow's box to
        // three inside it. A point between two edges is under every layer out to the outer one, and
        // those together have to cover it as the blur does at the middle of that band.
        var step = 6 * sigma / steps;
        var covered = 0.0;
        for (var index = 0; index < steps; index++)
        {
            var grow = 3 * sigma - index * step;
            var target = index == steps - 1
                ? alpha
                : alpha * Normal(-(grow - step / 2) / sigma);
            var layerAlpha = 1 - (1 - target) / (1 - covered);
            covered = target;
            if (width + 2 * grow <= 0 ||
                height + 2 * grow <= 0 ||
                layerAlpha <= 0)
            {
                continue;
            }

            layers.Add(new()
            {
                WidthPoints = width + 2 * grow,
                HeightPoints = height + 2 * grow,
                HorizontalPositionPoints = left - grow,
                VerticalPositionPoints = top - grow,
                HorizontalAnchor = horizontalAnchor,
                VerticalAnchor = verticalAnchor,
                FillColorHex = color,
                FillAlpha = layerAlpha,
                Preset = PresetShape.Rect,
                RelativeHeight = relativeHeight,
                NotPrinted = notPrinted
            });
        }

        return layers;
    }

    /// <summary>The standard normal distribution's cumulative probability, through Abramowitz and Stegun's 7.1.26 for erf (within 1.5e-7).</summary>
    static double Normal(double z)
    {
        var x = Math.Abs(z) / Math.Sqrt(2);
        var t = 1 / (1 + 0.3275911 * x);
        var erf = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return z >= 0 ? (1 + erf) / 2 : (1 - erf) / 2;
    }
}
