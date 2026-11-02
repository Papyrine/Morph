using static EditFixtures;

// What the editor keeps track of between edits: where the script says the reader is, and in which file's
// paragraphs; a hand-in that arrives while another edit is under way; the page a header's paragraph is
// opened again on; something put in over text selected; and an edit's undo step and draft when what
// follows it fails.
public class MorphEditorStateTests : BunitTestContext
{
    static readonly EditOptions plain = new("Ann", DateTimeOffset.UnixEpoch, Track: false);

    readonly BunitJSModuleInterop controller;

    public MorphEditorStateTests()
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

    void Caret(int paragraph, int offset) =>
        Selected(paragraph, offset, offset);

    void Selected(int paragraph, int from, int to) =>
        controller.Setup<int[]>("editTarget").SetResult([paragraph, paragraph, paragraph, to, paragraph, from, paragraph, to]);

    // Where a paragraph is on a page, and its text in the page's.
    static (EditBlock Block, int Offset) Place(byte[] docx, int paragraph, string text, int page = 0)
    {
        using var document = PagedDocument.Open(docx, InputFormat.Docx, Sample.FontDirectory, traceSources: true);
        var block = document.Edits(DocumentOutline.Read(docx)).Blocks(paragraph).First(_ => _.Page == page);
        return (block, document.TextLayer(block.Page).Text.IndexOf(text, StringComparison.Ordinal));
    }

    JsonElement Session()
    {
        var json = (string) controller.Invocations["beginEdit"][^1].Arguments[1]!;
        return JsonDocument.Parse(json).RootElement;
    }

    // The first paragraph's "First" changed with tracking on: one tracked change to accept.
    static byte[] Tracked() =>
        DocumentEditor.Rewrite(Paragraphs, 0, [new([new(0, "Very first paragraph here.")])], new("Ann", DateTimeOffset.UnixEpoch, Track: true)).Document;

