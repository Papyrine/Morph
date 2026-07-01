public class ErrorPanelTests : BunitTestContext
{
    [Test]
    public async Task WithIssueUrl_ShowsReportLink()
    {
        var cut = Render<ErrorPanel>(_ => _
            .Add(_ => _.Message, "Could not read the document: boom")
            .Add(_ => _.IssueUrl, "https://github.com/Papyrine/Morph/issues/new?title=x"));

        await Assert.That(cut.Find(".error-message").TextContent).IsEqualTo("Could not read the document: boom");

        var link = cut.Find(".error-report a");
        await Assert.That(link.GetAttribute("href")).IsEqualTo("https://github.com/Papyrine/Morph/issues/new?title=x");
        await Assert.That(link.GetAttribute("target")).IsEqualTo("_blank");
    }

    [Test]
    public async Task WithoutIssueUrl_ShowsNoReportLink()
    {
        var cut = Render<ErrorPanel>(_ => _
            .Add(_ => _.Message, "Upload a supported file."));

        await Assert.That(cut.FindAll(".error-report")).IsEmpty();
        await Assert.That(cut.FindAll("a")).IsEmpty();
    }
}
