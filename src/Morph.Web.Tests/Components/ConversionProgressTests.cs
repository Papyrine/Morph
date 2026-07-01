public class ConversionProgressTests : BunitTestContext
{
    [Test]
    public async Task Always_RendersIndeterminateBar()
    {
        var cut = Render<ConversionProgress>(_ => _
            .Add(_ => _.Label, "Rendering preview…"));

        await Assert.That(cut.Find(".progress-fill").ClassList).Contains("progress-fill-indeterminate");
        await Assert.That(cut.Find(".progress-label span").TextContent).IsEqualTo("Rendering preview…");
    }

    [Test]
    public async Task WithDetail_ShowsDetailCount()
    {
        var cut = Render<ConversionProgress>(_ => _
            .Add(_ => _.Label, "Rendering preview…")
            .Add(_ => _.Detail, "5 pages"));

        await Assert.That(cut.Find(".progress-count").TextContent).IsEqualTo("5 pages");
    }

    [Test]
    public async Task WithoutDetail_RendersNoCount()
    {
        var cut = Render<ConversionProgress>(_ => _
            .Add(_ => _.Label, "Reading document…"));

        await Assert.That(cut.FindAll(".progress-count")).IsEmpty();
    }

    [Test]
    public async Task Progressbar_UsesLabelForAccessibleName()
    {
        var cut = Render<ConversionProgress>(_ => _
            .Add(_ => _.Label, "Converting to PDF…"));

        await Assert.That(cut.Find(".progress").GetAttribute("aria-label")).IsEqualTo("Converting to PDF…");
    }
}
