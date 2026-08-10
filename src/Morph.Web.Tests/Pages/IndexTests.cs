public class IndexTests : BunitTestContext
{
    public IndexTests() =>
        JSInterop.Mode = JSRuntimeMode.Loose;

    [Test]
    public Task LayoutStructure()
    {
        var cut = Render<Morph.Web.Pages.Index>();

        return Verify(cut);
    }

    [Test]
    public async Task InitialRender_ShowsUploadPrompt()
    {
        var cut = Render<Morph.Web.Pages.Index>();

        await Assert.That(cut.Find(".file-drop-text strong").TextContent).IsEqualTo("Choose an Office file");
        await Assert.That(cut.FindAll(".convert-panel")).IsEmpty();
    }

    // One sample button per readable format, each labelled with that format's short name.
    [Test]
    public async Task InitialRender_ShowsASampleButtonPerReadableFormat()
    {
        var cut = Render<Morph.Web.Pages.Index>();

        var labels = cut.FindAll(".sample-btn").Select(_ => _.TextContent.Trim()).ToList();

        await Assert.That(labels.Count).IsEqualTo(ConversionService.ReadableFormats.Count);
        foreach (var format in ConversionService.ReadableFormats)
        {
            await Assert.That(labels).Contains($"{format.Icon} Sample {format.ShortName}");
        }
    }
}
