using static EditFixtures;

// What Track Changes looks like in the open paragraph, before it is handed in: what was there and is
// deleted is struck in its place, what is typed is shown inserted, and what was typed since — or
// inserted before by the same author — goes outright when it is deleted (WordProbe R-R-1, R-R-2).
public partial class SnapshotTests
{
    static async Task TrackAsync(IPage page)
    {
        await ChooseTabAsync(page, "Review");
        await page.ClickAsync("[data-edit-command=track]");
        await page.WaitForSelectorAsync("[data-edit-command=track][aria-pressed=true]");
    }

    // Each struck stretch of the box, joined by "|".
    static async Task<string> StruckAsync(IPage page) =>
        string.Join("|", await page.Locator($"{editor} .edit-deleted").AllInnerTextsAsync());

    // How the box draws the text of a run: its decoration lines and its colour.
    static Task<string> LookOfAsync(IPage page, string text) =>
        page.EvaluateAsync<string>(
            """
            text => {
                const span = [...document.querySelectorAll('.edit-box .edit-run, .edit-box .edit-deleted')]
                    .find(_ => _.textContent === text);
                const style = span ? getComputedStyle(span) : null;
                return style ? `${style.textDecorationLine} ${style.color}` : '';
            }
            """,
            text);

    // Backspace strikes what was there and leaves the caret before it, so what is typed next goes in
    // ahead of the deletion (WordProbe R-02, R-13); Backspace over what was just typed takes it out.
    [Test]
    [Property("WordBehavior", "R-R-1")]
    [Property("WordBehavior", "R-02")]
    [Property("WordBehavior", "R-13")]
    public async Task Tracked_Backspace_StrikesTheText_InTheBox()
    {
        var page = await OpenEditAsync(Paragraphs);
        await TrackAsync(page);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        for (var index = 0; index < "last.".Length; index++)
        {
            await page.Keyboard.PressAsync("Backspace");
        }

        await Assert.That(await BoxTextAsync(page)).IsEqualTo("Third and last.");
        await Assert.That(await StruckAsync(page)).IsEqualTo("last.");
        await Assert.That(await LookOfAsync(page, "last.")).IsEqualTo("line-through rgb(209, 52, 56)");

        await page.Keyboard.TypeAsync("z");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("Third and zlast.");
        await Assert.That(await LookOfAsync(page, "z")).IsEqualTo("underline rgb(209, 52, 56)");

        await page.Keyboard.PressAsync("Backspace");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("Third and last.");
        await Assert.That(await StruckAsync(page)).IsEqualTo("last.");

        // Handed in, what was typed ahead of the deletion stays ahead of it, as the box showed it:
        // compared as texts, "last." made "first." would be "la" replaced by "fir", behind it.
        await page.Keyboard.TypeAsync("first.");
        await HandInAsync(page);
        await Assert.That(Read(await SaveAsync(page))[2]).IsEqualTo("[Third and ]{+[first.]+}{-[last.]-}");
    }

    // Backspace on a selection leaves the caret before what it struck, so what is typed next goes in
    // ahead of it (WordProbe R-21).
    [Test]
    [Property("WordBehavior", "R-21")]
    public async Task Tracked_BackspaceOnASelection_LeavesTheCaretBeforeIt()
    {
        var page = await OpenEditAsync(Paragraphs);
        await TrackAsync(page);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "Third ".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        for (var index = 0; index < "and".Length; index++)
        {
            await page.Keyboard.PressAsync("Shift+ArrowRight");
        }

        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.TypeAsync("or");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("Third orand last.");
        await Assert.That(await StruckAsync(page)).IsEqualTo("and");
    }

    // Delete strikes what was there and steps the caret over it (WordProbe R-03); a selection typed
    // over is struck, and what replaces it goes in after it (R-05).
    [Test]
    [Property("WordBehavior", "R-R-2")]
    [Property("WordBehavior", "R-03")]
    [Property("WordBehavior", "R-05")]
    public async Task Tracked_DeleteAndTypingOver_StrikeTheText_InTheBox()
    {
        var page = await OpenEditAsync(Paragraphs);
        await TrackAsync(page);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "Third ".Length; index++)
        {
            await page.Keyboard.PressAsync("Delete");
        }

        await page.Keyboard.TypeAsync("X");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("Third Xand last.");
        await Assert.That(await StruckAsync(page)).IsEqualTo("Third ");

        for (var index = 0; index < "and".Length; index++)
        {
            await page.Keyboard.PressAsync("Shift+ArrowRight");
        }

        await page.Keyboard.TypeAsync("or");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("Third Xandor last.");
        await Assert.That(await StruckAsync(page)).IsEqualTo("Third |and");

        // Handed in, the file holds each change where the box showed it: compared as texts instead,
        // "Third and" made "Xor" would be one word replaced by another.
        await HandInAsync(page);
        await Assert.That(Read(await SaveAsync(page))[2]).IsEqualTo("{-[Third ]-}{+[X]+}{-[and]-}{+[or]+}[ last.]");
    }

    // Undo in the open paragraph takes a deletion back with the step that made it.
    [Test]
    [Property("WordBehavior", "R-R-8")]
    public async Task Tracked_UndoInTheBox_TakesTheDeletionBack()
    {
        var page = await OpenEditAsync(Paragraphs);
        await TrackAsync(page);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Backspace");
        await Assert.That(await StruckAsync(page)).IsEqualTo(".");

        await page.Keyboard.PressAsync("Control+z");
        await Assert.That(await StruckAsync(page)).IsEqualTo("");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("Third and last.");
    }

    // Text the same author inserted before, deleted under tracking, goes outright: it was never
    // there to strike (WordProbe R-R-6).
    [Test]
    [Property("WordBehavior", "R-R-1")]
    public async Task Tracked_TheAuthorsOwnInsertion_IsTakenOut_NotStruck()
    {
        var page = await OpenEditAsync(
            Build(
                P(
                    "",
                    R("Kept "),
                    "<w:ins w:id=\"1\" w:author=\"Ann Lee\" w:date=\"2026-01-01T00:00:00Z\">", R("new"), "</w:ins>",
                    R(" text."))));
        await TrackAsync(page);
        await page.FillAsync(".viewer-edit-signature input", "Ann Lee");

        await ClickInAsync(page, "text.");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "Kept new".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        for (var index = 0; index < "new".Length; index++)
        {
            await page.Keyboard.PressAsync("Backspace");
        }

        await Assert.That(await BoxTextAsync(page)).IsEqualTo("Kept  text.");
        await Assert.That(await StruckAsync(page)).IsEqualTo("");

        await HandInAsync(page);
        await Assert.That(string.Concat(Read(await SaveAsync(page)))).DoesNotContain("new");
    }
}
