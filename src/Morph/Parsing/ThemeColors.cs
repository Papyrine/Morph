/// <summary>
/// Theme color definitions from the document theme.
/// </summary>
sealed class ThemeColors
{
    /// <summary>Dark 1 color (typically black).</summary>
    public string Dark1 { get; init; } = "000000";

    /// <summary>Light 1 color (typically white).</summary>
    public string Light1 { get; init; } = "FFFFFF";

    /// <summary>Dark 2 color.</summary>
    public string Dark2 { get; init; } = "44546A";

    /// <summary>Light 2 color.</summary>
    public string Light2 { get; init; } = "E7E6E6";

    /// <summary>Accent color 1.</summary>
    public string Accent1 { get; init; } = "4472C4";

    /// <summary>Accent color 2.</summary>
    public string Accent2 { get; init; } = "ED7D31";

    /// <summary>Accent color 3.</summary>
    public string Accent3 { get; init; } = "A5A5A5";

    /// <summary>Accent color 4.</summary>
    public string Accent4 { get; init; } = "FFC000";

    /// <summary>Accent color 5.</summary>
    public string Accent5 { get; init; } = "5B9BD5";

    /// <summary>Accent color 6.</summary>
    public string Accent6 { get; init; } = "70AD47";

    /// <summary>Hyperlink color.</summary>
    public string Hyperlink { get; init; } = "0563C1";

    /// <summary>Followed hyperlink color.</summary>
    public string FollowedHyperlink { get; init; } = "954F72";

    /// <summary>
    /// Stroke widths (in EMU) from <c>theme/formatScheme/lnStyleLst</c>, indexed 1-based by
    /// <c>a:lnRef/@idx</c>. Defaults to Office's standard 0.5pt / 1pt / 1.5pt when the theme
    /// doesn't supply its own list. Index 0 in the list is unused (reserved for "no line" in
    /// some style refs).
    /// </summary>
    public IReadOnlyList<long> LineStyleWidthsEmu { get; init; } = [0, 6350, 12700, 19050];

    /// <summary>
    /// Resolves a theme color name to its hex value.
    /// </summary>
    /// <param name="themeColorName">Theme color name (e.g., "text1", "accent2", "hyperlink")</param>
    /// <param name="shade">Optional shade value (0-255 from WordprocessingML w:themeShade, darkens the color)</param>
    /// <param name="tint">Optional tint value (0-255 from WordprocessingML w:themeTint, lightens the color)</param>
    /// <returns>The resolved hex color value, or null if not found.</returns>
    public string? ResolveColor(string themeColorName, byte? shade = null, byte? tint = null) =>
        ResolveColor(
            themeColorName,
            new()
            {
                Shade = shade, Tint = tint
            });

    /// <summary>
    /// Resolves a theme color name to its hex value with full transform support.
    /// </summary>
    /// <param name="themeColorName">Theme color name (e.g., "text1", "accent2", "hyperlink")</param>
    /// <param name="transforms">Color transforms to apply (shade, tint, lumMod, satMod, etc.)</param>
    /// <returns>The resolved hex color value, or null if not found.</returns>
    public string? ResolveColor(string themeColorName, ColorTransforms transforms)
    {
        // Map theme color names to base colors
        var baseColor = themeColorName.ToLowerInvariant() switch
        {
            "text1" or "dark1" or "dk1" or "tx1" => Dark1,
            "text2" or "dark2" or "dk2" or "tx2" => Dark2,
            "background1" or "light1" or "lt1" or "bg1" => Light1,
            "background2" or "light2" or "lt2" or "bg2" => Light2,
            "accent1" => Accent1,
            "accent2" => Accent2,
            "accent3" => Accent3,
            "accent4" => Accent4,
            "accent5" => Accent5,
            "accent6" => Accent6,
            "hyperlink" or "hlink" => Hyperlink,
            "followedhyperlink" or "folhlink" => FollowedHyperlink,
            _ => null
        };

        if (baseColor == null)
        {
            return null;
        }

        return ApplyColorTransforms(baseColor, transforms);
    }

    /// <summary>
    /// Applies all color transforms to a base color.
    /// Order matters: lumMod/satMod first (HSL), then shade/tint (RGB).
    /// </summary>
    static string ApplyColorTransforms(string hexColor, ColorTransforms transforms)
    {
        if (!TryParseHexColor(hexColor, out var r, out var g, out var b))
        {
            return hexColor;
        }

        // Apply HSL-based transforms first (lumMod, satMod, lumOff, satOff)
        if (transforms.LumMod.HasValue || transforms.SatMod.HasValue ||
            transforms.LumOff.HasValue || transforms.SatOff.HasValue)
        {
            RgbToHsl(r, g, b, out var h, out var s, out var l);

            // Apply saturation modulation (percentage)
            if (transforms.SatMod.HasValue)
            {
                s *= transforms.SatMod.Value / 100.0;
            }

            // Apply saturation offset (percentage points)
            if (transforms.SatOff.HasValue)
            {
                s += transforms.SatOff.Value / 100.0;
            }

            // Apply luminance modulation (percentage)
            if (transforms.LumMod.HasValue)
            {
                l *= transforms.LumMod.Value / 100.0;
            }

            // Apply luminance offset (percentage points)
            if (transforms.LumOff.HasValue)
            {
                l += transforms.LumOff.Value / 100.0;
            }

            // Clamp values
            s = Math.Clamp(s, 0.0, 1.0);
            l = Math.Clamp(l, 0.0, 1.0);

            HslToRgb(h, s, l, out r, out g, out b);
        }

        // Apply RGB-based transforms (shade, tint)
        // Per ECMA-376: shade darkens the color, tint lightens it
        // Values are in 0-100 percentage scale
        if (transforms.Shade is > 0)
        {
            var shade = transforms.Shade.Value;
            r = (byte)(r * shade / 255);
            g = (byte)(g * shade / 255);
            b = (byte)(b * shade / 255);
        }

        if (transforms.Tint.HasValue)
        {
            // In OOXML, themeTint value is inverted: higher value = less tinting (closer to original)
            // 0xFF (255) = no change, 0x00 (0) = full white
            // So we use (255 - tint) as the amount of white to add
            var tintAmount = 255 - transforms.Tint.Value;
            r = (byte)(r + (255 - r) * tintAmount / 255);
            g = (byte)(g + (255 - g) * tintAmount / 255);
            b = (byte)(b + (255 - b) * tintAmount / 255);
        }

        return $"{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>
    /// Converts RGB to HSL color space.
    /// </summary>
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

    /// <summary>
    /// Converts HSL to RGB color space.
    /// </summary>
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
}