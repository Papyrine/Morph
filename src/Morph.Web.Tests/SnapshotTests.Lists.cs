using static EditFixtures;

// Home > Bullets' and Numbering's libraries, and Restart at 1 and Continue numbering, from the
// split buttons' menus.
public partial class SnapshotTests
{
    [Test]
    public async Task Lists_TheLibraries_RestartAndContinue()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var page = await OpenEditAsync(DocumentEditor.SetList(Paragraphs, [0, 1, 2], ListKind.Number, options));

        // The list the caret is in takes the look: every paragraph of it.
        await ClickInAsync(page, "paragraph here");
        await page.ClickAsync("[aria-label='Numbering library']");
        await page.ClickAsync(".editor-list-item[aria-label='Upper-case roman']");
        await WaitForNumberingAsync(page, _ => _.Contains("upperRoman"));
        await Assert.That(Read(await SaveAsync(page)).Select(_ => _[^4..])).IsEquivalentTo(["•1.0", "•1.0", "•1.0"]);

        await ClickInAsync(page, "paragraph here");
        await page.ClickAsync("[aria-label='Numbering library']");
        await page.WaitForSelectorAsync(".editor-list-item[aria-label='Upper-case roman'][aria-checked='true']");
        await page.ClickAsync("[aria-label='Numbering library']");

        await ClickInAsync(page, "and last");
        await page.ClickAsync("[aria-label='Numbering library']");
        await page.ClickAsync(".editor-menu-item:text-is('Restart at 1')");
        await WaitForListsAsync(page, ["•1.0", "•1.0", "•2.0"]);

        await ClickInAsync(page, "and last");
        await page.ClickAsync("[aria-label='Numbering library']");
        await page.ClickAsync(".editor-menu-item:text-is('Continue numbering')");
        await WaitForListsAsync(page, ["•1.0", "•1.0", "•1.0"]);

        // A paragraph out of the list takes a bullet of the library's.
        await ClickInAsync(page, "and last");
        await page.ClickAsync("[aria-label='Numbering']");
        await WaitForListsAsync(page, ["•1.0", "•1.0", ""]);
        await ClickInAsync(page, "and last");
        await page.ClickAsync("[aria-label='Bullet library']");
        await page.ClickAsync(".editor-list-item[aria-label='Check mark']");
        await WaitForNumberingAsync(page, _ => _.Contains(''));
    }

    // Home > Borders and Shading: on the text selected across two paragraphs, and on the caret's.
    [Test]
    public async Task Paragraph_BordersAndShading()
    {
        var page = await OpenEditAsync(Paragraphs);

        await SelectOnPagesAsync(page, "here", "Second");
        await page.ClickAsync("[aria-label='More paragraph borders']");
        await page.ClickAsync(".editor-menu-item:text-is('Outside borders')");
        await WaitForBodyAsync(page, _ => _.Split("<w:pBdr>").Length == 3);

        await ClickInAsync(page, "and last");
        await page.ClickAsync("[aria-label='More paragraph shading colours']");
        await page.ClickAsync(".editor-swatch[aria-label='Yellow']");
        await WaitForBodyAsync(page, _ => _.Contains("w:fill=\"FFFF00\""));

        await ClickInAsync(page, "paragraph here");
        await page.ClickAsync("[aria-label='More paragraph borders']");
        await page.ClickAsync(".editor-menu-item:text-is('No border')");
        await WaitForBodyAsync(page, _ => _.Split("<w:pBdr>").Length == 2);
    }

    // Format Painter picks up the bold word's formatting at the caret, and the text selected next on
    // the pages takes it; the painter is then off.
    [Test]
    public async Task FormatPainter_PaintsTheNextSelection()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "bold one");
        await page.Keyboard.PressAsync("Home");
        for (var index = 0; index < "Second bo".Length; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }

        await page.ClickAsync(".editor-tool:has-text('Format Painter')");
        await page.WaitForSelectorAsync(".viewer[data-painting=on]");
        await SelectOnPagesAsync(page, "and", "last");
        await page.Locator(".viewer-page[data-page-number='1'] .text-layer").ClickAsync(new() {Position = new() {X = 5, Y = 5}});
        await WaitForBodyAsync(page, _ => _.Split("<w:b />").Length == 3);

        await Assert.That(Read(await SaveAsync(page))[2]).IsEqualTo("[Third ][b:and last][.]");
        await Assert.That(await page.Locator(".viewer[data-painting=on]").CountAsync()).IsEqualTo(0);
    }

    static async Task WaitForListsAsync(IPage page, string[] expected)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var lists = Read(await SaveAsync(page)).Select(_ => _.Contains('•') ? _[_.LastIndexOf('•')..] : "").ToArray();
            if (lists.SequenceEqual(expected))
            {
                return;
            }

            await page.WaitForTimeoutAsync(250);
        }

        throw new($"The lists did not become {string.Join(" ", expected)}.");
    }

    static async Task WaitForNumberingAsync(IPage page, Func<string, bool> expected)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            using var stream = new MemoryStream(await SaveAsync(page));
            using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
            if (package.MainDocumentPart?.NumberingDefinitionsPart?.Numbering?.OuterXml is { } xml &&
                expected(xml))
            {
                return;
            }

            await page.WaitForTimeoutAsync(250);
        }

        throw new("The numbering did not change as expected.");
    }
}
