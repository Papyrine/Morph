using static EditFixtures;

// Going from one paragraph to another: what is typed while the paragraph left is handed in lands in the
// one going to, Ctrl+Z in a paragraph just opened undoes the edit before it, blank paper opens the nearest
// paragraph, and Up and Down reach empty paragraphs and empty cells.
public partial class SnapshotTests
{
    // Typed straight after the click on another paragraph, while the edited one is handed in: no key is
    // lost, as none is in Word.
    [Test]
    public async Task Move_KeysTypedWhileHandingIn_LandInTheParagraphClicked()
    {
        var page = await OpenEditAsync(Paragraphs);
        var offset = (await LayerTextAsync(page)).IndexOf("here", StringComparison.Ordinal);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" A");
        await ClickTextAsync(page, offset);
        await page.Keyboard.TypeAsync("XYZ");
        await WaitForEditorAsync(page, "First");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.edit-box[contenteditable=true]')?.innerText.includes('XYZ')",
            null,
            new()
            {
                Timeout = 60000
            });
        await HandInAsync(page);

        var paragraphs = Read(await SaveAsync(page));
        await Assert.That(paragraphs[0]).Contains("XYZ");
        await Assert.That(paragraphs[2]).IsEqualTo("[Third and last. A]");
    }

    // Typed through a row without waiting for each cell to open: Tab moves on, and what follows it is
    // typed in the cell it moves to, which it selects first.
    [Test]
    public async Task Move_KeysTypedAcrossCells_AreAllKept()
    {
        var page = await OpenEditAsync(TableDocument());

        await ClickInAsync(page, "Cell one");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync("1");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.TypeAsync("x");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.TypeAsync("y");
        await WaitForEditorAsync(page, "y");
        await HandInAsync(page);

        await WaitForTableAsync(page, _ => _.Text == "Cell one1|x|y/Cell four|Cell five|Cell six");
    }

    // The paragraph just opened has typed nothing to take back, so Ctrl+Z is the document's, as the Undo
    // button is: the edit handed in before is undone.
    [Test]
    public async Task Move_CtrlZ_InAParagraphJustOpened_UndoesTheEditBefore()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" More");
        await HandInAsync(page);
        await WaitForTextAsync(page, "Third and last. More");

        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.PressAsync("Control+z");
        await WaitForBodyAsync(page, _ => !_.Contains("More"));
    }

    // Blank paper below the last paragraph puts the caret at its end, as Word's click does — not at the
    // first line's end, which is the character the browser names there.
    [Test]
    public async Task Move_ClickBelowTheText_OpensTheLastParagraph()
    {
        var page = await OpenEditAsync(Paragraphs);
        var point = await page.EvaluateAsync<double[]>(
            """
            () => {
                const lines = [...document.querySelectorAll('.viewer-page[data-page-number="1"] .text-layer .text-line')];
                const last = lines[lines.length - 1].getBoundingClientRect();
                return [last.left + 20, last.bottom + 120];
            }
            """);

        await page.Mouse.ClickAsync((float) point[0], (float) point[1]);
        await WaitForEditorAsync(page, "Third");
        await page.Keyboard.TypeAsync("!");
        await HandInAsync(page);

        await Assert.That(Read(await SaveAsync(page))[2]).IsEqualTo("[Third and last.!]");
    }

    // Down from a line goes to the next line under it even when that is an empty paragraph, which draws
    // no text on the page to find.
    [Test]
    public async Task Move_ArrowDown_ReachesAnEmptyParagraph()
    {
        var page = await OpenEditAsync(Build(P("", R("First line")) + P("") + P("", R("Third line"))));

        await ClickInAsync(page, "First line");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.edit-box[contenteditable=true]')?.innerText.trim() === ''",
            null,
            new()
            {
                Timeout = 60000
            });
        await page.Keyboard.TypeAsync("Middle");
        await HandInAsync(page);

        await Assert.That(Read(await SaveAsync(page))[1]).IsEqualTo("[Middle]");
    }

    // And in a table, to the empty cell under the caret: the cell is what the column is told by, not
    // how far another cell's text runs.
    [Test]
    public async Task Move_ArrowDown_ReachesAnEmptyCell()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var docx = DocumentEditor.InsertTable(Paragraphs, 2, 15, 2, 3, options).Document;
        string[] cells = ["Cell one", "Cell two", "Cell three"];
        for (var index = 0; index < cells.Length; index++)
        {
            docx = DocumentEditor.Rewrite(docx, 3 + index, [new([new(-1, cells[index])])], options).Document;
        }

        var page = await OpenEditAsync(docx);

        await ClickInAsync(page, "Cell two");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.edit-box[contenteditable=true]')?.innerText.trim() === ''",
            null,
            new()
            {
                Timeout = 60000
            });
        await page.Keyboard.TypeAsync("Q");
        await HandInAsync(page);

        await WaitForTableAsync(page, _ => _.Text == "Cell one|Cell two|Cell three/|Q|");
    }
}
