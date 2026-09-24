// What selecting all of each page and copying it yields, for real documents: the bundled samples, and
// corpus fixtures that each exercise one thing the text layer has to get right — justified lines (spaces
// removed from the layout), tab leaders, rotated table cells, header and footer bands, warped WordArt,
// and a worksheet's cell grid.
public class TextLayerCorpusTests
{
    public static IEnumerable<string> CorpusFiles() =>
    [
        "align_justified.docx",
        "table_of_contents.docx",
        "table_text_direction.docx",
        "header_footer.docx",
        "wordart-envelope.docx",
        "inventory-list.xlsx"
    ];

    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public Task Sample_Snapshot(InputFormat source) =>
        Verify(PageTexts(Sample.BytesFor(source), source), extension: "txt");

    [Test]
    [MethodDataSource(nameof(CorpusFiles))]
    public Task Corpus_Snapshot(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "corpus", file));
        var source = ConversionService.Detect(file)!.Format;
        return Verify(PageTexts(bytes, source), extension: "txt");
    }

    // Tabs shown as → so the snapshot reads; each page under a rule.
    static string PageTexts(byte[] bytes, InputFormat source)
    {
        using var document = PagedDocument.Open(bytes, source, Sample.FontDirectory);
        var builder = new StringBuilder();
        for (var index = 0; index < document.PageCount; index++)
        {
            builder.Append($"--- page {index + 1}\n");
            builder.Append(document.TextLayer(index).Text.Replace("\t", "→"));
        }

        return builder.ToString();
    }
}
