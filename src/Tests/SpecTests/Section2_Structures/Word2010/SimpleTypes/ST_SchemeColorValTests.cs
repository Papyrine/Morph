/// <summary>
/// Tests for ST_SchemeColorVal simple type as specified in MS-DOCX Section 6.4.4.
/// ST_SchemeColorVal defines the valid values for scheme color references.
/// </summary>
public class ST_SchemeColorValTests
{
    [Test]
    public async Task Bg1_ResolvesToLight1()
    {
        var themeColors = new ThemeColors { Light1 = "FAFAFA" };
        var result = themeColors.ResolveColor("bg1");
        await Assert.That(result).IsEqualTo("FAFAFA");
    }

    [Test]
    public async Task Bg2_ResolvesToLight2()
    {
        var themeColors = new ThemeColors { Light2 = "E7E6E6" };
        var result = themeColors.ResolveColor("bg2");
        await Assert.That(result).IsEqualTo("E7E6E6");
    }

    [Test]
    public async Task Tx1_ResolvesToDark1()
    {
        var themeColors = new ThemeColors { Dark1 = "1F2937" };
        var result = themeColors.ResolveColor("tx1");
        await Assert.That(result).IsEqualTo("1F2937");
    }

    [Test]
    public async Task Tx2_ResolvesToDark2()
    {
        var themeColors = new ThemeColors { Dark2 = "44546A" };
        var result = themeColors.ResolveColor("tx2");
        await Assert.That(result).IsEqualTo("44546A");
    }

    [Test]
    [Arguments("accent1")]
    [Arguments("accent2")]
    [Arguments("accent3")]
    [Arguments("accent4")]
    [Arguments("accent5")]
    [Arguments("accent6")]
    public async Task AccentColors_AreValid(string colorName)
    {
        var themeColors = new ThemeColors();
        var result = themeColors.ResolveColor(colorName);
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task Hlink_IsValid()
    {
        var themeColors = new ThemeColors { Hyperlink = "0563C1" };
        var result = themeColors.ResolveColor("hlink");
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task FolHlink_IsValid()
    {
        var themeColors = new ThemeColors { FollowedHyperlink = "954F72" };
        var result = themeColors.ResolveColor("folhlink");
        await Assert.That(result).IsNotNull();
    }

    [Test]
    [Arguments("phClr")]
    [Arguments("invalid")]
    [Arguments("")]
    [Arguments("accent7")]
    public async Task InvalidColorNames_ReturnNull(string colorName)
    {
        var themeColors = new ThemeColors();
        var result = themeColors.ResolveColor(colorName);
        await Assert.That(result).IsNull();
    }

    [Test]
    [Arguments("bg1")]
    [Arguments("bg2")]
    [Arguments("tx1")]
    [Arguments("tx2")]
    [Arguments("dk1")]
    [Arguments("dk2")]
    [Arguments("lt1")]
    [Arguments("lt2")]
    [Arguments("accent1")]
    [Arguments("accent2")]
    [Arguments("accent3")]
    [Arguments("accent4")]
    [Arguments("accent5")]
    [Arguments("accent6")]
    [Arguments("hlink")]
    [Arguments("folhlink")]
    public async Task AllValidSchemeColorValues_Resolve(string colorName)
    {
        var themeColors = new ThemeColors();
        var result = themeColors.ResolveColor(colorName);
        await Assert.That(result).IsNotNull();
    }
}
