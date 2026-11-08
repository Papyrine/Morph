using static EditFixtures;

// Back where the reader was: a dialog cancelled, or the find bar closed, leaves them in the document —
// the paragraph open again, or what was found selected — rather than with the focus nowhere. And the
// rest of the second review against another editor: Ctrl+F's last search selected, a double-click
// inside a selection, the keys pressed while a paragraph is handed in, Ctrl+click on a link, the tab
// stops of the paragraph being typed, and a copy pasted back keeping its look.
public partial class SnapshotTests
{
    [Test]
    [Arguments("Paragraph settings")]
    [Arguments("Font settings")]
    public async Task Return_ADialogCancelled_OpensTheParagraphAgain(string launcher)
    {
        var page = await OpenEditAsync(Paragraphs);

        // The caret in a word, which the Font dialog acts on.
        await ClickInAsync(page, "and last");
        var session = await page.GetAttributeAsync(editor, "data-edit-session");
        await page.ClickAsync($"button[aria-label='{launcher}']");
        await page.WaitForSelectorAsync(".editor-dialog");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync(
            $"() => !document.querySelector('.editor-dialog') && Number(document.querySelector('{editor}')?.dataset.editSession) > Number('{session}') && document.activeElement?.matches('{editor}')",
            null,
            new()
            {
                Timeout = 60000
            });
        await page.Keyboard.TypeAsync("!");
        await HandInAsync(page);

        var third = string.Concat(Read(await SaveAsync(page))[2].Split('[', ']'));
        await Assert.That(third.Replace("!", "")).IsEqualTo("Third and last.");
        await Assert.That(third).Contains("!");
    }

    // Escape on the find bar leaves the match it was on selected on the pages, with the keys theirs,
    // so what is typed replaces it; Ctrl+F after selects the last search, so what is typed replaces that.
    [Test]
    public async Task Return_TheFindBarClosed_LeavesTheMatchSelected()
    {
        var page = await OpenEditAsync(Paragraphs);

        await page.FocusAsync(".viewer-pages");
        await page.Keyboard.PressAsync("Control+f");
        await page.WaitForSelectorAsync(".viewer-find-input:focus");
        await page.Keyboard.TypeAsync("paragraph");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.viewer-find-status')?.textContent.includes('1 of 1')",
            null,
            new()
            {
                Timeout = 60000
            });
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync("() => getSelection().toString() === 'paragraph'");
        await page.Keyboard.TypeAsync("line");
        await WaitForEditorAsync(page, "First line");
        await HandInAsync(page);
        await Assert.That(string.Concat(Read(await SaveAsync(page))[0].Split('[', ']'))).IsEqualTo("First line here.");

