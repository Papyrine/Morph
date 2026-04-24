public class DeterministicRenderingTests
{
    [Test]
    public async Task DefaultIsFalse() =>
        await Assert.That(DefaultFontSettings.DeterministicRendering).IsFalse();

    [Test]
    public async Task Set_BeforeRender_Changes()
    {
        DefaultFontSettings.DeterministicRendering = true;
        await Assert.That(DefaultFontSettings.DeterministicRendering).IsTrue();
    }

    [Test]
    public async Task Set_AfterRender_Throws()
    {
        DefaultFontSettings.MarkRenderOccurred();
        await Assert.That(() => DefaultFontSettings.DeterministicRendering = true)
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ResetToDefault_RestoresFalse()
    {
        DefaultFontSettings.DeterministicRendering = true;
        DefaultFontSettings.ResetToDefault();
        await Assert.That(DefaultFontSettings.DeterministicRendering).IsFalse();
    }
}
