using static EditFixtures;

// The open paragraph's own handling of what the browser would otherwise do its way: extend mode's end,
// characters beyond the Basic Multilingual Plane, a drag within the box, an anchor that is not seen, a
// line break among punctuation, Enter at a heading's end, the clipboard, a triple-click after Enter, a
// look waiting for an input method, a change the browser makes itself, and a Mac's keys.
public partial class SnapshotTests
{
    const string extendShown = ".editor-status-extend[data-value='true']";

    // Delete in extend mode deletes what it selected and ends the mode, so what is typed after is text;
    // a click ends it too. The status bar shows the mode while it is on.
    [Test]
    public async Task ExtendMode_EndsOnDeleteAndOnAClick()
    {
        var page = await OpenEditAsync(Build(P("", R("One two. The quick fox jumps. Last one."))));

        await ClickInAsync(page, "quick");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "One two. The ".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        await page.Keyboard.PressAsync("F8");
        await page.WaitForSelectorAsync(extendShown);
        await page.Keyboard.PressAsync("F8");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("quick ");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForSelectorAsync(extendShown, new() {State = WaitForSelectorState.Hidden});
        await page.Keyboard.TypeAsync("x");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("One two. The xfox jumps. Last one.");

        await page.Keyboard.PressAsync("F8");
        await page.WaitForSelectorAsync(extendShown);
        await page.Locator($"{editor} .edit-p").ClickAsync(new() {Position = new() {X = 5, Y = 5}});
        await page.WaitForSelectorAsync(extendShown, new() {State = WaitForSelectorState.Hidden});
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync("y");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("One two. The xfox jumps. Last one.y");
    }

    // Undo and Redo in an open paragraph follow what was typed in it: greyed out with nothing typed and
    // nothing in the file's history, on with the first key, and swapped by an Undo of the last one.
    [Test]
    public async Task UndoAndRedo_FollowWhatIsTypedInTheOpenParagraph()
    {
        var page = await OpenEditAsync(Build(P("", R("One two."))));
        const string undo = "[data-edit-command=undo]";
        const string redo = "[data-edit-command=redo]";

        await ClickInAsync(page, "two");
        await Assert.That(await page.IsDisabledAsync(undo)).IsTrue();
        await Assert.That(await page.IsDisabledAsync(redo)).IsTrue();

        await page.Keyboard.TypeAsync("x");
        await page.WaitForSelectorAsync($"{undo}:not([disabled])");
        await Assert.That(await page.IsDisabledAsync(redo)).IsTrue();

        await page.Keyboard.PressAsync("Control+z");
        await page.WaitForSelectorAsync($"{redo}:not([disabled])");
        await Assert.That(await page.IsDisabledAsync(undo)).IsTrue();
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("One two.");
    }

    // A character beyond the Basic Multilingual Plane is two UTF-16 code units and one character:
    // Overtype types over it whole, and F4 repeating a deletion deletes it whole.
    [Test]
    public async Task Overtype_AndRepeatedDelete_TakeASurrogatePairWhole()
    {
        var page = await OpenEditAsync(Build(P("", R("\U0001D400\U0001D401 end"))));

        await ClickInAsync(page, "end");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Insert");
        await page.Keyboard.TypeAsync("y");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("y\U0001D401 end");
        await page.Keyboard.PressAsync("Insert");

        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Control+Backspace");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("y\U0001D401 ");
        await page.Keyboard.PressAsync("F4");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("y\U0001D401");
        await page.Keyboard.PressAsync("F4");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("y");
        await HandInAsync(page);

        await Assert.That(Read(await SaveAsync(page))[0]).IsEqualTo("[y]");
    }

    // Text dragged to another place in the box is moved there.
    [Test]
    public async Task Drag_WithinTheBox_MovesTheText()
    {
        var page = await OpenEditAsync(Build(P("", R("One two three four."))));

        await ClickInAsync(page, "three");
        await SelectBoxTextAsync(page, "two ");
        await DragAsync(page, editor, 0, "One two three four.".Length, [5, 14]);
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("One three two four.");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("two ");
        await HandInAsync(page);

        await Assert.That(Read(await SaveAsync(page))[0]).IsEqualTo("[One three two four.]");
    }

    // A field dragged along with text moves with it: it is still a field in the file, not its result.
    [Test]
    public async Task Drag_WithinTheBox_MovesAFieldWithTheText()
    {
        var page = await OpenEditAsync(Build(P("", R("Page "), pageField, R(" here and there."))));

        await ClickInAsync(page, "here");
        var text = await BoxTextAsync(page);
        await SelectBoxTextAsync(page, "1 here ");
        await DragAsync(page, editor, 0, text.Length, [text.IndexOf("here", StringComparison.Ordinal) + 1, text.IndexOf("there", StringComparison.Ordinal)]);
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Page and 1 here there.");
        await HandInAsync(page);

        var saved = await SaveAsync(page);
        await Assert.That(DocumentBody(saved)).Contains("<w:fldSimple w:instr=\" PAGE \">");
        await Assert.That(string.Concat(Read(saved)[0].Split('[', ']'))).IsEqualTo("Page and 1 here there.");
    }

