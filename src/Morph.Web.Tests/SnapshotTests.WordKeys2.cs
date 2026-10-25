using static EditFixtures;

// More of Word's keys and mouse selections in the open paragraph, each test citing the rule it keeps true.
public partial class SnapshotTests
{
    // Tab goes to the next cell with its text selected, Shift+Tab back, Tab in the last cell adds a row;
    // Alt+Home and Alt+End go to the row's first and last cell (WordProbe T-01, T-09).
    [Test]
    [Property("WordBehavior", "R-T-1")]
    [Property("WordBehavior", "R-T-7")]
    public async Task Tab_MovesBetweenCells_AndAddsARow()
    {
        var page = await OpenEditAsync(TableDocument());

        await ClickInAsync(page, "Cell one");
        await page.Keyboard.PressAsync("Tab");
        await WaitForEditorAsync(page, "Cell two");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("Cell two");

        await page.Keyboard.PressAsync("Shift+Tab");
        await WaitForEditorAsync(page, "Cell one");

        await page.Keyboard.PressAsync("Alt+End");
        await WaitForEditorAsync(page, "Cell three");
        await page.Keyboard.PressAsync("Alt+Home");
        await WaitForEditorAsync(page, "Cell one");

        await ClickInAsync(page, "Cell six");
        await page.Keyboard.PressAsync("Tab");
        await WaitForTableAsync(page, _ => _.Rows == 3);
    }

