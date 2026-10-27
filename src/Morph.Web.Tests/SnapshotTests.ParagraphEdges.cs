using static EditFixtures;

// Left and Right cross a paragraph mark one position at a time, and at the story's ends the arrows go no
// further (WordProbe R-N-1, R-N-2).
public partial class SnapshotTests
{
    [Test]
    [Property("WordBehavior", "R-N-1")]
    [Property("WordBehavior", "R-N-2")]
    public async Task Arrows_AtAParagraphsEdges_StepOnePosition()
    {
        var page = await OpenEditAsync(Paragraphs);

        // Left at a paragraph's start: the end of the one before.
        await ClickInAsync(page, "bold one");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("ArrowLeft");
        await WaitForEditorAsync(page, "First paragraph here.");
        await Assert.That(await CaretAsync(page)).IsEqualTo("First paragraph here.".Length);

        // Right at its end: the start of the one after.
        await page.Keyboard.PressAsync("ArrowRight");
        await WaitForEditorAsync(page, "Second bold one.");
        await Assert.That(await CaretAsync(page)).IsEqualTo(0);

        // Typed in first, the paragraph is handed in on the way.
        await page.Keyboard.TypeAsync("X");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("ArrowLeft");
        await WaitForEditorAsync(page, "First paragraph here.");
        await Assert.That(Read(await SaveAsync(page))[1]).StartsWith("[X");

        // At the story's first position Left and Up do nothing. (Saving took the focus, and the paragraph.)
        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.PressAsync("ArrowUp");
        await page.WaitForTimeoutAsync(500);
        await WaitForEditorAsync(page, "First paragraph here.");
        await Assert.That(await CaretAsync(page)).IsEqualTo(0);

        // At its last, Right and Down do nothing.
        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(500);
        await WaitForEditorAsync(page, "Third and last.");
        await Assert.That(await CaretAsync(page)).IsEqualTo("Third and last.".Length);
    }

    // Ctrl+Right stops before a paragraph's mark, then at the next paragraph's start; Ctrl+Left at a start
    // goes back to the end of the paragraph before (WordProbe N-12).
    [Test]
    [Property("WordBehavior", "R-N-10")]
    public async Task CtrlArrows_CrossParagraphs()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Control+ArrowRight");
        await WaitForEditorAsync(page, "Second bold one.");
        await Assert.That(await CaretAsync(page)).IsEqualTo(0);

        await page.Keyboard.PressAsync("Control+ArrowRight");
        await Assert.That(await CaretAsync(page)).IsEqualTo("Second ".Length);

        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Control+ArrowLeft");
        await WaitForEditorAsync(page, "First paragraph here.");
        await Assert.That(await CaretAsync(page)).IsEqualTo("First paragraph here.".Length);
    }
}
