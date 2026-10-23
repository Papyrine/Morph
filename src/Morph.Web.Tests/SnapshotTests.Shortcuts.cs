using static EditFixtures;

// Word's keys for the Font and Paragraph groups (wordKey in morph-edit.js): the same commands as the
// ribbon's buttons, on the paragraph being typed in or on the text selected across the pages. Several
// are the browser's own keys too (Ctrl+R, Ctrl+J, Ctrl+=, Ctrl+1/2/5), so each is pressed here in a
// real Chromium, where the browser's own action would win were it not taken from it.
public partial class SnapshotTests
{
    [Test]
    public async Task Shortcut_ParagraphKeys_FormatTheOpenParagraph()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await ReopenedAsync(page, () => page.Keyboard.PressAsync("Control+Digit5"));
        await ReopenedAsync(page, () => page.Keyboard.PressAsync("Control+Shift+KeyL"));
        await page.Keyboard.PressAsync("Control+KeyR");
        await HandInAsync(page);

        var saved = await SaveAsync(page);
        await Assert.That(Read(saved)[0]).IsEqualTo("[First paragraph here.] (right) #ListParagraph •1.0");
        await Assert.That(DocumentBody(saved)).Contains("w:line=\"360\"");
    }

    [Test]
    public async Task Shortcut_ParagraphKeys_FormatTheParagraphsSelectedOnThePages()
    {
        var page = await OpenEditAsync(Paragraphs);

        await SelectFirstTwoAsync(page);
        await page.Keyboard.PressAsync("Control+KeyJ");
        await page.WaitForTimeoutAsync(300);
        await WaitForTextAsync(page, "First paragraph here.");
        var justified = Read(await SaveAsync(page));
        await Assert.That(justified[0]).IsEqualTo("[First paragraph here.] (both)");
        await Assert.That(justified[1]).IsEqualTo("[Second ][b:bold][ one.] (both)");
        await Assert.That(justified[2]).IsEqualTo("[Third and last.]");

        await SelectFirstTwoAsync(page);
        await page.Keyboard.PressAsync("Control+Digit2");
        await page.WaitForTimeoutAsync(300);
        await WaitForTextAsync(page, "First paragraph here.");
        var doubled = DocumentBody(await SaveAsync(page));
        await Assert.That(doubled.Split("w:line=\"480\"").Length - 1).IsEqualTo(2);

        await SelectFirstTwoAsync(page);
        await page.Keyboard.PressAsync("Control+Digit1");
        await page.WaitForTimeoutAsync(300);
        await WaitForTextAsync(page, "First paragraph here.");
        await Assert.That(DocumentBody(await SaveAsync(page))).DoesNotContain("w:line=\"480\"");
    }

    // From "here" in the first paragraph to "Second" in the second, read off the layer as it is now:
    // an edit lays the page out again, and the layer's text with it. The pages take the focus first,
    // as a reader's drag gives it them, so the key reaches the viewer.
    static async Task SelectFirstTwoAsync(IPage page)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var text = await LayerTextAsync(page);
            var from = text.IndexOf("here", StringComparison.Ordinal);
            var to = text.IndexOf("Second", StringComparison.Ordinal) + "Second".Length;
            await page.FocusAsync(".viewer-pages");
            await SelectAsync(page, from, to);
            await page.WaitForTimeoutAsync(300);
            if ((await page.EvaluateAsync<string>("() => getSelection().toString()")).Contains("Second"))
            {
                return;
            }
        }

        throw new("The text could not be selected.");
    }

    // Grow, subscript and superscript act on the word the caret is in, which stays selected, and
    // Ctrl+Space takes them all off again.
    [Test]
    public async Task Shortcut_FontKeys_FormatTheWordTheCaretIsIn()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "First para".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        var size = await page.GetAttributeAsync("[data-edit-show=size]", "data-value");
        await ReopenedAsync(page, () => page.Keyboard.PressAsync("Control+BracketRight"));
        await page.WaitForFunctionAsync($"() => document.querySelector('[data-edit-show=size]').dataset.value !== '{size}'");
        await ReopenedAsync(page, () => page.Keyboard.PressAsync("Control+Equal"));
        await HandInAsync(page);

        var subscript = DocumentBody(await SaveAsync(page));
        await Assert.That(subscript).Contains("<w:vertAlign w:val=\"subscript\" />");
        await Assert.That(subscript).Contains("<w:sz w:val=\"28\" />");

        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "First para".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        await ReopenedAsync(page, () => page.Keyboard.PressAsync("Control+Shift+Equal"));
        await ReopenedAsync(page, () => page.Keyboard.PressAsync("Control+BracketLeft"));
        await HandInAsync(page);

        var superscript = DocumentBody(await SaveAsync(page));
        await Assert.That(superscript).Contains("<w:vertAlign w:val=\"superscript\" />");
        await Assert.That(superscript).DoesNotContain("<w:sz w:val=\"28\" />");

        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "First para".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        await ReopenedAsync(page, () => page.Keyboard.PressAsync("Control+Space"));
        await HandInAsync(page);

        var saved = await SaveAsync(page);
        await Assert.That(DocumentBody(saved)).DoesNotContain("w:vertAlign");
        await Assert.That(string.Concat(Read(saved)[0].Split('[', ']'))).IsEqualTo("First paragraph here.");
    }
}
