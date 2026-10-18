using static EditFixtures;

// Editing a Word document in MorphEditor. Typing itself is the script's (morph-edit.js, and
// SnapshotTests.Edit drives it in a real browser); these tests pin what .NET does — which paragraph a
// click opens and what the script is given to show, what handing a paragraph back leaves in the file,
// and what the ribbon's commands do to it.
//
// The fixture is three paragraphs on one page: "First paragraph here.", a centred "Second bold one."
// and "Third and last.".
public class MorphEditorTests : BunitTestContext
{
    readonly BunitJSModuleInterop controller;

    public MorphEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        controller = SetupViewerController();
    }

    async Task<IRenderedComponent<MorphEditor>> Open(byte[] bytes, Action<ComponentParameterCollectionBuilder<MorphEditor>>? parameters = null, string name = "edit.docx")
    {
        var cut = Render(parameters ?? (_ => { }));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync(bytes, name, Sample.FontDirectory));
        return cut;
    }

    // The editor edits a Word document from the moment it opens.
    Task<IRenderedComponent<MorphEditor>> OpenEditing(byte[] bytes, Action<ComponentParameterCollectionBuilder<MorphEditor>>? parameters = null) =>
        Open(bytes, parameters);

    // Where the script says the reader is (ParagraphEditor.target): a caret, or a selection in one
    // paragraph, in places of it.
    void Caret(int paragraph, int offset) =>
        Selected(paragraph, offset, offset);

    void Selected(int paragraph, int from, int to) =>
        controller.Setup<int[]>("editTarget").SetResult([paragraph, paragraph, paragraph, to, paragraph, from, paragraph, to]);

    static Task ChooseTabAsync(IRenderedComponent<MorphEditor> cut, string name) =>
        cut.FindAll(".editor-tab").First(_ => _.TextContent.Trim() == name).ClickAsync(new());

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
    public async Task TheEditor_EditsAWordDocument_FromTheStart()
    {
        var cut = await Open(Paragraphs);

        await Assert.That((bool) controller.Invocations["setEditMode"][^1].Arguments[0]!).IsTrue();
        await Assert.That(string.Join(' ', cut.FindAll(".editor-tools [data-edit-command]").Select(_ => _.GetAttribute("data-edit-command"))))
            .IsEqualTo("undo redo bold italic underline strike align-left align-center align-right align-justify");
        await Assert.That(cut.FindAll(".editor-tools [data-edit-command][disabled]")).IsEmpty();
        await Assert.That(string.Join(' ', cut.FindAll(".editor-tab").Select(_ => _.TextContent.Trim()))).IsEqualTo("Home Insert Review View");
    }

    // Text is edited in MorphEditor: the viewer only shows it.
    [Test]
    public async Task TheViewer_DoesNotEdit()
    {
        var cut = Render<MorphViewer>();
        await cut.InvokeAsync(() => cut.Instance.OpenAsync(Paragraphs, "edit.docx", Sample.FontDirectory));

        await Assert.That(cut.FindAll("[data-edit-command]")).IsEmpty();
        await Assert.That(controller.Invocations["setEditMode"].All(_ => !(bool) _.Arguments[0]!)).IsTrue();
    }

    [Test]
    public async Task Editing_IsNotTurnedOn_WhereItIsNotAllowed()
    {
        var locked = Build(P("", R("Read only.")), "<w:documentProtection w:edit=\"comments\" w:enforcement=\"1\"/>");

        var readOnly = await Open(Paragraphs, _ => _.Add(component => component.ReadOnly, true));
        var guarded = await Open(locked);
        var workbook = await Open(Sample.XlsxBytes, null, "sample.xlsx");

        foreach (var cut in new[] {readOnly, guarded, workbook})
        {
            await Assert.That((bool) controller.Invocations["setEditMode"][^1].Arguments[0]!).IsFalse();
            await Assert.That(cut.FindAll(".editor-tools [data-edit-command]").All(_ => _.HasAttribute("disabled"))).IsTrue();
        }

        await Assert.That(readOnly.Find(".editor-status").TextContent).Contains("Read-only");
        await Assert.That(guarded.Find(".editor-status").TextContent).Contains("Read-only");
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
        await Assert.That(cut.Find(".editor-status-note").TextContent).Contains("could not be read");
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
        await Assert.That(cut.Find(".editor-status-note").TextContent).Contains("Select some of the document's text");
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
        await ChooseTabAsync(cut, "Review");
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
        await ChooseTabAsync(cut, "Review");

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

    // The ribbon. Each command acts where the script says the reader is, or on what is selected
    // across the pages.

    [Test]
    public async Task AStyle_IsSetOnTheParagraphTheCaretIsIn_AndTheEditorOpensThereAgain()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        Caret(2, 3);

        await cut.Find(".editor-style-button").ClickAsync(new());
        await cut.FindAll(".editor-style").First(_ => _.TextContent.Trim() == "Heading 1").ClickAsync(new());

        await Assert.That(Read(reported!)[2]).IsEqualTo("[Third and last.] #Heading1");
        await Assert.That(Text(Session())).IsEqualTo("Third and last.");
        await Assert.That(Session().GetProperty("from").GetInt32()).IsEqualTo(3);
        await Assert.That(cut.Find(".editor-style-name").TextContent).IsEqualTo("Heading 1");
    }

    [Test]
    public async Task TheStyleMenu_ListsWordsStyles_EachDrawnAsItLooks()
    {
        var cut = await OpenEditing(Paragraphs);

        await cut.Find(".editor-style-button").ClickAsync(new());

        var styles = cut.FindAll(".editor-style");
        await Assert.That(string.Join(", ", styles.Select(_ => _.TextContent.Trim())))
            .IsEqualTo("Normal, No Spacing, Heading 1, Heading 2, Heading 3, Title, Subtitle, Quote, Intense Quote, List Paragraph");
        await Assert.That(styles.First(_ => _.TextContent.Trim() == "Title").GetAttribute("style")).Contains("font-size: 20pt");
        await Assert.That(styles.First(_ => _.TextContent.Trim() == "Normal").GetAttribute("aria-selected")).IsEqualTo("true");
    }

    // Selected across the pages, every paragraph the selection touches.
    [Test]
    public async Task Bullets_GoOnEveryParagraphSelected_AndComeOffAgain()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        var (_, first) = Place(Paragraphs, 0, "paragraph");
        var (_, second) = Place(Paragraphs, 1, "Second");
        controller.Setup<int[]>("editTarget").SetResult([]);
        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([0, first, 0, second + 3]));

        await cut.Find("[aria-label=Bullets]").ClickAsync(new());

        await Assert.That(string.Join(" / ", Read(reported!)))
            .IsEqualTo("[First paragraph here.] #ListParagraph •1.0 / [Second ][b:bold][ one.] (center) #ListParagraph •1.0 / [Third and last.]");

        var (_, again) = Place(reported!, 0, "paragraph");
        var (_, next) = Place(reported!, 1, "Second");
        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([0, again, 0, next + 3]));
        await cut.Find("[aria-label=Bullets]").ClickAsync(new());

        await Assert.That(string.Join(" / ", Read(reported!)))
            .IsEqualTo("[First paragraph here.] / [Second ][b:bold][ one.] (center) / [Third and last.]");
    }

    [Test]
    public async Task Indent_AndLineSpacing_SetTheParagraphsSpacing()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        Caret(0, 0);

        await cut.Find("[aria-label='Increase indent']").ClickAsync(new());
        await cut.Find("[aria-label='Line and paragraph spacing']").ClickAsync(new());
        await cut.FindAll(".editor-menu-item").First(_ => _.TextContent.Trim() == "2.0").ClickAsync(new());

        var xml = Body(reported!);
        await Assert.That(xml).StartsWith("<w:p><w:pPr><w:spacing w:line=\"480\" w:lineRule=\"auto\" /><w:ind w:left=\"720\" /></w:pPr>");
    }

    [Test]
    public async Task APageBreak_GoesWhereTheCaretIs_AndTypingGoesOnAfterIt()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        Caret(0, 6);
        await ChooseTabAsync(cut, "Insert");

        await cut.FindAll(".editor-tool").First(_ => _.TextContent.Contains("Page break")).ClickAsync(new());

        await Assert.That(string.Join(" / ", Read(reported!).Take(3))).IsEqualTo("[First ] / [] / [paragraph here.]");
        await Assert.That(Text(Session())).IsEqualTo("paragraph here.");
        await Assert.That(cut.Find(".editor-status").TextContent).Contains("Page 1 of 2");
    }

    [Test]
    public async Task ATable_IsInsertedWhereTheCaretIs_AndItsTabComesUp()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        Caret(2, 15);
        await ChooseTabAsync(cut, "Insert");

        await cut.FindAll(".editor-tool").First(_ => _.TextContent.Contains("Table")).ClickAsync(new());
        await cut.Find(".editor-grid-cell[aria-label='2 by 3']").ClickAsync(new());

        await Assert.That(Read(reported!).Count).IsEqualTo(3 + 6 + 1);
        await Assert.That(string.Join(' ', cut.FindAll(".editor-tab").Select(_ => _.TextContent.Trim()))).IsEqualTo("Home Insert Table Review View");

        // The caret is in the first cell: a row goes in below it.
        Caret(3, 0);
        await ChooseTabAsync(cut, "Table");
        await cut.FindAll(".editor-tool").First(_ => _.TextContent.Contains("Insert below")).ClickAsync(new());

        await Assert.That(Read(reported!).Count).IsEqualTo(3 + 9 + 1);
    }

    [Test]
    public async Task ALink_IsMadeOfWhatIsSelected()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        Selected(0, 6, 15);
        await ChooseTabAsync(cut, "Insert");

        await cut.FindAll(".editor-tool").First(_ => _.TextContent.Contains("Link")).ClickAsync(new());
        await cut.Find(".editor-dialog input").InputAsync(new() {Value = "example.com/docs"});
        await cut.FindAll(".editor-dialog .editor-button").First(_ => _.TextContent == "Insert").ClickAsync(new());

        await Assert.That(Body(reported!)).Contains("<w:t xml:space=\"preserve\">paragraph</w:t></w:r></w:hyperlink>");
        await Assert.That(DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(reported!), false).MainDocumentPart!.HyperlinkRelationships.Single().Uri.ToString())
            .IsEqualTo("https://example.com/docs");
    }

    [Test]
    public async Task ALink_WithNothingSelected_SaysWhatToDo()
    {
        var cut = await OpenEditing(Paragraphs);
        Caret(0, 6);
        await ChooseTabAsync(cut, "Insert");

        await cut.FindAll(".editor-tool").First(_ => _.TextContent.Contains("Link")).ClickAsync(new());

        await Assert.That(cut.FindAll(".editor-dialog")).IsEmpty();
        await Assert.That(cut.Find(".editor-status-note").TextContent).Contains("Select the text to make a link");
    }

    [Test]
    public async Task ReplaceAll_ReplacesEveryMatch_InOneEdit()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        controller.Setup<int[]>("editTarget").SetResult([]);

        await cut.InvokeAsync(() => cut.Instance.OnReplaceRequested());
        await cut.Find(".viewer-find-input").InputAsync(new() {Value = "e"});
        await cut.Find(".viewer-find-input").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs {Key = "Enter"});
        await cut.Find(".viewer-replace input").InputAsync(new() {Value = "E"});
        await cut.FindAll(".viewer-text-button").First(_ => _.TextContent.Contains("Replace all")).ClickAsync(new());

        await Assert.That(string.Join(" / ", Read(reported!)))
            .IsEqualTo("[First paragraph hErE.] / [SEcond ][b:bold][ onE.] (center) / [Third and last.]");
        await Assert.That(cut.Find(".viewer-replace").TextContent).Contains("Replaced 4");

        await cut.InvokeAsync(() => cut.Instance.OnEditCommand("undo"));
        await Assert.That(Read(reported!)[0]).IsEqualTo("[First paragraph here.]");
    }

    [Test]
    public async Task TheWordCount_CountsTheBody()
    {
        var cut = await OpenEditing(Paragraphs);

        await Assert.That(cut.Find(".editor-status-button").TextContent.Trim()).IsEqualTo("9 words");

        await cut.Find(".editor-status-button").ClickAsync(new());

        await Assert.That(string.Join(" ", cut.FindAll(".editor-count dd").Select(_ => _.TextContent))).IsEqualTo("1 9 46 52 3 3");
    }

    [Test]
    public async Task AComment_IsWrittenOnWhatIsSelectedInTheParagraph()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        Selected(2, 0, 5);
        await ChooseTabAsync(cut, "Review");

        await cut.FindAll(".editor-tool").First(_ => _.TextContent.Contains("New comment")).ClickAsync(new());

        await Assert.That(cut.Find(".review-new .review-quote").TextContent).IsEqualTo("Third");
        await cut.Find(".review-input").InputAsync(new() {Value = "Which third?"});
        await cut.Find(".review-button-primary").ClickAsync(new());

        await Assert.That(DocumentReview.Read(reported!).Comments.Single().Quote).IsEqualTo("Third");
    }

    static string Body(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
        return package.MainDocumentPart!.Document!.Body!.InnerXml.Replace(" xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"", "");
    }
}