    // A floating picture's anchor takes a place in the text and is not seen: Backspace beside it deletes
    // the character beyond it, and Overtype types over what follows it, the picture kept either way.
    [Test]
    public async Task AnAnchor_IsPassedOver_ByBackspaceAndOvertype()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var pictured = DocumentEditor.InsertPicture(Paragraphs, 0, 6, new(pixelPng, "image/png", 20, 10), options).Document;
        var floating = DocumentEditor.SetPictureWrap(pictured, 0, 6, PictureWrap.Square, options);
        var page = await OpenEditAsync(floating);

        await ClickInAsync(page, "paragraph here");
        await CaretBesideAnchorAsync(page, after: true);
        await page.Keyboard.PressAsync("Backspace");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Firstparagraph here.");
        await page.Keyboard.PressAsync("Backspace");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Firsparagraph here.");

        await CaretBesideAnchorAsync(page, after: false);
        await page.Keyboard.PressAsync("Insert");
        await page.Keyboard.TypeAsync("P");
        await page.Keyboard.PressAsync("Insert");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("FirsParagraph here.");
        await HandInAsync(page);

        var body = DocumentBody(await SaveAsync(page));
        await Assert.That(body).Contains("wp:anchor");
        await Assert.That(Read(await SaveAsync(page))[0]).Contains("Paragraph here.");
    }

    // Ctrl+Backspace after a line break deletes the break alone, not the punctuation before it.
    [Test]
    public async Task CtrlBackspace_AfterALineBreak_DeletesTheBreakAlone()
    {
        var page = await OpenEditAsync(Build(P("", R("One,"), "<w:r><w:br/></w:r>", R("two"))));

        await ClickInAsync(page, "two");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Control+Backspace");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("One,two");
    }

    // Enter at a heading's end finishes its last word as any Enter does: AutoCorrect runs over it.
    [Test]
    public async Task Enter_AtAHeadingsEnd_RunsAutoCorrect()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var headed = DocumentEditor.SetStyle(Build(P("", R("About teh")) + P("", R("Body."))), [0], "Heading1", options);
        var page = await OpenEditAsync(headed);

        await ClickInAsync(page, "About");
        await page.Keyboard.PressAsync("End");
        await ReopenedAsync(page, () => page.Keyboard.PressAsync("Enter"));
        await page.Keyboard.TypeAsync("Next");
        await HandInAsync(page);

        var read = Read(await SaveAsync(page));
        await Assert.That(read[0]).IsEqualTo("[About the] #Heading1");
        await Assert.That(read[1]).IsEqualTo("[Next] #Normal");
    }

    // Copy writes the paragraph as it reads: a field as its result, emphasis as tags, and none of the
    // box's own styling.
    [Test]
    public async Task Copy_FromTheBox_WritesTheModel()
    {
        var page = await OpenEditAsync(Build(P("", R("Page "), pageField, R(" is "), R("bold", "<w:b/>"))));
        await page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        await ClickInAsync(page, "bold");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Shift+End");
        await page.Keyboard.PressAsync("Control+c");
        var copied = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const [item] = await navigator.clipboard.read();
                return [
                    await (await item.getType('text/plain')).text(),
                    await (await item.getType('text/html')).text()
                ];
            }
            """);

        await Assert.That(copied[0]).IsEqualTo("Page 1 is bold");
        await Assert.That(copied[1]).Contains("<b>bold</b>");
        await Assert.That(copied[1]).DoesNotContain("cqw");
    }

    // A triple-click on the first of two blocks Enter made selects that block, not the next.
    [Test]
    public async Task TripleClick_AfterEnter_SelectsTheBlockClicked()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("New block");
        await page.Locator($"{editor} .edit-p").First.ClickAsync(new() {ClickCount = 3, Position = new() {X = 10, Y = 5}});

        await Assert.That(await SelectedAsync(page)).IsEqualTo("Third and last.");
    }

    // A look set between spaces (Ctrl+B) is taken by what an input method composes there, as by what
    // is typed.
    [Test]
    public async Task PendingLook_IsTakenByComposedText()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync("  ");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.PressAsync("Control+b");
        var devTools = await page.Context.NewCDPSessionAsync(page);
        await devTools.SendAsync(
            "Input.imeSetComposition",
            new()
            {
                ["text"] = "x",
                ["selectionStart"] = 1,
                ["selectionEnd"] = 1
            });
        await devTools.SendAsync(
            "Input.insertText",
            new()
            {
                ["text"] = "x"
            });
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and last. x ");
        await HandInAsync(page);

        await Assert.That(Read(await SaveAsync(page))[2]).IsEqualTo("[Third and last. ][b:x][ ]");
    }

    // A change the browser makes to the box itself — a spelling correction — is undone by Ctrl+Z.
    [Test]
    public async Task BrowserChange_IsUndoable()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.EvaluateAsync(
            """
            () => {
                const box = document.querySelector('.edit-box[contenteditable=true]');
                const text = box.querySelector('.edit-run').firstChild;
                text.data = 'Third and least.';
                getSelection().collapse(text, text.data.length);
                box.dispatchEvent(new InputEvent('input', { inputType: 'insertReplacementText', bubbles: true }));
            }
            """);
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and least.");

        await page.Keyboard.PressAsync("Control+z");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and last.");
    }

    // On a Mac Option+Right steps by Word's word stops, and Command is what Bold is held with.
    [Test]
    public async Task MacKeys_OptionStepsByWords_CommandBolds()
    {
        var page = await browser!.NewPageAsync();
        await page.AddInitScriptAsync(
            """
            Object.defineProperty(Navigator.prototype, 'platform', { get: () => 'MacIntel' });
            Object.defineProperty(Navigator.prototype, 'userAgentData', { get: () => undefined });
            """);
        await page.GotoAsync($"http://localhost:{port}/edit");
        await LoadIntoAsync(page, Paragraphs, "edit.docx");
        await page.WaitForSelectorAsync(".viewer[data-edit=on]");

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Alt+ArrowRight");
        await page.Keyboard.TypeAsync("X");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third Xand last.");

        await page.Keyboard.PressAsync("Shift+ArrowLeft");
        await page.Keyboard.PressAsync("Meta+b");
        // Command+Enter hands the paragraph in, as Ctrl+Enter does elsewhere.
        await page.Keyboard.PressAsync("Meta+Enter");
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (Read(await SaveAsync(page))[2] == "[Third ][b:X][and last.]")
            {
                return;
            }

            await page.WaitForTimeoutAsync(250);
        }

        throw new("The paragraph was not handed in bold.");
    }

    const string pageField = "<w:fldSimple w:instr=\" PAGE \"><w:r><w:t>1</w:t></w:r></w:fldSimple>";

    // The open paragraph's text, its text nodes one after another — what the drags count offsets in.
    static Task<string> BoxTextAsync(IPage page) =>
        page.EvaluateAsync<string>(
            """
            () => {
                const box = document.querySelector('.edit-box[contenteditable=true]');
                const walker = document.createTreeWalker(box, NodeFilter.SHOW_TEXT);
                let text = '';
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    text += node.data;
                }

                return text;
            }
            """);

    // Selects the first occurrence of some text in the open paragraph, counted in its text nodes.
    static Task SelectBoxTextAsync(IPage page, string text) =>
        page.EvaluateAsync(
            """
            text => {
                const box = document.querySelector('.edit-box[contenteditable=true]');
                const walker = document.createTreeWalker(box, NodeFilter.SHOW_TEXT);
                const nodes = [];
                let all = '';
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    nodes.push([node, all.length]);
                    all += node.data;
                }

                // A place in what is not text — a field's result — is the place before it, in its paragraph.
                const find = offset => {
                    const found = nodes.find(([node, at]) => offset < at + node.data.length) ?? nodes[nodes.length - 1];
                    const atom = found[0].parentElement.closest('[contenteditable=false]');
                    if (atom) {
                        return [atom.parentNode, Array.prototype.indexOf.call(atom.parentNode.childNodes, atom)];
                    }

                    return [found[0], offset - found[1]];
                };
                const start = all.indexOf(text);
                const [startNode, startOffset] = find(start);
                const [endNode, endOffset] = find(start + text.length);
                getSelection().setBaseAndExtent(startNode, startOffset, endNode, endOffset);
            }
            """,
            text);

    // Puts the caret just before or just after the open paragraph's one unseen anchor.
    static Task CaretBesideAnchorAsync(IPage page, bool after) =>
        page.EvaluateAsync(
            """
            after => {
                const anchor = document.querySelector('.edit-box[contenteditable=true] .edit-atom.edit-hidden');
                const block = anchor.parentElement;
                const index = Array.prototype.indexOf.call(block.childNodes, anchor);
                getSelection().collapse(block, index + (after ? 1 : 0));
            }
            """,
            after);
}
