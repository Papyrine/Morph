using static EditFixtures;

// The viewer script's keys and pointer in a real Chromium (morph-viewer.js): the ruler after a zoom,
// Ctrl+F before anything has the focus, keys behind a dialog, Paste without the clipboard API, the menus'
// arrow keys, and Down off a page's last line.
public partial class SnapshotTests
{
    [Test]
    public async Task ViewerKeys_CtrlF_OpensFind_BeforeThePagesHaveTheFocus()
    {
        var page = await OpenViewerAsync(InputFormat.Docx);
        await page.EvaluateAsync("() => document.activeElement?.blur()");
        await Assert.That(await page.EvaluateAsync<bool>("() => document.activeElement === document.body")).IsTrue();

        await page.Keyboard.PressAsync("Control+KeyF");

        await page.WaitForSelectorAsync(".viewer-findbar", new() {Timeout = 10000});
    }

    // Drawn at the zoom it is used at: after a zoom the ruler's strip is the page's width again.
    [Test]
    public async Task ViewerKeys_Ruler_FollowsTheZoom()
    {
        var page = await OpenEditAsync(Paragraphs);
        await ChooseTabAsync(page, "View");
        await page.ClickAsync(".editor-tool:has-text('Ruler')");
        await ClickInAsync(page, "paragraph here");
        await page.WaitForSelectorAsync(".editor-ruler .ruler-strip");
        const string widths =
            """
            () => {
                const strip = document.querySelector('.editor-ruler .ruler-strip').getBoundingClientRect().width;
                const sheet = document.querySelector('.edit-box').closest('.viewer-page').getBoundingClientRect().width;
                return [strip, sheet];
            }
            """;
        var before = await page.EvaluateAsync<double[]>(widths);
        await Assert.That(Math.Abs(before[0] - before[1])).IsLessThan(1);

        await page.Keyboard.PressAsync("Control+NumpadAdd");
        await page.WaitForFunctionAsync($"() => document.querySelector('.edit-box').closest('.viewer-page').getBoundingClientRect().width > {before[1] + 10}");

        var after = await page.EvaluateAsync<double[]>(widths);
        await Assert.That(Math.Abs(after[0] - after[1])).IsLessThan(1);
    }

    // A letter pressed with the Paragraph dialog open over a selection on the pages types nothing.
    [Test]
    public async Task ViewerKeys_DialogOpen_KeysDoNotReachThePages()
    {
        var page = await OpenEditAsync(Paragraphs);
        await SelectFirstTwoAsync(page);
        await page.ClickAsync("button[aria-label='Paragraph settings']");
        await page.WaitForSelectorAsync(".editor-dialog");
        await page.FocusAsync(".viewer-pages");

        await page.Keyboard.PressAsync("x");
        await page.WaitForTimeoutAsync(500);
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(".editor-dialog", new() {State = WaitForSelectorState.Detached});

        var saved = Read(await SaveAsync(page));
        await Assert.That(saved[0]).IsEqualTo("[First paragraph here.]");
    }

    // Without navigator.clipboard.readText Paste is greyed out, not taken away.
    [Test]
    public async Task ViewerKeys_Paste_IsGreyedWithoutTheClipboardApi()
    {
        var page = await browser!.NewPageAsync();
        await page.AddInitScriptAsync("Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true })");
        await page.GotoAsync($"http://localhost:{port}/edit");
        await LoadIntoAsync(page, Paragraphs, "edit.docx");
        await page.WaitForSelectorAsync(".viewer[data-edit=on]");
        await ClickInAsync(page, "paragraph here");

        await page.WaitForSelectorAsync(".editor-ribbon [data-edit-command=paste][data-unselected]", new() {State = WaitForSelectorState.Attached});
        await Assert.That(await page.EvaluateAsync<bool>("() => document.querySelector('.editor-ribbon [data-edit-command=paste]').hidden")).IsFalse();
    }

