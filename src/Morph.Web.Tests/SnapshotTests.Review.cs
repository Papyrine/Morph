// The viewer's review pane end to end, on the real WASM runtime in a real browser: that is where the
// comments' ranges are painted, a selection is read back as a place in the document, and an edited
// document is laid out again and swapped in under the reader.
public partial class SnapshotTests
{
    static byte[] TrackedChanges { get; } = File.ReadAllBytes(ProjectFiles.corpus.tracked_changes_docx);

    static byte[] Comments { get; } = File.ReadAllBytes(ProjectFiles.corpus.comments_docx);

    [Test]
    public async Task Review_ListsAComment_AndPaintsWhatItIsOn()
    {
        var page = await OpenReviewAsync(Comments);

        await Assert.That(await page.InnerTextAsync(".review-comment-text")).IsEqualTo("Looks good to me.");
        await Assert.That(await page.InnerTextAsync(".viewer-badge")).IsEqualTo("1");
        await Assert.That(await HighlightedAsync(page, "morph-comment")).IsEqualTo("Some text with a review note attached.");
        await Assert.That(await HighlightedAsync(page, "morph-review-current")).IsEqualTo("");

        await page.ClickAsync(".review-card");

        await Assert.That(await HighlightedAsync(page, "morph-review-current", wait: true)).IsEqualTo("Some text with a review note attached.");
        await Assert.That(await page.GetAttributeAsync(".review-card", "aria-current")).IsEqualTo("true");
    }

