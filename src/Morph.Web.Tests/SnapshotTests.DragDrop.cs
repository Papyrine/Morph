using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using static EditFixtures;

// Drag and drop of text, as Word's (WordProbe M-80 to M-85, C-22): selected text dragged and let go
// elsewhere is moved there, or with Ctrl held copied, spaced from its neighbours by smart cut and paste,
// and left selected. Within the open paragraph the script moves it (moveDragged in morph-edit.js); on
// the pages outside it, .NET does (OnEditDrop, DocumentEditor.Drop).
public partial class SnapshotTests
{
    // "paragraph" dragged from the first paragraph to before "and" in the third: the space after it goes
    // with it from the first, and one comes after it in the third.
    [Test]
    public async Task DragDrop_ToAnotherParagraph_MovesTheText()
    {
        var page = await OpenEditAsync(Paragraphs);
        await ClickInAsync(page, "paragraph here");
        await SelectInEditorAsync(page, "First ".Length, "paragraph".Length);

        await DragSelectionToAsync(page, "and last", copy: false);

        // Selected where it went, in the paragraph opened there; saving takes the focus, so first.
        await page.WaitForFunctionAsync(
            "() => getSelection().toString() === 'paragraph '",
            null,
            new()
            {
                Timeout = 60000
            });
        await WaitForTextsAsync(page, ["First here.", "Second bold one.", "Third paragraph and last."]);

        // Undo says what it would take back, as Word's does (WordProbe U-02g).
        await Assert.That(await page.GetAttributeAsync("button[aria-label='Undo']", "title")).IsEqualTo("Undo Move (Ctrl+Z)");
    }

    // One undo puts it back, selected where it was (WordProbe U-04f, U-07e).
    [Test]
    public async Task DragDrop_Undone_SelectsTheTextWhereItWas()
    {
        var page = await OpenEditAsync(Paragraphs);
        await ClickInAsync(page, "paragraph here");
        await SelectInEditorAsync(page, "First ".Length, "paragraph".Length);
        await DragSelectionToAsync(page, "and last", copy: false);
        await page.WaitForFunctionAsync(
            "() => getSelection().toString() === 'paragraph '",
            null,
            new()
            {
                Timeout = 60000
            });

        await page.Keyboard.PressAsync("Control+z");

        await page.WaitForFunctionAsync(
            "() => getSelection().toString() === 'paragraph'",
            null,
            new()
            {
                Timeout = 60000
            });
        await WaitForTextsAsync(page, ["First paragraph here.", "Second bold one.", "Third and last."]);
    }

    // With Ctrl held, the text is copied and stays where it was.
    [Test]
    public async Task DragDrop_WithCtrl_CopiesTheText()
    {
        var page = await OpenEditAsync(Paragraphs);
        await ClickInAsync(page, "paragraph here");
        await SelectInEditorAsync(page, "First ".Length, "paragraph".Length);

        await DragSelectionToAsync(page, "and last", copy: true);

        await WaitForTextsAsync(page, ["First paragraph here.", "Second bold one.", "Third paragraph and last."]);
    }

    // Within the open paragraph: beta moved before "end." takes the space before it away and gets one
    // after it, as Word's smart cut and paste does.
    [Test]
    public async Task DragDrop_WithinTheParagraph_IsSpacedAsWordSpacesIt()
    {
        var page = await OpenEditAsync(words);
        await ClickInAsync(page, "beta");
        await SelectInEditorAsync(page, "alpha ".Length, "beta".Length);

        await DragWithinEditorAsync(page, wordsText.IndexOf("end.", StringComparison.Ordinal));
        await Assert.That(await SelectedAfterDragAsync(page)).IsEqualTo("beta ");
        await HandInAsync(page);

        await WaitForTextsAsync(page, ["alpha,  gamma-delta 3.14 beta end."]);
    }

    // Dropped on itself, nothing moves.
    [Test]
    public async Task DragDrop_OntoItself_ChangesNothing()
    {
        var page = await OpenEditAsync(words);
        await ClickInAsync(page, "beta");
        await SelectInEditorAsync(page, "alpha ".Length, "beta".Length);

        await DragWithinEditorAsync(page, "alpha be".Length);
        await HandInAsync(page);

        await WaitForTextsAsync(page, [wordsText]);
    }

    // A picture in the line, selected in the open paragraph and dragged into another, moves there whole
    // (WordProbe M-62, I-05).
    [Test]
    public async Task DragDrop_AnInlinePicture_MovesToAnotherParagraph()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var pictured = DocumentEditor.InsertPicture(Paragraphs, 0, 6, new(pixelPng, "image/png", 40, 20), options).Document;
        var page = await OpenEditAsync(pictured);
        await ClickInAsync(page, "paragraph here");
        await page.ClickAsync($"{editor} .edit-picture");

