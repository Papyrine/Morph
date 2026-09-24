using Microsoft.AspNetCore.Components.Web;

// The document viewer. The page column and the zoom are the script's (morph-viewer.js); these tests pin the
// half .NET owns — opening a file, the toolbar, find, printing and download — through the calls it makes
// on the script's controller.
public class MorphViewerTests : BunitTestContext
{
    readonly BunitJSModuleInterop controller;

    public MorphViewerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        controller = SetupViewerController();
    }

    async Task<IRenderedComponent<MorphViewer>> OpenSample(Action<ComponentParameterCollectionBuilder<MorphViewer>>? parameters = null)
    {
        var cut = Render(parameters ?? (_ => { }));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync(Sample.DocxBytes, "minutes.docx", Sample.FontDirectory));
        return cut;
    }

    [Test]
    public Task LayoutStructure()
    {
        var cut = Render<MorphViewer>();

        return Verify(cut);
    }

    [Test]
    public async Task EmptyViewer_OffersOpen_AndNoSamplesByDefault()
    {
        var cut = Render<MorphViewer>();

        await Assert.That(cut.Find(".viewer-empty-title").TextContent).IsEqualTo("Open a Word, Excel or PowerPoint file");
        await Assert.That(cut.FindAll(".viewer-open input[type=file]").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".sample-btn")).IsEmpty();
        // Nothing to act on yet.
        await Assert.That(cut.Find("button[aria-label=Print]").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task ShowSamples_OffersOneSamplePerFormat()
    {
        var cut = Render<MorphViewer>(_ => _.Add(component => component.ShowSamples, true));

        await Assert.That(cut.FindAll(".sample-btn").Count).IsEqualTo(ConversionService.ReadableFormats.Count);
    }

    [Test]
    public async Task ShowFlags_DropTheirButtons()
    {
        var cut = Render<MorphViewer>(_ => _
            .Add(component => component.ShowOpenFile, false)
            .Add(component => component.ShowPrint, false)
            .Add(component => component.ShowDownload, false));

        await Assert.That(cut.FindAll(".viewer-open")).IsEmpty();
        await Assert.That(cut.FindAll("button[aria-label=Print]")).IsEmpty();
        await Assert.That(cut.FindAll("button[aria-label=Download]")).IsEmpty();
        await Assert.That(cut.FindAll(".viewer-empty-open")).IsEmpty();
    }

    // Opening parses and lays out once, then hands the script every page's size and text layer; the
    // images follow on demand.
    [Test]
    public async Task Open_LoadsEveryPageIntoTheScript()
    {
        var cut = await OpenSample();

        var load = controller.Invocations["load"].Single();
        var sizes = (double[]) load.Arguments[1]!;
        var texts = (string[]) load.Arguments[2]!;
        await Assert.That(sizes.SequenceEqual([612d, 792, 612, 792])).IsTrue();
        await Assert.That(texts.Length).IsEqualTo(2);
        await Assert.That(load.Arguments[3]).IsEqualTo(0);
        await Assert.That(load.Arguments[4]).IsEqualTo("auto");
        await Assert.That(load.Arguments[5]).IsEqualTo("page");
        await Assert.That(cut.Find(".viewer-page-count").TextContent).IsEqualTo("of 2");
        await Assert.That(cut.Find(".viewer-title").TextContent).IsEqualTo("minutes.docx");
        await Assert.That(cut.FindAll(".viewer-empty")).IsEmpty();
    }

    [Test]
    public async Task InitialZoomAndPage_ReachTheScript()
    {
        await OpenSample(_ => _
            .Add(component => component.InitialZoom, ViewerZoom.PageFit)
            .Add(component => component.InitialPage, 2));

        var load = controller.Invocations["load"].Single();
        await Assert.That(load.Arguments[3]).IsEqualTo(1);
        await Assert.That(load.Arguments[4]).IsEqualTo("page-fit");
    }

    // A deck's pages are slides, in the labels too.
    [Test]
    public async Task Deck_CallsItsPagesSlides()
    {
        var cut = Render<MorphViewer>();
        await cut.InvokeAsync(() => cut.Instance.OpenAsync(Sample.PptxBytes, "deck.pptx", Sample.FontDirectory));

        await Assert.That(controller.Invocations["load"].Single().Arguments[5]).IsEqualTo("slide");
        await Assert.That(cut.Find(".viewer-page-input").GetAttribute("aria-label")).IsEqualTo("Slide number");
    }

    [Test]
    public async Task ViewerState_UpdatesTheToolbar()
    {
        var cut = await OpenSample();

        await cut.InvokeAsync(() => cut.Instance.OnViewerState(2, 1.5, "custom", false, true));

        await Assert.That(cut.Find(".viewer-page-input").GetAttribute("value")).IsEqualTo("2");
        await Assert.That(cut.Find(".viewer-zoom").GetAttribute("value")).IsEqualTo("1.5");
        await Assert.That(cut.Find("button[aria-label='Toggle sidebar']").GetAttribute("aria-pressed")).IsEqualTo("true");
        await Assert.That(cut.Find("button[aria-label='Next page']").HasAttribute("disabled")).IsTrue();
    }

    // A zoom that matches no preset shows as its percentage.
    [Test]
    public async Task CustomZoom_ShowsItsPercentage()
    {
        var cut = await OpenSample();

        await cut.InvokeAsync(() => cut.Instance.OnViewerState(1, 1.37, "custom", false, false));

        var selected = cut.Find(".viewer-zoom option[value=custom]");
        await Assert.That(selected.TextContent).IsEqualTo("137%");
    }

    [Test]
    public async Task ToolbarButtons_DriveTheScript()
    {
        var cut = await OpenSample();

        await cut.Find("button[aria-label='Next page']").ClickAsync(new());
        await cut.Find("button[aria-label='Zoom in']").ClickAsync(new());
        await cut.Find("button[aria-label='Rotate clockwise']").ClickAsync(new());
        await cut.Find("button[aria-label='Toggle sidebar']").ClickAsync(new());
        await cut.Find(".viewer-zoom").ChangeAsync(new() { Value = "page-fit" });
        await cut.Find(".viewer-zoom").ChangeAsync(new() { Value = "2" });

        await Assert.That(controller.Invocations["stepPage"].Single().Arguments[0]).IsEqualTo(1);
        await Assert.That(controller.Invocations["zoomBy"].Single().Arguments[0]).IsEqualTo(1);
        await Assert.That(controller.Invocations["rotate"].Single().Arguments[0]).IsEqualTo(90);
        await Assert.That((bool) controller.Invocations["toggleSidebar"].Single().Arguments[0]!).IsTrue();
        var zooms = controller.Invocations["setZoom"].Select(_ => _.Arguments[0]).ToList();
        await Assert.That(zooms[0]).IsEqualTo("page-fit");
        await Assert.That(zooms[1]).IsEqualTo(2d);
    }

    [Test]
    public async Task PageInput_GoesToThePage_OrResetsWhenInvalid()
    {
        var cut = await OpenSample();

        await cut.Find(".viewer-page-input").ChangeAsync(new() { Value = "2" });
        await Assert.That(controller.Invocations["goToPage"].Single().Arguments[0]).IsEqualTo(1);

        await cut.Find(".viewer-page-input").ChangeAsync(new() { Value = "99" });
        await Assert.That(controller.Invocations["goToPage"].Count).IsEqualTo(1);
        await Assert.That(cut.Find(".viewer-page-input").GetAttribute("value")).IsEqualTo("1");
    }

    // Find searches the pages' text in .NET and sends the script (page, start, length) triples to paint.
    [Test]
    public async Task Find_SendsMatchTriplesAndCounts()
    {
        var cut = await OpenSample();

        await cut.Find("button[aria-label='Find in document']").ClickAsync(new());
        await cut.Find(".viewer-find-input").InputAsync(new() { Value = "the new Secretary" });

        cut.WaitForAssertion(() => cut.Find(".viewer-find-status").TextContent.MarkupMatches("1 of 2"), TimeSpan.FromSeconds(10));
        var sent = controller.Invocations["setFindResults"][^1];
        var triples = (int[]) sent.Arguments[1]!;
        await Assert.That(triples.Length).IsEqualTo(6);
        await Assert.That(sent.Arguments[2]).IsEqualTo(0);

        await cut.Find(".viewer-find-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await Assert.That(controller.Invocations["setFindResults"][^1].Arguments[2]).IsEqualTo(1);
        await Assert.That(cut.Find(".viewer-find-status").TextContent).IsEqualTo("2 of 2");

        await cut.Find(".viewer-find-input").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        await Assert.That(cut.FindAll(".viewer-findbar")).IsEmpty();
        controller.VerifyInvoke("clearFind");
    }

    [Test]
    public async Task Find_WithNoMatch_SaysSo()
    {
        var cut = await OpenSample();

        await cut.InvokeAsync(() => cut.Instance.OnFindRequested());
        await cut.Find(".viewer-find-input").InputAsync(new() { Value = "zebra crossing" });

        cut.WaitForAssertion(() => cut.Find(".viewer-find-status").TextContent.MarkupMatches("No results"), TimeSpan.FromSeconds(10));
    }

    // Print renders every page at the print resolution and streams it to the script, then opens the dialog.
    [Test]
    public async Task Print_StreamsEveryPage_ThenOpensTheDialog()
    {
        var cut = await OpenSample();

        await cut.Find("button[aria-label=Print]").ClickAsync(new());

        controller.VerifyInvoke("beginPrint");
        await Assert.That(controller.Invocations["addPrintPage"].Count).IsEqualTo(2);
        var page = (byte[]) controller.Invocations["addPrintPage"][0].Arguments[1]!;
        // PNG IHDR width, big-endian at offset 16: a US Letter page at 150 DPI.
        await Assert.That((page[16] << 24) | (page[17] << 16) | (page[18] << 8) | page[19]).IsEqualTo(1275);
        controller.VerifyInvoke("finishPrint");
        await Assert.That(cut.FindAll(".viewer-overlay")).IsEmpty();
    }

    // The script asks for pages as (kind, page, dpi) triples; each is rendered and pushed back.
    [Test]
    public async Task RenderQueue_PushesEachRequestedImage()
    {
        var cut = await OpenSample();
        var documentId = (int) controller.Invocations["load"].Single().Arguments[0]!;

        await cut.InvokeAsync(() => cut.Instance.OnRenderQueue([0, 1, 72, 1, 0, 36]));

        // Polled rather than WaitForAssertion: bUnit re-checks that only when the component renders, and
        // the render worker pushes images to the script without re-rendering anything. A first render
        // builds the page's render context (its fonts), so allow it time.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((controller.Invocations["setPageImage"].Count == 0 || controller.Invocations["setThumbnail"].Count == 0) &&
               DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        var image = controller.Invocations["setPageImage"].Single();
        controller.VerifyInvoke("setThumbnail");
        await Assert.That(image.Arguments[0]).IsEqualTo(documentId);
        await Assert.That(image.Arguments[1]).IsEqualTo(1);
        await Assert.That(image.Arguments[3]).IsEqualTo(72);
    }

    [Test]
    public async Task Download_SavesTheOriginalFile()
    {
        var morph = JSInterop.SetupModule($"./{MorphAssets.Script}");
        morph.Mode = JSRuntimeMode.Loose;
        var cut = await OpenSample();

        await cut.Find("button[aria-label=Download]").ClickAsync(new());

        var download = morph.Invocations["download"].Single();
        await Assert.That(download.Arguments[0]).IsEqualTo("minutes.docx");
        await Assert.That(download.Arguments[1]).IsEqualTo(ConversionService.Find(InputFormat.Docx).ContentType);
        await Assert.That(((byte[]) download.Arguments[2]!).AsSpan().SequenceEqual(Sample.DocxBytes)).IsTrue();
    }

    // A file Morph cannot read is the user's mistake, not a bug: no issue link.
    [Test]
    public async Task UnreadableFile_ShowsAnErrorWithoutAnIssueLink()
    {
        var cut = Render<MorphViewer>();

        await cut.InvokeAsync(() => cut.Instance.OpenAsync([1, 2, 3], "notes.txt", Sample.FontDirectory));

        await Assert.That(cut.Find(".error-message").TextContent).Contains("Can't read 'notes.txt'");
        await Assert.That(cut.FindAll(".error-report")).IsEmpty();
    }

    [Test]
    public async Task CorruptFile_ShowsAnErrorWithAnIssueLink()
    {
        var cut = Render<MorphViewer>();

        await cut.InvokeAsync(() => cut.Instance.OpenAsync([1, 2, 3], "broken.docx", Sample.FontDirectory));

        await Assert.That(cut.Find(".error-message").TextContent).StartsWith("Could not open the Word document");
        await Assert.That(cut.Find(".error-report a").GetAttribute("href")).StartsWith("https://github.com/Papyrine/Morph/issues/new?");
    }

    [Test]
    public async Task Dispose_ReleasesTheController()
    {
        var cut = await OpenSample();

        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());

        controller.VerifyInvoke("dispose");
    }
}
