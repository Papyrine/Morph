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
    /// Applies all colour transforms to a base colour in the HSL space Word uses.
    ///
    /// Both the DrawingML transforms (<c>lumMod</c>/<c>lumOff</c>/<c>satMod</c>/<c>satOff</c>, given
    /// as percentages) and the WordprocessingML <c>w:themeShade</c>/<c>w:themeTint</c> bytes are
    /// luminance modulation: Word scales (and offsets) the HSL <em>luminance</em> while preserving
    /// hue and saturation. The byte forms map onto that model as
    /// <list type="bullet">
    /// <item><c>themeShade S</c>: <c>L' = L · (S/255)</c> — i.e. <c>lumMod = S/255</c>.</item>
    /// <item><c>themeTint T</c>: <c>L' = L · (T/255) + (1 − T/255)</c> — i.e. <c>lumMod = T/255</c>,
    /// <c>lumOff = (255 − T)/255</c>.</item>
    /// </list>
    /// The previous implementation applied shade/tint as an RGB blend toward black/white, which
    /// <em>desaturates</em> the colour (a shaded accent came out muted, e.g. accent3 748DF3 +
    /// shade BF gave 5669B6 instead of Word's 2048EB). Luminance-only scaling reproduces Word.
    /// </summary>
    static string ApplyColorTransforms(string hexColor, ColorTransforms transforms)
    {
        if (!TryParseHexColor(hexColor, out var r, out var g, out var b))
        {
            return hexColor;
        }

        var lumMod = (transforms.LumMod ?? 100.0) / 100.0;
        var lumOff = (transforms.LumOff ?? 0.0) / 100.0;
        var satMod = (transforms.SatMod ?? 100.0) / 100.0;
        var satOff = (transforms.SatOff ?? 0.0) / 100.0;

        // Fold the byte shade/tint into the luminance modulation (they never co-occur with the
        // DrawingML lum/sat transforms on the same colour, but composing is harmless if they did).
        // A byte shade of 0 is treated as "unspecified" rather than "scale luminance to zero"
        // (a literal w:themeShade="00" never occurs; 0 is the absent-value sentinel here).
        if (transforms.Shade is > 0 and { } shade)
        {
            lumMod *= shade / 255.0;
        }

        if (transforms.Tint is { } tint)
        {
            lumMod *= tint / 255.0;
            lumOff += (255 - tint) / 255.0;
        }

        // ReSharper disable once CompareOfFloatsByEqualityOperator — exact 1.0/0.0 means "no-op".
        if (lumMod == 1.0 && lumOff == 0.0 && satMod == 1.0 && satOff == 0.0)
        {
            return $"{r:X2}{g:X2}{b:X2}";
        }

        RgbToHsl(r, g, b, out var h, out var s, out var l);
        s = Math.Clamp(s * satMod + satOff, 0.0, 1.0);
        l = Math.Clamp(l * lumMod + lumOff, 0.0, 1.0);
        HslToRgb(h, s, l, out r, out g, out b);

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