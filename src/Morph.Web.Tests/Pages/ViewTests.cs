// The /view page is a thin host for the package's MorphViewer — see MorphViewerTests for the viewer itself.
public class ViewTests : BunitTestContext
{
    public ViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetupViewerController();
    }

    [Test]
    public async Task HostsTheViewer_WithSamples()
    {
        var cut = Render<Morph.Web.Pages.View>();

        await Assert.That(cut.FindComponents<MorphViewer>().Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".sample-btn").Count).IsEqualTo(ConversionService.ReadableFormats.Count);
    }
}