    // The page's pixels change with the document: the struck-through text goes, and the file that
    // downloads is the file as edited.
    [Test]
    public async Task Review_AcceptingEveryChange_RedrawsThePage_AndDownloadsTheEditedFile()
    {
        var page = await OpenReviewAsync(TrackedChanges);
        await Assert.That(await page.Locator(".review-change").CountAsync()).IsEqualTo(2);
        var before = await page.GetAttributeAsync(".viewer-page[data-current] img", "src");
        await Assert.That(await LayerTextAsync(page)).IsEqualTo("Hello inserted world removed.");

        await page.ClickAsync("text=Accept all");

        await page.WaitForSelectorAsync(
            ".review-empty",
            new()
            {
                Timeout = 60000
            });
        await page.WaitForFunctionAsync(
            "before => { const image = document.querySelector('.viewer-page[data-current][data-rendered-dpi] img'); return image && image.src !== before; }",
            before,
            new()
            {
                Timeout = 60000
            });
        await page.WaitForSelectorAsync(".viewer-page[data-current] .text-layer[data-text-state=ready]");
        await Assert.That(await LayerTextAsync(page)).IsEqualTo("Hello inserted world");
        await Assert.That(await page.Locator(".viewer-badge").CountAsync()).IsEqualTo(0);

        var saved = await DownloadAsync(page);
        await Assert.That(DocumentReview.Read(saved).Changes).IsEmpty();

        // Undo brings the changes back, to the page as well as the list.
        await page.ClickAsync(".viewer-review button[aria-label=Undo]");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.review-change').length === 2",
            null,
            new()
            {
                Timeout = 60000
            });
        await page.WaitForSelectorAsync(".viewer-page[data-current] .text-layer[data-text-state=ready]");
        await Assert.That(await LayerTextAsync(page)).IsEqualTo("Hello inserted world removed.");
    }

    [Test]
    public async Task Review_CommentsOnTheSelectedText()
    {
        var page = await OpenReviewAsync(TrackedChanges);
        await Assert.That(await page.IsDisabledAsync(".review-action-primary")).IsTrue();

        // "lo ins": from inside one run to inside the next, as a reader dragging over it would.
        await SelectAsync(page, 3, 9);
        await page.WaitForFunctionAsync("() => !document.querySelector('.review-action-primary').disabled");
        await page.ClickAsync(".review-action-primary");
        await Assert.That(await page.InnerTextAsync(".review-new .review-quote")).IsEqualTo("lo ins");
        await Assert.That(await HighlightedAsync(page, "morph-review-current", wait: true)).IsEqualTo("lo ins");

        await page.FillAsync(".review-signature-input", "Ann Lee");
        await page.FillAsync(".review-input", "Why the change?");
        await page.ClickAsync(".review-new .review-button-primary");

        await page.WaitForSelectorAsync(
            ".review-comment.review-active",
            new()
            {
                Timeout = 60000
            });
        await page.WaitForSelectorAsync(".viewer-page[data-current] .text-layer[data-text-state=ready]");
        await Assert.That(await page.InnerTextAsync(".review-comment .review-name")).IsEqualTo("Ann Lee");
        await Assert.That(await page.InnerTextAsync(".review-comment .review-quote")).IsEqualTo("lo ins");
        await Assert.That(await HighlightedAsync(page, "morph-comment", wait: true)).IsEqualTo("lo ins");
        await Assert.That(await page.Locator(".review-change").CountAsync()).IsEqualTo(2);

        var comment = DocumentReview.Read(await DownloadAsync(page)).Comments.Single();
        await Assert.That(comment.Author).IsEqualTo("Ann Lee");
        await Assert.That(comment.Text).IsEqualTo("Why the change?");
        await Assert.That(comment.Quote).IsEqualTo("lo ins");
    }

    // The app is built without time zone data, so .NET's own clock there is UTC and only the browser
    // knows the reader's. Word reads a comment's date at face value: one dated in UTC shows as made
    // hours ago, or hours from now. Kolkata keeps UTC+5:30 all year, and is neither this suite's
    // container nor a likely host.
    [Test]
    public async Task Review_DatesACommentByTheBrowsersClock()
    {
        var page = await OpenReviewAsync(TrackedChanges, "Asia/Kolkata");

        await SelectAsync(page, 3, 9);
        await page.WaitForFunctionAsync("() => !document.querySelector('.review-action-primary').disabled");
        await page.ClickAsync(".review-action-primary");
        await page.FillAsync(".review-input", "When?");
        var posted = DateTime.UtcNow.AddMinutes(330);
        await page.ClickAsync(".review-new .review-button-primary");
        await page.WaitForSelectorAsync(
            ".review-comment.review-active",
            new()
            {
                Timeout = 60000
            });

        var comment = DocumentReview.Read(await DownloadAsync(page)).Comments.Single();
        await Assert.That((comment.Date!.Value - posted).Duration()).IsLessThan(TimeSpan.FromMinutes(2));
    }

    // A click on text a change covers chooses the change; a click on plain text chooses nothing.
    [Test]
    public async Task Review_AClickOnThePage_ChoosesWhatIsThere()
    {
        var page = await OpenReviewAsync(TrackedChanges);

        await ClickTextAsync(page, 1);
        await page.WaitForTimeoutAsync(500);
        await Assert.That(await page.Locator(".review-active").CountAsync()).IsEqualTo(0);

        await ClickTextAsync(page, 24);
        await page.WaitForSelectorAsync(".review-change.review-active[data-kind=Deletion]");
        await Assert.That(await HighlightedAsync(page, "morph-review-current", wait: true)).IsEqualTo("removed.");
    }

    // Opens /view on a file, as the Open button would, and opens the review pane once the page has
    // painted and has its text.
    static async Task<IPage> OpenReviewAsync(byte[] docx, string? timeZone = null)
    {
        var page = await browser!.NewPageAsync(
            new()
            {
                TimezoneId = timeZone
            });
        await page.GotoAsync($"http://localhost:{port}/view");
        await page.WaitForSelectorAsync(
            ".viewer-open input[type=file]",
            new()
            {
                State = WaitForSelectorState.Attached,
                Timeout = 120000
            });
        await page.SetInputFilesAsync(
            ".viewer-open input[type=file]",
            new FilePayload
            {
                Name = "review.docx",
                MimeType = ConversionService.Find(InputFormat.Docx).ContentType,
                Buffer = docx
            });
        await page.WaitForSelectorAsync(
            ".viewer-page[data-current][data-rendered-dpi] .text-layer[data-text-state=ready]",
            new()
            {
                Timeout = 120000
            });
        await page.ClickAsync(".viewer-review-toggle");
        await page.WaitForSelectorAsync(".viewer-review");
        return page;
    }

    static async Task<byte[]> DownloadAsync(IPage page)
    {
        var download = await page.RunAndWaitForDownloadAsync(
            () => page.ClickAsync("button[aria-label=Download]"),
            new()
            {
                Timeout = 30000
            });
        return await File.ReadAllBytesAsync(await download.PathAsync());
    }

    // What a named highlight covers, its ranges joined. Waits for it to cover something when told to:
    // the ranges are painted a round trip after the click that asked for them.
    static async Task<string> HighlightedAsync(IPage page, string name, bool wait = false)
    {
        const string read = "name => [...(CSS.highlights.get(name) ?? [])].map(_ => _.toString()).join('|')";
        if (wait)
        {
            await page.WaitForFunctionAsync(
                $"name => ({read})(name).length > 0",
                name,
                new()
                {
                    Timeout = 30000
                });
        }

        return await page.EvaluateAsync<string>(read, name);
    }

    // The first page's text as its layer holds it, less the line break that ends it.
    static async Task<string> LayerTextAsync(IPage page) =>
        (await page.EvaluateAsync<string>(
            """
            () => {
                const layer = document.querySelector('.viewer-page[data-page-number="1"] .text-layer');
                const walker = document.createTreeWalker(layer, NodeFilter.SHOW_TEXT);
                let text = '';
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    text += node.data;
                }

                return text;
            }
            """)).TrimEnd();

    // Selects characters [start, end) of the first page's text, by walking its text nodes.
    static Task SelectAsync(IPage page, int start, int end) =>
        page.EvaluateAsync(
            """
            ([start, end]) => {
                const layer = document.querySelector('.viewer-page[data-page-number="1"] .text-layer');
                const walker = document.createTreeWalker(layer, NodeFilter.SHOW_TEXT);
                const range = document.createRange();
                let offset = 0;
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    const next = offset + node.data.length;
                    if (start >= offset && start < next) {
                        range.setStart(node, start - offset);
                    }

                    if (end > offset && end <= next) {
                        range.setEnd(node, end - offset);
                    }

                    offset = next;
                }

                const selection = getSelection();
                selection.removeAllRanges();
                selection.addRange(range);
            }
            """,
            new[] { start, end });

    // Clicks the middle of one character of the first page's text.
    static async Task ClickTextAsync(IPage page, int offset)
    {
        var box = await page.EvaluateAsync<double[]>(
            """
            offset => {
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
                        return [box.left + box.width / 2, box.top + box.height / 2];
                    }

                    start = next;
                }

                return [];
            }
            """,
            offset);
        await page.Mouse.ClickAsync((float) box[0], (float) box[1]);
    }
}
