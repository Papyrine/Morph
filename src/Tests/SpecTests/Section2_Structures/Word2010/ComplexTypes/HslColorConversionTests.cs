/// <summary>
/// Tests for HSL (Hue, Saturation, Luminance) color space conversions.
/// These are used internally by CT_SchemeColor color transforms.
/// </summary>
public class HslColorConversionTests
{
    [Test]
    public async Task PureRed_RoundTrip_PreservesColor()
    {
        var themeColors = new ThemeColors { Accent1 = "FF0000" };
        var transforms = new ColorTransforms { LumMod = 100, SatMod = 100 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsEqualTo("FF0000");
    }

    [Test]
    public async Task PureGreen_RoundTrip_PreservesColor()
    {
        var themeColors = new ThemeColors { Accent1 = "00FF00" };
        var transforms = new ColorTransforms { LumMod = 100, SatMod = 100 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsEqualTo("00FF00");
    }

    [Test]
    public async Task PureBlue_RoundTrip_PreservesColor()
    {
        var themeColors = new ThemeColors { Accent1 = "0000FF" };
        var transforms = new ColorTransforms { LumMod = 100, SatMod = 100 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsEqualTo("0000FF");
    }

    [Test]
    public async Task White_HasMaxLuminance()
    {
        var themeColors = new ThemeColors { Light1 = "FFFFFF" };
        var transforms = new ColorTransforms { LumMod = 50 };
        var result = themeColors.ResolveColor("light1", transforms);
        await Assert.That(result).IsEqualTo("808080");
    }

    [Test]
    public async Task Black_HasZeroLuminance()
    {
        var themeColors = new ThemeColors { Dark1 = "000000" };
        var transforms = new ColorTransforms { LumMod = 50 };
        var result = themeColors.ResolveColor("dark1", transforms);
        await Assert.That(result).IsEqualTo("000000");
    }

    [Test]
    public async Task MediumGray_RoundTrip_PreservesColor()
    {
        var themeColors = new ThemeColors { Accent1 = "808080" };
        var transforms = new ColorTransforms { LumMod = 100 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsEqualTo("808080");
    }

    [Test]
    public async Task Gray_SatModIncrease_StaysGray()
    {
        var themeColors = new ThemeColors { Accent1 = "808080" };
        var transforms = new ColorTransforms { SatMod = 200 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsEqualTo("808080");
    }

    [Test]
    public async Task LumOff_PushesAbove100_ClampedToMax()
    {
        var themeColors = new ThemeColors { Light1 = "C0C0C0" };
        var transforms = new ColorTransforms { LumOff = 50 };
        var result = themeColors.ResolveColor("light1", transforms);
        await Assert.That(result).IsEqualTo("FFFFFF");
    }

    [Test]
    public async Task LumOff_PushesBelow0_ClampedToMin()
    {
        var themeColors = new ThemeColors { Dark1 = "404040" };
        var transforms = new ColorTransforms { LumOff = -50 };
        var result = themeColors.ResolveColor("dark1", transforms);
        await Assert.That(result).IsEqualTo("000000");
    }

    [Test]
    public async Task SatMod_ZeroPercent_RemovesSaturation()
    {
        var themeColors = new ThemeColors { Accent1 = "FF0000" };
        var transforms = new ColorTransforms { SatMod = 0 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsEqualTo("808080");
    }

    [Test]
    public async Task LumMod_PreservesHue_RedStaysReddish()
    {
        var themeColors = new ThemeColors { Accent1 = "FF0000" };
        var transforms = new ColorTransforms { LumMod = 75 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsNotNull();
        var r = Convert.ToByte(result![0..2], 16);
        var g = Convert.ToByte(result[2..4], 16);
        var b = Convert.ToByte(result[4..6], 16);
        await Assert.That(r).IsGreaterThan(g);
        await Assert.That(r).IsGreaterThan(b);
    }

    [Test]
    public async Task SatMod_PreservesHue_BlueStaysBluish()
    {
        var themeColors = new ThemeColors { Accent1 = "0000FF" };
        var transforms = new ColorTransforms { SatMod = 50 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsNotNull();
        var r = Convert.ToByte(result![0..2], 16);
        var g = Convert.ToByte(result[2..4], 16);
        var b = Convert.ToByte(result[4..6], 16);
        await Assert.That(b).IsGreaterThan(r);
        await Assert.That(b).IsGreaterThan(g);
    }
}
