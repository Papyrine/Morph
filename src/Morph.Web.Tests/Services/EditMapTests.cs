using static EditFixtures;

// Where a document's paragraphs are on its pages, which is what lets a click open the right one and
// an editor be laid exactly over it. The layout keeps where each line starts and how wide its text
// is; the box the text is set in is worked out from that and from what the lines stand in.
public class EditMapTests
{
    static (PagedDocument Document, DocumentOutline Outline, EditMap Map) Open(byte[] docx)
    {
        var document = PagedDocument.Open(docx, InputFormat.Docx, Sample.FontDirectory, traceSources: true);
        var outline = DocumentOutline.Read(docx);
        return (document, outline, document.Edits(outline));
    }

    [Test]
    public async Task EveryParagraph_HasABlock_TheWidthOfTheTextArea()
    {
        var (document, outline, map) = Open(Paragraphs);
        using var _ = document;
        var width = document.WidthPoints(0);

        await Assert.That(outline.Paragraphs.Count).IsEqualTo(3);
        var blocks = Enumerable.Range(0, 3).Select(index => map.Blocks(index).Single()).ToList();
        foreach (var block in blocks)
        {
            await Assert.That(block.Page).IsEqualTo(0);
            await Assert.That((double) block.Left).IsEqualTo(72).Within(0.01);
            await Assert.That((double) block.Right).IsEqualTo(width - 72).Within(0.01);
            await Assert.That(block.Lines.Count).IsEqualTo(1);
        }

        // Down the page, one after another.
        await Assert.That(blocks[0].Bottom).IsLessThanOrEqualTo(blocks[1].Top);
        await Assert.That(blocks[1].Bottom).IsLessThanOrEqualTo(blocks[2].Top);
    }

    [Test]
    public async Task At_IsTheParagraphUnderAPoint_AndNothingWhereThereIsNone()
    {
        var (document, _, map) = Open(Paragraphs);
        using var disposing = document;
        var second = map.Blocks(1).Single();

        // Blank paper beside a centred line is still the line's paragraph.
        await Assert.That(map.At(0, second.Left + 1, second.Top + 1)?.Paragraph).IsEqualTo(1);
        await Assert.That(map.At(0, second.Right - 1, second.Bottom - 1)?.Paragraph).IsEqualTo(1);
        await Assert.That(map.At(0, 10, second.Top + 1)).IsNull();
        await Assert.That(map.At(0, second.Left + 1, 5)).IsNull();
        await Assert.That(map.At(7, second.Left + 1, second.Top + 1)).IsNull();
    }

    [Test]
    public async Task AnEmptyParagraph_HasABlockToo()
    {
        var (document, _, map) = Open(Build(P("", R("above")) + "<w:p/>" + P("", R("below"))));
        using var disposing = document;

        var empty = map.Blocks(1).Single();

        await Assert.That(empty.Height).IsGreaterThan(0);
        await Assert.That(map.At(0, empty.Left + 20, empty.Top + empty.Height / 2)?.Paragraph).IsEqualTo(1);
    }

    [Test]
    public async Task AnIndentedParagraph_IsBoxedFromWhereItsLinesStart()
    {
        var text = string.Join(' ', Enumerable.Repeat("An indented paragraph long enough to wrap onto a second line and a third.", 3));
        var (document, _, map) = Open(
            Build(
                P("<w:ind w:left=\"720\" w:right=\"1440\" w:firstLine=\"360\"/>", R(text)) +
                P("<w:ind w:left=\"720\" w:hanging=\"360\"/>", R(text))));
        using var disposing = document;
        var width = document.WidthPoints(0);

        var first = map.Blocks(0).Single();
        var hanging = map.Blocks(1).Single();

        await Assert.That(first.Lines.Count).IsGreaterThan(1);
        await Assert.That((double) first.Left).IsEqualTo(72 + 36).Within(0.01);
        await Assert.That((double) first.Right).IsEqualTo(width - 72 - 72).Within(0.01);
        await Assert.That((double) first.Indent).IsEqualTo(18).Within(0.01);
        await Assert.That((double) hanging.Left).IsEqualTo(72 + 36).Within(0.01);
        await Assert.That((double) hanging.Indent).IsEqualTo(-18).Within(0.01);
    }

