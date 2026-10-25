using static EditFixtures;

// Word's keys whose rules src/word-behavior.md marks DONE for the open paragraph, each test citing the
// rule it keeps true (WordBehaviorCoverageTests).
public partial class SnapshotTests
{
    // Insert turns Overtype on: what is typed takes the place of what follows the caret, and at the
    // paragraph's end is added; Insert again goes back to inserting (WordProbe E-11, E-105).
    [Test]
    [Property("WordBehavior", "R-E-5")]
    public async Task Overtype_TypesOverWhatFollows_AndAppendsAtTheEnd()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Insert");
        await page.WaitForSelectorAsync(".editor-status-item:text('Overtype')");
        await page.Keyboard.TypeAsync("Fifth");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync("!");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Fifth and last.!");

        await page.Keyboard.PressAsync("Insert");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.TypeAsync("A ");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("A Fifth and last.!");
        await HandInAsync(page);

        await Assert.That(Read(await SaveAsync(page))[2]).IsEqualTo("[A Fifth and last.!]");
    }

    // Under Track Changes Overtype overwrites nothing: what is typed is inserted (WordProbe E-11 tracked).
    [Test]
    [Property("WordBehavior", "R-E-5")]
    public async Task Overtype_UnderTrackChanges_Inserts()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ChooseTabAsync(page, "Review");
        await page.ClickAsync("[data-edit-command=track]");
        await page.WaitForSelectorAsync("[data-edit-command=track][aria-pressed=true]");
        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Insert");
        await page.Keyboard.TypeAsync("New ");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("New Third and last.");
        await HandInAsync(page);

        await Assert.That(Read(await SaveAsync(page))[2]).IsEqualTo("{+[New ]+}[Third and last.]");
    }

    // F8 extends in steps from where it was first pressed: the word with its trailing space, the
    // sentence, the paragraph; Shift+F8 steps back; a typed letter extends to its next occurrence, and
    // Esc ends the mode keeping the selection (WordProbe S-10).
    [Test]
    [Property("WordBehavior", "R-S-5")]
    public async Task ExtendMode_F8_StepsThroughWordSentenceParagraph()
    {
        var page = await OpenEditAsync(Build(P("", R("One two. The quick fox jumps. Last one."))));

        await ClickInAsync(page, "quick");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "One two. The qu".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        await page.Keyboard.PressAsync("F8");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("");
        await page.Keyboard.PressAsync("F8");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("quick ");
        await page.Keyboard.PressAsync("F8");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("The quick fox jumps. ");
        await page.Keyboard.PressAsync("F8");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("One two. The quick fox jumps. Last one.");
        await page.Keyboard.PressAsync("Shift+F8");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("The quick fox jumps. ");
        await page.Keyboard.PressAsync("Shift+F8");
        await page.Keyboard.PressAsync("Shift+F8");
        await page.Keyboard.PressAsync("j");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("ick fox j");
        await page.Keyboard.PressAsync("Escape");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("ick fox j");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("One two. The quick fox jumps. Last one.");
    }

    static Task<string> SelectedAsync(IPage page) =>
        page.EvaluateAsync<string>("() => getSelection().toString()");
}
