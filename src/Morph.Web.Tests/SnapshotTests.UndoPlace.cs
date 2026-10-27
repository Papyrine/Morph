using static EditFixtures;

// Undo puts the selection back where the undone action began, and redo where it left it (WordProbe
// R-U-2): undone typing leaves the caret at the typing's start, an undone delete selects again what it
// deleted.
public partial class SnapshotTests
{
    [Test]
    [Property("WordBehavior", "R-U-2")]
    public async Task Undo_OfTyping_PutsTheCaretWhereTheTypingBegan()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "Third ".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        await page.Keyboard.TypeAsync("very ");
        await HandInAsync(page);
        await WaitForTextAsync(page, "Third very and last.");

        await page.FocusAsync(".viewer-pages");
        await page.Keyboard.PressAsync("Control+z");
        await WaitForEditorAsync(page, "Third and last.");
        await Assert.That(await CaretAsync(page)).IsEqualTo("Third ".Length);

        // Redo leaves the caret after what it put back.
        await page.ClickAsync("[aria-label='Redo']");
        await WaitForEditorAsync(page, "Third very and last.");
        await Assert.That(await CaretAsync(page)).IsEqualTo("Third very ".Length);
    }

    [Test]
    [Property("WordBehavior", "R-U-2")]
    public async Task Undo_OfADelete_SelectsWhatWasDeleted()
    {
        var page = await OpenEditAsync(Paragraphs);

        await SelectOnPagesAsync(page, "bold", "bold");
        await page.Keyboard.PressAsync("Delete");
        await WaitForBodyAsync(page, _ => !_.Contains("bold"));

        await page.FocusAsync(".viewer-pages");
        await page.Keyboard.PressAsync("Control+z");
        await WaitForEditorAsync(page, "Second bold one.");
        await Assert.That(await page.EvaluateAsync<string>("() => getSelection().toString()")).IsEqualTo("bold");
    }

    // The caret's offset into the open paragraph's text.
    static Task<int> CaretAsync(IPage page) =>
        page.EvaluateAsync<int>(
            """
            () => {
                const box = document.querySelector('.edit-box[contenteditable=true]');
                const caret = getSelection().getRangeAt(0);
                const before = document.createRange();
                before.setStart(box, 0);
                before.setEnd(caret.startContainer, caret.startOffset);
                return before.toString().length;
            }
            """);
}
