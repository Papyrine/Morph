using static EditFixtures;

// The editor's keys and commands that live in the script or in the component's glue, where no spec
// test reaches: links by Ctrl+K, the open paragraph's own history, keys on text selected across the
// pages, the File menu, the word count and presenting.
public partial class SnapshotTests
{
    const string linkInput = ".editor-dialog input[type=url]";

    // Ctrl+K on a word selected in the open paragraph offers Insert link; on the link, Remove link.
    [Test]
    public async Task Gap_CtrlK_InTheOpenParagraph_LinksAndUnlinks()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await SelectInEditorAsync(page, "First ".Length, "paragraph".Length);
        await page.Keyboard.PressAsync("Control+k");
        await page.WaitForSelectorAsync(linkInput);
        await Assert.That(await page.InnerTextAsync("#editor-link-title")).IsEqualTo("Insert link");
        await page.FillAsync(linkInput, "https://example.com/");
        await page.ClickAsync(".editor-dialog .editor-button-primary");
        await WaitForBodyAsync(page, _ => _.Contains("<w:hyperlink"));

        var linked = await SaveAsync(page);
        await Assert.That(Read(linked)[0]).IsEqualTo("[First ][paragraph][ here.]");
        await Assert.That(Relationships(linked)).Contains("https://example.com/");

