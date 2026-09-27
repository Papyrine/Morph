using static EditFixtures;

// Editing a Word document's text in the viewer. The editor itself is the script's (morph-edit.js, and
// SnapshotTests.Edit drives it in a real browser); these tests pin what .NET does — which paragraph a
// click opens and what the script is given to show, and what handing a paragraph back leaves in the file.
//
// The fixture is three paragraphs on one page: "First paragraph here.", a centred "Second bold one."
// and "Third and last.".
public class MorphViewerEditTests : BunitTestContext
{
    readonly BunitJSModuleInterop controller;

    public MorphViewerEditTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        controller = SetupViewerController();
    }

    async Task<IRenderedComponent<MorphViewer>> Open(byte[] bytes, Action<ComponentParameterCollectionBuilder<MorphViewer>>? parameters = null, string name = "edit.docx")
    {
        var cut = Render(parameters ?? (_ => { }));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync(bytes, name, Sample.FontDirectory));
        return cut;
    }

    async Task<IRenderedComponent<MorphViewer>> OpenEditing(byte[] bytes, Action<ComponentParameterCollectionBuilder<MorphViewer>>? parameters = null)
    {
        var cut = await Open(bytes, parameters);
        await cut.Find(".viewer-edit-toggle").ClickAsync(new());
        return cut;
    }

    // Where a paragraph is on the page, and its text in the page's.
    static (EditBlock Block, int Offset) Place(byte[] docx, int paragraph, string text)
    {
        using var document = PagedDocument.Open(docx, InputFormat.Docx, Sample.FontDirectory, traceSources: true);
        var block = document.Edits(DocumentOutline.Read(docx)).Blocks(paragraph)[0];
        return (block, document.TextLayer(block.Page).Text.IndexOf(text, StringComparison.Ordinal));
    }

    // The session the script was last given.
    System.Text.Json.JsonElement Session()
    {
        var json = (string) controller.Invocations["beginEdit"][^1].Arguments[1]!;
        return System.Text.Json.JsonDocument.Parse(json).RootElement;
    }

    static string Text(System.Text.Json.JsonElement session) =>
        string.Concat(session.GetProperty("u").EnumerateArray().Select(_ => _.GetProperty("t").GetString()));

    [Test]
    public async Task Editing_IsOffered_ForAWordDocument()
    {
        var cut = await Open(Paragraphs);

        await Assert.That(cut.FindAll(".viewer-edit-toggle").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".viewer-editbar")).IsEmpty();

        await cut.Find(".viewer-edit-toggle").ClickAsync(new());

        await Assert.That(cut.Find(".viewer-edit-toggle").GetAttribute("aria-pressed")).IsEqualTo("true");
        await Assert.That(string.Join(' ', cut.FindAll(".viewer-editbar [data-edit-command]").Select(_ => _.GetAttribute("data-edit-command"))))
            .IsEqualTo("bold italic underline strike align-left align-center align-right align-justify undo redo track");
        await Assert.That((bool) controller.Invocations["setEditMode"][^1].Arguments[0]!).IsTrue();

        await cut.Find(".viewer-edit-toggle").ClickAsync(new());

        await Assert.That(cut.FindAll(".viewer-editbar")).IsEmpty();
        await Assert.That((bool) controller.Invocations["setEditMode"][^1].Arguments[0]!).IsFalse();
    }

    [Test]
    public async Task Editing_IsNotOffered_WhereItIsNotAllowed()
    {
        var locked = Build(P("", R("Read only.")), "<w:documentProtection w:edit=\"comments\" w:enforcement=\"1\"/>");

        var readOnly = await Open(Paragraphs, _ => _.Add(component => component.ReadOnly, true));
        var hidden = await Open(Paragraphs, _ => _.Add(component => component.ShowEdit, false));
        var guarded = await Open(locked);
        var workbook = await Open(Sample.XlsxBytes, null, "sample.xlsx");

        await Assert.That(readOnly.FindAll(".viewer-edit-toggle")).IsEmpty();
        await Assert.That(hidden.FindAll(".viewer-edit-toggle")).IsEmpty();
        await Assert.That(guarded.FindAll(".viewer-edit-toggle")).IsEmpty();
        await Assert.That(workbook.FindAll(".viewer-edit-toggle")).IsEmpty();
    }

    [Test]
    public async Task AClick_OpensTheParagraphItIsIn_AtTheCharacterItWasOn()
    {
        var cut = await OpenEditing(Paragraphs);
        var (block, offset) = Place(Paragraphs, 1, "bold");

        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 100, block.Top + 2, offset + 2, offset + 2));

        var session = Session();
        await Assert.That(Text(session)).IsEqualTo("Second bold one.");
        await Assert.That(session.GetProperty("from").GetInt32()).IsEqualTo(9);
        await Assert.That(session.GetProperty("to").GetInt32()).IsEqualTo(9);
        await Assert.That(session.GetProperty("align").GetInt32()).IsEqualTo(1);
        await Assert.That(session.GetProperty("x").GetDouble()).IsEqualTo(72).Within(0.01);
        await Assert.That(session.GetProperty("y").GetDouble()).IsEqualTo(block.Top).Within(0.01);
    }

    [Test]
    public async Task TextSelectedInOneParagraph_OpensItWithThatSelected_AndAcrossTwo_OpensNothing()
    {
        var cut = await OpenEditing(Paragraphs);
        var (block, offset) = Place(Paragraphs, 1, "bold");
        var (_, third) = Place(Paragraphs, 2, "Third");

        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 100, block.Top + 2, offset, offset + 4));

        await Assert.That(Session().GetProperty("from").GetInt32()).IsEqualTo(7);
        await Assert.That(Session().GetProperty("to").GetInt32()).IsEqualTo(11);

        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 100, block.Top + 2, offset, third + 3));

        await Assert.That(controller.Invocations["beginEdit"].Count).IsEqualTo(1);
    }

    // A click that finds no text still finds the paragraph: blank paper beside a short line, or
    // a paragraph with nothing in it.
    [Test]
    public async Task AClickOnBlankPaper_OpensTheParagraphWhoseBoxItIsIn()
    {
        var docx = Build(P("", R("above")) + "<w:p/>" + P("", R("below")));
        var cut = await OpenEditing(docx);
        var (empty, _) = Place(docx, 1, "");
        var (above, _) = Place(docx, 0, "above");

        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, empty.Left + 200, empty.Top + 2, -1, -1));

        await Assert.That(Text(Session())).IsEqualTo("");
        await Assert.That(Session().GetProperty("u").GetArrayLength()).IsEqualTo(0);

        // Far to the right of "above", in the lower half of its line: the caret goes to its end.
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, above.Right - 5, above.Bottom - 1, -1, -1));

        await Assert.That(Text(Session())).IsEqualTo("above");
        await Assert.That(Session().GetProperty("from").GetInt32()).IsEqualTo(5);
    }

    [Test]
    public async Task HandingAParagraphIn_WritesItToTheFile_AndLaysTheFileOutAgain()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        var (block, offset) = Place(Paragraphs, 0, "First");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 10, block.Top + 2, offset, offset));
        var id = Session().GetProperty("id").GetInt32();

        await cut.InvokeAsync(() => cut.Instance.OnEditCommit(id, """[{"a":null,"i":[[0,"First ",0],[0,"new",1],[0," paragraph here.",0]]}]""", 0));

        await Assert.That(string.Join(" / ", Read(reported!)))
            .IsEqualTo("[First ][b:new][ paragraph here.] / [Second ][b:bold][ one.] (center) / [Third and last.]");
        controller.VerifyInvoke("reload");

        // The same session cannot be handed in twice.
        reported = null;
        await cut.InvokeAsync(() => cut.Instance.OnEditCommit(id, """[{"a":null,"i":[[0,"Again",0]]}]""", 0));
        await Assert.That(reported).IsNull();
    }

    [Test]
    public async Task HandingInWhatCannotBeRead_ChangesNothing_AndSaysSo()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        var (block, offset) = Place(Paragraphs, 0, "First");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 10, block.Top + 2, offset, offset));

        await cut.InvokeAsync(() => cut.Instance.OnEditCommit(Session().GetProperty("id").GetInt32(), "not json", 0));

        await Assert.That(reported).IsNull();
        await Assert.That(cut.Find(".viewer-edit-status").TextContent).Contains("could not be read");
    }

    // Backspace at a paragraph's start: what was typed is kept, the two are joined, and the reader
    // goes on typing where they met.
    [Test]
    public async Task HandingIn_ToJoinTheParagraphBefore_GoesOnEditingWhereTheTwoMet()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        var (block, offset) = Place(Paragraphs, 2, "Third");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 10, block.Top + 2, offset, offset));

        await cut.InvokeAsync(() => cut.Instance.OnEditCommit(Session().GetProperty("id").GetInt32(), """[{"a":null,"i":[[0,"Third, and last.",0]]}]""", 1));

        await Assert.That(string.Join(" / ", Read(reported!)))
            .IsEqualTo("[First paragraph here.] / [Second ][b:bold][ one.][Third, and last.] (center)");
        await Assert.That(controller.Invocations["beginEdit"].Count).IsEqualTo(2);
        await Assert.That(Text(Session())).IsEqualTo("Second bold one.Third, and last.");
        await Assert.That(Session().GetProperty("from").GetInt32()).IsEqualTo(16);
    }

    [Test]
    public async Task TheToolbar_FormatsAndAlignsAndDeletes_WhatIsSelectedOnThePages()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        var (_, first) = Place(Paragraphs, 0, "paragraph");
        var (_, second) = Place(Paragraphs, 1, "Second");

        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([0, first, 0, second + 6]));
        await cut.InvokeAsync(() => cut.Instance.OnEditCommand("italic"));

        await Assert.That(string.Join(" / ", Read(reported!)))
            .IsEqualTo("[First ][i:paragraph here.] / [i:Second][ ][b:bold][ one.] (center) / [Third and last.]");

        // All of what is selected is bold: the button takes it off.
        var (_, bold) = Place(reported!, 1, "bold");
        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([0, bold, 0, bold + 4]));
        await cut.InvokeAsync(() => cut.Instance.OnEditCommand("bold"));

        await Assert.That(Read(reported!)[1]).IsEqualTo("[i:Second][ ][bold][ one.] (center)");

        var (_, third) = Place(reported!, 2, "and");
        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([0, third, 0, third + 4]));
        await cut.InvokeAsync(() => cut.Instance.OnEditCommand("align-right"));
        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([0, third, 0, third + 4]));
        await cut.InvokeAsync(() => cut.Instance.OnEditCommand("delete"));

        await Assert.That(Read(reported!)[2]).IsEqualTo("[Third last.] (right)");
    }

    [Test]
    public async Task TheToolbar_WithNothingSelected_SaysWhatToDo()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        await cut.InvokeAsync(() => cut.Instance.OnEditCommand("bold"));

        await Assert.That(reported).IsNull();
        await Assert.That(cut.Find(".viewer-edit-status").TextContent).Contains("Select some of the document's text");
    }

    [Test]
    public async Task TrackChanges_IsTheDocumentsOwnSetting_AndWhatIsTypedUnderItIsAChange()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(
            Paragraphs,
            _ => _
                .Add(component => component.Author, "Ann Lee")
                .Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        await Assert.That(cut.Find("[data-edit-command=track]").GetAttribute("aria-pressed")).IsEqualTo("false");

        await cut.InvokeAsync(() => cut.Instance.OnEditCommand("track"));

        await Assert.That(DocumentOutline.Read(reported!).Tracking).IsTrue();
        await Assert.That(cut.Find("[data-edit-command=track]").GetAttribute("aria-pressed")).IsEqualTo("true");

        var (block, offset) = Place(Paragraphs, 0, "First");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 10, block.Top + 2, offset, offset));
        await cut.InvokeAsync(() => cut.Instance.OnEditCommit(Session().GetProperty("id").GetInt32(), """[{"a":null,"i":[[0,"First short paragraph here.",0]]}]""", 0));

        await Assert.That(Read(reported!)[0]).IsEqualTo("[First ]{+[short ]+}[paragraph here.]");
        var change = DocumentReview.Read(reported!).Changes.Single();
        await Assert.That(change.Author).IsEqualTo("Ann Lee");
        await Assert.That(cut.Find(".viewer-badge").TextContent).IsEqualTo("1");
    }

    // A document protected so that every change is tracked has them tracked, whatever its setting.
    [Test]
    public async Task ADocumentThatHasEveryChangeTracked_DoesNotOfferToStop()
    {
        var docx = Build(P("", R("Tracked.")), "<w:documentProtection w:edit=\"trackedChanges\" w:enforcement=\"1\"/>");
        byte[]? reported = null;
        var cut = await OpenEditing(docx, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        await Assert.That(cut.Find("[data-edit-command=track]").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Find("[data-edit-command=track]").GetAttribute("aria-pressed")).IsEqualTo("true");

        var (block, offset) = Place(docx, 0, "Tracked");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 10, block.Top + 2, offset, offset));
        await cut.InvokeAsync(() => cut.Instance.OnEditCommit(Session().GetProperty("id").GetInt32(), """[{"a":null,"i":[[0,"Tracked, always.",0]]}]""", 0));

        await Assert.That(Read(reported!)[0]).IsEqualTo("[Tracked]{+[, always]+}[.]");
    }

    [Test]
    public async Task Undo_TakesAnEditBack()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        var (block, offset) = Place(Paragraphs, 0, "First");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 10, block.Top + 2, offset, offset));
        await cut.InvokeAsync(() => cut.Instance.OnEditCommit(Session().GetProperty("id").GetInt32(), """[{"a":null,"i":[[0,"Changed.",0]]}]""", 0));
        await Assert.That(Read(reported!)[0]).IsEqualTo("[Changed.]");

        await cut.InvokeAsync(() => cut.Instance.OnEditCommand("undo"));
        await Assert.That(Read(reported!)[0]).IsEqualTo("[First paragraph here.]");

        await cut.InvokeAsync(() => cut.Instance.OnEditCommand("redo"));
        await Assert.That(Read(reported!)[0]).IsEqualTo("[Changed.]");
    }
}
