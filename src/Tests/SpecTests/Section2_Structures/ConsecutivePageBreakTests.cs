/// <summary>
/// Word does not absorb an explicit page break that lands at the top of a fresh page: each
/// <c>w:br w:type="page"</c> starts a page whether or not anything has been drawn on the current
/// one, so consecutive break-only paragraphs produce genuinely blank pages.
///
/// Verified against Word itself with minimal fixtures of N consecutive break-only paragraphs,
/// which render N+1 pages (1 break -> 2 pages, 2 -> 3, 3 -> 4).
///
/// This exists to stop a plausible-looking "skip the break when CurrentY is still at ContentTop"
/// guard being added to the PageBreakElement case. That guard would suppress these pages. It is
/// tempting because it makes brochures/06 stop emitting a blank page under the docDefaults w:line
/// cascade — but that blank page is a symptom of cell content crossing the table's atLeast row
/// floors too readily, not of the break handling. See DocumentParser's
/// docDefaultLineSpacingMultiplier note.
/// </summary>
public class ConsecutivePageBreakTests
{
    [Test]
    [Arguments(1, 2)]
    [Arguments(2, 3)]
    [Arguments(3, 4)]
    public async Task BreakOnlyParagraphs_EachStartAPage(int breaks, int expectedPages)
    {
        var elements = new List<DocumentElement>
        {
            Paragraph("ONE")
        };
        for (var i = 0; i < breaks; i++)
        {
            elements.Add(new PageBreakElement());
        }

        elements.Add(Paragraph("LAST"));

        var document = new ParsedDocument
        {
            Elements = elements,
            PageSettings = new()
            {
                WidthPoints = 612,
                HeightPoints = 792,
                MarginTop = 72,
                MarginBottom = 72,
                MarginLeft = 72,
                MarginRight = 72
            }
        };

        using var context = new SkiaRenderContext(document.PageSettings, 96, fontDirectory: ProjectFonts.Directory);
        using var renderer = new SkiaPageRenderer(context) {CountOnly = true};

        await Assert.That(renderer.RenderDocument(document, _ => { })).IsEqualTo(expectedPages);
    }

    static ParagraphElement Paragraph(string text) =>
        new()
        {
            Runs = [new() {Text = text, Properties = new() {FontSizePoints = 11}}]
        };
}
