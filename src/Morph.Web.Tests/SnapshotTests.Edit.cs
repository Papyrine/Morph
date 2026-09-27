using static EditFixtures;

// Editing a Word document's text end to end, on the real WASM runtime in a real browser: that is where
// the editor is laid over a paragraph, keys are turned into text, and what was typed is handed to .NET,
// written into the file and drawn again.
//
// The fixture is three paragraphs on one page, which the page's text layer holds as
//   "First paragraph here." "Second bold one." "Third and last."
public partial class SnapshotTests
{
    const string editor = ".edit-box[contenteditable=true]";

    [Test]
    public async Task Edit_TypingInAParagraph_ChangesThePage_AndTheFile()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("First paragraph here.");

        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" And more.");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("First paragraph here. And more.");

        await HandInAsync(page);

        await Assert.That(await LayerTextAsync(page)).Contains("First paragraph here. And more.");
        await Assert.That(string.Join(" / ", Read(await DownloadAsync(page))))
            .IsEqualTo("[First paragraph here. And more.] / [Second ][b:bold][ one.] (center) / [Third and last.]");
    }

    // The caret lands where the click did, so what is typed goes where the reader pointed.
    [Test]
    public async Task Edit_TheCaretIsWhereTheClickWas()
    {
        var page = await OpenEditAsync(Paragraphs);

        // Between "First " and "paragraph".
        await ClickBeforeAsync(page, "paragraph here");
        await page.WaitForSelectorAsync(editor);
        await page.Keyboard.TypeAsync("short ");
        await HandInAsync(page);

        await Assert.That(Read(await DownloadAsync(page))[0]).IsEqualTo("[First short paragraph here.]");
    }

    [Test]
    public async Task Edit_Escape_AbandonsWhatWasTyped()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.TypeAsync("Never mind");
        await page.Keyboard.PressAsync("Escape");

        await page.WaitForSelectorAsync(
            ".edit-box",
            new()
            {
                State = WaitForSelectorState.Detached
            });
        await Assert.That(Read(await DownloadAsync(page))[0]).IsEqualTo("[First paragraph here.]");
    }

    [Test]
    public async Task Edit_Enter_SplitsTheParagraph_AndBackspaceAtItsStart_JoinsItToTheOneBefore()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickBeforeAsync(page, "and last");
        await page.WaitForSelectorAsync(editor);
        await page.Keyboard.PressAsync("Enter");
        await Assert.That(await page.Locator($"{editor} .edit-p").CountAsync()).IsEqualTo(2);
        await page.Keyboard.TypeAsync("Fourth, ");
        await HandInAsync(page);

        await Assert.That(string.Join(" / ", Read(await DownloadAsync(page))))
            .IsEqualTo("[First paragraph here.] / [Second ][b:bold][ one.] (center) / [Third ] / [Fourth, and last.]");

        // Backspace at the start of the new paragraph takes the break back out, and leaves the
        // reader typing where the two met.
        await ClickInAsync(page, "Fourth, and");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Backspace");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.edit-box[contenteditable=true]')?.innerText === 'Third Fourth, and last.'",
            null,
            new()
            {
                Timeout = 60000
            });
        await page.Keyboard.TypeAsync("and ");
        await HandInAsync(page);

        await Assert.That(string.Join(" / ", Read(await DownloadAsync(page))))
            .IsEqualTo("[First paragraph here.] / [Second ][b:bold][ one.] (center) / [Third and ][Fourth, and last.]");
    }

    [Test]
    public async Task Edit_TheToolbarFormatsWhatIsSelectedInTheEditor()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < 5; index++)
        {
            await page.Keyboard.PressAsync("Shift+ArrowRight");
        }

        await page.ClickAsync("[data-edit-command=bold]");
        await Assert.That(await page.GetAttributeAsync("[data-edit-command=bold]", "aria-pressed")).IsEqualTo("true");
        await page.Keyboard.PressAsync("Control+i");
        await page.ClickAsync("[data-edit-command=align-right]");
        await HandInAsync(page);

        await Assert.That(Read(await DownloadAsync(page))[0]).IsEqualTo("[bi:First][ paragraph here.] (right)");
    }

    // With no paragraph open, the toolbar is for what is selected on the pages.
    [Test]
    public async Task Edit_TheToolbarFormatsWhatIsSelectedOnThePages()
    {
        var page = await OpenEditAsync(Paragraphs);
        var text = await LayerTextAsync(page);
        var from = text.IndexOf("here", StringComparison.Ordinal);
        var to = text.IndexOf("Second", StringComparison.Ordinal) + "Second".Length;

        await SelectAsync(page, from, to);
        await page.WaitForTimeoutAsync(300);
        await page.ClickAsync("[data-edit-command=underline]");
        await WaitForTextAsync(page, "First paragraph here.");

        await Assert.That(string.Join(" / ", Read(await DownloadAsync(page))))
            .IsEqualTo("[First paragraph ][u:here.] / [u:Second][ ][b:bold][ one.] (center) / [Third and last.]");

        // And Undo takes it back.
        await page.ClickAsync("[data-edit-command=undo]");
        await page.WaitForTimeoutAsync(300);
        await WaitForTextAsync(page, "First paragraph here.");
        await Assert.That(Read(await DownloadAsync(page))[0]).IsEqualTo("[First paragraph here.]");
    }

    [Test]
    public async Task Edit_Tracked_WhatIsTypedIsAChangeToReview()
    {
        var page = await OpenEditAsync(Paragraphs);

        await page.ClickAsync("[data-edit-command=track]");
        await page.WaitForSelectorAsync("[data-edit-command=track][aria-pressed=true]");
        await page.FillAsync(".viewer-edit-signature input", "Ann Lee");
        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" Indeed.");
        await HandInAsync(page);

        var saved = await DownloadAsync(page);
        await Assert.That(Read(saved)[2]).IsEqualTo("[Third and last.]{+[ Indeed.]+}");
        var change = DocumentReview.Read(saved).Changes.Single();
        await Assert.That(change.Author).IsEqualTo("Ann Lee");
        await Assert.That(change.Text).IsEqualTo(" Indeed.");
        await Assert.That(DocumentOutline.Read(saved).Tracking).IsTrue();
        await Assert.That(await page.InnerTextAsync(".viewer-badge")).IsEqualTo("1");
    }

    // A field is shown and left alone: the caret steps over it as over one character, and what is
    // typed either side of it goes either side of it in the file.
    [Test]
    public async Task Edit_WhatIsNotText_StaysWhereItIs_WhileTheTextAroundItIsEdited()
    {
        const string field =
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> DATE </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1 May</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";
        var page = await OpenEditAsync(Build(P("", R("Dated "), field, R(" by Ann"))));

        await ClickInAsync(page, "by Ann");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Dated 1 May by Ann");
        await Assert.That(await page.InnerTextAsync($"{editor} .edit-field")).IsEqualTo("1 May");

        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync("!");
        for (var index = 0; index < " by Ann!".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowLeft");
        }

        // Just after the field, and then just before it.
        await page.Keyboard.TypeAsync(",");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.TypeAsync("on ");

        // Everything selected and deleted: the text goes, and the field stays.
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Dated on 1 May, by Ann!");
        await HandInAsync(page);

        await Assert.That(string.Join(" / ", Read(await DownloadAsync(page)))).IsEqualTo("[Dated on ][][][][1 May][][, by Ann!]");

        await ClickInAsync(page, "by Ann");
        await page.Keyboard.PressAsync("Control+a");
        await page.Keyboard.PressAsync("Delete");
        await Assert.That((await page.InnerTextAsync(editor)).Trim()).IsEqualTo("1 May");
        await HandInAsync(page);

        await Assert.That(string.Join(" / ", Read(await DownloadAsync(page)))).IsEqualTo("[][][][1 May][]");
    }

    // Clicking another paragraph hands in the one that was open and opens the one clicked.
    [Test]
    public async Task Edit_ClickingAnotherParagraph_HandsInTheFirst_AndOpensTheSecond()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" More.");
        await ClickInAsync(page, "and last", "Third and last.");

        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" Done.");
        await HandInAsync(page);

        await Assert.That(string.Join(" / ", Read(await DownloadAsync(page))))
            .IsEqualTo("[First paragraph here. More.] / [Second ][b:bold][ one.] (center) / [Third and last. Done.]");
    }

    // An arrow key that would take the caret out of a paragraph takes the reader to the next one.
    [Test]
    public async Task Edit_ArrowDown_AtTheLastLine_GoesToTheNextParagraph()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.edit-box[contenteditable=true]')?.innerText === 'Second bold one.'",
            null,
            new()
            {
                Timeout = 60000
            });

        await page.Keyboard.PressAsync("ArrowUp");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.edit-box[contenteditable=true]')?.innerText === 'First paragraph here.'",
            null,
            new()
            {
                Timeout = 60000
            });
    }

    [Test]
    public async Task Edit_IsNotOffered_ForAWorkbook_OrAProtectedDocument()
    {
        var locked = Build(P("", R("Read only.")), "<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\"/>");

        var page = await OpenFileAsync(locked, "locked.docx");

        await Assert.That(await page.Locator(".viewer-edit-toggle").CountAsync()).IsEqualTo(0);
    }

    // Opens /view on a file and turns editing on, once the page has painted and has its text.
    static async Task<IPage> OpenEditAsync(byte[] docx)
    {
        var page = await OpenFileAsync(docx, "edit.docx");
        await page.ClickAsync(".viewer-edit-toggle");
        await page.WaitForSelectorAsync(".viewer-editbar");
        return page;
    }

    static async Task<IPage> OpenFileAsync(byte[] docx, string name)
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/view");
        await page.WaitForSelectorAsync(
            ".viewer-open input[type=file]",
            new()
            {
                State = WaitForSelectorState.Attached,
                Timeout = 120000
            });
        await page.SetInputFilesAsync(
            ".viewer-open input[type=file]",
            new FilePayload
            {
                Name = name,
                MimeType = ConversionService.Find(InputFormat.Docx).ContentType,
                Buffer = docx
            });
        await page.WaitForSelectorAsync(
            ".viewer-page[data-current][data-rendered-dpi] .text-layer[data-text-state=ready]",
            new()
            {
                Timeout = 120000
            });
        return page;
    }

    // Clicks the first character of some text of the first page, and waits for the editor that
    // opens on its paragraph — which, if an editor was open, is the one that takes its place.
    static async Task ClickInAsync(IPage page, string text, string? paragraph = null)
    {
        var layer = await LayerTextAsync(page);
        var offset = layer.IndexOf(text, StringComparison.Ordinal);
        await Assert.That(offset).IsGreaterThanOrEqualTo(0);
        await ClickTextAsync(page, offset);
        if (paragraph == null)
        {
            await page.WaitForSelectorAsync(
                editor,
                new()
                {
                    Timeout = 60000
                });
            return;
        }

        await page.WaitForFunctionAsync(
            "text => document.querySelector('.edit-box[contenteditable=true]')?.innerText.startsWith(text)",
            paragraph[..5],
            new()
            {
                Timeout = 60000
            });
    }

    // Clicks the left edge of the first character of some text: the caret goes before it.
    static async Task ClickBeforeAsync(IPage page, string text)
    {
        var layer = await LayerTextAsync(page);
        var offset = layer.IndexOf(text, StringComparison.Ordinal);
        await Assert.That(offset).IsGreaterThanOrEqualTo(0);
        var box = await page.EvaluateAsync<double[]>(
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
                        const box = range.getBoundingClientRect();
                        return [box.left + 0.5, box.top + box.height / 2];
                    }

                    start = next;
                }

                return [];
            }
            """,
            offset);
        await page.Mouse.ClickAsync((float) box[0], (float) box[1]);
    }

    // Ctrl+Enter hands the paragraph in; the editor goes once the page has been drawn again.
    static async Task HandInAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Control+Enter");
        await page.WaitForSelectorAsync(
            ".edit-box",
            new()
            {
                State = WaitForSelectorState.Detached,
                Timeout = 60000
            });
        await page.WaitForSelectorAsync(
            ".viewer-page[data-current][data-rendered-dpi] .text-layer[data-text-state=ready]",
            new()
            {
                Timeout = 60000
            });
    }

    static Task WaitForTextAsync(IPage page, string text) =>
        page.WaitForFunctionAsync(
            """
            text => {
                const page = document.querySelector('.viewer-page[data-page-number="1"][data-rendered-dpi]');
                const layer = page?.querySelector('.text-layer[data-text-state=ready]');
                return layer !== null && layer !== undefined && layer.textContent.includes(text) &&
                    document.querySelector('.viewer-editbar')?.getAttribute('aria-busy') === 'false';
            }
            """,
            text,
            new()
            {
                Timeout = 60000
            });
}
