using static ReviewDocuments;

// A page break that ends the document makes no page (Word 16.0.20527's own page count over COM,
// 2026-11-08, compatibility modes 12, 14 and 15): only something after the break — text, a paragraph,
// another break — takes a page past it.
public class TrailingPageBreakTests
{
    const string content = "<w:p><w:r><w:t>Some content.</w:t></w:r></w:p>";
    const string pageBreak = "<w:r><w:br w:type=\"page\"/></w:r>";

    static int Pages(string body)
    {
        using var stream = new MemoryStream(Build(body));
        var document = new DocumentParser().Parse(stream);
        return new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility)
            .Layout(document.Elements, document.PageSettings, null, null, null, null, null, null, DocumentNotes.From(document))
            .Pages.Count;
    }

    [Test]
    [Arguments(content + "<w:p>" + pageBreak + "</w:p>", 1)]
    [Arguments("<w:p><w:r><w:t>Some content.</w:t></w:r>" + pageBreak + "</w:p>", 1)]
    [Arguments(content + "<w:p>" + pageBreak + "<w:r><w:t>After.</w:t></w:r></w:p>", 2)]
    [Arguments(content + "<w:p>" + pageBreak + "</w:p><w:p/>", 2)]
    [Arguments(content + "<w:p>" + pageBreak + "</w:p><w:p><w:r><w:t>After.</w:t></w:r></w:p>", 2)]
    [Arguments(content + "<w:p>" + pageBreak + pageBreak + "</w:p>", 2)]
    public async Task OnlyWhatFollowsABreak_TakesAPage(string body, int pages) =>
        await Assert.That(Pages(body)).IsEqualTo(pages);
}
