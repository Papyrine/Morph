// The converter's page preview. Each page with a text layer gets an overlay the script fills with
// selectable text; the component must hand each layer over once, not on every repaint of its parent.
public class DocumentPreviewTests : BunitTestContext
{
    readonly BunitJSModuleInterop textModule;

    public DocumentPreviewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        textModule = JSInterop.SetupModule($"./{MorphAssets.TextScript}");
        // Completed at once, as the browser's build is: a planned call left pending would stall the
        // component's sequential builds after the first.
        textModule.SetupVoid("renderTextLayer", _ => true).SetVoidResult();
    }

    static readonly IReadOnlyList<string> pages = ["page1.png", "page2.png"];

    static PageTextLayer[] Layers() =>
        [new("""{"v":1,"w":612,"h":792,"i":[]}""", "one\n"), new("""{"v":1,"w":612,"h":792,"i":[]}""", "two\n")];

    [Test]
    public Task PagesWithTextLayers_Snapshot()
    {
        var cut = Render<DocumentPreview>(_ => _
            .Add(_ => _.Pages, pages)
            .Add(_ => _.TextLayers, Layers()));

        return Verify(cut);
    }

    [Test]
    public async Task EachLayer_IsBuiltOnce()
    {
        var cut = Render<DocumentPreview>(_ => _
            .Add(_ => _.Pages, pages)
            .Add(_ => _.TextLayers, Layers()));

        cut.Render();

        textModule.VerifyInvoke("renderTextLayer", calledTimes: 2);
        await Assert.That(cut.FindAll(".preview-sheet .text-layer").Count).IsEqualTo(2);
    }

    [Test]
    public async Task NewLayers_AreBuiltAgain()
    {
        var cut = Render<DocumentPreview>(_ => _
            .Add(_ => _.Pages, pages)
            .Add(_ => _.TextLayers, Layers()));

        cut.Render(_ => _.Add(_ => _.TextLayers, Layers()));

        textModule.VerifyInvoke("renderTextLayer", calledTimes: 4);
        await Assert.That(cut.FindAll(".text-layer").Count).IsEqualTo(2);
    }

    // Without layers the pages stay plain pictures, described by their alt text.
    [Test]
    public async Task WithoutLayers_PagesArePlainImages()
    {
        var cut = Render<DocumentPreview>(_ => _.Add(_ => _.Pages, pages));

        await Assert.That(cut.FindAll(".text-layer")).IsEmpty();
        await Assert.That(cut.Find("img.preview-page").GetAttribute("alt")).IsEqualTo("Rendered page preview");
        textModule.VerifyNotInvoke("renderTextLayer");
    }

    [Test]
    public async Task Busy_ShowsProgressInsteadOfPages()
    {
        var cut = Render<DocumentPreview>(_ => _
            .Add(_ => _.Pages, pages)
            .Add(_ => _.TextLayers, Layers())
            .Add(_ => _.Busy, true)
            .Add(_ => _.Label, "Rendering preview…"));

        await Assert.That(cut.FindAll(".preview-sheet")).IsEmpty();
        await Assert.That(cut.Find(".progress-label").TextContent).Contains("Rendering preview…");
    }
}
