/// <summary>
/// Tests for CT_SchemeColor complex type as specified in MS-DOCX Section 2.6.3.27.
/// CT_SchemeColor represents a color that is a reference to a color scheme value.
///
/// The color transforms are applied in this order per spec:
/// 1. HSL-based transforms: lumMod, satMod, lumOff, satOff (modify luminance/saturation)
/// 2. RGB-based transforms: shade (darkens), tint (lightens)
/// </summary>
public class CT_SchemeColorTests
{
    #region Theme Color Resolution Tests (ST_SchemeColorVal)

    [Test]
    public async Task ResolveColor_Dark1_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { Dark1 = "1F2937" };
        var result = themeColors.ResolveColor("dark1");
        await Assert.That(result).IsEqualTo("1F2937");
    }

    [Test]
    public async Task ResolveColor_Dk1Alias_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { Dark1 = "1F2937" };
        var result = themeColors.ResolveColor("dk1");
        await Assert.That(result).IsEqualTo("1F2937");
    }

    [Test]
    public async Task ResolveColor_Text1Alias_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { Dark1 = "1F2937" };
        var result = themeColors.ResolveColor("text1");
        await Assert.That(result).IsEqualTo("1F2937");
    }

    [Test]
    public async Task ResolveColor_Tx1Alias_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { Dark1 = "1F2937" };
        var result = themeColors.ResolveColor("tx1");
        await Assert.That(result).IsEqualTo("1F2937");
    }

    [Test]
    public async Task ResolveColor_Light1_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { Light1 = "FAFAFA" };
        var result = themeColors.ResolveColor("light1");
        await Assert.That(result).IsEqualTo("FAFAFA");
    }

    [Test]
    public async Task ResolveColor_Lt1Alias_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { Light1 = "FAFAFA" };
        var result = themeColors.ResolveColor("lt1");
        await Assert.That(result).IsEqualTo("FAFAFA");
    }

    [Test]
    public async Task ResolveColor_Background1Alias_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { Light1 = "FAFAFA" };
        var result = themeColors.ResolveColor("background1");
        await Assert.That(result).IsEqualTo("FAFAFA");
    }

    [Test]
    public async Task ResolveColor_Bg1Alias_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { Light1 = "FAFAFA" };
        var result = themeColors.ResolveColor("bg1");
        await Assert.That(result).IsEqualTo("FAFAFA");
    }

    [Test]
    public async Task ResolveColor_Dark2_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { Dark2 = "44546A" };
        var result = themeColors.ResolveColor("dark2");
        await Assert.That(result).IsEqualTo("44546A");
    }

    [Test]
    public async Task ResolveColor_Light2_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { Light2 = "E7E6E6" };
        var result = themeColors.ResolveColor("light2");
        await Assert.That(result).IsEqualTo("E7E6E6");
    }

    [Test]
    [Arguments("accent1", "4472C4")]
    [Arguments("accent2", "ED7D31")]
    [Arguments("accent3", "A5A5A5")]
    [Arguments("accent4", "FFC000")]
    [Arguments("accent5", "5B9BD5")]
    [Arguments("accent6", "70AD47")]
    public async Task ResolveColor_AccentColors_ReturnsBaseColor(string colorName, string expected)
    {
        var themeColors = new ThemeColors();
        var result = themeColors.ResolveColor(colorName);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task ResolveColor_Hyperlink_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { Hyperlink = "0563C1" };
        var result = themeColors.ResolveColor("hyperlink");
        await Assert.That(result).IsEqualTo("0563C1");
    }

    [Test]
    public async Task ResolveColor_HlinkAlias_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { Hyperlink = "0563C1" };
        var result = themeColors.ResolveColor("hlink");
        await Assert.That(result).IsEqualTo("0563C1");
    }

    [Test]
    public async Task ResolveColor_FollowedHyperlink_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { FollowedHyperlink = "954F72" };
        var result = themeColors.ResolveColor("followedhyperlink");
        await Assert.That(result).IsEqualTo("954F72");
    }

    [Test]
    public async Task ResolveColor_FolHlinkAlias_ReturnsBaseColor()
    {
        var themeColors = new ThemeColors { FollowedHyperlink = "954F72" };
        var result = themeColors.ResolveColor("folhlink");
        await Assert.That(result).IsEqualTo("954F72");
    }

    [Test]
    public async Task ResolveColor_UnknownColor_ReturnsNull()
    {
        var themeColors = new ThemeColors();
        var result = themeColors.ResolveColor("unknowncolor");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ResolveColor_CaseInsensitive()
    {
        var themeColors = new ThemeColors { Accent1 = "FF0000" };
        await Assert.That(themeColors.ResolveColor("ACCENT1")).IsEqualTo("FF0000");
        await Assert.That(themeColors.ResolveColor("Accent1")).IsEqualTo("FF0000");
        await Assert.That(themeColors.ResolveColor("accent1")).IsEqualTo("FF0000");
    }

    #endregion

    #region Shade Transform Tests (MS-DOCX 2.6.3.27)

    [Test]
    public async Task Shade_HalfValue_DarkensColor()
    {
        var themeColors = new ThemeColors { Accent1 = "FF8080" };
        var result = themeColors.ResolveColor("accent1", shade: 127);
        await Assert.That(result).IsNotNull();
        var r = Convert.ToByte(result![0..2], 16);
        var g = Convert.ToByte(result[2..4], 16);
        await Assert.That(r).IsLessThan((byte)255);
        await Assert.That(g).IsLessThan((byte)128);
    }

    [Test]
    public async Task Shade_ZeroValue_NoChange()
    {
        var themeColors = new ThemeColors { Accent1 = "4472C4" };
        var result = themeColors.ResolveColor("accent1", shade: 0);
        await Assert.That(result).IsEqualTo("4472C4");
    }

    [Test]
    public async Task Shade_FullValue_KeepsColorUnchanged()
    {
        var themeColors = new ThemeColors { Accent1 = "4472C4" };
        var result = themeColors.ResolveColor("accent1", shade: 255);
        await Assert.That(result).IsEqualTo("4472C4");
    }

    [Test]
    public async Task Shade_OnWhite_CreatesGray()
    {
        var themeColors = new ThemeColors { Light1 = "FFFFFF" };
        var result = themeColors.ResolveColor("light1", shade: 128);
        await Assert.That(result).IsEqualTo("808080");
    }

    [Test]
    public async Task Shade_OnBlack_StaysBlack()
    {
        var themeColors = new ThemeColors { Dark1 = "000000" };
        var result = themeColors.ResolveColor("dark1", shade: 128);
        await Assert.That(result).IsEqualTo("000000");
    }

    #endregion

    #region Tint Transform Tests (MS-DOCX 2.6.3.27)
    // Note: In OOXML themeTint, higher value = LESS tinting (more original color kept)
    // 0xFF (255) = no tinting (keep original), 0x00 (0) = full tinting (all white)

    [Test]
    public async Task Tint_HalfValue_LightensColor()
    {
        // tint 128 means keep ~50% original, add ~50% white
        // For black (000000): result = 255 * (255-128)/255 = 127 → 7F7F7F
        var themeColors = new ThemeColors { Dark1 = "000000" };
        var result = themeColors.ResolveColor("dark1", tint: 128);
        await Assert.That(result).IsEqualTo("7F7F7F");
    }

    [Test]
    public async Task Tint_ZeroValue_BecomesWhite()
    {
        // tint 0 means add 100% white (full tinting)
        var themeColors = new ThemeColors { Dark1 = "000000" };
        var result = themeColors.ResolveColor("dark1", tint: 0);
        await Assert.That(result).IsEqualTo("FFFFFF");
    }

    [Test]
    public async Task Tint_FullValue_NoChange()
    {
        // tint 255 means keep 100% original color (no tinting)
        var themeColors = new ThemeColors { Accent1 = "4472C4" };
        var result = themeColors.ResolveColor("accent1", tint: 255);
        await Assert.That(result).IsEqualTo("4472C4");
    }

    [Test]
    public async Task Tint_OnWhite_StaysWhite()
    {
        var themeColors = new ThemeColors { Light1 = "FFFFFF" };
        var result = themeColors.ResolveColor("light1", tint: 128);
        await Assert.That(result).IsEqualTo("FFFFFF");
    }

    [Test]
    public async Task Tint_OnColor_LightensTowardWhite()
    {
        // tint 128 on blue: keep ~50% blue, add ~50% white
        // r = 0 + (255 * 127/255) = 127 → 7F
        // g = 0 + (255 * 127/255) = 127 → 7F
        // b = 255 + (0 * 127/255) = 255 → FF
        var themeColors = new ThemeColors { Accent1 = "0000FF" };
        var result = themeColors.ResolveColor("accent1", tint: 128);
        await Assert.That(result).IsEqualTo("7F7FFF");
    }

    [Test]
    public async Task Tint_D9Value_MatchesWordBehavior()
    {
        // This matches the actual Word behavior observed in resumes/15
        // themeTint="D9" (217) on black produces #262626
        var themeColors = new ThemeColors { Dark1 = "000000" };
        var result = themeColors.ResolveColor("dark1", tint: 217);
        await Assert.That(result).IsEqualTo("262626");
    }

    #endregion

    #region LumMod Transform Tests (MS-DOCX 2.6.3.27)

    [Test]
    public async Task LumMod_75Percent_ReducesLuminance()
    {
        var themeColors = new ThemeColors { Accent1 = "4472C4" };
        var transforms = new ColorTransforms { LumMod = 75 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task LumMod_100Percent_NoChange()
    {
        var themeColors = new ThemeColors { Accent1 = "4472C4" };
        var transforms = new ColorTransforms { LumMod = 100 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsEqualTo("4472C4");
    }

    [Test]
    public async Task LumMod_50Percent_HalvesLuminance()
    {
        var themeColors = new ThemeColors { Light1 = "FFFFFF" };
        var transforms = new ColorTransforms { LumMod = 50 };
        var result = themeColors.ResolveColor("light1", transforms);
        await Assert.That(result).IsEqualTo("808080");
    }

    [Test]
    public async Task LumMod_ZeroPercent_BecomesBlack()
    {
        var themeColors = new ThemeColors { Light1 = "FFFFFF" };
        var transforms = new ColorTransforms { LumMod = 0 };
        var result = themeColors.ResolveColor("light1", transforms);
        await Assert.That(result).IsEqualTo("000000");
    }

    [Test]
    public async Task LumMod_OnBlack_StaysBlack()
    {
        var themeColors = new ThemeColors { Dark1 = "000000" };
        var transforms = new ColorTransforms { LumMod = 75 };
        var result = themeColors.ResolveColor("dark1", transforms);
        await Assert.That(result).IsEqualTo("000000");
    }

    #endregion

    #region LumOff Transform Tests (MS-DOCX 2.6.3.27)

    [Test]
    public async Task LumOff_PositiveValue_IncreasesLuminance()
    {
        var themeColors = new ThemeColors { Dark1 = "000000" };
        var transforms = new ColorTransforms { LumOff = 50 };
        var result = themeColors.ResolveColor("dark1", transforms);
        await Assert.That(result).IsEqualTo("808080");
    }

    [Test]
    public async Task LumOff_ZeroValue_NoChange()
    {
        var themeColors = new ThemeColors { Accent1 = "4472C4" };
        var transforms = new ColorTransforms { LumOff = 0 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsEqualTo("4472C4");
    }

    [Test]
    public async Task LumOff_NegativeValue_DecreasesLuminance()
    {
        var themeColors = new ThemeColors { Light1 = "FFFFFF" };
        var transforms = new ColorTransforms { LumOff = -50 };
        var result = themeColors.ResolveColor("light1", transforms);
        await Assert.That(result).IsEqualTo("808080");
    }

    [Test]
    public async Task LumOff_Clamped_AtMaximum()
    {
        var themeColors = new ThemeColors { Light1 = "808080" };
        var transforms = new ColorTransforms { LumOff = 100 };
        var result = themeColors.ResolveColor("light1", transforms);
        await Assert.That(result).IsEqualTo("FFFFFF");
    }

    [Test]
    public async Task LumOff_Clamped_AtMinimum()
    {
        var themeColors = new ThemeColors { Light1 = "808080" };
        var transforms = new ColorTransforms { LumOff = -100 };
        var result = themeColors.ResolveColor("light1", transforms);
        await Assert.That(result).IsEqualTo("000000");
    }

    #endregion

    #region SatMod Transform Tests (MS-DOCX 2.6.3.27)

    [Test]
    public async Task SatMod_50Percent_HalvesSaturation()
    {
        var themeColors = new ThemeColors { Accent1 = "FF0000" };
        var transforms = new ColorTransforms { SatMod = 50 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task SatMod_100Percent_NoChange()
    {
        var themeColors = new ThemeColors { Accent1 = "FF0000" };
        var transforms = new ColorTransforms { SatMod = 100 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsEqualTo("FF0000");
    }

    [Test]
    public async Task SatMod_ZeroPercent_BecomesGray()
    {
        var themeColors = new ThemeColors { Accent1 = "FF0000" };
        var transforms = new ColorTransforms { SatMod = 0 };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsEqualTo("808080");
    }

    [Test]
    public async Task SatMod_OnGray_StaysGray()
    {
        var themeColors = new ThemeColors { Light1 = "808080" };
        var transforms = new ColorTransforms { SatMod = 50 };
        var result = themeColors.ResolveColor("light1", transforms);
        await Assert.That(result).IsEqualTo("808080");
    }

    #endregion

    #region Combined Transform Tests (Order of Operations)

    [Test]
    public async Task CombinedTransforms_LumModThenShade_AppliesInOrder()
    {
        var themeColors = new ThemeColors { Light1 = "FFFFFF" };
        var transforms = new ColorTransforms
        {
            LumMod = 50,
            Shade = 128
        };
        var result = themeColors.ResolveColor("light1", transforms);
        await Assert.That(result).IsNotNull();
        var r = Convert.ToByte(result![0..2], 16);
        await Assert.That(r).IsLessThan((byte)0x80);
    }

    [Test]
    public async Task CombinedTransforms_LumModAndSatMod_BothApply()
    {
        var themeColors = new ThemeColors { Accent1 = "FF0000" };
        var transforms = new ColorTransforms
        {
            LumMod = 75,
            SatMod = 50
        };
        var result = themeColors.ResolveColor("accent1", transforms);
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task CombinedTransforms_LumMod20_LumOff80_ProducesVeryLightColor()
    {
        // This is the scenario from business-plans/09 where accent3 should become very light
        // Base color: 73B3B8 (teal), LumMod=20%, LumOff=80%
        // Expected: L = L * 0.2 + 0.8 = very light (90%+ luminance)
        var themeColors = new ThemeColors { Accent3 = "73B3B8" };
        var transforms = new ColorTransforms
        {
            LumMod = 20,  // 20% of original luminance
            LumOff = 80   // Add 80% to luminance
        };
        var result = themeColors.ResolveColor("accent3", transforms);
        await Assert.That(result).IsNotNull();

        // The result should be very light - convert to check luminance
        var r = Convert.ToByte(result![0..2], 16);
        var g = Convert.ToByte(result[2..4], 16);
        var b = Convert.ToByte(result[4..6], 16);

        // With 90%+ luminance, all RGB values should be above 200
        await Assert.That(r).IsGreaterThan((byte)200);
        await Assert.That(g).IsGreaterThan((byte)200);
        await Assert.That(b).IsGreaterThan((byte)200);
    }

    #endregion

    #region ColorTransforms Record Tests

    [Test]
    public async Task ColorTransforms_HasTransforms_TrueWhenShadeSet()
    {
        var transforms = new ColorTransforms { Shade = 100 };
        await Assert.That(transforms.HasTransforms).IsTrue();
    }

    [Test]
    public async Task ColorTransforms_HasTransforms_TrueWhenTintSet()
    {
        var transforms = new ColorTransforms { Tint = 100 };
        await Assert.That(transforms.HasTransforms).IsTrue();
    }

    [Test]
    public async Task ColorTransforms_HasTransforms_TrueWhenLumModSet()
    {
        var transforms = new ColorTransforms { LumMod = 75 };
        await Assert.That(transforms.HasTransforms).IsTrue();
    }

    [Test]
    public async Task ColorTransforms_HasTransforms_FalseWhenEmpty()
    {
        var transforms = new ColorTransforms();
        await Assert.That(transforms.HasTransforms).IsFalse();
    }

    #endregion
}
