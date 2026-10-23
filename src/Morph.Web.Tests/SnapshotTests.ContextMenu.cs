using static EditFixtures;

// Word's right-click menu and Escape, in a real browser: a right-click selects what is under it — the
// paragraph there opened, as a click opens it — and offers what belongs to that place; Escape closes a
// menu or a dialog and goes no further, so what was being typed stays.
public partial class SnapshotTests
{
    const string contextMenu = ".editor-context-menu";

    static async Task RightClickInAsync(IPage page, string text)
    {
        var layer = await LayerTextAsync(page);
        var offset = layer.IndexOf(text, StringComparison.Ordinal);
        await Assert.That(offset).IsGreaterThanOrEqualTo(0);
        await ClickTextAsync(page, offset, MouseButton.Right);
        await page.WaitForSelectorAsync(contextMenu);
    }

    static Task ChooseFromContextAsync(IPage page, string line) =>
        page.ClickAsync($"{contextMenu} .editor-menu-item:text-is('{line}')");

    [Test]
    public async Task ContextMenu_OnAParagraph_OpensIt_AndOffersWhatBelongsThere()
    {
        var page = await OpenEditAsync(Paragraphs);

        await RightClickInAsync(page, "bold");

        // The paragraph is open, as a click there opens it.
        await page.WaitForFunctionAsync("() => document.querySelector('.edit-box[contenteditable=true]')?.innerText === 'Second bold one.'");
        var lines = await page.Locator($"{contextMenu} > .editor-menu-item, {contextMenu} > .editor-menu-sub > .editor-menu-item").AllInnerTextsAsync();
        await Assert.That(string.Join(" | ", lines)).IsEqualTo("Cut | Copy | Paste | Font… | Paragraph… | Link… | New comment");

        await ChooseFromContextAsync(page, "Paragraph…");
        await page.WaitForSelectorAsync("#editor-paragraph-title");
        await Assert.That(await page.Locator(contextMenu).CountAsync()).IsEqualTo(0);

        // A dialog closes on Escape too, though nothing in it has the focus.
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(
            ".editor-dialog",
            new()
            {
                State = WaitForSelectorState.Detached
            });
    }

    // In a table the menu offers its rows and columns, and a submenu's line runs its command.
    [Test]
    public async Task ContextMenu_InATable_InsertsARow()
    {
        var page = await OpenEditAsync(TableDocument());

        await RightClickInAsync(page, "Cell one");
        await page.HoverAsync($"{contextMenu} .editor-menu-item:text-is('Insert')");
        await ChooseFromContextAsync(page, "Row below");

        await WaitForTableAsync(page, _ => _.Rows == 3);
        await Assert.That(await page.Locator(contextMenu).CountAsync()).IsEqualTo(0);
    }

    // Escape closes the menu that is open — the ribbon's or a right-click's — and goes no further: what
    // was typed in the open paragraph stays, where Escape with no menu open abandons it.
    [Test]
    public async Task Escape_ClosesAMenu_AndKeepsWhatWasTyped()
    {
        var page = await OpenEditAsync(Paragraphs);
        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" More");

        await ChooseTabAsync(page, "Layout");
        await page.ClickAsync(".editor-tool:has-text('Margins')");
        await page.WaitForSelectorAsync(".editor-menu");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(
            ".editor-menu",
            new()
            {
                State = WaitForSelectorState.Detached
            });
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and last. More");

        await RightClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(
            contextMenu,
            new()
            {
                State = WaitForSelectorState.Detached
            });
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and last. More");

        await HandInAsync(page);
        await Assert.That(Read(await SaveAsync(page))[2]).IsEqualTo("[Third and last. More]");
    }

    // The clipboard's lines are the script's, inside the press, as the ribbon's buttons are: a word
    // copied from the menu, then pasted from it over another, replaces that one. A right-click on text
    // already selected leaves it selected.
    [Test]
    public async Task ContextMenu_CopyAndPaste_GoThroughTheClipboard()
    {
        await using var context = await browser!.NewContextAsync(
            new()
            {
                Permissions = ["clipboard-read", "clipboard-write"]
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/edit");
        await LoadIntoAsync(page, Paragraphs, "edit.docx");
        await page.WaitForSelectorAsync(".viewer[data-edit=on]");

        await ClickInAsync(page, "and last");
        await SelectInEditorAsync(page, "Third ".Length, "and".Length);
        await RightClickInAsync(page, "and last");
        await ChooseFromContextAsync(page, "Copy");

        await SelectInEditorAsync(page, "Third and ".Length, "last".Length);
        await RightClickInAsync(page, "last");
        await ChooseFromContextAsync(page, "Paste");

        await page.WaitForFunctionAsync(
            "() => document.querySelector('.edit-box[contenteditable=true]')?.innerText === 'Third and and.'",
            null,
            new()
            {
                Timeout = 60000
            });
        await HandInAsync(page);
        await Assert.That(Read(await SaveAsync(page))[2]).IsEqualTo("[Third and and.]");
    }
}
