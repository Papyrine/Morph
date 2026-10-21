using static EditDocuments;
using static ReviewDocuments;

// What Word's Paragraph and Font dialogs set, written as Word writes it (Word-probed 2026-10-21, see
// DocumentEditor.ParagraphFormat.cs and DocumentEditor.Set).
public class DialogFormatTests
{
    // The probe's paragraph: indents, spacing in points, exact line spacing, and its pagination.
    [Test]
    public async Task TheParagraphDialog_WritesWhatWordWrites()
    {
        var edited = DocumentEditor.FormatParagraphs(
            Build(P(R("Indented and spaced"))),
            [0],
            new(
                LeftPoints: 36,
                RightPoints: 18,
                FirstLinePoints: -18,
                BeforePoints: 6,
                AfterPoints: 12,
                LineRule: LineSpacingRule.Exactly,
                LineValue: 20,
                WidowControl: false,
                KeepNext: true,
                KeepLines: true,
                PageBreakBefore: true),
            Plain);

        await Assert.That(Xml(edited)).StartsWith(
            "<w:p><w:pPr><w:keepNext /><w:keepLines /><w:pageBreakBefore /><w:widowControl w:val=\"0\" /><w:spacing w:before=\"120\" w:after=\"240\" w:line=\"400\" w:lineRule=\"exact\" /><w:ind w:left=\"720\" w:right=\"360\" w:hanging=\"360\" /></w:pPr>");
    }

    [Test]
    public async Task Multiple_IsInSixtieths_AndAtLeastInPoints()
    {
        var docx = Build(P(R("Lines")));

        var multiple = DocumentEditor.FormatParagraphs(docx, [0], new(LineRule: LineSpacingRule.Auto, LineValue: 1.5, FirstLinePoints: 18), Plain);
        var atLeast = DocumentEditor.FormatParagraphs(docx, [0], new(LineRule: LineSpacingRule.AtLeast, LineValue: 14), Plain);

        await Assert.That(Xml(multiple)).Contains("<w:spacing w:line=\"360\" w:lineRule=\"auto\" /><w:ind w:firstLine=\"360\" />");
        await Assert.That(Xml(atLeast)).Contains("<w:spacing w:line=\"280\" w:lineRule=\"atLeast\" />");
    }

    // A switch the paragraph had of its own is taken off rather than written off; widow control,
    // on unless something says otherwise, is written only to turn it off.
    [Test]
    public async Task ASwitch_IsWrittenOnlyWhereItDiffers()
    {
        var docx = Build(P("<w:pPr><w:keepNext/></w:pPr>" + R("Kept")));

        var edited = DocumentEditor.FormatParagraphs(docx, [0], new(KeepNext: false, WidowControl: true), Plain);

        await Assert.That(Xml(edited)).StartsWith("<w:p><w:pPr /><w:r>");
    }

    [Test]
    public async Task Tracked_IsAParagraphPropertiesChange()
    {
        var edited = DocumentEditor.FormatParagraphs(Build(P(R("Changed"))), [0], new(KeepNext: true, BeforePoints: 6), Tracked);

        await Assert.That(Xml(edited)).Contains("<w:pPrChange ");
    }

    // The Font dialog's effects, each on a word of its own in the probe.
    [Test]
    [Arguments("dotted", "<w:u w:val=\"dotted\" />")]
    [Arguments("wave", "<w:u w:val=\"wave\" />")]
    [Arguments("thick", "<w:u w:val=\"thick\" />")]
    [Arguments("none", "<w:u w:val=\"none\" />")]
    public async Task AnUnderline_IsItsKind(string kind, string written)
    {
        var edited = DocumentEditor.Format(Build(P(R("Underlined"))), new(0, 0), new(0, 4), new(UnderlineStyle: kind), Plain);

        await Assert.That(Xml(edited)).Contains($"<w:rPr>{written}</w:rPr>");
    }

    // Word's dialog keeps strikethrough and double strikethrough apart, and small and all capitals.
    [Test]
    public async Task Effects_TakeTheirOppositesOff()
    {
        var docx = Build(P("<w:r><w:rPr><w:caps/><w:strike/></w:rPr><w:t>Effects</w:t></w:r>"));

        var edited = DocumentEditor.Format(docx, new(0, 0), new(0, 7), new(DoubleStrike: true, SmallCaps: true, Hidden: true), Plain);

        await Assert.That(Xml(edited)).StartsWith("<w:p><w:r><w:rPr><w:smallCaps /><w:dstrike /><w:vanish /></w:rPr>");
    }
}