    [Test]
    public async Task ACellsParagraph_IsBoxedByItsCell()
    {
        const string cell = "<w:tc><w:tcPr><w:tcW w:w=\"2880\" w:type=\"dxa\"/></w:tcPr>";
        var (document, outline, map) = Open(
            Build(
                P("", R("above")) +
                "<w:tbl><w:tblPr><w:tblW w:w=\"5760\" w:type=\"dxa\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2880\"/><w:gridCol w:w=\"2880\"/></w:tblGrid><w:tr>" +
                cell + P("", R("left")) + "</w:tc>" +
                cell + P("<w:jc w:val=\"right\"/>", R("right")) + P("", R("under")) + "</w:tc>" +
                "</w:tr></w:tbl>" +
                P("", R("below"))));
        using var disposing = document;

        await Assert.That(outline.Paragraphs.Count).IsEqualTo(5);
        var left = map.Blocks(1).Single();
        var right = map.Blocks(2).Single();
        var under = map.Blocks(3).Single();

        // Two cells of two inches, side by side. The table has no style to pad its cells, and its
        // lines say so: text that starts at a cell's edge, and text that ends at one.
        await Assert.That((double) left.Left).IsEqualTo(72).Within(0.01);
        await Assert.That((double) left.Width).IsEqualTo(144).Within(0.01);
        await Assert.That((double) right.Left).IsEqualTo(72 + 144).Within(0.01);
        await Assert.That((double) right.Width).IsEqualTo(144).Within(0.01);
        await Assert.That((double) under.Left).IsEqualTo(right.Left).Within(0.01);
        await Assert.That(map.At(0, right.Left + 2, right.Top + 2)?.Paragraph).IsEqualTo(2);
        await Assert.That(map.At(0, under.Left + 2, under.Top + 2)?.Paragraph).IsEqualTo(3);

        // The two paragraphs that are not one another's neighbours cannot be joined.
        await Assert.That(outline.Paragraphs[1].HasNext).IsFalse();
        await Assert.That(outline.Paragraphs[2].HasNext).IsTrue();
        await Assert.That(outline.Paragraphs[3].HasPrevious).IsTrue();
    }

    // Whatever the document, a block's box holds its lines, a point on any of its lines is in it,
    // and the paragraph it names is one the file has.
    [Test]
    [Arguments("sample.docx")]
    [Arguments("corpus/tracked_changes.docx")]
    [Arguments("corpus/comments.docx")]
    [Arguments("corpus/align_justified.docx")]
    [Arguments("corpus/table_of_contents.docx")]
    [Arguments("corpus/table_text_direction.docx")]
    [Arguments("corpus/header_footer.docx")]
    public async Task Blocks_HoldTheirLines(string file)
    {
        var (document, outline, map) = Open(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, file)));
        using var disposing = document;

        var blocks = 0;
        for (var paragraph = 0; paragraph < outline.Paragraphs.Count; paragraph++)
        {
            foreach (var block in map.Blocks(paragraph))
            {
                blocks++;
                await Assert.That(block.Width).IsGreaterThan(0);
                await Assert.That(block.Height).IsGreaterThan(0);
                foreach (var line in block.Lines)
                {
                    await Assert.That(line.X + line.Width).IsLessThanOrEqualTo(block.Right + 0.01f);
                    await Assert.That(line.Y).IsGreaterThanOrEqualTo(block.Top - 0.01f);
                    var found = map.At(block.Page, line.X + line.Width / 2, line.Y + line.Height / 2);
                    await Assert.That(found).IsNotNull();
                }

                // What the script is given to show it can be written, whatever the paragraph holds.
                var session = new EditSession(1, outline.Paragraphs[paragraph], block, map);
                await Assert.That(session.Json(0, 0)).StartsWith("{\"v\":1,");
            }
        }

        await Assert.That(blocks).IsGreaterThan(0);
    }
}
