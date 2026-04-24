public class DefaultFontSettingsTests
{
    [Test]
    public async Task DefaultIsGeorgia() =>
        await Assert.That(DefaultFontSettings.DefaultFont).IsEqualTo("Georgia");

    [Test]
    public async Task SetDefaultFont_BeforeRender_Changes()
    {
        DefaultFontSettings.DefaultFont = "Verdana";
        await Assert.That(DefaultFontSettings.DefaultFont).IsEqualTo("Verdana");
    }

    [Test]
    public async Task SetDefaultFont_AfterRender_Throws()
    {
        DefaultFontSettings.MarkRenderOccurred();
        await Assert.That(() => DefaultFontSettings.DefaultFont = "Verdana")
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SetFontWidthScale_AfterRender_Throws()
    {
        DefaultFontSettings.MarkRenderOccurred();
        await Assert.That(() => DefaultFontSettings.FontWidthScale = 1.2)
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ResetToDefault_RestoresGeorgia()
    {
        DefaultFontSettings.DefaultFont = "Verdana";
        DefaultFontSettings.ResetToDefault();
        await Assert.That(DefaultFontSettings.DefaultFont).IsEqualTo("Georgia");
    }
}
