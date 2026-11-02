using static EditFixtures;

// What another layout of the pages leaves standing, in a real Chromium: the place a paragraph left as it
// was keeps for the next command, a click on text over a picture behind it, and the match Replace acts
// on once what was typed has been handed in.
public partial class SnapshotTests
{
    const string trackedDate = "2026-01-01T00:00:00Z";

    // A paragraph open when the review pane's Accept all is pressed is handed in, and its place kept for
    // the next command in the ordinals of the file the hand-in made: once the accept has taken a
    // paragraph out, those name the paragraph after it, so a style from the ribbon styles nothing rather
    // than the wrong paragraph.
    [Test]
    public async Task Relayout_AfterAcceptAll_TheLastPlaceIsNotUsed()
    {
        var docx = Build(
            P("", R("Kept line.")) +
            P(
                $"<w:rPr><w:del w:id=\"1\" w:author=\"Ann\" w:date=\"{trackedDate}\"/></w:rPr>",
                $"<w:del w:id=\"2\" w:author=\"Ann\" w:date=\"{trackedDate}\"><w:r><w:delText>Gone line.</w:delText></w:r></w:del>") +
            P("", R("Middle line.")) +
            P("", R("Target line.")) +
            P("", R("Next line.")));
        var page = await OpenEditAsync(docx);
        await ChooseTabAsync(page, "Review");
        await page.ClickAsync(".editor-tool:has-text('Comments and changes')");
        await page.WaitForSelectorAsync(".review-change");
        await ClickInAsync(page, "Target line");

        await page.ClickAsync(".review-action:has-text('Accept all')");
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('.viewer-page[data-page-number=\"1\"] .text-layer')?.textContent.includes('Gone line')",
            null,
            new() {Timeout = 60000});
        await page.WaitForSelectorAsync(
            ".viewer-page[data-current][data-rendered-dpi] .text-layer[data-text-state=ready]",
            new() {Timeout = 60000});

        await ChooseTabAsync(page, "Home");
        await page.ClickAsync(".editor-style-button");
        await page.ClickAsync(".editor-style:text-is('Heading 1')");
        await page.WaitForTimeoutAsync(2000);

        var saved = Read(await SaveAsync(page));
        await Assert.That(string.Join(" / ", saved)).IsEqualTo("[Kept line.] / [Middle line.] / [Target line.] / [Next line.]");
    }

    // A picture behind the text is behind it for the mouse too: a click on a character over it opens
    // the paragraph, and only a click on the picture's bare part selects the picture.
    [Test]
    public async Task Relayout_BehindTextPicture_LeavesTheTextOverItToTheText()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var pictured = DocumentEditor.InsertPicture(Paragraphs, 0, 6, new(pixelPng, "image/png", 300, 150), options).Document;
        var behind = DocumentEditor.SetPictureWrap(pictured, 0, 6, PictureWrap.BehindText, options);
        var page = await OpenEditAsync(behind);

        await ClickInAsync(page, "paragraph here");
        await Assert.That(await page.Locator(".viewer-picture-frame").CountAsync()).IsEqualTo(0);
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(editor, new() {State = WaitForSelectorState.Detached});

        var box = await page.EvaluateAsync<double[]>(
            """
            () => {
                const page = document.querySelector('.viewer-page[data-page-number="1"]');
                const rect = page.getBoundingClientRect();
                return [rect.left, rect.top, rect.width / Number(getComputedStyle(page).getPropertyValue('--pw'))];
            }
            """);
        var (left, top, scale) = (box[0], box[1], box[2]);
        // Below the three lines of text, within the picture.
        await page.Mouse.ClickAsync((float) (left + (72 + 180) * scale), (float) (top + (72 + 60) * scale));
        await page.WaitForSelectorAsync(".viewer-picture-frame");
    }

    // The match chosen is the one replaced, though the paragraph open with typing in it is handed in
    // first and the pages are laid out and searched again.
    [Test]
    public async Task Relayout_ReplaceAfterAHandIn_ReplacesTheMatchChosen()
    {
        var page = await OpenEditAsync(Build(
            P("", R("cat one.")) +
            P("", R("cat two.")) +
            P("", R("cat three."))));

        await page.Locator(".viewer-pages").ClickAsync(new() {Position = new() {X = 5, Y = 5}});
        await page.Keyboard.PressAsync("Control+h");
        await page.FillAsync(".viewer-findbar input[type=search]", "cat");
        await page.WaitForSelectorAsync(".viewer-find-status:has-text('1 of 3')", new() {Timeout = 30000});
        await page.FillAsync(".viewer-replace input", "dog");
        await page.ClickAsync("[aria-label='Next match']");
        await page.WaitForSelectorAsync(".viewer-find-status:has-text('2 of 3')");

        await ClickBeforeAsync(page, "one.");
        await page.WaitForSelectorAsync(editor, new() {Timeout = 60000});
        await page.Keyboard.TypeAsync("big ");
        await page.ClickAsync(".viewer-replace .viewer-text-button:text-is('Replace')");
        await page.WaitForSelectorAsync(".viewer-find-status:has-text('of 2')", new() {Timeout = 60000});

        var saved = Read(await SaveAsync(page)).Select(_ => string.Concat(_.Split('[', ']'))).ToList();
        await Assert.That(string.Join(" / ", saved)).IsEqualTo("cat big one. / dog two. / cat three.");
    }
}