        // A caret inside the link, with nothing selected: the dialog has no address to ask for, only
        // Remove link (and once failed, focusing the address it had not drawn).
        await ClickInAsync(page, "paragraph here");
        await SelectInEditorAsync(page, "First para".Length, 0);
        await page.Keyboard.PressAsync("Control+k");
        await page.WaitForSelectorAsync("#editor-link-title:text-is('Link')");
        await Assert.That(await page.Locator(linkInput).CountAsync()).IsEqualTo(0);
        await page.ClickAsync(".editor-dialog .editor-button:text-is('Remove link')");
        await WaitForBodyAsync(page, _ => !_.Contains("<w:hyperlink"));
    }

    // With no paragraph open, Ctrl+K links the text selected on the pages, which the dialog says.
    [Test]
    public async Task Gap_CtrlK_OnThePages_LinksTheSelection()
    {
        var page = await OpenEditAsync(Paragraphs);

        await SelectOnPagesAsync(page, "bold", "bold");
        await page.Keyboard.PressAsync("Control+k");
        await page.WaitForSelectorAsync(linkInput);
        await page.FillAsync(linkInput, "https://example.org/");
        await page.ClickAsync(".editor-dialog .editor-button-primary");
        await WaitForBodyAsync(page, _ => _.Contains("<w:hyperlink"));

        await Assert.That(Relationships(await SaveAsync(page))).Contains("https://example.org/");
    }

    // Undo and redo inside the open paragraph are what was typed there, and need no round trip.
    [Test]
    public async Task Gap_UndoRedo_InTheOpenParagraph()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" More");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and last. More");

        await page.Keyboard.PressAsync("Control+z");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and last.");
        await page.Keyboard.PressAsync("Control+y");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and last. More");
        await page.Keyboard.PressAsync("Control+z");
        await page.Keyboard.PressAsync("Control+Shift+z");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and last. More");
        await HandInAsync(page);

        await Assert.That(Read(await SaveAsync(page))[2]).IsEqualTo("[Third and last. More]");
    }

    // Ctrl+B on text selected across the pages bolds it; Ctrl+Z on the pages takes the edit back.
    [Test]
    public async Task Gap_FormatKeys_OnThePages_AndUndo()
    {
        var page = await OpenEditAsync(Paragraphs);

        await SelectOnPagesAsync(page, "here", "Second");
        await page.Keyboard.PressAsync("Control+b");
        await WaitForBodyAsync(page, _ => _.Split("<w:b />").Length > 2);
        await Assert.That(string.Join(" / ", Read(await SaveAsync(page))))
            .IsEqualTo("[First paragraph ][b:here.] / [b:Second][ ][b:bold][ one.] (center) / [Third and last.]");

        await page.FocusAsync(".viewer-pages");
        await page.Keyboard.PressAsync("Control+z");
        await WaitForBodyAsync(page, _ => _.Split("<w:b />").Length == 2);
        await Assert.That(Read(await SaveAsync(page))[0]).IsEqualTo("[First paragraph here.]");
    }

    // Delete with text selected across two paragraphs deletes it and joins what is left.
    [Test]
    public async Task Gap_Delete_OnThePages_JoinsTheParagraphs()
    {
        var page = await OpenEditAsync(Paragraphs);

        await SelectOnPagesAsync(page, "here", "Second ");
        await page.Keyboard.PressAsync("Delete");
        await WaitForBodyAsync(page, _ => !_.Contains("here."));

        var read = Read(await SaveAsync(page));
        await Assert.That(read.Count).IsEqualTo(2);
        await Assert.That(string.Concat(read[0].Split('[', ']')).Replace("b:", "")).StartsWith("First paragraph bold one.");
    }

    // Shift+Tab takes a list's paragraph a level out, as Tab took it in.
    [Test]
    public async Task Gap_ShiftTab_InAList_GoesALevelOut()
    {
        var listed = DocumentEditor.SetList(Paragraphs, [0, 1, 2], ListKind.Bullet, new("Ann", DateTimeOffset.UnixEpoch, Track: false));
        var page = await OpenEditAsync(listed);

        await ClickInAsync(page, "bold one");
        await ReopenedAsync(page, () => page.Keyboard.PressAsync("Tab"));
        await HandInAsync(page);
        await Assert.That(Read(await SaveAsync(page))[1]).EndsWith("•1.1");

        await ClickInAsync(page, "bold one");
        await ReopenedAsync(page, () => page.Keyboard.PressAsync("Shift+Tab"));
        await HandInAsync(page);
        await Assert.That(Read(await SaveAsync(page))[1]).EndsWith("•1.0");
    }

    // Shift+Enter is a line break and Tab outside a list a tab, both in the paragraph's own text.
    [Test]
    public async Task Gap_ShiftEnter_IsALineBreak_AndTab_IsATab()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Shift+Enter");
        await page.Keyboard.TypeAsync("Next");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.TypeAsync("tabbed");
        await HandInAsync(page);

        var read = Read(await SaveAsync(page));
        await Assert.That(read.Count).IsEqualTo(3);
        await Assert.That(string.Concat(read[2].Split('[', ']'))).IsEqualTo("Third and last.\\nNext\\ttabbed");
    }

    // File > Export as writes the document in each format, named after the file.
    [Test]
    [Arguments("PDF (.pdf)", ".pdf")]
    [Arguments("HTML (.html)", ".html")]
    [Arguments("Markdown (.md)", ".md")]
    [Arguments("Plain text (.txt)", ".txt")]
    [Arguments("PNG image (.png)", ".png")]
    public async Task Gap_FileMenu_ExportsEachFormat(string item, string extension)
    {
        var page = await OpenEditAsync(Paragraphs);

        await page.ClickAsync(".editor-file");
        var download = await page.RunAndWaitForDownloadAsync(
            () => page.ClickAsync($".editor-file-menu .editor-menu-item:has-text('{item}')"),
            new()
            {
                Timeout = 60000
            });

        await Assert.That(download.SuggestedFilename).EndsWith(extension);
        var bytes = await File.ReadAllBytesAsync(await download.PathAsync());
        await Assert.That(bytes.Length).IsGreaterThan(0);
        if (extension == ".pdf")
        {
            await Assert.That(Encoding.ASCII.GetString(bytes, 0, 5)).IsEqualTo("%PDF-");
        }
        else if (extension != ".png")
        {
            await Assert.That(Encoding.UTF8.GetString(bytes)).Contains("Second");
        }
    }

    // A new document over edits not saved asks first: Cancel keeps them, Discard edits starts afresh.
    [Test]
    public async Task Gap_FileMenu_NewOverEdits_Asks()
    {
        var page = await OpenEditAsync(Paragraphs);
        page.Dialog += (_, dialog) => dialog.AcceptAsync();

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync("!");
        await HandInAsync(page);
        await page.WaitForSelectorAsync(".editor-dirty");

        await page.ClickAsync(".editor-file");
        await page.ClickAsync(".editor-file-menu .editor-menu-item:has-text('New blank document')");
        await page.WaitForSelectorAsync("#editor-discard-title");
        await page.ClickAsync(".editor-dialog .editor-button:text-is('Cancel')");
        await page.WaitForSelectorAsync("#editor-discard-title", new() { State = WaitForSelectorState.Detached });
        await Assert.That(await LayerTextAsync(page)).Contains("Third and last.!");

        await page.ClickAsync(".editor-file");
        await page.ClickAsync(".editor-file-menu .editor-menu-item:has-text('New blank document')");
        await page.ClickAsync(".editor-dialog .editor-button:text-is('Discard edits')");
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('.editor-dirty') && !document.querySelector('.viewer-page .text-layer')?.textContent.includes('Third')",
            null,
            new()
            {
                Timeout = 60000
            });
        await Assert.That(Read(await SaveAsync(page)).All(_ => !_.Contains("Third"))).IsTrue();
    }

    // The start page offers an unsaved draft back; Discard lets it go, for good.
    [Test]
    public async Task Gap_StartPage_DiscardsTheDraft()
    {
        var page = await OpenEditAsync(Paragraphs);
        page.Dialog += (_, dialog) => dialog.AcceptAsync();

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync("!");
        await HandInAsync(page);
        await page.WaitForSelectorAsync(".editor-dirty");
        await page.WaitForFunctionAsync(
            $"async () => ({draftKept})",
            null,
            new()
            {
                Timeout = 30000
            });

        await page.ReloadAsync();
        await page.WaitForSelectorAsync(".editor-recover", new() { Timeout = 120000 });
        await page.ClickAsync(".editor-recover .editor-button:text-is('Discard')");
        await page.WaitForSelectorAsync(".editor-recover", new() { State = WaitForSelectorState.Detached });
        await page.WaitForFunctionAsync($"async () => !({draftKept})");

        await page.ReloadAsync();
        await page.WaitForSelectorAsync(".editor-start");
        await page.WaitForTimeoutAsync(2000);
        await Assert.That(await page.Locator(".editor-recover").CountAsync()).IsEqualTo(0);
    }

    // The word count, from the status bar and the Review tab: the body's words, characters and
    // paragraphs, and what is selected.
    [Test]
    public async Task Gap_WordCount_CountsTheBody_AndTheSelection()
    {
        var page = await OpenEditAsync(Paragraphs);

        await page.ClickAsync(".editor-status-button[title='Word count']");
        await page.WaitForSelectorAsync("#editor-count-title");
        await Assert.That(await CountAsync(page, "Words")).IsEqualTo("9");
        await Assert.That(await CountAsync(page, "Paragraphs")).IsEqualTo("3");
        await Assert.That(await CountAsync(page, "Characters (with spaces)")).IsEqualTo("52");
        await Assert.That(await CountAsync(page, "Characters (no spaces)")).IsEqualTo("46");
        await page.ClickAsync(".editor-dialog .editor-button:text-is('Close')");

        await SelectOnPagesAsync(page, "Second", "bold");
        await ChooseTabAsync(page, "Review");
        await page.ClickAsync(".editor-tool:has-text('Word count')");
        await Assert.That(await page.InnerTextAsync(".editor-dialog .editor-dialog-note >> nth=0")).IsEqualTo("Selected: 2 words, 10 characters");
    }

    // Present shows one page at a time: the keys step through the pages, and Escape (or leaving
    // fullscreen) ends it.
    [Test]
    public async Task Gap_Present_StepsThroughThePages()
    {
        const string pageBreak = "<w:r><w:br w:type=\"page\"/></w:r>";
        var pages = Build(
            P("", R("One.")) + P("", pageBreak, R("Two.")) + P("", pageBreak, R("Three.")));
        var page = await OpenEditAsync(pages);

        await ChooseTabAsync(page, "View");
        await page.ClickAsync("[data-viewer-action=presentation]");
        await page.WaitForSelectorAsync(".viewer[data-presenting]");
        await CurrentPageAsync(page, "1");

        await page.Keyboard.PressAsync("ArrowRight");
        await CurrentPageAsync(page, "2");
        await page.Keyboard.PressAsync("End");
        await CurrentPageAsync(page, "3");
        await page.Keyboard.PressAsync("ArrowLeft");
        await CurrentPageAsync(page, "2");
        await page.Keyboard.PressAsync("Home");
        await CurrentPageAsync(page, "1");

        if (await page.GetAttributeAsync(".viewer", "data-presenting") == "fullscreen")
        {
            await page.EvaluateAsync("() => document.exitFullscreen()");
        }
        else
        {
            await page.Keyboard.PressAsync("Escape");
        }

        await page.WaitForSelectorAsync(".viewer[data-presenting]", new() { State = WaitForSelectorState.Detached });
    }

    // Whether the editor's draft is in IndexedDB, as an expression for an async function.
    const string draftKept =
        """
        await (async () => {
            if (!(await indexedDB.databases()).some(_ => _.name === 'morph-editor')) {
                return false;
            }

            return await new Promise(resolve => {
                const opening = indexedDB.open('morph-editor');
                opening.onerror = () => resolve(false);
                opening.onsuccess = () => {
                    const database = opening.result;
                    if (!database.objectStoreNames.contains('drafts')) {
                        database.close();
                        resolve(false);
                        return;
                    }

                    const reading = database.transaction('drafts').objectStore('drafts').get('last');
                    reading.onsuccess = () => {
                        database.close();
                        resolve(!!reading.result);
                    };
                    reading.onerror = () => {
                        database.close();
                        resolve(false);
                    };
                };
            });
        })()
        """;

    static Task CurrentPageAsync(IPage page, string number) =>
        page.WaitForSelectorAsync($".viewer[data-current-page='{number}']");

    static Task<string> CountAsync(IPage page, string term) =>
        page.InnerTextAsync($".editor-count dt:text-is('{term}') + dd");

    // Selects within the open paragraph, by its text's offsets, as Shift and the arrows would.
    static async Task SelectInEditorAsync(IPage page, int start, int length)
    {
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < start; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        for (var index = 0; index < length; index++)
        {
            await page.Keyboard.PressAsync("Shift+ArrowRight");
        }
    }

    // Selects the pages' text from the start of one phrase to the end of another, as a drag would,
    // and again until the selection holds: after an edit the page is drawn again under it.
    static async Task SelectOnPagesAsync(IPage page, string first, string last)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var text = await LayerTextAsync(page);
            var from = text.IndexOf(first, StringComparison.Ordinal);
            var to = text.IndexOf(last, from, StringComparison.Ordinal) + last.Length;
            await page.FocusAsync(".viewer-pages");
            await SelectAsync(page, from, to);
            await page.WaitForTimeoutAsync(300);
            if ((await page.EvaluateAsync<string>("() => getSelection().toString()")).Contains(last))
            {
                return;
            }
        }

        throw new("The text could not be selected.");
    }

    // Saves until the body reads as expected: an edit made on the pages is made in .NET, after the key.
    static async Task WaitForBodyAsync(IPage page, Func<string, bool> expected)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (expected(DocumentBody(await SaveAsync(page))))
            {
                return;
            }

            await page.WaitForTimeoutAsync(250);
        }

        throw new("The edit was not made.");
    }

    static string Relationships(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
        return string.Join(" ", package.MainDocumentPart!.HyperlinkRelationships.Select(_ => _.Uri.ToString()));
    }
}
