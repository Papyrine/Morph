using static EditFixtures;

// Insert > Page number and Date & time, and References > Table of contents and Update table, end to
// end: the table's pages come from the edited file laid out, so a heading on the second page is
// listed at 2.
public partial class SnapshotTests
{
    [Test]
    public async Task Fields_PageNumberAndDate_GoInAtTheCaret()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await ChooseTabAsync(page, "Insert");
        await page.ClickAsync(".editor-tool:has-text('Page number')");
        await page.ClickAsync(".editor-menu-item:text-is('Page number')");
        await WaitForBodyAsync(page, _ => _.Contains(" PAGE   \\* MERGEFORMAT "));

        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.PressAsync("End");
        await page.ClickAsync(".editor-tool:has-text('Date & time')");
        await page.WaitForSelectorAsync("#editor-date-title");
        await page.ClickAsync(".editor-dialog [data-format='yyyy-MM-dd']");
        await page.ClickAsync(".editor-dialog .editor-check input");
        await page.ClickAsync(".editor-dialog .editor-button-primary");
        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await WaitForBodyAsync(page, _ => _.Contains(today) && !_.Contains(" DATE "));
    }

    [Test]
    public async Task Fields_TableOfContents_ListsThePagesItsHeadingsAreOn()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var docx = Build(
            P("", R("Introduction.")) +
            P("", R("Early part.")) +
            P("", "<w:r><w:br w:type=\"page\"/></w:r>", R("Chapter one")) +
            P("", R("Body text.")) +
            P("", R("Section two")));
        docx = DocumentEditor.SetStyle(docx, [2], "Heading1", options);
        docx = DocumentEditor.SetStyle(docx, [4], "Heading2", options);
        var page = await OpenEditAsync(docx);

        await ClickInAsync(page, "Introduction");
        await page.Keyboard.PressAsync("Home");
        await ChooseTabAsync(page, "References");
        await page.ClickAsync(".editor-tool:has-text('Table of contents')");
        await WaitForBodyAsync(page, _ => _.Contains("docPartGallery"));
        await page.WaitForFunctionAsync("() => document.querySelector('.viewer-page[data-page-number=\"1\"] .text-layer')?.textContent.includes('Contents')");

        // Both headings are on the second page — the one the break starts — once the table is in.
        var saved = DocumentBody(await SaveAsync(page));
        await Assert.That(saved.Split("<w:t>2</w:t>").Length - 1).IsEqualTo(2);

        // A heading made on the first page, then Update table: it is listed, and the table is still one.
        await ClickInAsync(page, "Early part");
        await ReopenedAsync(page, () => page.Keyboard.PressAsync("Control+Alt+Digit1"));
        await HandInAsync(page);
        await page.ClickAsync(".editor-tool:has-text('Update table')");
        await WaitForBodyAsync(page, _ => _.Contains("<w:t xml:space=\"preserve\">Early part.</w:t></w:r><w:r><w:rPr><w:noProof /><w:webHidden /></w:rPr><w:tab />"));
        await Assert.That(DocumentBody(await SaveAsync(page)).Split(" TOC \\o").Length).IsEqualTo(2);
    }
}
