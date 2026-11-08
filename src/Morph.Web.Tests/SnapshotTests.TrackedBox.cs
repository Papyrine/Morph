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
    // ahead of it (WordProbe R-21); the selected word takes its space with it (smart cut, E-50c).
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
        await Assert.That(await StruckAsync(page)).IsEqualTo("and ");
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

    // "Kept ", an insertion "added " and a deletion "gone " both by Ann Lee, then "text stays.", opened
    // under tracking as Ann Lee: in places, "Kept added " is 0 to 11, the deletion is place 11, and
    // "text stays." follows it (WordProbe R-10's fixture).
    static async Task<IPage> OwnRevisionsAsync()
    {
        var page = await OpenEditAsync(
            Build(
                P(
                    "",
                    R("Kept "),
                    "<w:ins w:id=\"1\" w:author=\"Ann Lee\" w:date=\"2026-01-01T00:00:00Z\">", R("added "), "</w:ins>",
                    "<w:del w:id=\"2\" w:author=\"Ann Lee\" w:date=\"2026-01-01T00:00:00Z\"><w:r><w:delText xml:space=\"preserve\">gone </w:delText></w:r></w:del>",
                    R("text stays."))));
        await TrackAsync(page);
        await page.FillAsync(".viewer-edit-signature input", "Ann Lee");
        await ClickInAsync(page, "text stays");
        await page.Keyboard.PressAsync("Home");
        return page;
    }

    // The author's own revisions are theirs to undo (WordProbe R-R-6, R-10): Delete before their
    // deletion steps over it, Backspace in their insertion takes a letter out and typing there joins
    // it, and deleting the insertion with the deletion takes the one out and leaves the other.
    [Test]
    [Property("WordBehavior", "R-R-6")]
    [Property("WordBehavior", "R-10")]
    public async Task Tracked_TheAuthorsOwnRevisions_AreTheirsToUndo()
    {
        var page = await OwnRevisionsAsync();
        for (var index = 0; index < "Kept added ".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        await page.Keyboard.PressAsync("Delete");
        await page.Keyboard.TypeAsync("Z");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("Kept added gone Ztext stays.");
        await HandInAsync(page);
        await Assert.That(Read(await SaveAsync(page))[0]).IsEqualTo("[Kept ]{+[added ]+}{-[gone ]-}{+[Z]+}[text stays.]");

        page = await OwnRevisionsAsync();
        for (var index = 0; index < "Kept ad".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.TypeAsync("x");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("Kept axded gone text stays.");
        await Assert.That(await StruckAsync(page)).IsEqualTo("gone ");
        await HandInAsync(page);
        await Assert.That(Read(await SaveAsync(page))[0]).IsEqualTo("[Kept ]{+[axded ]+}{-[gone ]-}[text stays.]");

        page = await OwnRevisionsAsync();
        for (var index = 0; index < "Kept ".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        for (var index = 0; index < "added ".Length + 1; index++)
        {
            await page.Keyboard.PressAsync("Shift+ArrowRight");
        }

        await page.Keyboard.PressAsync("Delete");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("Kept gone text stays.");
        await HandInAsync(page);
        await Assert.That(Read(await SaveAsync(page))[0]).IsEqualTo("[Kept ]{-[gone ]-}[text stays.]");
    }
}
