using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using WPG = DocumentFormat.OpenXml.Office2010.Word.DrawingGroup;
using WPS = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

/// <summary>
/// Parses shape elements from Word documents.
/// </summary>
static class ShapeParser
{
    /// <summary>
    /// Parses a Drawing element to extract background shapes (solid fill or image fill shapes behind text).
    /// Filters out decorative shapes (those with complex bezier paths) and returns remaining shapes.
    /// </summary>
    public static List<FloatingShapeElement> ParseBackgroundShapes(Drawing drawing, ThemeColors? themeColors, MainDocumentPart? mainPart = null, double paragraphSpacingBeforePoints = 0)
    {
        var result = new List<FloatingShapeElement>();

        // Must be an anchored drawing with behindDoc attribute
        var anchor = drawing.GetFirstChild<DW.Anchor>();
        if (anchor == null || anchor.BehindDoc?.Value != true)
        {
            return result;
        }

        // Get anchor dimensions (target size after transform)
        var extent = anchor.Extent;
        if (extent == null)
        {
            return result;
        }

        var anchorDimensions = extent.GetDimensions();
        if (anchorDimensions == null)
        {
            return result;
        }

        var (anchorWidthPt, anchorHeightPt) = anchorDimensions.Value;

        // Parse base positioning from anchor
        var positioning = anchor.ParsePositioning();

        // Check for WordprocessingGroup
        var wgp = drawing.Descendants<WPG.WordprocessingGroup>().FirstOrDefault();
        if (wgp != null)
        {
            // Get group transform info for applying to individual shapes
            var grpSpPr = wgp.GetFirstChild<WPG.GroupShapeProperties>();
            var grpXfrm = grpSpPr?.GetFirstChild<A.TransformGroup>();

            // Child coordinate space (source)
            long chOffX = 0, chOffY = 0;
            long chExtCx = 1, chExtCy = 1;

            var chOff = grpXfrm?.ChildOffset;
            var chExt = grpXfrm?.ChildExtents;

            if (chOff != null)
            {
                chOffX = chOff.X ?? 0;
                chOffY = chOff.Y ?? 0;
            }

            if (chExt != null)
            {
                chExtCx = chExt.Cx ?? 1;
                chExtCy = chExt.Cy ?? 1;
            }

            // Calculate scale factors (anchor extent / child extent)
            var scaleX = (extent.Cx ?? 1) / (double) chExtCx;
            var scaleY = (extent.Cy ?? 1) / (double) chExtCy;

            // Process ALL non-decorative shapes in the group
            foreach (var wsp in wgp.Descendants<WPS.WordprocessingShape>())
            {
                var shapeElement = ParseGroupedShape(wsp, themeColors, positioning,
                    chOffX, chOffY, scaleX, scaleY, mainPart);
                if (shapeElement != null)
                {
                    result.Add(shapeElement);
                }
            }
        }
        else
        {
            // Standalone shape
            var wsp = drawing.Descendants<WPS.WordprocessingShape>().FirstOrDefault();
            if (wsp != null)
            {
                var shapeElement = ParseStandaloneShape(wsp, themeColors, positioning,
                    anchorWidthPt, anchorHeightPt, mainPart);
                if (shapeElement != null)
                {
                    result.Add(shapeElement);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Parses a standalone shape using anchor dimensions directly.
    /// </summary>
    static FloatingShapeElement? ParseStandaloneShape(
        WPS.WordprocessingShape wsp,
        ThemeColors? themeColors,
        AnchorPositioning positioning,
        double widthPoints,
        double heightPoints,
        MainDocumentPart? mainPart)
    {
        var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
        if (shapeProps == null)
        {
            return null;
        }

        // Try solid fill first
        var solidFill = shapeProps.GetFirstChild<A.SolidFill>();
        if (solidFill != null)
        {
            var fillColorHex = ExtractSolidFillColor(solidFill, themeColors);
            if (fillColorHex != null)
            {
                return new()
                {
                    WidthPoints = widthPoints,
                    HeightPoints = heightPoints,
                    HorizontalPositionPoints = positioning.HorizontalPositionPoints,
                    VerticalPositionPoints = positioning.VerticalPositionPoints,
                    HorizontalAnchor = positioning.HorizontalAnchor,
                    VerticalAnchor = positioning.VerticalAnchor,
                    BehindText = true,
                    FillColorHex = fillColorHex
                };
            }
        }

        // Try blip fill (image fill)
        var blipFill = shapeProps.GetFirstChild<A.BlipFill>();
        if (blipFill != null && mainPart != null)
        {
            var (imageData, contentType) = ExtractBlipFillImage(blipFill, mainPart);
            if (imageData != null)
            {
                return new()
                {
                    WidthPoints = widthPoints,
                    HeightPoints = heightPoints,
                    HorizontalPositionPoints = positioning.HorizontalPositionPoints,
                    VerticalPositionPoints = positioning.VerticalPositionPoints,
                    HorizontalAnchor = positioning.HorizontalAnchor,
                    VerticalAnchor = positioning.VerticalAnchor,
                    BehindText = true,
                    ImageData = imageData,
                    ImageContentType = contentType
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a shape within a group, applying group transforms to get individual shape dimensions.
    /// Filters out decorative shapes (those with complex bezier paths).
    /// </summary>
    static FloatingShapeElement? ParseGroupedShape(
        WPS.WordprocessingShape wsp,
        ThemeColors? themeColors,
        AnchorPositioning positioning,
        long chOffX, long chOffY,
        double scaleX, double scaleY,
        MainDocumentPart? mainPart)
    {
        var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
        if (shapeProps == null)
        {
            return null;
        }

        // Filter out decorative shapes (complex paths with curves)
        if (IsDecorativeShape(shapeProps))
        {
            return null;
        }

        // Get shape transform first (needed for both fill types)
        var xfrm = shapeProps.GetFirstChild<A.Transform2D>();
        if (xfrm == null)
        {
            return null;
        }

        var off = xfrm.Offset;
        var ext = xfrm.Extents;
        if (off == null || ext == null)
        {
            return null;
        }

        // Shape position in child coordinates (relative to group)
        long shapeX = off.X ?? 0;
        long shapeY = off.Y ?? 0;
        long shapeCx = ext.Cx ?? 0;
        long shapeCy = ext.Cy ?? 0;

        if (shapeCx == 0 || shapeCy == 0)
        {
            return null;
        }

        // Apply group transform: scale and translate
        // Position: (shapePos - childOffset) * scale, then convert to points
        var xPt = ((shapeX - chOffX) * scaleX).EmuToPoints();
        var yPt = ((shapeY - chOffY) * scaleY).EmuToPoints();
        var widthPt = (shapeCx * scaleX).EmuToPoints();
        var heightPt = (shapeCy * scaleY).EmuToPoints();

        // Try solid fill first
        var solidFill = shapeProps.GetFirstChild<A.SolidFill>();
        if (solidFill != null)
        {
            var fillColorHex = ExtractSolidFillColor(solidFill, themeColors);
            if (fillColorHex != null)
            {
                return new()
                {
                    WidthPoints = widthPt,
                    HeightPoints = heightPt,
                    HorizontalPositionPoints = positioning.HorizontalPositionPoints + xPt,
                    VerticalPositionPoints = positioning.VerticalPositionPoints + yPt,
                    HorizontalAnchor = positioning.HorizontalAnchor,
                    VerticalAnchor = positioning.VerticalAnchor,
                    BehindText = true,
                    FillColorHex = fillColorHex
                };
            }
        }

        // Try blip fill (image fill)
        var blipFill = shapeProps.GetFirstChild<A.BlipFill>();
        if (blipFill != null && mainPart != null)
        {
            var (imageData, contentType) = ExtractBlipFillImage(blipFill, mainPart);
            if (imageData != null)
            {
                return new()
                {
                    WidthPoints = widthPt,
                    HeightPoints = heightPt,
                    HorizontalPositionPoints = positioning.HorizontalPositionPoints + xPt,
                    VerticalPositionPoints = positioning.VerticalPositionPoints + yPt,
                    HorizontalAnchor = positioning.HorizontalAnchor,
                    VerticalAnchor = positioning.VerticalAnchor,
                    BehindText = true,
                    ImageData = imageData,
                    ImageContentType = contentType
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Determines if a shape is decorative based on path complexity.
    /// Decorative shapes typically have complex paths with curves (cubicBezTo).
    /// </summary>
    static bool IsDecorativeShape(WPS.ShapeProperties shapeProps)
    {
        // Check for custom geometry with curves
        var custGeom = shapeProps.GetFirstChild<A.CustomGeometry>();
        if (custGeom != null)
        {
            var pathList = custGeom.GetFirstChild<A.PathList>();
            if (pathList != null)
            {
                // If any path contains cubic bezier curves, it's decorative
                if (pathList.Descendants<A.CubicBezierCurveTo>().Any())
                {
                    return true;
                }

                // Also check for quadratic bezier curves
                if (pathList.Descendants<A.QuadraticBezierCurveTo>().Any())
                {
                    return true;
                }
            }
        }

        // Check aspect ratio as a backup heuristic
        var xfrm = shapeProps.GetFirstChild<A.Transform2D>();
        if (xfrm?.Extents != null)
        {
            long cx = xfrm.Extents.Cx ?? 0;
            long cy = xfrm.Extents.Cy ?? 0;

            if (cx > 0 && cy > 0)
            {
                var aspectRatio = (double)cx / cy;
                // Very thin lines (width > 50x height) are likely decorative
                if (aspectRatio > 50)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts the color from a solid fill element.
    /// </summary>
    public static string? ExtractSolidFillColor(A.SolidFill solidFill, ThemeColors? themeColors)
    {
        // Try RGB color first
        var rgbColor = solidFill.GetFirstChild<A.RgbColorModelHex>();
        if (rgbColor?.Val?.HasValue == true)
        {
            // Check for color transforms on RGB color too
            var transforms = ExtractColorTransforms(rgbColor);
            if (transforms.HasTransforms)
            {
                return ApplyTransformsToRgb(rgbColor.Val.Value!, transforms);
            }
            return rgbColor.Val.Value;
        }

        // Try scheme color (theme-based)
        var schemeClr = solidFill.GetFirstChild<A.SchemeColor>();
        if (schemeClr?.Val?.HasValue == true && themeColors != null)
        {
            // Get the actual XML value (e.g., "tx2" not "Text2")
            var schemeValue = ((IEnumValue)schemeClr.Val.Value).Value;

            // Check for alpha - if nearly invisible, skip this shape
            // Only skip shapes that are less than 5% opaque (nearly invisible)
            var alphaEl = schemeClr.GetFirstChild<A.Alpha>();
            if (alphaEl?.Val is { HasValue: true, Value: < 5000 })
            {
                // Skip nearly invisible shapes
                return null;
            }

            // Extract all color transforms
            var transforms = ExtractColorTransforms(schemeClr);

            return themeColors.ResolveColor(schemeValue, transforms);
        }

        return null;
    }

    /// <summary>
    /// Extracts image data from a blip fill element.
    /// </summary>
    static (byte[]? ImageData, string? ContentType) ExtractBlipFillImage(A.BlipFill blipFill, MainDocumentPart mainPart)
    {
        var blip = blipFill.GetFirstChild<A.Blip>();
        if (blip == null)
        {
            return (null, null);
        }

        var embedAttr = blip.Embed?.Value;
        if (string.IsNullOrEmpty(embedAttr))
        {
            return (null, null);
        }

        // Try to get the image part
        if (mainPart.GetPartById(embedAttr) is not ImagePart imagePart)
        {
            return (null, null);
        }

        using var stream = imagePart.GetStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var imageData = ms.ToArray();

        if (imageData.Length == 0)
        {
            return (null, null);
        }

        return (imageData, imagePart.ContentType);
    }

    /// <summary>
    /// Extracts color transform parameters from a color element.
    /// </summary>
    static ColorTransforms ExtractColorTransforms(OpenXmlElement colorElement)
    {
        byte? shade = null;
        byte? tint = null;
        double? lumMod = null;
        double? lumOff = null;
        double? satMod = null;
        double? satOff = null;

        // Shade (0-100000 -> 0-255)
        var shadeEl = colorElement.GetFirstChild<A.Shade>();
        if (shadeEl?.Val?.HasValue == true)
        {
            shade = (byte)Math.Clamp((int)(shadeEl.Val.Value / 100000.0 * 255), 0, 255);
        }

        // Tint (0-100000 -> 0-255)
        var tintEl = colorElement.GetFirstChild<A.Tint>();
        if (tintEl?.Val?.HasValue == true)
        {
            tint = (byte)Math.Clamp((int)(tintEl.Val.Value / 100000.0 * 255), 0, 255);
        }

        // Luminance modulation (0-100000+ -> percentage, e.g., 75000 -> 75%)
        var lumModEl = colorElement.GetFirstChild<A.LuminanceModulation>();
        if (lumModEl?.Val?.HasValue == true)
        {
            lumMod = lumModEl.Val.Value / 1000.0;
        }

        // Luminance offset (0-100000 -> percentage points)
        var lumOffEl = colorElement.GetFirstChild<A.LuminanceOffset>();
        if (lumOffEl?.Val?.HasValue == true)
        {
            lumOff = lumOffEl.Val.Value / 1000.0;
        }

        // Saturation modulation (0-100000+ -> percentage)
        var satModEl = colorElement.GetFirstChild<A.SaturationModulation>();
        if (satModEl?.Val?.HasValue == true)
        {
            satMod = satModEl.Val.Value / 1000.0;
        }

        // Saturation offset (0-100000 -> percentage points)
        var satOffEl = colorElement.GetFirstChild<A.SaturationOffset>();
        if (satOffEl?.Val?.HasValue == true)
        {
            satOff = satOffEl.Val.Value / 1000.0;
        }

        return new()
        {
            Shade = shade,
            Tint = tint,
            LumMod = lumMod,
            LumOff = lumOff,
            SatMod = satMod,
            SatOff = satOff
        };
    }

    /// <summary>
    /// Applies color transforms directly to an RGB hex color.
    /// </summary>
    static string ApplyTransformsToRgb(string hexColor, ColorTransforms transforms)
    {
        // For direct RGB colors with transforms, we need to apply the transforms ourselves
        // This is a simplified version - for full support, use ThemeColors
        if (!TryParseHexColor(hexColor, out var r, out var g, out var b))
        {
            return hexColor;
        }

        // Apply HSL transforms if present
        if (transforms.LumMod.HasValue || transforms.SatMod.HasValue ||
            transforms.LumOff.HasValue || transforms.SatOff.HasValue)
        {
            RgbToHsl(r, g, b, out var h, out var s, out var l);

            if (transforms.SatMod.HasValue)
            {
                s *= transforms.SatMod.Value / 100.0;
            }

            if (transforms.SatOff.HasValue)
            {
                s += transforms.SatOff.Value / 100.0;
            }

            if (transforms.LumMod.HasValue)
            {
                l *= transforms.LumMod.Value / 100.0;
            }

            if (transforms.LumOff.HasValue)
            {
                l += transforms.LumOff.Value / 100.0;
            }

            s = Math.Clamp(s, 0.0, 1.0);
            l = Math.Clamp(l, 0.0, 1.0);

            HslToRgb(h, s, l, out r, out g, out b);
        }

        // Apply shade/tint transforms
        // Per ECMA-376: shade darkens the color, tint lightens it
        // Values are in 0-255 scale
        if (transforms.Shade is > 0)
        {
            var shade = transforms.Shade.Value;
            r = (byte)(r * shade / 255);
            g = (byte)(g * shade / 255);
            b = (byte)(b * shade / 255);
        }

        if (transforms.Tint is > 0)
        {
            var tint = transforms.Tint.Value;
            r = (byte)(r + (255 - r) * tint / 255);
            g = (byte)(g + (255 - g) * tint / 255);
            b = (byte)(b + (255 - b) * tint / 255);
        }

        return $"{r:X2}{g:X2}{b:X2}";
    }

    static bool TryParseHexColor(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (hex.Length != 6)
        {
            return false;
        }

        return byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, null, out r) &&
               byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, null, out g) &&
               byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, null, out b);
    }

    static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        var rd = r / 255.0;
        var gd = g / 255.0;
        var bd = b / 255.0;

        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var delta = max - min;

        l = (max + min) / 2.0;

        if (delta == 0)
        {
            h = 0;
            s = 0;
        }
        else
        {
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

            if (max == rd)
            {
                h = ((gd - bd) / delta + (gd < bd ? 6 : 0)) / 6.0;
            }
            else if (max == gd)
            {
                h = ((bd - rd) / delta + 2) / 6.0;
            }
            else
            {
                h = ((rd - gd) / delta + 4) / 6.0;
            }
        }
    }

    static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        double rd, gd, bd;

        if (s == 0)
        {
            rd = gd = bd = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;

            rd = HueToRgb(p, q, h + 1.0 / 3.0);
            gd = HueToRgb(p, q, h);
            bd = HueToRgb(p, q, h - 1.0 / 3.0);
        }

        r = (byte)Math.Round(rd * 255);
        g = (byte)Math.Round(gd * 255);
        b = (byte)Math.Round(bd * 255);
    }

    static double HueToRgb(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6.0)
        {
            return p + (q - p) * 6 * t;
        }

        if (t < 1.0 / 2.0)
        {
            return q;
        }

        if (t < 2.0 / 3.0)
        {
            return p + (q - p) * (2.0 / 3.0 - t) * 6;
        }

        return p;
    }
}
