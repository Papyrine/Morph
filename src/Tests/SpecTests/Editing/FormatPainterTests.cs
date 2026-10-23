using static EditDocuments;
using static ReviewDocuments;

// Home > Format Painter, against what Word's painter did (Word-probed 2026-10-23, see
// DocumentEditor.FormatPainter.cs). The source is "Source BOLD text", centred and indented, with
// "BOLD" bold, red and 16pt: runs 0, 1 and 2. Each target paragraph is a run of its own after it.
public class FormatPainterTests
{
    static byte[] Document() =>
        Build(
            P("<w:pPr><w:ind w:left=\"720\"/><w:jc w:val=\"center\"/></w:pPr>",
                R("Source "),
                "<w:r><w:rPr><w:b/><w:color w:val=\"FF0000\"/><w:sz w:val=\"32\"/></w:rPr><w:t>BOLD</w:t></w:r>",
                R(" text")) +
            P(R("Target a here")) +
            P(R("Target b here")));

    static readonly SourcePosition caret = new(1, 1);

    // Case a: part of a paragraph takes the caret's run's formatting, and only that.
    [Test]
    public async Task PaintedOntoPartOfAParagraph_TheTextTakesTheCaretsRunsFormatting()
    {
        var copy = DocumentEditor.CopyFormat(Document(), 0, caret);

        var painted = DocumentEditor.PasteFormat(Document(), new(3, 7), new(3, 8), [], copy, Plain);

        await Assert.That(Xml(painted)).Contains(
            "<w:p><w:r><w:t xml:space=\"preserve\">Target </w:t></w:r>" +
            "<w:r><w:rPr><w:b /><w:color w:val=\"FF0000\" /><w:sz w:val=\"32\" /></w:rPr><w:t xml:space=\"preserve\">a</w:t></w:r>" +
            "<w:r><w:t xml:space=\"preserve\"> here</w:t></w:r></w:p>");
    }

    // Case b: a whole paragraph takes the source paragraph's formatting too, and its mark the run's.
    [Test]
    public async Task PaintedOntoAWholeParagraph_ItTakesTheParagraphsFormattingToo()
    {
        var copy = DocumentEditor.CopyFormat(Document(), 0, caret);

        var painted = DocumentEditor.PasteFormat(Document(), new(4, 0), new(4, 13), [2], copy, Plain);

        await Assert.That(Xml(painted)).Contains(
            "<w:p><w:pPr><w:ind w:left=\"720\" /><w:jc w:val=\"center\" /><w:rPr><w:b /><w:color w:val=\"FF0000\" /><w:sz w:val=\"32\" /></w:rPr></w:pPr>" +
            "<w:r><w:rPr><w:b /><w:color w:val=\"FF0000\" /><w:sz w:val=\"32\" /></w:rPr><w:t xml:space=\"preserve\">Target b here</w:t></w:r></w:p>");
    }

    // The painted text's own formatting goes: plain formatting painted onto bold leaves it plain.
    [Test]
    public async Task PaintingReplaces_RatherThanAdds()
    {
        var copy = DocumentEditor.CopyFormat(Document(), 0, new(0, 2));

        var painted = DocumentEditor.PasteFormat(Document(), new(1, 0), new(1, 4), [], copy, Plain);

        await Assert.That(Xml(painted)).Contains("<w:r><w:t xml:space=\"preserve\">Source </w:t></w:r><w:r><w:t>BOLD</w:t></w:r>");
    }

    [Test]
    public async Task Painting_Tracked_IsAFormattingChange()
    {
        var copy = DocumentEditor.CopyFormat(Document(), 0, caret);

        var painted = DocumentEditor.PasteFormat(Document(), new(3, 7), new(3, 8), [], copy, Tracked);

        await Assert.That(Xml(painted)).Contains("<w:rPrChange ");
        await Assert.That(Xml(ReviewEditor.ResolveAll(painted, false))).Contains("<w:r><w:t xml:space=\"preserve\">a</w:t></w:r>");
    }
}