    // Ctrl+Home and Ctrl+End go to the document's ends (WordProbe N-11).
    [Test]
    [Property("WordBehavior", "R-N-9")]
    public async Task CtrlHomeAndEnd_GoToTheDocumentsEnds()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "bold one");
        await page.Keyboard.PressAsync("Control+End");
        await WaitForEditorAsync(page, "Third");
        await page.Keyboard.PressAsync("Control+Home");
        await WaitForEditorAsync(page, "First");
    }

    // F4 and Ctrl+Y with nothing to redo repeat the last action: Bold on the next selection (WordProbe U-06).
    [Test]
    [Property("WordBehavior", "R-U-3")]
    public async Task F4_RepeatsTheLastFormatting()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await SelectInEditorAsync(page, 0, "First".Length);
        await page.Keyboard.PressAsync("Control+b");
        await SelectInEditorAsync(page, "First paragraph ".Length, "here".Length);
        await page.Keyboard.PressAsync("F4");
        await HandInAsync(page);

        await Assert.That(Read(await SaveAsync(page))[0]).IsEqualTo("[b:First][ paragraph ][b:here][.]");
    }

    // A double-click takes the word with its trailing space, a triple-click the paragraph, and a
    // Ctrl+click the sentence (WordProbe M-10, M-20, M-24).
    [Test]
    [Property("WordBehavior", "R-M-4")]
    [Property("WordBehavior", "R-M-5")]
    [Property("WordBehavior", "R-M-6")]
    public async Task MouseSelections_AreWords()
    {
        var page = await OpenEditAsync(Build(P("", R("One two. The quick fox jumps. Last one."))));

        await ClickInAsync(page, "quick");
        var word = await page.EvaluateAsync<double[]>(
            "() => { const box = document.querySelector('.edit-box'); const walker = document.createTreeWalker(box, NodeFilter.SHOW_TEXT); while (walker.nextNode()) { const at = walker.currentNode.textContent.indexOf('quick'); if (at >= 0) { const range = document.createRange(); range.setStart(walker.currentNode, at + 1); range.setEnd(walker.currentNode, at + 2); const rect = range.getBoundingClientRect(); return [rect.x + rect.width / 2, rect.y + rect.height / 2]; } } return [0, 0]; }");
        await page.Mouse.DblClickAsync((float) word[0], (float) word[1]);
        await Assert.That(await SelectedAsync(page)).IsEqualTo("quick ");

        await page.Mouse.ClickAsync((float) word[0], (float) word[1], new() { ClickCount = 3 });
        await Assert.That(await SelectedAsync(page)).IsEqualTo("One two. The quick fox jumps. Last one.");

        await page.Mouse.ClickAsync((float) word[0], (float) word[1]);
        await page.Keyboard.DownAsync("Control");
        await page.Mouse.ClickAsync((float) word[0], (float) word[1]);
        await page.Keyboard.UpAsync("Control");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("The quick fox jumps. ");
    }

    // Ctrl+Right and Ctrl+Shift+Right step by Word's word stops; Ctrl+Shift+Down selects to the
    // paragraph's end (WordProbe N-02, S-05, S-06).
    [Test]
    [Property("WordBehavior", "R-S-4")]
    public async Task CtrlArrows_StepByWords()
    {
        var page = await OpenEditAsync(Build(P("", R("alpha beta, re-run it"))));

        await ClickInAsync(page, "alpha");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Control+Shift+ArrowRight");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("alpha ");
        await page.Keyboard.PressAsync("Control+Shift+ArrowRight");
        await page.Keyboard.PressAsync("Control+Shift+ArrowRight");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("alpha beta, ");
        await page.Keyboard.PressAsync("Control+Shift+ArrowDown");
        await Assert.That(await SelectedAsync(page)).IsEqualTo("alpha beta, re-run it");
    }

    // AutoCorrect on Tab and the smiley; `1.` then Tab starts a list; AutoComplete finishes a month on
    // Enter (WordProbe A-01, A-11, A-18, A-19).
    [Test]
    [Property("WordBehavior", "R-A-5")]
    public async Task AutoComplete_FinishesAMonth_OnEnter()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" in Octo");
        await page.Keyboard.PressAsync("Enter");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and last. In October");
        await page.Keyboard.TypeAsync(" :) teh");
        await page.Keyboard.PressAsync("Tab");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and last. In October ☺ the\t");
    }

    [Test]
    [Property("WordBehavior", "R-A-3")]
    public async Task OneDotThenTab_StartsAList()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.TypeAsync("1.");
        await page.Keyboard.PressAsync("Tab");
        await WaitForBodyAsync(page, _ => _.Contains("<w:numPr>"));

        await Assert.That(Read(await SaveAsync(page))[2]).StartsWith("[Third and last.]");
    }

    // Ctrl+Backspace takes a space left before punctuation with the word (WordProbe E-70).
    [Test]
    [Property("WordBehavior", "R-E-17")]
    public async Task CtrlBackspace_TakesTheSpaceBeforePunctuation()
    {
        var page = await OpenEditAsync(Build(P("", R("alpha beta, gamma"))));

        await ClickInAsync(page, "beta");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "alpha beta".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        await page.Keyboard.PressAsync("Control+Backspace");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("alpha, gamma");
    }

    // On text selected across the pages Centre pressed again goes back to the left, as Word's does
    // (WordProbe E-89).
    [Test]
    [Property("WordBehavior", "R-E-20")]
    public async Task Centre_OnCentredParagraphs_GoesBackToTheLeft()
    {
        var page = await OpenEditAsync(Paragraphs);

        await SelectOnPagesAsync(page, "Second", "bold");
        await page.ClickAsync("[aria-label='Centre']");
        await WaitForBodyAsync(page, _ => !_.Contains("<w:jc w:val=\"center\" />"));
    }

    // Alt+Shift+Down moves the paragraph past the next one (WordProbe E-96).
    [Test]
    [Property("WordBehavior", "R-E-21")]
    public async Task AltShiftDown_MovesTheParagraph()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.PressAsync("Alt+Shift+ArrowDown");
        await WaitForBodyAsync(page, _ => _.IndexOf("Second", StringComparison.Ordinal) < _.IndexOf("First", StringComparison.Ordinal));

        var saved = Read(await SaveAsync(page));
        await Assert.That(saved[1]).IsEqualTo("[First paragraph here.]");
    }

    // Ctrl+B between two spaces waits for what is typed next, and an arrow key drops it
    // (WordProbe E-09b).
    [Test]
    [Property("WordBehavior", "R-E-3")]
    public async Task APendingLook_IsDroppedByAnArrow()
    {
        var page = await OpenEditAsync(Build(P("", R("one  two"))));

        await ClickInAsync(page, "two");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "one ".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        await page.Keyboard.PressAsync("Control+b");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.TypeAsync("y");
        await HandInAsync(page);

        await Assert.That(Read(await SaveAsync(page))[0]).IsEqualTo("[one y two]");
    }

    static Task WaitForEditorAsync(IPage page, string text) =>
        page.WaitForFunctionAsync(
            "text => document.querySelector('.edit-box[contenteditable=true]')?.innerText.startsWith(text)",
            text,
            new()
            {
                Timeout = 60000
            });
}
