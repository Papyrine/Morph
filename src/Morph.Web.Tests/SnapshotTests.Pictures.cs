using System.Text.RegularExpressions;
using static EditFixtures;

// A floating picture on the page: a click selects it and frames it, a corner sizes it in proportion,
// and Layout > Wrap text puts it back in the line.
public partial class SnapshotTests
{
    // A real one-pixel PNG, drawn 100pt by 50pt.
    static readonly byte[] pixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Test]
    public async Task Pictures_AFloatingOne_IsSelectedSizedAndWrapped()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var pictured = DocumentEditor.InsertPicture(Paragraphs, 0, 6, new(pixelPng, "image/png", 100, 50), options).Document;
        var floating = DocumentEditor.SetPictureWrap(pictured, 0, 6, PictureWrap.Square, options);
        var page = await OpenEditAsync(floating);

        // The picture stands at the column's left, level with its paragraph's top: the page's margins.
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
        double[] margins = [72, 72];
        await page.Mouse.ClickAsync((float) (left + (margins[0] + 50) * scale), (float) (top + (margins[1] + 25) * scale));
        await page.WaitForSelectorAsync(".viewer-picture-frame");
        await Assert.That(await page.GetAttributeAsync(".viewer-picture-frame", "data-size")).IsEqualTo("100 × 50 pt");

        var handle = await page.Locator(".viewer-picture-handle[data-corner='ne']").BoundingBoxAsync();
        await page.Mouse.MoveAsync(handle!.X + 5, handle.Y + 5);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(handle.X + 5 + (float) (100 * scale), handle.Y + 5, new() {Steps = 5});
        await page.Mouse.UpAsync();
        // Twice as wide, and as high, to within a point of the drag.
        await WaitForBodyAsync(page, _ => Extent(_) is var (width, height) &&
                                          Math.Abs(width - 200) < 1 &&
                                          Math.Abs(height - width / 2) < 0.1);

        await page.Mouse.ClickAsync((float) (left + (margins[0] + 50) * scale), (float) (top + (margins[1] + 25) * scale));
        await page.WaitForSelectorAsync(".viewer-picture-frame");
        await ChooseTabAsync(page, "Layout");
        await page.ClickAsync(".editor-tool:has-text('Wrap text')");
        await page.WaitForSelectorAsync(".editor-menu-item[aria-checked='true']:text-is('Square')");
        await page.ClickAsync(".editor-menu-item:text-is('In line with text')");
        await WaitForBodyAsync(page, _ => _.Contains("wp:inline") &&
                                          !_.Contains("wp:anchor"));
    }

    // A picture in the line that ends its paragraph, selected in the open paragraph, is wrapped from
    // Layout > Wrap text. With no text after it, it once read as nothing selected, and the command
    // answered "Select a picture first."
    [Test]
    public async Task Pictures_AnInlineOneEndingItsParagraph_IsWrapped()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var pictured = DocumentEditor.InsertPicture(Paragraphs, 2, 15, new(pixelPng, "image/png", 100, 50), options).Document;
        var page = await OpenEditAsync(pictured);

        await ClickInAsync(page, "and last");
        await page.ClickAsync($"{editor} .edit-drawing");
        await page.WaitForSelectorAsync(".edit-frame .edit-handle-se");
        await ChooseTabAsync(page, "Layout");
        await page.ClickAsync(".editor-tool:has-text('Wrap text')");
        await page.ClickAsync(".editor-menu-item:text-is('Square')");
        await WaitForBodyAsync(page, _ => _.Contains("wp:wrapSquare") &&
                                          !_.Contains("wp:inline"));
    }

    // A picture after the text it shares a line with makes the line as tall as the page drew it, and
    // the box over the paragraph is no taller: it once set its lines at the picture's height and stood
    // the picture on top of that, its paper over the paragraphs below.
    [Test]
    public async Task Pictures_OneAfterTheText_LeavesTheBoxItsLinesHeight()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var pictured = DocumentEditor.InsertPicture(Paragraphs, 0, 21, new(pixelPng, "image/png", 100, 50), options).Document;
        var page = await OpenEditAsync(pictured);

        await ClickInAsync(page, "paragraph here");
        var gap = await page.EvaluateAsync<double>(
            """
            () => {
                const box = document.querySelector('.edit-box[contenteditable=true]').getBoundingClientRect();
                const next = [...document.querySelectorAll('.text-layer span')]
                    .find(_ => _.textContent.includes('Second'))
                    .getBoundingClientRect();
                return next.top - box.bottom;
            }
            """);
        await Assert.That(gap).IsGreaterThan(-1);
    }

    // The first drawing's size in points.
    static (double Width, double Height)? Extent(string body)
    {
        var match = DrawingExtent.Match(body);
        if (!match.Success)
        {
            return null;
        }

        return (long.Parse(match.Groups[1].Value) / 12700.0, long.Parse(match.Groups[2].Value) / 12700.0);
    }

    // A drawing's wp:extent, cx and cy in EMUs.
    [GeneratedRegex("<wp:extent cx=\"(\\d+)\" cy=\"(\\d+)\"")]
    private static partial Regex DrawingExtent { get; }
}