    // The script keeps the place of the last paragraph open after the pages are laid out again under it,
    // in the ordinals of the file it was opened in: once another edit has made a new file, that place is
    // refused rather than taken to be the same paragraph of the new one.
    [Test]
    public async Task APlaceFromAnOlderFile_IsRefused()
    {
        byte[]? reported = null;
        var cut = await Open(Tracked(), _ => _.Add(_ => _.OnDocumentChanged, _ => reported = _));
        var (block, offset) = Place(Tracked(), 2, "Third");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 10, block.Top + 2, offset, offset));

        await cut.InvokeAsync(() => cut.Instance.ResolveAllAsync(true));
        await Assert.That(DocumentReview.Read(reported!).Changes).IsEmpty();

        Caret(2, 3);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("heading1"));

        await Assert.That(Read(reported!)[2]).IsEqualTo("[Third and last.]");
        await Assert.That(cut.Find(".editor-status-note").TextContent).Contains("Click in the document first");
    }

    // A paragraph handed in while another edit is being laid out — the box lost the focus to the button
    // that made it — waits for that edit, and is then made, rather than refused and lost.
    [Test]
    public async Task AHandIn_DuringAnotherEdit_WaitsForIt()
    {
        byte[]? reported = null;
        var tracked = Tracked();
        var cut = await Open(tracked, _ => _.Add(_ => _.OnDocumentChanged, _ => reported = _));
        var (block, offset) = Place(tracked, 2, "Third");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 10, block.Top + 2, offset, offset));
        var id = Session().GetProperty("id").GetInt32();

        Task? resolving = null;
        await cut.InvokeAsync(() =>
        {
            resolving = cut.Instance.ResolveAllAsync(true);
        });
        cut.WaitForState(() => cut.Instance.revising, TimeSpan.FromSeconds(30));

        await cut.InvokeAsync(() => cut.Instance.OnEditCommit(id, """[{"a":null,"i":[[0,"Third, typed, and last.",0]]}]""", 0));
        await resolving!;

        await Assert.That(DocumentReview.Read(reported!).Changes).IsEmpty();
        await Assert.That(Read(reported!)[2]).IsEqualTo("[Third, typed, and last.]");
    }

    // A header is drawn on every page; one opened on the second page opens again there after a command,
    // not on the first.
    [Test]
    public async Task AHeader_IsOpenedAgain_OnThePageItWasOn()
    {
        var broken = DocumentEditor.InsertPageBreak(Paragraphs, 1, 0, plain).Document;
        var headed = DocumentEditor.EditHeader(broken, 0, false, plain);
        var docx = DocumentEditor.Rewrite(headed.Document, headed.Paragraph, [new([new(-1, "Running head")])], plain).Document;
        var header = DocumentOutline.Read(docx).Paragraphs.Single(_ => _.Part == ReviewPart.Header).Ordinal;
        var cut = await Open(docx);
        var (block, offset) = Place(docx, header, "Running", page: 1);

        await cut.InvokeAsync(() => cut.Instance.OnEditHit(1, block.Left + 2, block.Top + 2, offset + 2, offset + 2));
        await Assert.That(Session().GetProperty("page").GetInt32()).IsEqualTo(1);

        Caret(header, 2);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("story-end"));

        await Assert.That(Session().GetProperty("p").GetInt32()).IsEqualTo(header);
        await Assert.That(Session().GetProperty("page").GetInt32()).IsEqualTo(1);
    }

    // A page number put in with text selected takes the text's place, as Word's does.
    [Test]
    public async Task AnInsert_OverASelection_ReplacesIt()
    {
        byte[]? reported = null;
        var cut = await Open(Paragraphs, _ => _.Add(_ => _.OnDocumentChanged, _ => reported = _));

        Selected(0, 6, 15);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("page-field"));

        var first = Read(reported!)[0];
        await Assert.That(first).StartsWith("[First ]");
        await Assert.That(first).DoesNotContain("paragraph");
        await Assert.That(first).EndsWith("[ here.]");
        await Assert.That(DocumentOutline.Read(reported!).Paragraphs[0].Units.Any(_ => _.Kind == EditUnitKind.Field)).IsTrue();
    }

    // A link's text put in at the caret leaves the caret after it, as Word's does.
    [Test]
    public async Task ALinksText_LeavesTheCaretAfterIt()
    {
        var cut = await Open(Paragraphs);
        Caret(0, 6);
        await cut.InvokeAsync(async () => await cut.Instance.BeginLinkAsync());

        await cut.InvokeAsync(() => cut.Instance.LinkTextAsync("site ", "example.com"));

        await Assert.That(Session().GetProperty("p").GetInt32()).IsEqualTo(0);
        await Assert.That(Session().GetProperty("from").GetInt32()).IsEqualTo(11);
    }

    // A host that throws on hearing of an edit costs the edit neither its undo step nor its draft.
    [Test]
    public async Task AnEdit_TheHostThrowsOn_KeepsItsUndo_AndItsDraft()
    {
        var cut = await Open(Paragraphs, _ => _.Add(_ => _.OnDocumentChanged, _ => throw new InvalidOperationException("The host fell over.")));

        Caret(1, 0);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("bullets"));

        var kept = (byte[]) controller.Invocations["keepDraft"].Single().Arguments[1]!;
        await Assert.That(DocumentEditor.ListOf(kept, 1)).IsEqualTo(ListKind.Bullet);
        await Assert.That(cut.Find(".error-message").TextContent).Contains("The host fell over.");

        // Undone back to the file as opened, which is the file as saved: the draft goes.
        await cut.InvokeAsync(() => cut.Instance.UndoAsync());
        await Assert.That(controller.Invocations["reload"].Count).IsEqualTo(2);
        await Assert.That(controller.Invocations["dropDraft"].Count).IsEqualTo(1);
    }

    // Under a WOPI host the file is the host's: no draft is kept in the browser, though the page still
    // asks before it is left with edits not saved.
    [Test]
    public async Task KeepDraftsOff_KeepsNoDraft()
    {
        var cut = await Open(Paragraphs, _ => _.Add(_ => _.KeepDrafts, false));

        Caret(1, 0);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("bullets"));

        await Assert.That(controller.Invocations["keepDraft"].Count).IsEqualTo(0);
        await Assert.That((bool) controller.Invocations["setUnsaved"][^1].Arguments[0]!).IsTrue();
    }
}