    // Down and Up step through the right-click menu's lines that can be chosen, End goes to the last,
    // and the paragraph being typed keeps the focus.
    [Test]
    public async Task ViewerKeys_Menu_ArrowKeysStepThroughItsLines()
    {
        var page = await OpenEditAsync(Paragraphs);
        await RightClickInAsync(page, "paragraph here");
        await page.WaitForSelectorAsync(".editor-context-menu");

        await page.Keyboard.PressAsync("ArrowDown");
        var first = await page.EvaluateAsync<string>("() => document.querySelector('.editor-context-menu [data-menu-active]')?.textContent.trim()");
        await page.Keyboard.PressAsync("ArrowDown");
        var second = await page.EvaluateAsync<string>("() => document.querySelector('.editor-context-menu [data-menu-active]')?.textContent.trim()");
        await page.Keyboard.PressAsync("End");
        var last = await page.EvaluateAsync<bool>(
            """
            () => {
                const lines = [...document.querySelectorAll('.editor-context-menu [role^=menuitem]')]
                    .filter(_ => _.closest('[role=menu]').classList.contains('editor-context-menu') && !_.disabled && !_.hasAttribute('data-unselected'));
                return lines[lines.length - 1] === document.querySelector('.editor-context-menu [data-menu-active]');
            }
            """);

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNotNull();
        await Assert.That(second).IsNotEqualTo(first);
        await Assert.That(last).IsTrue();
        await Assert.That(await page.EvaluateAsync<bool>("() => !!document.activeElement?.closest('.edit-box')")).IsTrue();
    }

    // Down on the last line of a page's last paragraph opens the first paragraph of the next page.
    [Test]
    public async Task ViewerKeys_ArrowDown_CrossesToTheNextPage()
    {
        var paragraphs = Enumerable.Range(1, 80).Select(_ => P("", R($"Paragraph number {_:00} of the long document."))).ToArray();
        var page = await OpenEditAsync(Build(string.Concat(paragraphs)));
        var layer = await LayerTextAsync(page);
        var last = System.Text.RegularExpressions.Regex.Matches(layer, @"Paragraph number (\d\d)")[^1].Groups[1].Value;
        await ClickInAsync(page, $"Paragraph number {last}");
        await page.Keyboard.PressAsync("End");

        await page.Keyboard.PressAsync("ArrowDown");

        var next = (int.Parse(last, CultureInfo.InvariantCulture) + 1).ToString("00", CultureInfo.InvariantCulture);
        await page.WaitForFunctionAsync(
            "text => document.querySelector('.viewer-page[data-page-number=\"2\"] .edit-box[contenteditable=true]')?.innerText.startsWith(text)",
            $"Paragraph number {next}",
            new() {Timeout = 30000});
    }

    // A floating picture selected, dragged by its body, moves by as much as it was dragged; a click on
    // it still selects it.
    [Test]
    public async Task ViewerKeys_FloatingPicture_DraggedByItsBody_Moves()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var pictured = DocumentEditor.InsertPicture(Paragraphs, 0, 6, new(pixelPng, "image/png", 100, 50), options).Document;
        var floating = DocumentEditor.SetPictureWrap(pictured, 0, 6, PictureWrap.Square, options);
        var page = await OpenEditAsync(floating);
        var box = await page.EvaluateAsync<double[]>(
            """
            () => {
                const page = document.querySelector('.viewer-page[data-page-number="1"]');
                page.scrollIntoView({block: "start"});
                const rect = page.getBoundingClientRect();
                return [rect.left, rect.top, rect.width / Number(getComputedStyle(page).getPropertyValue('--pw'))];
            }
            """);
        var (left, top, scale) = (box[0], box[1], box[2]);
        var x = (float) (left + (72 + 50) * scale);
        var y = (float) (top + (72 + 25) * scale);
        await page.Mouse.ClickAsync(x, y);
        await page.WaitForSelectorAsync(".viewer-picture-frame");
        await page.Mouse.ClickAsync(x, y);
        await page.WaitForSelectorAsync(".viewer-picture-frame");
        var before = Offsets(DocumentBody(await SaveAsync(page)));

        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + (float) (40 * scale), y + (float) (20 * scale), new() {Steps = 6});
        await page.Mouse.UpAsync();

        await WaitForBodyAsync(page, _ => Offsets(_) is var (right, down) &&
                                          Math.Abs(right - before.Right - 40) < 1.5 &&
                                          Math.Abs(down - before.Down - 20) < 1.5);
    }

    // The first drawing's horizontal and vertical offsets, in points.
    static (double Right, double Down) Offsets(string body)
    {
        var found = System.Text.RegularExpressions.Regex.Matches(body, "<wp:posOffset>(-?\\d+)</wp:posOffset>");
        if (found.Count < 2)
        {
            return (double.NaN, double.NaN);
        }

        return (long.Parse(found[0].Groups[1].Value, CultureInfo.InvariantCulture) / 12700.0,
            long.Parse(found[1].Groups[1].Value, CultureInfo.InvariantCulture) / 12700.0);
    }
}
