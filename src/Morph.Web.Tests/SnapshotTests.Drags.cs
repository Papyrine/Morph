using static EditFixtures;

// A drag selects by character, as Word's does with automatic word selection off (WordProbe M-34,
// M-34b), and a double-click held and dragged by whole words, as Word's does either way (M-34c, M-34d).
// Each drag moves in steps, as a hand does: the selection then passes through the states a drag that
// jumps straight to its end never reaches, which is where letting go once fell back to the first line.
public partial class SnapshotTests
{
    // Two words at 72pt, too wide for one line: MINUTES wraps to a line of its own.
    static readonly byte[] wrappedTitle = Build(P("", R("MEETING MINUTES", "<w:sz w:val=\"144\"/>")));

    const string wordsText = "alpha beta,  gamma-delta 3.14 end.";

    static readonly byte[] words = Build(P("", R(wordsText, "<w:sz w:val=\"48\"/>")));

    [Test]
    public async Task Drag_AcrossAWrap_KeepsTheSecondLine()
    {
        var page = await OpenEditAsync(wrappedTitle);
        await AssertWrappedAsync(page);

        // Down to the start of MINUTES, then along it and past its end, letting go off its glyphs: the
        // way a hand goes, and where the selection once fell back to the first line.
        await DragOnPagesAsync(page, "MEETING MINUTES", [2, 8, 15], flick: true);

        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo("ETING MINUTES");
    }

    [Test]
    public async Task Drag_AcrossAWrap_IsByCharacter()
    {
        var page = await OpenEditAsync(wrappedTitle);
        await AssertWrappedAsync(page);

        await DragOnPagesAsync(page, "MEETING MINUTES", [2, 11]);
        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo("ETING MIN");

        // The same the other way, in the paragraph now open.
        await DragInEditorAsync(page, 11, 2);
        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo("ETING MIN");
    }

    [Test]
    public async Task Drag_OnOneLine_IsByCharacter()
    {
        var page = await OpenEditAsync(words);

        // From beta into gamma.
        await DragOnPagesAsync(page, wordsText, [7, 15]);
        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo(wordsText[7..15]);

        // Leftwards, in the open paragraph.
        await DragInEditorAsync(page, 15, 7);
        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo(wordsText[7..15]);

        await DragInEditorAsync(page, 2, 21);
        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo(wordsText[2..21]);
    }

    [Test]
    public async Task Drag_WithinAWord_IsByCharacter()
    {
        var page = await OpenEditAsync(words);

        await DragOnPagesAsync(page, wordsText, [7, 8]);

        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo("e");
    }

    [Test]
    public async Task DoubleClickDrag_ExtendsByWords()
    {
        var page = await OpenEditAsync(words);
        await ClickInAsync(page, "beta");

        // Within beta: beta alone.
        await DragInEditorAsync(page, 7, 8, clicks: 2);
        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo("beta");

        // From beta into gamma: gamma to the hyphen.
        await DragInEditorAsync(page, 7, 15, clicks: 2);
        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo("beta,  gamma");

        // Leftwards: gamma's end to beta's start.
        await DragInEditorAsync(page, 15, 7, clicks: 2);
        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo("beta,  gamma");

        // Into delta, which takes the space after it.
        await DragInEditorAsync(page, 2, 21, clicks: 2);
        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo("alpha beta,  gamma-delta ");
    }

    // Across a wrap the moving end is by words too, unlike a plain drag.
    [Test]
    public async Task DoubleClickDrag_AcrossAWrap_ExtendsByWords()
    {
        var page = await OpenEditAsync(wrappedTitle);
        await AssertWrappedAsync(page);
        await ClickInAsync(page, "MEETING");

        await DragInEditorAsync(page, 2, 11, clicks: 2);
        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo("MEETING MINUTES");

        await DragInEditorAsync(page, 11, 2, clicks: 2);
        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo("MEETING MINUTES");
    }

    static async Task AssertWrappedAsync(IPage page)
    {
        var lines = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.viewer-page[data-page-number=\"1\"] .text-line')].map(_ => _.textContent)");
        await Assert.That(lines).IsEquivalentTo(["MEETING ", "MINUTES"]);
    }

    // Drags through offsets into a paragraph of the first page's text, each the left quarter of its
    // character (the end, past the paragraph's last one), and waits for the paragraph the drag let go
    // in to open.
    static async Task DragOnPagesAsync(IPage page, string paragraph, int[] path, bool flick = false)
    {
        var start = (await LayerTextAsync(page)).IndexOf(paragraph, StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThanOrEqualTo(0);
        await DragAsync(page, ".viewer-page[data-page-number=\"1\"] .text-layer", start, start + paragraph.Length, path, flick);
        await page.WaitForSelectorAsync(
            editor,
            new()
            {
                Timeout = 60000
            });
    }

    // The same in the paragraph open in the editor, whose text is its own. What the last drag selected
    // is let go of first: a press inside a selection drags the text rather than selecting.
    static async Task DragInEditorAsync(IPage page, int from, int to, int clicks = 1)
    {
        await page.Keyboard.PressAsync("End");
        var length = await page.EvaluateAsync<int>($"() => document.querySelector('{editor}').innerText.trimEnd().length");
        await DragAsync(page, editor, 0, length, [from, to], clicks: clicks);
    }

    // Through the path in steps, as a hand drags; with flick, its last leg in one move, as a quick
    // hand covers a line, so no character between is ever under the pointer. clicks above 1 makes the
    // press the last of a double-click.
    static async Task DragAsync(IPage page, string root, int start, int end, int[] path, bool flick = false, int clicks = 1)
    {
        var points = await page.EvaluateAsync<double[]>(
            """
            ([root, offsets, end]) => {
                const element = document.querySelector(root);
                const nodes = [];
                const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    nodes.push(node);
                }

                const box = offset => {
                    let at = 0;
                    for (const node of nodes) {
                        if (offset < at + node.data.length) {
                            const range = document.createRange();
                            range.setStart(node, offset - at);
                            range.setEnd(node, offset - at + 1);
                            return range.getBoundingClientRect();
                        }

                        at += node.data.length;
                    }

                    return null;
                };

                box(offsets[0]).width && nodes[0].parentElement.scrollIntoView({ block: 'center' });
                return offsets.flatMap(offset => {
                    if (offset >= end) {
                        const last = box(end - 1);
                        return [last.right + 20, last.top + last.height / 2];
                    }

                    const glyph = box(offset);
                    return [glyph.left + glyph.width / 4, glyph.top + glyph.height / 2];
                });
            }
            """,
            new object[] {root, path.Select(_ => start + _).ToArray(), end});
        await page.Mouse.MoveAsync((float) points[0], (float) points[1]);
        for (var click = 1; click < clicks; click++)
        {
            await page.Mouse.DownAsync(new() {ClickCount = click});
            await page.Mouse.UpAsync(new() {ClickCount = click});
        }

        await page.Mouse.DownAsync(new() {ClickCount = clicks});
        for (var index = 2; index < points.Length; index += 2)
        {
            var leap = flick && index == points.Length - 2;
            await page.Mouse.MoveAsync((float) points[index], (float) points[index + 1], new() {Steps = leap ? 1 : 20});
        }

        await page.Mouse.UpAsync(new() {ClickCount = clicks});
    }

    // What is selected once the drag has settled: the editor opens on a drag let go on the pages.
    static async Task<string> SelectedAfterDragAsync(IPage page)
    {
        await page.WaitForTimeoutAsync(300);
        return await page.EvaluateAsync<string>("() => getSelection().toString()");
    }
}
