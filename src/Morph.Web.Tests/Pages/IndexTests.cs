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

        await Assert.That(cut.Find(".file-drop-text strong").TextContent).IsEqualTo("Choose a Word document");
        await Assert.That(cut.FindAll(".convert-panel")).IsEmpty();
    }
}
