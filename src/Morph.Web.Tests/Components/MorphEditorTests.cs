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

    // The Font group: with the caret in a word, the word is what it formats, as Word's does.
    [Test]
    public async Task TheFontGroup_FormatsTheWordTheCaretIsIn()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        Caret(2, 2);
        await cut.Find(".editor-font-button").ClickAsync(new());
        await cut.FindAll(".editor-font").First(_ => _.TextContent.Trim() == "Georgia").ClickAsync(new());

        await Assert.That(Read(reported!)[2]).IsEqualTo("[Third][ and last.]");
        await Assert.That(Body(reported!)).Contains("<w:rFonts w:ascii=\"Georgia\" w:hAnsi=\"Georgia\" w:cs=\"Georgia\" /></w:rPr><w:t xml:space=\"preserve\">Third</w:t>");

        // The paragraph is open again with the word selected, and the ribbon shows its look.
        await Assert.That(Session().GetProperty("from").GetInt32()).IsEqualTo(0);
        await Assert.That(Session().GetProperty("to").GetInt32()).IsEqualTo(5);
        await Assert.That(cut.Find("[data-edit-show='font']").GetAttribute("data-value")).IsEqualTo("Georgia");

        Caret(2, 2);
        await cut.Find(".editor-size-button").ClickAsync(new());
        await cut.FindAll(".editor-sizes .editor-menu-item").First(_ => _.TextContent.Trim() == "18").ClickAsync(new());
        await Assert.That(Body(reported!)).Contains("<w:sz w:val=\"36\" />");
        await Assert.That(cut.Find("[data-edit-show='size']").GetAttribute("data-value")).IsEqualTo("18");

        Caret(2, 2);
        await cut.Find("[aria-label='Grow font']").ClickAsync(new());
        await Assert.That(Body(reported!)).Contains("<w:sz w:val=\"40\" />");

        Caret(2, 2);
        await cut.Find("[aria-label='Superscript']").ClickAsync(new());
        await Assert.That(Body(reported!)).Contains("<w:vertAlign w:val=\"superscript\" />");
        await Assert.That(cut.Find("[aria-label='Superscript']").GetAttribute("aria-pressed")).IsEqualTo("true");

        // Pressed again, it comes off.
        Caret(2, 2);
        await cut.Find("[aria-label='Superscript']").ClickAsync(new());
        await Assert.That(Body(reported!)).DoesNotContain("vertAlign");

        Caret(2, 2);
        await cut.Find("[aria-label='Clear formatting']").ClickAsync(new());
        await Assert.That(Body(reported!)).EndsWith("<w:p><w:r><w:t xml:space=\"preserve\">Third</w:t></w:r><w:r><w:t xml:space=\"preserve\"> and last.</w:t></w:r></w:p>");
    }

    // A colour chosen from the palette is the one the button applies after.
    [Test]
    public async Task TheColourButtons_ApplyTheColourLastChosen()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        Selected(0, 6, 15);
        await cut.Find("[aria-label='More font colours']").ClickAsync(new());
        await cut.Find(".editor-swatch[aria-label='Blue']").ClickAsync(new());

        await Assert.That(Read(reported!)[0]).IsEqualTo("[First ][paragraph][ here.]");
        await Assert.That(Body(reported!)).Contains("<w:color w:val=\"0070C0\" /></w:rPr><w:t xml:space=\"preserve\">paragraph</w:t>");
        await Assert.That(cut.Find("[aria-label='Font colour']").GetAttribute("style")).Contains("#0070C0");

        Caret(2, 2);
        await cut.Find("[aria-label='Font colour']").ClickAsync(new());
        await Assert.That(Body(reported!)).Contains("<w:color w:val=\"0070C0\" /></w:rPr><w:t xml:space=\"preserve\">Third</w:t>");

        Caret(2, 2);
        await cut.Find("[aria-label='More font colours']").ClickAsync(new());
        await cut.FindAll(".editor-palette .editor-menu-item").First(_ => _.TextContent.Trim() == "Automatic").ClickAsync(new());
        await Assert.That(Body(reported!)).DoesNotContain("<w:color w:val=\"0070C0\" /></w:rPr><w:t xml:space=\"preserve\">Third</w:t>");

        Selected(0, 6, 15);
        await cut.Find("[aria-label='Highlight']").ClickAsync(new());
        await Assert.That(Body(reported!)).Contains("<w:highlight w:val=\"yellow\" />");

        Selected(0, 6, 15);
        await cut.Find("[aria-label='More highlight colours']").ClickAsync(new());
        await cut.FindAll(".editor-palette .editor-menu-item").First(_ => _.TextContent.Trim() == "No colour").ClickAsync(new());
        await Assert.That(Body(reported!)).DoesNotContain("highlight");
    }

    // A caret between two words, or at a paragraph's end, is in no word: there is nothing to format.
    [Test]
    public async Task TheFontGroup_WithTheCaretInNoWord_SaysWhatToDo()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        Caret(2, 15);

        await cut.Find("[aria-label='Grow font']").ClickAsync(new());

        await Assert.That(reported).IsNull();
        await Assert.That(cut.Find(".editor-status-note").TextContent).Contains("Select the text to format, or click in a word.");
    }

    // Word's Grow Font and Shrink Font: along its list of sizes, and a point at a time past its ends.
    [Test]
    [Arguments(11, 1, 12)]
    [Arguments(11, -1, 10.5)]
    [Arguments(10.5, 1, 11)]
    [Arguments(13, 1, 14)]
    [Arguments(13, -1, 12)]
    [Arguments(72, 1, 73)]
    [Arguments(8, -1, 7)]
    [Arguments(1, -1, 1)]
    public async Task GrowAndShrink_StepAlongWordsSizes(double size, int steps, double expected) =>
        await Assert.That(MorphViewer.Step(size, steps)).IsEqualTo(expected);

    // What is not text, left out of what the script hands in — Backspace beside a field — is deleted.
    [Test]
    public async Task AFieldLeftOutOfWhatIsHandedIn_IsDeleted()
    {
        const string field =
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> DATE </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1 May</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";
        var dated = Build(P("", R("Dated "), field, R(" by Ann")));
        byte[]? reported = null;
        var cut = await OpenEditing(dated, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        var (block, offset) = Place(dated, 0, "Dated");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 2, block.Top + 2, offset, offset));

        await cut.InvokeAsync(() => cut.Instance.OnEditCommit(Session().GetProperty("id").GetInt32(), """[{"a":null,"i":[[0,"Dated ",0],[2," by Ann",0]]}]""", 0));

        await Assert.That(Read(reported!)[0]).IsEqualTo("[Dated ][ by Ann]");
        await Assert.That(Body(reported!)).DoesNotContain("fldChar");
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

    // A PNG 40 pixels by 20, made for 144 DPI: 20pt by 10pt on the page, near enough.
    static byte[] Picture()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(40, 20);
        image.Metadata.HorizontalResolution = 144;
        image.Metadata.VerticalResolution = 144;
        image.Metadata.ResolutionUnits = SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerInch;
        using var stream = new MemoryStream();
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, stream);
        return stream.ToArray();
    }

    [Test]
    public async Task APicture_GoesWhereTheCaretWas_AtTheSizeItWasMadeFor()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        Caret(0, 6);
        await ChooseTabAsync(cut, "Insert");

        await cut.Find("label.editor-tool").ClickAsync(new());
        cut.FindComponents<Microsoft.AspNetCore.Components.Forms.InputFile>()[^1].UploadFiles(InputFileContent.CreateFromBinary(Picture(), "dot.png", null, "image/png"));
        cut.WaitForState(() => reported != null, TimeSpan.FromSeconds(30));

        // A PNG says its resolution in pixels a metre: 144 DPI is written 5669, which is 143.99 DPI, and
        // the picture a hair over 20pt by 10pt — as Word, which does not round it either, draws it.
        var body = Body(reported!);
        await Assert.That(body).Contains("<wp:extent cx=\"254013\" cy=\"127007\" />");
        await Assert.That(body).Contains("descr=\"dot\"");
        await Assert.That(DocumentOutline.Read(reported!).Paragraphs[0].Units.Select(_ => _.Kind)).IsEquivalentTo([EditUnitKind.Text, EditUnitKind.Drawing, EditUnitKind.Text]);
        await Assert.That(Session().GetProperty("from").GetInt32()).IsEqualTo(7);
    }

    [Test]
    public async Task AFileThatIsNotAPicture_IsRefused_AndSaysSo()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        Caret(0, 6);
        await ChooseTabAsync(cut, "Insert");

        await cut.Find("label.editor-tool").ClickAsync(new());
        cut.FindComponents<Microsoft.AspNetCore.Components.Forms.InputFile>()[^1].UploadFiles(InputFileContent.CreateFromText("not a picture", "notes.png", null, "image/png"));
        cut.WaitForState(() => cut.Find(".editor-status-note").TextContent.Length > 0, TimeSpan.FromSeconds(30));

        await Assert.That(reported).IsNull();
        await Assert.That(cut.Find(".editor-status-note").TextContent).Contains("is not a picture");
    }

    // A caret in a link's text offers to take the link away, whole.
    [Test]
    public async Task RemoveLink_TakesTheLinkTheCaretIsIn()
    {
        var linked = Build(P("", "<w:hyperlink w:anchor=\"top\"><w:r><w:t>Linked</w:t></w:r></w:hyperlink>", R(" plain")));
        byte[]? reported = null;
        var cut = await OpenEditing(linked, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        var (block, offset) = Place(linked, 0, "Linked");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 5, block.Top + 2, offset + 2, offset + 2));
        Caret(0, 2);
        await ChooseTabAsync(cut, "Insert");

        await cut.FindAll(".editor-tool").First(_ => _.TextContent.Contains("Remove link")).ClickAsync(new());

        await Assert.That(Body(reported!)).DoesNotContain("hyperlink");
        await Assert.That(Read(reported!)[0]).IsEqualTo("[Linked][ plain]");
    }

    static readonly byte[] Grid = Build(
        "<w:tbl><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>" +
        "<w:tr><w:tc><w:p><w:r><w:t>a</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>b</w:t></w:r></w:p></w:tc></w:tr>" +
        "<w:tr><w:tc><w:p><w:r><w:t>c</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>d</w:t></w:r></w:p></w:tc></w:tr>" +
        "</w:tbl>" + P("", R("After")));

    // The Table tab is there while the caret is in a table, and its commands act on that table.
    [Test]
    [Arguments("Delete row", "[c] / [d] / [After]")]
    [Arguments("Delete column", "[b] / [d] / [After]")]
    [Arguments("Delete table", "[After]")]
    public async Task TheTableTab_DeletesWhereTheCaretIs(string command, string expected)
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Grid, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        var (block, offset) = Place(Grid, 0, "a");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 2, block.Top + 2, offset, offset));
        Caret(0, 0);
        await ChooseTabAsync(cut, "Table");

        await cut.FindAll(".editor-tool").First(_ => _.TextContent.Trim() == command).ClickAsync(new());

        await Assert.That(string.Join(" / ", Read(reported!))).IsEqualTo(expected);
    }

    [Test]
    public async Task TheTableTab_GoesWhenTheCaretLeavesTheTable()
    {
        var cut = await OpenEditing(Grid);
        var (cell, inCell) = Place(Grid, 0, "a");
        var (after, outside) = Place(Grid, 4, "After");

        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, cell.Left + 2, cell.Top + 2, inCell, inCell));
        await ChooseTabAsync(cut, "Table");
        await Assert.That(cut.Find(".editor-tab[aria-selected=true]").TextContent.Trim()).IsEqualTo("Table");

        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, after.Left + 2, after.Top + 2, outside, outside));

        await Assert.That(string.Join(' ', cut.FindAll(".editor-tab").Select(_ => _.TextContent.Trim()))).IsEqualTo("Home Insert Review View");
        await Assert.That(cut.Find(".editor-tab[aria-selected=true]").TextContent.Trim()).IsEqualTo("Home");
    }

    // Space before and after are toggles, as Word's Add/Remove Space Before Paragraph is.
    [Test]
    public async Task SpaceBeforeAndAfter_GoOnAndComeOff()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        var (block, offset) = Place(Paragraphs, 0, "First");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 2, block.Top + 2, offset, offset));
        Caret(0, 0);

        await cut.Find("[aria-label='Line and paragraph spacing']").ClickAsync(new());
        await cut.FindAll(".editor-menu-item").First(_ => _.TextContent.Trim() == "Add space before paragraph").ClickAsync(new());

        await Assert.That(Body(reported!)).StartsWith("<w:p><w:pPr><w:spacing w:before=\"240\" /></w:pPr>");

        await cut.Find("[aria-label='Line and paragraph spacing']").ClickAsync(new());
        await cut.FindAll(".editor-menu-item").First(_ => _.TextContent.Trim() == "Remove space before paragraph").ClickAsync(new());

        await Assert.That(Body(reported!)).StartsWith("<w:p><w:pPr><w:spacing w:before=\"0\" /></w:pPr>");
    }

    [Test]
    public async Task Replace_ReplacesTheCurrentMatch_AndGoesOnToTheNext()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        controller.Setup<int[]>("editTarget").SetResult([]);

        await cut.InvokeAsync(() => cut.Instance.OnReplaceRequested());
        await cut.Find(".viewer-find-input").InputAsync(new() {Value = "e"});

        // Found as it is typed, once the reader pauses: Enter now would go on to the second match.
        cut.WaitForState(() => cut.Find(".viewer-find-status").TextContent == "1 of 4", TimeSpan.FromSeconds(10));
        await cut.Find(".viewer-replace input").InputAsync(new() {Value = "E"});
        var replace = () => cut.FindAll(".viewer-text-button").First(_ => _.TextContent.Trim() == "Replace").ClickAsync(new());

        await replace();
        await Assert.That(Read(reported!)[0]).IsEqualTo("[First paragraph hEre.]");

        // The "E" put in still matches "e", the case not matched; the next match is the one after it.
        await Assert.That(cut.Find(".viewer-find-status").TextContent).IsEqualTo("2 of 4");

        await replace();
        await Assert.That(Read(reported!)[0]).IsEqualTo("[First paragraph hErE.]");
    }

    [Test]
    public async Task Export_DownloadsTheDocumentAsTheFormatChosen()
    {
        var morph = JSInterop.SetupModule($"./{MorphAssets.Script}");
        morph.Mode = JSRuntimeMode.Loose;
        var cut = await OpenEditing(Paragraphs);

        await cut.Find(".editor-file").ClickAsync(new());
        await cut.FindAll(".editor-menu-item").First(_ => _.TextContent.Contains("Markdown")).ClickAsync(new());

        var download = morph.Invocations["download"].Single();
        await Assert.That(download.Arguments[0]).IsEqualTo("edit.md");
        await Assert.That(Encoding.UTF8.GetString((byte[]) download.Arguments[2]!)).Contains("First paragraph here.");
        await Assert.That(cut.FindAll(".editor-menu")).IsEmpty();
    }

    // An edit not yet saved is shown, and Save a copy is what clears it.
    [Test]
    public async Task AnEdit_IsShownAsUnsaved_UntilSaved()
    {
        var morph = JSInterop.SetupModule($"./{MorphAssets.Script}");
        morph.Mode = JSRuntimeMode.Loose;
        var cut = await OpenEditing(Paragraphs);
        await Assert.That(cut.FindAll(".editor-dirty")).IsEmpty();

        Caret(1, 0);
        await cut.Find("[aria-label=Numbering]").ClickAsync(new());
        await Assert.That(cut.Find(".editor-dirty").TextContent).Contains("edited");

        await cut.Find("button[aria-label='Save a copy']").ClickAsync(new());

        await Assert.That(cut.FindAll(".editor-dirty")).IsEmpty();
        await Assert.That(DocumentEditor.ListOf((byte[]) morph.Invocations["download"].Single().Arguments[2]!, 1)).IsEqualTo(ListKind.Number);
    }

    // Word's keys in a list (Word-probed 2026-10-19): the script hands each to .NET by name, and it
    // acts on the paragraph the caret is in.
    [Test]
    public async Task ListKeys_MoveALevel_EndTheList_AndTakeTheNumberOff()
    {
        var listed = DocumentEditor.SetList(Paragraphs, [0, 1, 2], ListKind.Bullet, new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false));
        byte[]? reported = null;
        var cut = await OpenEditing(listed, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        // In the list's first paragraph, Tab moves the whole list in, not the paragraph a level.
        Caret(0, 2);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("indent"));
        await Assert.That(DocumentOutline.Read(reported!).Paragraphs[0].ListLevel).IsEqualTo(0);
        await Assert.That(Numbering(reported!)).Contains("<w:ind w:left=\"1080\" w:hanging=\"360\" />");

        Caret(1, 3);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("indent"));
        await Assert.That(DocumentOutline.Read(reported!).Paragraphs[1].ListLevel).IsEqualTo(1);
        await Assert.That(Session().GetProperty("list").GetInt32()).IsEqualTo(1);
        await Assert.That(Session().GetProperty("from").GetInt32()).IsEqualTo(3);

        Caret(1, 3);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("outdent"));
        await Assert.That(DocumentOutline.Read(reported!).Paragraphs[1].ListLevel).IsEqualTo(0);

        Caret(2, 0);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("end-list"));
        await Assert.That(DocumentOutline.Read(reported!).Paragraphs[2].ListLevel).IsNull();
        await Assert.That(Session().TryGetProperty("list", out _)).IsFalse();

        // Backspace at the start: the number goes, then the indent List Paragraph gave it.
        Caret(0, 0);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("unindent"));
        await Assert.That(DocumentOutline.Read(reported!).Paragraphs[0].ListLevel).IsNull();
        await Assert.That(Body(reported!)).StartsWith("<w:p><w:pPr><w:pStyle w:val=\"ListParagraph\" /><w:ind w:left=\"1080\" /></w:pPr>");
        await Assert.That(Session().GetProperty("indented").GetBoolean()).IsTrue();

        Caret(0, 0);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("unindent"));
        await Assert.That(Body(reported!)).StartsWith("<w:p><w:r>");
        await Assert.That(Session().TryGetProperty("indented", out _)).IsFalse();
    }

    // HTML pasted at the caret: the first paragraph ends the one pasted into, the last begins what
    // followed the caret, and those between keep their heading and list. Typing goes on after it.
    [Test]
    public async Task HtmlPasted_KeepsItsHeadingsListsAndEmphasis()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        Caret(2, 5);

        await cut.InvokeAsync(() => cut.Instance.OnEditPaste("<p> <b>bold</b> words</p><h2>Head</h2><ul><li>item</li></ul><p>tail</p>", ""));

        var read = Read(reported!);
        await Assert.That(read[2]).IsEqualTo("[Third][b:bold][ words]");
        await Assert.That(read[3]).IsEqualTo("[Head] #Heading2");
        await Assert.That(read[4]).IsEqualTo("[item] #ListParagraph •1.0");
        await Assert.That(read[5]).IsEqualTo("[tail and last.]");
        await Assert.That(Text(Session())).IsEqualTo("tail and last.");
        await Assert.That(Session().GetProperty("from").GetInt32()).IsEqualTo(4);
    }

    // HTML with nothing in it to read — a picture alone — pastes the clipboard's plain text.
    [Test]
    public async Task HtmlWithNothingToRead_PastesThePlainText()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        Caret(0, 0);

        await cut.InvokeAsync(() => cut.Instance.OnEditPaste("<img src=\"x.png\">", "one\ntwo "));

        var read = Read(reported!);
        await Assert.That(read[0]).IsEqualTo("[one]");
        await Assert.That(read[1]).IsEqualTo("[two First paragraph here.]");
    }

    // With nothing to take off, Backspace at a paragraph's start joins it to the one before.
    [Test]
    public async Task Unindent_OfAParagraphNotIndented_JoinsIt()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        Caret(2, 0);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("unindent"));

        await Assert.That(Read(reported!)).Count().IsEqualTo(2);
        await Assert.That(Text(Session())).IsEqualTo("Second bold one.Third and last.");
        await Assert.That(Session().GetProperty("from").GetInt32()).IsEqualTo(16);
    }

    static string Numbering(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
        return package.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!.InnerXml.Replace(" xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"", "");
    }

    // What the script is told of a paragraph, for its keys: its list level, and whether Enter at its
    // end starts another style.
    [Test]
    public async Task TheSession_SaysWhetherTheParagraphIsInAList_OrIsFollowedByAnotherStyle()
    {
        var docx = DocumentEditor.SetStyle(DocumentEditor.SetList(Paragraphs, [2], ListKind.Number, new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false)), [0], "Heading1", new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false));
        var cut = await OpenEditing(docx);

        foreach (var (paragraph, text) in new[] {(0, "First"), (2, "Third")})
        {
            var (block, offset) = Place(docx, paragraph, text);
            await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 2, block.Top + 2, offset, offset));
            await Assert.That(Session().TryGetProperty("follow", out _)).IsEqualTo(paragraph == 0);
            await Assert.That(Session().TryGetProperty("list", out _)).IsEqualTo(paragraph == 2);
        }
    }

    // Ctrl+Alt+1 to 3, and Ctrl+K.
    [Test]
    public async Task HeadingKeys_SetTheStyle_AndCtrlK_AsksForALink()
    {
        byte[]? reported = null;
        var cut = await OpenEditing(Paragraphs, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        Caret(2, 3);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("heading2"));
        await Assert.That(Read(reported!)[2]).IsEqualTo("[Third and last.] #Heading2");

        Selected(0, 6, 15);
        await cut.InvokeAsync(() => cut.Instance.OnEditKey("link"));
        await Assert.That(cut.Find(".editor-dialog").TextContent).Contains("Insert link");
    }

    // An edit not saved is kept in the browser, and saving it lets the draft go.
    [Test]
    public async Task AnEdit_IsKeptAsADraft_UntilSaved()
    {
        var morph = JSInterop.SetupModule($"./{MorphAssets.Script}");
        morph.Mode = JSRuntimeMode.Loose;
        var cut = await OpenEditing(Paragraphs);
        await Assert.That((bool) controller.Invocations["setUnsaved"].Single().Arguments[0]!).IsFalse();

        Caret(1, 0);
        await cut.Find("[aria-label=Numbering]").ClickAsync(new());

        var kept = controller.Invocations["keepDraft"].Single();
        await Assert.That(kept.Arguments[0]).IsEqualTo("edit.docx");
        await Assert.That(DocumentEditor.ListOf((byte[]) kept.Arguments[1]!, 1)).IsEqualTo(ListKind.Number);
        await Assert.That((bool) controller.Invocations["setUnsaved"][^1].Arguments[0]!).IsTrue();

        await cut.Find("button[aria-label='Save a copy']").ClickAsync(new());

        await Assert.That(controller.Invocations["dropDraft"].Count).IsEqualTo(1);
        await Assert.That((bool) controller.Invocations["setUnsaved"][^1].Arguments[0]!).IsFalse();
    }

    // The start page offers the draft back. Recovering it is SnapshotTests.Edit_UnsavedWork_IsKept's:
    // it fetches the bundled fonts, which only a browser serves.
    [Test]
    public async Task TheStartPage_OffersTheDraftBack()
    {
        controller.Setup<DraftInfo?>("describeDraft").SetResult(new("Notes.docx", 1760700000000));
        controller.Setup<int>("zoneOffset").SetResult(600);
        var cut = Render<MorphEditor>();

        cut.WaitForElement(".editor-recover");

        await Assert.That(cut.Find(".editor-recover-text").TextContent).IsEqualTo("Notes.docx was edited on 17 Oct at 21:20 and not saved.");
    }

    [Test]
    public async Task ADraftDiscarded_FromTheStartPage_IsDropped()
    {
        controller.Setup<DraftInfo?>("describeDraft").SetResult(new("Notes.docx", 1760700000000));
        var cut = Render<MorphEditor>();
        cut.WaitForElement(".editor-recover");

        await cut.FindAll(".editor-recover .editor-button").First(_ => _.TextContent == "Discard").ClickAsync(new());

        await Assert.That(cut.FindAll(".editor-recover")).IsEmpty();
        await Assert.That(controller.Invocations["dropDraft"].Count).IsEqualTo(1);
    }

    // A new document over an edit not saved asks first.
    [Test]
    public async Task ANewDocument_OverAnUnsavedEdit_AsksFirst()
    {
        var cut = await OpenEditing(Paragraphs);
        Caret(1, 0);
        await cut.Find("[aria-label=Numbering]").ClickAsync(new());

        await cut.Find(".editor-file").ClickAsync(new());
        await cut.FindAll(".editor-menu-item").First(_ => _.TextContent.Contains("New blank document")).ClickAsync(new());

        await Assert.That(cut.Find(".editor-dialog-title").TextContent).IsEqualTo("Start a new document?");
        await cut.FindAll(".editor-dialog .editor-button").First(_ => _.TextContent == "Cancel").ClickAsync(new());

        await Assert.That(cut.FindAll(".editor-dialog")).IsEmpty();
        await Assert.That(cut.Find(".editor-name").TextContent).Contains("edit.docx");
    }

    // Snapshots of what the editor draws: the start page, each tab of the ribbon, each menu and
    // dialog, and the status bar, over the three-paragraph fixture.

    [Test]
    public Task TheStartPage_Snapshot()
    {
        var cut = Render<MorphEditor>(_ => _.Add(component => component.ShowSamples, true));

        return Verify(cut);
    }

    [Test]
    [Arguments("Home")]
    [Arguments("Insert")]
    [Arguments("Table")]
    [Arguments("Review")]
    [Arguments("View")]
    public async Task TheRibbon_Snapshot(string tab)
    {
        var cut = await OpenEditing(Grid);
        var (cell, inCell) = Place(Grid, 0, "a");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, cell.Left + 2, cell.Top + 2, inCell, inCell));
        await ChooseTabAsync(cut, tab);

        await Verify(cut.Find(".editor-ribbon").Pretty(), "html");
    }

    [Test]
    [Arguments("styles")]
    [Arguments("spacing")]
    [Arguments("table")]
    [Arguments("file")]
    [Arguments("font")]
    [Arguments("size")]
    [Arguments("color")]
    [Arguments("highlight")]
    public async Task AMenu_Snapshot(string name)
    {
        var cut = await OpenEditing(Paragraphs);
        var (block, offset) = Place(Paragraphs, 1, "Second");
        await cut.InvokeAsync(() => cut.Instance.OnEditHit(0, block.Left + 2, block.Top + 2, offset, offset));

        switch (name)
        {
            case "styles":
                await cut.Find(".editor-style-button").ClickAsync(new());
                break;
            case "spacing":
                await cut.Find("[aria-label='Line and paragraph spacing']").ClickAsync(new());
                break;
            case "table":
                await ChooseTabAsync(cut, "Insert");
                await cut.FindAll(".editor-tool").First(_ => _.TextContent.Contains("Table")).ClickAsync(new());
                await cut.Find(".editor-grid-cell[aria-label='3 by 4']").MouseOverAsync(new());
                break;
            case "font":
                await cut.Find(".editor-font-button").ClickAsync(new());
                break;
            case "size":
                await cut.Find(".editor-size-button").ClickAsync(new());
                break;
            case "color":
                await cut.Find("[aria-label='More font colours']").ClickAsync(new());
                break;
            case "highlight":
                await cut.Find("[aria-label='More highlight colours']").ClickAsync(new());
                break;
            default:
                await cut.Find(".editor-file").ClickAsync(new());
                break;
        }

        await Verify(cut.Find(".editor-menu").Pretty(), "html");
    }

    [Test]
    [Arguments("link")]
    [Arguments("count")]
    [Arguments("discard")]
    public async Task ADialog_Snapshot(string name)
    {
        var cut = await OpenEditing(Paragraphs);
        switch (name)
        {
            case "link":
                Selected(0, 6, 15);
                await ChooseTabAsync(cut, "Insert");
                await cut.FindAll(".editor-tool").First(_ => _.TextContent.Contains("Link")).ClickAsync(new());
                break;
            case "count":
                await cut.Find(".editor-status-button").ClickAsync(new());
                break;
            default:
                Caret(1, 0);
                await cut.Find("[aria-label=Numbering]").ClickAsync(new());
                await cut.Find(".editor-file").ClickAsync(new());
                await cut.FindAll(".editor-menu-item").First(_ => _.TextContent.Contains("New blank document")).ClickAsync(new());
                break;
        }

        await Verify(cut.Find(".editor-dialog").Pretty(), "html");
    }

    [Test]
    public async Task TheStatusBar_Snapshot()
    {
        var cut = await OpenEditing(Paragraphs);
        await cut.InvokeAsync(() => cut.Instance.OnEditCommand("track"));

        await Verify(cut.Find(".editor-status").Pretty(), "html");
    }

    static string Body(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
        return package.MainDocumentPart!.Document!.Body!.InnerXml.Replace(" xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"", "");
    }
}

static class MarkupExtensions
{
    // An element's markup, indented a tag to a line, so a snapshot's diff shows what moved.
    public static string Pretty(this AngleSharp.Dom.IElement element)
    {
        using var writer = new StringWriter();
        element.ToHtml(writer, new AngleSharp.Html.PrettyMarkupFormatter());
        return writer.ToString();
    }
}
