using static EditFixtures;

// What the viewer's keys may do where the host takes Download and Print away (MorphWopi's DisableExport
// and DisablePrint), what a file opened over edits not saved waits for, and what an arrow key leaving a
// paragraph is told of a point on a page.
public class MorphViewerKeysTests : BunitTestContext
{
    readonly BunitJSModuleInterop controller;

    public MorphViewerKeysTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        controller = SetupViewerController();
    }

    async Task<IRenderedComponent<MorphEditor>> Open(byte[] bytes, Action<ComponentParameterCollectionBuilder<MorphEditor>>? parameters = null)
    {
        var cut = Render(parameters ?? (_ => { }));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync(bytes, "edit.docx", Sample.FontDirectory));
        return cut;
    }

    // An edit to the first paragraph, handed in: the document has edits not saved.
    async Task EditAsync(IRenderedComponent<MorphEditor> cut)
    {
        using var document = PagedDocument.Open(Paragraphs, InputFormat.Docx, Sample.FontDirectory, traceSources: true);
        var block = document.Edits(DocumentOutline.Read(Paragraphs)).Blocks(0)[0];
        var offset = document.TextLayer(block.Page).Text.IndexOf("First", StringComparison.Ordinal);
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 10, block.Top + 2, offset, offset));
        var json = (string) controller.Invocations["beginEdit"][^1].Arguments[1]!;
        var id = JsonDocument.Parse(json).RootElement.GetProperty("id").GetInt32();
        await cut.InvokeAsync(() => cut.Instance.OnEditCommit(id, """[{"a":null,"i":[[0,"First new paragraph here.",0]]}]""", 0));
    }

    [Test]
    public async Task SaveAndPrintKeys_DoNothing_WhereDownloadAndPrintAreNotOffered()
    {
        var cut = await Open(
            Paragraphs,
            _ => _
                .Add(_ => _.ShowDownload, false)
                .Add(_ => _.ShowPrint, false));
        await EditAsync(cut);

        await cut.InvokeAsync(() => cut.Instance.OnSaveRequested());
        await cut.InvokeAsync(() => cut.Instance.OnPrintRequested());

        await Assert.That(JSInterop.Invocations.Any(_ => _.Identifier == "download")).IsFalse();
        await Assert.That(controller.Invocations.Any(_ => _.Identifier == "beginPrint")).IsFalse();
        // And the script is told, so Ctrl+S and Ctrl+P keep the browser's own meaning.
        var abilities = controller.Invocations["setAbilities"][^1].Arguments;
        await Assert.That((bool) abilities[0]!).IsFalse();
        await Assert.That((bool) abilities[1]!).IsFalse();
    }

    [Test]
    public async Task SaveKey_Downloads_WhereDownloadIsOffered()
    {
        var cut = await Open(Paragraphs);
        await EditAsync(cut);

        await cut.InvokeAsync(() => cut.Instance.OnSaveRequested());

        await Assert.That(JSInterop.Invocations.Any(_ => _.Identifier == "download")).IsTrue();
        var abilities = controller.Invocations["setAbilities"][^1].Arguments;
        await Assert.That((bool) abilities[0]!).IsTrue();
        await Assert.That((bool) abilities[1]!).IsTrue();
    }

    // Another file over edits not saved asks first, as New does; Cancel keeps the document, and Discard
    // opens the file.
    [Test]
    public async Task AnotherFile_OverEditsNotSaved_WaitsForTheReader()
    {
        var other = Build(P("", R("Another document.")));
        var cut = await Open(Paragraphs);
        await EditAsync(cut);

        await cut.InvokeAsync(() => cut.Instance.OpenOrAskAsync(() => cut.Instance.OpenAsync(other, "other.docx", Sample.FontDirectory)));

        await Assert.That(cut.Find(".editor-dialog-title").TextContent).IsEqualTo("Open another document?");
        await Assert.That(cut.Find(".editor-name").TextContent.Trim()).StartsWith("edit.docx");

        await cut.FindAll(".editor-dialog .editor-button").First(_ => _.TextContent.Trim() == "Cancel").ClickAsync(new());
        await Assert.That(cut.FindAll(".editor-dialog")).IsEmpty();
        await Assert.That(cut.Find(".editor-name").TextContent.Trim()).StartsWith("edit.docx");

        await cut.InvokeAsync(() => cut.Instance.OpenOrAskAsync(() => cut.Instance.OpenAsync(other, "other.docx", Sample.FontDirectory)));
        await cut.FindAll(".editor-dialog .editor-button").First(_ => _.TextContent.Trim() == "Discard edits").ClickAsync(new());

        cut.WaitForState(() => cut.Find(".editor-name").TextContent.Trim() == "other.docx");
    }

    // A file with nothing to lose opens at once; New still asks with its own title.
    [Test]
    public async Task AnotherFile_OverNoEdits_OpensAtOnce()
    {
        var other = Build(P("", R("Another document.")));
        var cut = await Open(Paragraphs);

        await cut.InvokeAsync(() => cut.Instance.OpenOrAskAsync(() => cut.Instance.OpenAsync(other, "other.docx", Sample.FontDirectory)));

        await Assert.That(cut.FindAll(".editor-dialog")).IsEmpty();
        await Assert.That(cut.Find(".editor-name").TextContent.Trim()).IsEqualTo("other.docx");
    }

    // The probe an arrow key leaving a paragraph makes: the paragraph a point is in, and its story,
    // with nothing opened.
    [Test]
    public async Task Probe_NamesTheParagraphAtAPoint_AndOpensNothing()
    {
        var cut = await Open(Paragraphs);
        using var document = PagedDocument.Open(Paragraphs, InputFormat.Docx, Sample.FontDirectory, traceSources: true);
        var block = document.Edits(DocumentOutline.Read(Paragraphs)).Blocks(2)[0];
        var opened = controller.Invocations.Count(_ => _.Identifier == "beginEdit");

        var found = await cut.InvokeAsync(() => cut.Instance.OnEditProbe(block.Page, block.Left + 5, block.Top + 2));
        var nowhere = await cut.InvokeAsync(() => cut.Instance.OnEditProbe(block.Page, 1, 1));

        await Assert.That(found).IsEquivalentTo([2, (int) ReviewPart.Body]);
        await Assert.That(nowhere).IsEmpty();
        await Assert.That(controller.Invocations.Count(_ => _.Identifier == "beginEdit")).IsEqualTo(opened);
    }
}
