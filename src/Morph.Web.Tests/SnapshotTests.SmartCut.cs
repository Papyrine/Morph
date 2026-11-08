using static EditFixtures;

// Word's smart cut, tracked or not: whole words deleted take a space with them rather than leave two,
// or one before punctuation (WordProbe E-50c, E-50d, E-70e). "The quick brown fox jumps over the lazy
// dog." is WordProbe's plain fixture.
public partial class SnapshotTests
{
    const string quickFox = "The quick brown fox jumps over the lazy dog.";

    // Opens the fox paragraph with the places from to to selected.
    static async Task<IPage> FoxAsync(int from, int to, bool tracked = false)
    {
        var page = await OpenEditAsync(Build(P("", R(quickFox))));
        if (tracked)
        {
            await TrackAsync(page);
        }

        await ClickInAsync(page, "fox");
        await SelectFoxAsync(page, from, to);
        return page;
    }

    static async Task SelectFoxAsync(IPage page, int from, int to)
    {
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < from; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        for (var index = from; index < to; index++)
        {
            await page.Keyboard.PressAsync("Shift+ArrowRight");
        }
    }

    // Takes back what a case did, and checks the paragraph is as it was.
    static async Task UndoFoxAsync(IPage page, int steps)
    {
        for (var index = 0; index < steps; index++)
        {
            await page.Keyboard.PressAsync("Control+z");
        }

        await Assert.That(await BoxTextAsync(page)).IsEqualTo(quickFox);
    }

    [Test]
    [Property("WordBehavior", "E-50c")]
    public async Task SmartCut_AWordDeleted_TakesASpaceWithIt()
    {
        // Between two words: the space after it, the caret where the word was.
        var page = await FoxAsync(10, 15);
        await page.Keyboard.PressAsync("Delete");
        await page.Keyboard.TypeAsync("X");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("The quick Xfox jumps over the lazy dog.");
        await UndoFoxAsync(page, 2);

        // Before the full stop: the space before it.
        await SelectFoxAsync(page, 40, 43);
        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.TypeAsync("X");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("The quick brown fox jumps over the lazyX.");
        await UndoFoxAsync(page, 2);

        // At the paragraph's end: the word alone.
        await SelectFoxAsync(page, 40, 44);
        await page.Keyboard.PressAsync("Delete");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("The quick brown fox jumps over the lazy ");
        await UndoFoxAsync(page, 1);

        // Part of a word: what is selected alone.
        await SelectFoxAsync(page, 11, 15);
        await page.Keyboard.PressAsync("Delete");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("The quick b fox jumps over the lazy dog.");
    }

    // Before two spaces, or a comma, the space before the word goes: "alpha  gamma", "alpha," (E-50d).
    [Test]
    [Property("WordBehavior", "E-50d")]
    public async Task SmartCut_AWordBeforeTwoSpaces_TakesTheSpaceBeforeIt()
    {
        var page = await OpenEditAsync(Build(P("", R("alpha beta,  gamma end."))));
        await ClickInAsync(page, "gamma");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "alpha ".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        for (var index = 0; index < "beta,".Length; index++)
        {
            await page.Keyboard.PressAsync("Shift+ArrowRight");
        }

        await page.Keyboard.PressAsync("Backspace");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("alpha  gamma end.");
    }

    // Smart cut is for a selection: Backspace at a caret takes the one character, even a word of one
    // letter, which the browser hands in as a range of its own.
    [Test]
    public async Task SmartCut_BackspaceAtACaret_TakesOneCharacter()
    {
        var page = await OpenEditAsync(Build(P("", R("I saw a cat."))));
        await ClickInAsync(page, "cat");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "I saw a".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        await page.Keyboard.PressAsync("Backspace");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("I saw  cat.");
    }

    // Ctrl+Backspace after a word a space follows takes the space before it (E-70e).
    [Test]
    [Property("WordBehavior", "E-70e")]
    public async Task SmartCut_CtrlBackspaceAfterAWord_TakesTheSpaceBeforeIt()
    {
        var page = await FoxAsync(15, 15);
        await page.Keyboard.PressAsync("Control+Backspace");
        await page.Keyboard.TypeAsync("X");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("The quickX fox jumps over the lazy dog.");
    }

    // Tracked, the space is struck with the word, and the caret goes as the key leaves it (R-21).
    [Test]
    [Property("WordBehavior", "E-50c")]
    [Property("WordBehavior", "R-21")]
    public async Task SmartCut_Tracked_StrikesTheSpaceWithTheWord()
    {
        var page = await FoxAsync(10, 15, true);
        await page.Keyboard.PressAsync("Delete");
        await Assert.That(await StruckAsync(page)).IsEqualTo("brown ");
        await page.Keyboard.TypeAsync("X");
        await Assert.That(await BoxTextAsync(page)).IsEqualTo("The quick brown Xfox jumps over the lazy dog.");

        await HandInAsync(page);
        await Assert.That(Read(await SaveAsync(page))[0]).IsEqualTo("[The quick ]{-[brown ]-}{+[X]+}[fox jumps over the lazy dog.]");
    }
}