        await page.Keyboard.PressAsync("Control+f");
        await page.WaitForFunctionAsync(
            """
            () => {
                const input = document.querySelector('.viewer-find-input');
                return document.activeElement === input && input.value === 'paragraph' &&
                    input.selectionStart === 0 && input.selectionEnd === input.value.length;
            }
            """);
    }

    // A double-click on a word of text already selected in the paragraph selects the word, as anywhere
    // else: the first press, in the selection, does not leave the selection collapsed after the clicks.
    [Test]
    public async Task Return_ADoubleClickInsideASelection_SelectsTheWord()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Shift+End");
        var point = await EditorPointAsync(page, "and");
        await page.Mouse.DblClickAsync((float) point[0], (float) point[1]);

        await page.WaitForFunctionAsync("() => getSelection().toString() === 'and '");
        await page.WaitForTimeoutAsync(200);
        await Assert.That(await page.EvaluateAsync<string>("() => getSelection().toString()")).IsEqualTo("and ");
    }

    // The keys pressed straight after a click on another paragraph, while the one edited is handed in,
    // are the paragraph clicked's: End goes to its end, and what is typed goes there.
    [Test]
    public async Task Return_ANavigationKeyWhileHandingIn_MovesInTheParagraphClicked()
    {
        var page = await OpenEditAsync(Paragraphs);
        var offset = (await LayerTextAsync(page)).IndexOf("paragraph", StringComparison.Ordinal);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" A");
        await ClickTextAsync(page, offset);
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync("Z");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.edit-box[contenteditable=true]')?.innerText.startsWith('First paragraph here.Z')",
            null,
            new()
            {
                Timeout = 60000
            });
        await HandInAsync(page);

        var paragraphs = Read(await SaveAsync(page));
        await Assert.That(paragraphs[0]).IsEqualTo("[First paragraph here.Z]");
        await Assert.That(paragraphs[2]).IsEqualTo("[Third and last. A]");
    }

    // Ctrl+click on a link to a bookmark — a table of contents' entry — goes to where the bookmark
    // starts, from the pages and from the paragraph being typed alike, rather than opening the link's
    // paragraph or selecting its sentence.
    [Test]
    public async Task Return_CtrlClickOnALink_GoesToItsBookmark()
    {
        const string link = "<w:hyperlink w:anchor=\"target\"><w:r><w:rPr><w:rStyle w:val=\"Hyperlink\"/></w:rPr><w:t>the end</w:t></w:r></w:hyperlink>";
        var page = await OpenEditAsync(
            Build(
                P("", R("Go to "), link) +
                P("", R("In between")) +
                P("", "<w:bookmarkStart w:id=\"0\" w:name=\"target\"/>", R("Target here"), "<w:bookmarkEnd w:id=\"0\"/>")));

        var offset = (await LayerTextAsync(page)).IndexOf("the end", StringComparison.Ordinal);
        var onPage = await LayerPointAsync(page, offset + 2);
        await page.Keyboard.DownAsync("Control");
        await page.Mouse.ClickAsync((float) onPage[0], (float) onPage[1]);
        await page.Keyboard.UpAsync("Control");
        await WaitForEditorAsync(page, "Target here");

        await ClickInAsync(page, "Go to", "Go to");
        var inBox = await EditorPointAsync(page, "end");
        await page.Keyboard.DownAsync("Control");
        await page.Mouse.ClickAsync((float) inBox[0], (float) inBox[1]);
        await page.Keyboard.UpAsync("Control");
        await WaitForEditorAsync(page, "Target here");
    }

    // The paragraph being typed sets its tabs at the paragraph's own stops, as the page does: a right
    // stop at 4 inches with a dot leader ends the text after the tab there, the dots across the gap.
    [Test]
    public async Task Return_TheParagraphBeingTyped_KeepsItsTabStops()
    {
        var page = await OpenEditAsync(
            Build(P("<w:tabs><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"5760\"/></w:tabs>", R("Left"), "<w:r><w:tab/></w:r>", R("Right"))));

        await ClickInAsync(page, "Left");
        var placed = await page.EvaluateAsync<double[]>(
            """
            () => {
                const box = document.querySelector('.edit-box');
                const tab = box.querySelector('.edit-tab');
                const layer = document.querySelector('.edit-layer').getBoundingClientRect();
                const width = document.querySelector('.viewer-page svg').viewBox.baseVal.width;
                const walker = document.createTreeWalker(box, NodeFilter.SHOW_TEXT);
                let left = 0;
                let right = 0;
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    const range = document.createRange();
                    range.selectNodeContents(node);
                    if (node.data.includes('Left')) {
                        left = range.getBoundingClientRect().left;
                    }

                    if (node.data.includes('Right')) {
                        right = range.getBoundingClientRect().right;
                    }
                }

                const points = x => (x - layer.left) / layer.width * width;
                return [points(right) - points(left), tab?.dataset.leader === 'dot' ? 1 : 0];
            }
            """);

        // The stop is 4 inches past the margin, where "Left" starts.
        await Assert.That(placed[0]).IsBetween(288 - 3, 288 + 3);
        await Assert.That(placed[1]).IsEqualTo(1);
    }

    // A word copied out of the paragraph being typed and pasted into another keeps its own size, as
    // Word's Keep Source Formatting keeps a copy made within the document, and stands a space apart
    // from the word it is pasted against.
    [Test]
    public async Task Return_ACopyPastedBack_KeepsItsLook()
    {
        await using var context = await browser!.NewContextAsync(
            new()
            {
                Permissions = ["clipboard-read", "clipboard-write"]
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/edit");
        await LoadIntoAsync(page, Build(P("", R("big", "<w:sz w:val=\"40\"/>"), R(" small")) + P("", R("Target"))), "edit.docx");
        await page.WaitForSelectorAsync(".viewer[data-edit=on]");

        await ClickInAsync(page, "small");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < 3; index++)
        {
            await page.Keyboard.PressAsync("Shift+ArrowRight");
        }

        await page.Keyboard.PressAsync("Control+c");
        await ClickInAsync(page, "Target", "Target");
        await page.Keyboard.PressAsync("End");
        await ReopenedAsync(page, () => page.Keyboard.PressAsync("Control+v"));
        await HandInAsync(page);

        var saved = await SaveAsync(page);
        await Assert.That(Read(saved)[1]).IsEqualTo("[Target ][big]");
        await Assert.That(DocumentBody(saved).Split("Target")[1]).Contains("<w:sz w:val=\"40\" />");
    }

    // Ctrl+Enter is Word's page break (WordProbe E-28): a paragraph of its own between the halves, and
    // what is typed straight after goes at the start of the paragraph after it.
    [Test]
    public async Task Return_CtrlEnter_BreaksThePage()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickBeforeAsync(page, "and last");
        await WaitForEditorAsync(page, "Third");
        await page.Keyboard.PressAsync("Control+Enter");
        await page.Keyboard.TypeAsync("Now ");
        await WaitForEditorAsync(page, "Now and last.");
        await HandInAsync(page);

        var saved = await SaveAsync(page);
        var paragraphs = Read(saved);
        await Assert.That(DocumentBody(saved)).Contains("w:type=\"page\"");
        await Assert.That(string.Concat(paragraphs[2].Split('[', ']'))).IsEqualTo("Third ");
        await Assert.That(string.Concat(paragraphs[^1].Split('[', ']'))).IsEqualTo("Now and last.");
    }

    // Ctrl+Shift+Enter is Word's column break (WordProbe E-29): a character in the line, and typing goes
    // on after it.
    [Test]
    public async Task Return_CtrlShiftEnter_BreaksTheColumn()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickBeforeAsync(page, "and last");
        await WaitForEditorAsync(page, "Third");
        await page.Keyboard.PressAsync("Control+Shift+Enter");
        await page.Keyboard.TypeAsync("X");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.edit-box[contenteditable=true]')?.innerText.includes('Xand last.')",
            null,
            new()
            {
                Timeout = 60000
            });
        await HandInAsync(page);

        var saved = await SaveAsync(page);
        await Assert.That(DocumentBody(saved)).Contains("w:type=\"column\"");
        await Assert.That(string.Concat(Read(saved)[2].Split('[', ']'))).Contains("Xand last.");
    }

    // Two tabs edit two documents: each keeps its own draft, and the first, reloaded, offers its own
    // back rather than the one the second wrote after it.
    [Test]
    public async Task Return_EachTab_KeepsItsOwnDraft()
    {
        await using var context = await browser!.NewContextAsync();
        var first = await EditInTabAsync(context, "first.docx");
        await EditInTabAsync(context, "second.docx");

        first.Dialog += (_, dialog) => dialog.AcceptAsync();
        await first.ReloadAsync();
        await first.WaitForSelectorAsync(
            ".editor-recover",
            new()
            {
                Timeout = 120000
            });
        await Assert.That(await first.InnerTextAsync(".editor-recover-text")).StartsWith("first.docx was edited on");
    }

    // A tab with a document edited, and its draft written.
    static async Task<IPage> EditInTabAsync(IBrowserContext context, string name)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/edit");
        await LoadIntoAsync(page, Paragraphs, name);
        await page.WaitForSelectorAsync(".viewer[data-edit=on]");
        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync("!");
        await HandInAsync(page);
        await page.WaitForSelectorAsync(".editor-dirty");
        // A draft is written a moment after the edit (morph-draft.js, writeDelay).
        await page.WaitForTimeoutAsync(1500);
        return page;
    }

    // The middle of the first occurrence of some text in the paragraph being typed, in the window's
    // coordinates.
    static Task<double[]> EditorPointAsync(IPage page, string text) =>
        page.EvaluateAsync<double[]>(
            """
            text => {
                const box = document.querySelector('.edit-box[contenteditable=true]');
                const walker = document.createTreeWalker(box, NodeFilter.SHOW_TEXT);
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    const at = node.data.indexOf(text);
                    if (at >= 0) {
                        const range = document.createRange();
                        range.setStart(node, at);
                        range.setEnd(node, at + text.length);
                        const rect = range.getBoundingClientRect();
                        return [rect.left + rect.width / 2, rect.top + rect.height / 2];
                    }
                }

                return [];
            }
            """,
            text);

    // The middle of the character at an offset of the first page's text, in the window's coordinates.
    static Task<double[]> LayerPointAsync(IPage page, int offset) =>
        page.EvaluateAsync<double[]>(
            """
            offset => {
                const layer = document.querySelector('.viewer-page[data-page-number="1"] .text-layer');
                const walker = document.createTreeWalker(layer, NodeFilter.SHOW_TEXT);
                let start = 0;
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    const next = start + node.data.length;
                    if (offset >= start && offset < next) {
                        const range = document.createRange();
                        range.setStart(node, offset - start);
                        range.setEnd(node, offset - start + 1);
                        const rect = range.getBoundingClientRect();
                        return [rect.left + rect.width / 2, rect.top + rect.height / 2];
                    }

                    start = next;
                }

                return [];
            }
            """,
            offset);
}