        await DragSelectionToAsync(page, "and last", copy: false);

        await WaitForBodyAsync(
            page,
            _ => _.IndexOf("<w:drawing>", StringComparison.Ordinal) > _.IndexOf("Third", StringComparison.Ordinal) &&
                 _.Split("<w:drawing>").Length == 2);
    }

    // A floating picture dragged with Ctrl held leaves a copy where it went and stays where it was; with
    // Shift held it moves along the one axis it went furthest along (WordProbe I-15).
    [Test]
    public async Task DragDrop_AFloatingPicture_WithCtrlIsCopied_AndWithShiftKeepsToOneAxis()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var pictured = DocumentEditor.InsertPicture(Paragraphs, 0, 6, new(pixelPng, "image/png", 100, 50), options).Document;
        var floating = DocumentEditor.SetPictureWrap(pictured, 0, 6, PictureWrap.Square, options);
        var page = await OpenEditAsync(floating);
        var box = await page.EvaluateAsync<double[]>(
            """
            () => {
                const page = document.querySelector('.viewer-page[data-page-number="1"]');
                page.scrollIntoView({block: "start"});
                const rect = page.getBoundingClientRect();
                const style = getComputedStyle(page);
                return [rect.left, rect.top, rect.width / Number(style.getPropertyValue('--pw'))];
            }
            """);
        var (left, top, scale) = (box[0], box[1], box[2]);
        var x = (float) (left + 122 * scale);
        var y = (float) (top + 97 * scale);

        // Shift: 60pt right and 10pt down is 60pt right alone.
        await page.Mouse.ClickAsync(x, y);
        await page.WaitForSelectorAsync(".viewer-picture-frame");
        await page.Keyboard.DownAsync("Shift");
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + (float) (60 * scale), y + (float) (10 * scale), new() {Steps = 8});
        await page.Mouse.UpAsync();
        await page.Keyboard.UpAsync("Shift");
        await WaitForBodyAsync(page, _ => PictureOffsets(_) is [(var right, var down)] && Math.Abs(right - 60) < 1 && Math.Abs(down) < 0.1);

        // Ctrl: a copy 40pt further down, the picture where it was.
        x += (float) (60 * scale);
        await page.Mouse.ClickAsync(x, y);
        await page.WaitForSelectorAsync(".viewer-picture-frame");
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x, y + (float) (40 * scale), new() {Steps = 8});
        await page.Keyboard.DownAsync("Control");
        await page.Mouse.UpAsync();
        await page.Keyboard.UpAsync("Control");
        await WaitForBodyAsync(page, _ => PictureOffsets(_) is [(var copyRight, var copyDown), (var right, var down)] &&
                                          Math.Abs(copyRight - 60) < 1 && Math.Abs(copyDown - 40) < 1 &&
                                          Math.Abs(right - 60) < 1 && Math.Abs(down) < 0.1);
    }

    // Text dragged in from another application and dropped on the pages goes in where it was dropped,
    // spaced as a paste is, and is left selected.
    [Test]
    public async Task DragDrop_TextFromOutside_GoesInWhereItIsDropped()
    {
        var page = await OpenEditAsync(Paragraphs);

        await DropFromOutsideAsync(page, "and last", "{ 'text/plain': 'Dropped' }");

        await page.WaitForFunctionAsync(
            "() => getSelection().toString() === 'Dropped '",
            null,
            new()
            {
                Timeout = 60000
            });
        await WaitForTextsAsync(page, ["First paragraph here.", "Second bold one.", "Third Dropped and last."]);
    }

    // HTML keeps what a paste keeps of it: here, bold.
    [Test]
    public async Task DragDrop_HtmlFromOutside_KeepsItsEmphasis()
    {
        var page = await OpenEditAsync(Paragraphs);

        await DropFromOutsideAsync(page, "and last", "{ 'text/html': '<b>Strong</b>', 'text/plain': 'Strong' }");

        await WaitForBodyAsync(page, _ => Regex.IsMatch(_, "<w:b ?/>(?:(?!</w:r>).)*Strong"));
    }

    // A picture's file dropped on the pages is put in there, as Insert › Picture puts one in.
    [Test]
    public async Task DragDrop_APictureFile_IsPutInWhereItIsDropped()
    {
        var page = await OpenEditAsync(Paragraphs);

        await DropFromOutsideAsync(page, "and last", "{}", picture: true);

        await WaitForBodyAsync(
            page,
            _ => _.Split("<w:drawing>").Length == 2 &&
                 _.IndexOf("<w:drawing>", StringComparison.Ordinal) > _.IndexOf("Third", StringComparison.Ordinal));
    }

    // A picture in the line is dragged in one go, without being clicked first: to another paragraph,
    // and within its own.
    [Test]
    public async Task DragDrop_AnUnselectedPicture_IsDraggedInOneGo()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var pictured = DocumentEditor.InsertPicture(Paragraphs, 0, 6, new(pixelPng, "image/png", 40, 20), options).Document;
        var page = await OpenEditAsync(pictured);
        await ClickInAsync(page, "paragraph here");

        // Within its paragraph, to before "here".
        await DragPictureToAsync(page, editor, "here");
        await HandInAsync(page);
        await WaitForBodyAsync(
            page,
            _ => _.IndexOf("<w:drawing>", StringComparison.Ordinal) > _.IndexOf("paragraph", StringComparison.Ordinal) &&
                 _.IndexOf("<w:drawing>", StringComparison.Ordinal) < _.IndexOf("here", StringComparison.Ordinal));

        // And on to the third paragraph, from the pages' own copy of the first.
        await ClickInAsync(page, "First");
        await DragPictureToAsync(page, ".viewer-page[data-page-number=\"1\"] .text-layer", "and last");
        await WaitForBodyAsync(
            page,
            _ => _.Split("<w:drawing>").Length == 2 &&
                 _.IndexOf("<w:drawing>", StringComparison.Ordinal) > _.IndexOf("Third", StringComparison.Ordinal));
    }

    // Presses the open paragraph's picture and drags it, in steps, to the left quarter of the first
    // character of some text under root, letting go there.
    static async Task DragPictureToAsync(IPage page, string root, string target)
    {
        var points = await page.EvaluateAsync<double[]>(
            """
            ([root, target]) => {
                const picture = document.querySelector('.edit-box[contenteditable=true] .edit-picture').getBoundingClientRect();
                const element = document.querySelector(root);
                const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    const at = node.data.indexOf(target);
                    if (at >= 0) {
                        const range = document.createRange();
                        range.setStart(node, at);
                        range.setEnd(node, at + 1);
                        const box = range.getBoundingClientRect();
                        return [picture.left + picture.width / 2, picture.top + picture.height / 2, box.left + box.width / 4, box.top + box.height / 2];
                    }
                }

                return [];
            }
            """,
            new object[] {root, target});
        await DragAsync(page, points, copy: false);
    }

    // What another application's drag gives the page, dropped at the left quarter of the first
    // character of some text of the first page: the data, as a JavaScript object of type to value, or
    // a one-pixel PNG's file.
    static async Task DropFromOutsideAsync(IPage page, string target, string data, bool picture = false)
    {
        var layer = await LayerTextAsync(page);
        var offset = layer.IndexOf(target, StringComparison.Ordinal);
        await Assert.That(offset).IsGreaterThanOrEqualTo(0);
        await page.EvaluateAsync(
            $$"""
            async ([offset, picture]) => {
                const layer = document.querySelector('.viewer-page[data-page-number="1"] .text-layer');
                const walker = document.createTreeWalker(layer, NodeFilter.SHOW_TEXT);
                let start = 0;
                let point = null;
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    const next = start + node.data.length;
                    if (offset >= start && offset < next) {
                        const range = document.createRange();
                        range.setStart(node, offset - start);
                        range.setEnd(node, offset - start + 1);
                        const box = range.getBoundingClientRect();
                        point = [box.left + box.width / 4, box.top + box.height / 2];
                        break;
                    }

                    start = next;
                }

                const transfer = new DataTransfer();
                for (const [type, value] of Object.entries({{data}})) {
                    transfer.setData(type, value);
                }

                if (picture) {
                    const png = Uint8Array.from(atob('{{Convert.ToBase64String(pixelPng)}}'), _ => _.charCodeAt(0));
                    transfer.items.add(new File([png], 'pixel.png', { type: 'image/png' }));
                }

                const element = document.elementFromPoint(point[0], point[1]);
                const init = { bubbles: true, cancelable: true, clientX: point[0], clientY: point[1], dataTransfer: transfer };
                element.dispatchEvent(new DragEvent('dragenter', init));
                element.dispatchEvent(new DragEvent('dragover', init));
                element.dispatchEvent(new DragEvent('drop', init));
            }
            """,
            new object[] {offset, picture});
    }

    // WordProbe's own text-box fixture: a square-wrapped box anchored in the first paragraph, its two
    // paragraphs 1 and 2 (the mc:Choice Word reads; 3 and 4 are its VML fallback), the body's 0, 5 and 6.
    static byte[] TextBoxFixture() =>
        File.ReadAllBytes(Path.Combine(ProjectFiles.ProjectDirectory, "..", "WordProbe", "Fixtures", "text-box.docx"));

    // A click in the box opens its paragraph, and what is typed there is written into it.
    [Test]
    public async Task TextBox_ItsTextIsTypedInto()
    {
        var page = await OpenEditAsync(TextBoxFixture());
        await ClickInAsync(page, "line two");
        await SelectInEditorAsync(page, "Box ".Length, 0);
        await page.Keyboard.TypeAsync("next ");
        await HandInAsync(page);

        await WaitForOutlineAsync(page, ["Box line one", "Box next line two", "Second paragraph of text here."]);
    }

    // Text dragged from the body into the box, and back out of it (WordProbe M-87, I-26b). A word
    // dropped inside a word lands at that word's start.
    [Test]
    public async Task DragDrop_IntoATextBox_AndOutOfIt()
    {
        var page = await OpenEditAsync(TextBoxFixture());
        await ClickInAsync(page, "Second paragraph");
        await SelectInEditorAsync(page, 0, "Second".Length);

        // To "Bo|x".
        await DragSelectionToAsync(page, "x line one", copy: false);
        await WaitForOutlineAsync(page, ["Second Box line one", "Box line two", "paragraph of text here."]);

        // And "Box" back out, to "para|graph" in the first paragraph, the first on the page to read so.
        await ClickInAsync(page, "Box line one");
        await SelectInEditorAsync(page, "Second ".Length, "Box".Length);
        await DragSelectionToAsync(page, "graph of text", copy: false);
        await WaitForOutlineAsync(page, ["Second line one", "Box line two", "paragraph of text here."]);
        await Assert.That(string.Concat(DocumentOutline.Read(await SaveAsync(page)).Paragraphs[0].Units.Where(_ => _.Editable).Select(_ => _.Text))).IsEqualTo("First Box paragraph of text here.");
    }

    // The text box's two paragraphs and the body's second, as the outline reads them, saved until they
    // read as expected.
    static async Task WaitForOutlineAsync(IPage page, string[] expected)
    {
        string[] texts = [];
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var paragraphs = DocumentOutline.Read(await SaveAsync(page)).Paragraphs;
            texts = [.. new[] {1, 2, 5}.Select(_ => string.Concat(paragraphs[_].Units.Where(unit => unit.Editable).Select(unit => unit.Text)))];
            if (texts.SequenceEqual(expected))
            {
                return;
            }

            await page.WaitForTimeoutAsync(250);
        }

        await Assert.That(texts).IsEquivalentTo(expected);
    }

    // Cells selected on the pages and dragged onto a cell are pasted as cells from there (WordProbe T-33):
    // a1 over c3, b1 into a cell row 3 is given, a1 and b1 left empty.
    [Test]
    public async Task DragDrop_CellsOntoACell_ArePastedAsCells()
    {
        var docx = File.ReadAllBytes(Path.Combine(ProjectFiles.ProjectDirectory, "..", "WordProbe", "Fixtures", "table-3x3.docx"));
        var page = await OpenEditAsync(docx);
        await SelectOnPagesAsync(page, "a1", "b1");

        var points = await page.EvaluateAsync<double[]>(
            """
            () => {
                const layer = document.querySelector('.viewer-page[data-page-number="1"] .text-layer');
                const box = text => {
                    const walker = document.createTreeWalker(layer, NodeFilter.SHOW_TEXT);
                    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                        const at = node.data.indexOf(text);
                        if (at >= 0) {
                            const range = document.createRange();
                            range.setStart(node, at);
                            range.setEnd(node, at + 1);
                            return range.getBoundingClientRect();
                        }
                    }

                    return null;
                };
                [...layer.querySelectorAll('*')].find(_ => _.textContent === 'c3')?.scrollIntoView({ block: 'center' });
                const from = box('a1');
                const to = box('c3');
                return [from.left + from.width / 2, from.top + from.height / 2, to.left + to.width / 4, to.top + to.height / 2];
            }
            """);
        await DragAsync(page, points, copy: false);

        string[] texts = [];
        for (var attempt = 0; attempt < 40; attempt++)
        {
            texts = [.. DocumentOutline.Read(await SaveAsync(page)).Paragraphs.Select(_ => string.Concat(_.Units.Where(unit => unit.Editable).Select(unit => unit.Text)))];
            if (texts.Length == 12 && texts[9] == "a1")
            {
                break;
            }

            await page.WaitForTimeoutAsync(250);
        }

        await Assert.That(string.Join(" | ", texts)).IsEqualTo("Before the table. |  |  | c1 | a2 | b2 | c2 | a3 | b3 | a1 | b1 | After the table.");
        await Assert.That(await page.GetAttributeAsync("button[aria-label='Undo']", "title")).IsEqualTo("Undo Move (Ctrl+Z)");
    }

    // Each floating picture's offsets from what it stands against, in points, in the body's order.
    static List<(double Right, double Down)> PictureOffsets(string body) =>
        Regex.Matches(body, "<wp:positionH[^>]*><wp:posOffset>(-?\\d+)</wp:posOffset></wp:positionH><wp:positionV[^>]*><wp:posOffset>(-?\\d+)</wp:posOffset>")
            .Select(_ => (double.Parse(_.Groups[1].Value) / 12700, double.Parse(_.Groups[2].Value) / 12700))
            .ToList();

    // The body's paragraphs as they read, saved until they read as expected.
    static async Task WaitForTextsAsync(IPage page, string[] expected)
    {
        string[] texts = [];
        for (var attempt = 0; attempt < 40; attempt++)
        {
            using var stream = new MemoryStream(await SaveAsync(page));
            using var package = WordprocessingDocument.Open(stream, false);
            texts = [.. package.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().Select(_ => _.InnerText)];
            if (texts.SequenceEqual(expected))
            {
                return;
            }

            await page.WaitForTimeoutAsync(250);
        }

        await Assert.That(texts).IsEquivalentTo(expected);
    }

    // Drags what is selected in the open paragraph — pressed in the middle of it — to the left quarter
    // of the first character of some text on the first page, outside the paragraph, and lets go there.
    static async Task DragSelectionToAsync(IPage page, string target, bool copy)
    {
        var layer = await LayerTextAsync(page);
        var offset = layer.IndexOf(target, StringComparison.Ordinal);
        await Assert.That(offset).IsGreaterThanOrEqualTo(0);
        var points = await page.EvaluateAsync<double[]>(
            """
            offset => {
                const selected = getSelection().getRangeAt(0).getBoundingClientRect();
                const layer = document.querySelector('.viewer-page[data-page-number="1"] .text-layer');
                const walker = document.createTreeWalker(layer, NodeFilter.SHOW_TEXT);
                let start = 0;
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    const next = start + node.data.length;
                    if (offset >= start && offset < next) {
                        const range = document.createRange();
                        range.setStart(node, offset - start);
                        range.setEnd(node, offset - start + 1);
                        const box = range.getBoundingClientRect();
                        return [selected.left + selected.width / 2, selected.top + selected.height / 2, box.left + box.width / 4, box.top + box.height / 2];
                    }

                    start = next;
                }

                return [];
            }
            """,
            offset);
        await DragAsync(page, points, copy);
    }

    // Drags what is selected in the open paragraph to the left quarter of a character of its own.
    static async Task DragWithinEditorAsync(IPage page, int offset)
    {
        var points = await page.EvaluateAsync<double[]>(
            $$"""
            offset => {
                const selected = getSelection().getRangeAt(0).getBoundingClientRect();
                const element = document.querySelector('{{editor}}');
                const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                let start = 0;
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    const next = start + node.data.length;
                    if (offset >= start && offset < next) {
                        const range = document.createRange();
                        range.setStart(node, offset - start);
                        range.setEnd(node, offset - start + 1);
                        const box = range.getBoundingClientRect();
                        return [selected.left + selected.width / 2, selected.top + selected.height / 2, box.left + box.width / 4, box.top + box.height / 2];
                    }

                    start = next;
                }

                return [];
            }
            """,
            offset);
        await DragAsync(page, points, copy: false);
    }

    static async Task DragAsync(IPage page, double[] points, bool copy)
    {
        await Assert.That(points.Length).IsEqualTo(4);
        await page.Mouse.MoveAsync((float) points[0], (float) points[1]);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float) points[0] + 8, (float) points[1], new() {Steps = 4});
        await page.Mouse.MoveAsync((float) points[2], (float) points[3], new() {Steps = 20});
        if (copy)
        {
            await page.Keyboard.DownAsync("Control");
        }

        await page.Mouse.UpAsync();
        if (copy)
        {
            await page.Keyboard.UpAsync("Control");
        }
    }
}
