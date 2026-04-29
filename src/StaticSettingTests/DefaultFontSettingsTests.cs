public class DefaultFontSettingsTests
{
    [Test]
    public async Task DefaultIsAptos() =>
        await Assert.That(DefaultFontSettings.DefaultFont).IsEqualTo("Aptos");

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
    public async Task ResetToDefault_RestoresAptos()
    {
        DefaultFontSettings.DefaultFont = "Verdana";
        DefaultFontSettings.ResetToDefault();
        await Assert.That(DefaultFontSettings.DefaultFont).IsEqualTo("Aptos");
    }
}
