using static ReviewDocuments;

// What a tracked formatting change did, in Word's words: each case is the markup Word wrote for a
// change made with tracking on, and the text its revision's FormatDescription gave for it, less
// "Formatted: " (Word 16.0.20527.20150 over COM, 2026-11-07; FormatWords). Word's doubled spaces after
// a colon are written single.
public class FormatWordsTests
{
    const string revision = "w:id=\"1\" w:author=\"Ann\" w:date=\"2026-11-07T10:30:00Z\"";

    // A run of "beta" formatted from what the change records to what the run now says.
    static string? Character(string now, string was = "")
    {
        var docx = Build(P(R("Alpha "), $"<w:r><w:rPr>{now}<w:rPrChange {revision}><w:rPr>{was}</w:rPr></w:rPrChange></w:rPr><w:t>beta</w:t></w:r>"));
        return DocumentReview.Read(docx).Changes.Single().Description;
    }

    static string? Paragraph(string now, string was = "", string? styles = null, string? numbering = null)
    {
        var docx = Build(
            $"<w:p><w:pPr>{now}<w:pPrChange {revision}><w:pPr>{was}</w:pPr></w:pPrChange></w:pPr>{R("Alpha beta")}</w:p>",
            styles: styles,
            numbering: numbering);
        return DocumentReview.Read(docx).Changes.Single().Description;
    }

    [Test]
    [Arguments("<w:b/>", "", "Font: Bold")]
    [Arguments("", "<w:b/>", "Font: Not Bold")]
    [Arguments("<w:i/>", "", "Font: Italic")]
    [Arguments("<w:u w:val=\"single\"/>", "", "Underline")]
    [Arguments("<w:u w:val=\"double\"/>", "", "Double underline")]
    [Arguments("<w:u w:val=\"wavyHeavy\"/>", "", "Wave Heavy underline")]
    [Arguments("", "<w:u w:val=\"single\"/>", "No underline")]
    [Arguments("<w:strike/>", "", "Strikethrough")]
    [Arguments("<w:dstrike/>", "", "Double strikethrough")]
    [Arguments("<w:sz w:val=\"21\"/>", "", "Font: 10.5 pt")]
    [Arguments("<w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\" w:cs=\"Arial\"/>", "", "Font: (Default) Arial")]
    [Arguments("<w:highlight w:val=\"yellow\"/>", "", "Highlight")]
    [Arguments("<w:vertAlign w:val=\"superscript\"/>", "", "Superscript")]
    [Arguments("<w:smallCaps/>", "", "Small caps")]
    [Arguments("<w:caps/>", "", "All caps")]
    [Arguments("<w:vanish/>", "", "Hidden")]
    [Arguments("<w:spacing w:val=\"40\"/>", "", "Expanded by 2 pt")]
    [Arguments("<w:spacing w:val=\"-20\"/>", "", "Condensed by 1 pt")]
    [Arguments("<w:position w:val=\"-6\"/>", "", "Lowered by 3 pt")]
    [Arguments("<w:w w:val=\"150\"/>", "", "Character scale: 150%")]
    [Arguments("<w:u w:val=\"single\" w:color=\"FF0000\"/>", "", "Underline, Underline color: Red")]
    public async Task OneProperty(string now, string was, string expected) =>
        await Assert.That(Character(now, was)).IsEqualTo(expected);

    // The font's own items share one "Font:", and the rest follow in Word's order whatever order the
    // markup gives them in.
    [Test]
    public async Task SeveralProperties_ComeInWordsOrder()
    {
        await Assert.That(Character("<w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\" w:cs=\"Arial\"/><w:color w:val=\"FF0000\"/><w:sz w:val=\"28\"/>"))
            .IsEqualTo("Font: (Default) Arial, 14 pt, Font color: Red");
        await Assert.That(Character("<w:b/><w:color w:val=\"FF0000\"/><w:u w:val=\"single\"/>"))
            .IsEqualTo("Font: Bold, Underline, Font color: Red");
        await Assert.That(Character("<w:b/>", "<w:i/>"))
            .IsEqualTo("Font: Bold, Not Italic");
        await Assert.That(Character("<w:caps/><w:dstrike/><w:w w:val=\"150\"/><w:kern w:val=\"24\"/><w:position w:val=\"6\"/><w:u w:val=\"double\"/><w:vertAlign w:val=\"subscript\"/>"))
            .IsEqualTo("Double underline, Double strikethrough, Subscript, All caps, Character scale: 150%, Raised by 3 pt, Kern at 12 pt");
    }

    [Test]
    public async Task EverythingTakenOff()
    {
        var description = Character(
            "",
            "<w:b/><w:i/><w:smallCaps/><w:strike/><w:vanish/><w:color w:val=\"FF0000\"/><w:spacing w:val=\"40\"/><w:highlight w:val=\"yellow\"/><w:u w:val=\"single\"/><w:vertAlign w:val=\"superscript\"/>");

        await Assert.That(description).IsEqualTo(
            "Font: Not Bold, Not Italic, No underline, Font color: Auto, Not Strikethrough, Not Superscript/ Subscript, " +
            "Not Small caps, Not Hidden, Not Expanded by / Condensed by, Not Highlight");
    }

    // Word names the colours of its palettes, a theme's colour by its slot, and any other by its value.
    [Test]
    [Arguments("<w:color w:val=\"C00000\"/>", "Font color: Dark Red")]
    [Arguments("<w:color w:val=\"0070C0\"/>", "Font color: Blue")]
    [Arguments("<w:color w:val=\"404040\"/>", "Font color: Gray-75%")]
    [Arguments("<w:color w:val=\"996633\"/>", "Font color: Custom Color(RGB(153,102,51))")]
    [Arguments("<w:color w:val=\"156082\" w:themeColor=\"accent1\"/>", "Font color: Accent 1")]
    [Arguments("<w:color w:val=\"0E2841\" w:themeColor=\"dark2\"/>", "Font color: Text/Background 3")]
    [Arguments("<w:color w:val=\"auto\"/>", "Font color: Auto")]
    public async Task Colors(string now, string expected) =>
        await Assert.That(Character(now)).IsEqualTo(expected);

    [Test]
    public async Task ACharacterStyle_ComesFirst_ByItsName()
    {
        const string styles = "<w:style w:type=\"character\" w:styleId=\"Strong\"><w:name w:val=\"Strong\"/><w:rPr><w:b/></w:rPr></w:style>";
        var docx = Build(
            P($"<w:r><w:rPr><w:rStyle w:val=\"Strong\"/><w:i/><w:color w:val=\"FF0000\"/><w:rPrChange {revision}><w:rPr/></w:rPrChange></w:rPr><w:t>beta</w:t></w:r>"),
            styles: styles);

        await Assert.That(DocumentReview.Read(docx).Changes.Single().Description).IsEqualTo("Strong, Font: Italic, Font color: Red");
    }

    // Kerning turned off is a change Word has no words for.
    [Test]
    public async Task KerningTakenOff_SaysNothing() =>
        await Assert.That(Character("<w:kern w:val=\"0\"/>", "<w:kern w:val=\"24\"/>")).IsNull();

    // A font or a size taken off is the style's again, which Word names and the change does not.
    [Test]
    public async Task AFontTakenOff_IsLeftOut() =>
        await Assert.That(Character("", "<w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\" w:cs=\"Arial\"/><w:sz w:val=\"28\"/>")).IsNull();

    [Test]
    [Arguments("<w:jc w:val=\"center\"/>", "", "Centered")]
    [Arguments("<w:jc w:val=\"both\"/>", "", "Justified")]
    [Arguments("", "<w:jc w:val=\"center\"/>", "Left")]
    [Arguments("<w:ind w:left=\"720\"/>", "", "Indent: Left: 1.27 cm")]
    [Arguments("<w:ind w:right=\"720\"/>", "", "Right: 1.27 cm")]
    [Arguments("<w:ind w:firstLine=\"720\"/>", "", "Indent: First line: 1.27 cm")]
    [Arguments("<w:ind w:left=\"720\" w:hanging=\"720\"/>", "", "Indent: Left: 0 cm, Hanging: 1.27 cm")]
    [Arguments("<w:ind w:left=\"720\" w:hanging=\"360\"/>", "<w:ind w:left=\"720\"/>", "Indent: Hanging: 0.63 cm")]
    [Arguments("", "<w:ind w:left=\"720\"/>", "Indent: Left: 0 cm")]
    [Arguments("<w:spacing w:after=\"240\"/>", "", "Space After: 12 pt")]
    [Arguments("<w:spacing w:line=\"480\" w:lineRule=\"auto\"/>", "", "Line spacing: Double")]
    [Arguments("<w:spacing w:line=\"276\" w:lineRule=\"auto\"/>", "", "Line spacing: Multiple 1.15 li")]
    [Arguments("<w:spacing w:line=\"360\" w:lineRule=\"exact\"/>", "", "Line spacing: Exactly 18 pt")]
    [Arguments("<w:spacing w:line=\"360\" w:lineRule=\"atLeast\"/>", "", "Line spacing: At least 18 pt")]
    [Arguments("<w:keepNext/>", "", "Keep with next")]
    [Arguments("", "<w:keepNext/>", "Don't keep with next")]
    [Arguments("<w:widowControl w:val=\"0\"/>", "", "No widow/orphan control")]
    [Arguments("<w:tabs><w:tab w:val=\"right\" w:pos=\"1701\"/><w:tab w:val=\"decimal\" w:leader=\"dot\" w:pos=\"2835\"/></w:tabs>", "", "Tab stops: 3 cm, Right + 5 cm, Decimal aligned,Leader: …")]
    [Arguments("", "<w:tabs><w:tab w:val=\"left\" w:pos=\"1701\"/></w:tabs>", "Tab stops: Not at 3 cm")]
    public async Task ParagraphProperty(string now, string was, string expected) =>
        await Assert.That(Paragraph(now, was)).IsEqualTo(expected);

    [Test]
    public async Task ParagraphProperties_ComeInWordsOrder()
    {
        var description = Paragraph(
            "<w:keepNext/><w:keepLines/><w:pageBreakBefore/><w:widowControl w:val=\"0\"/><w:spacing w:before=\"120\" w:after=\"240\" w:line=\"360\" w:lineRule=\"auto\"/>" +
            "<w:ind w:left=\"720\" w:right=\"360\" w:firstLine=\"360\"/><w:jc w:val=\"center\"/>");

        await Assert.That(description).IsEqualTo(
            "Centered, Indent: Left: 1.27 cm, First line: 0.63 cm, Right: 0.63 cm, Space Before: 6 pt, After: 12 pt, " +
            "Line spacing: 1.5 lines, No widow/orphan control, Page break before, Keep with next, Keep lines together");
    }

    const string headingStyles =
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"ListParagraph\"><w:name w:val=\"List Paragraph\"/></w:style>";

    const string lists =
        "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum>" +
        "<w:abstractNum w:abstractNumId=\"1\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"upperLetter\"/><w:lvlText w:val=\"%1.\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum>" +
        "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>" +
        "<w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num>";

    // A paragraph style by the name Word shows it under, a built-in one capitalised; taken off, the
    // document's default.
    [Test]
    public async Task AParagraphStyle_ByItsName()
    {
        await Assert.That(Paragraph("<w:pStyle w:val=\"Heading1\"/><w:jc w:val=\"center\"/>", styles: headingStyles)).IsEqualTo("Heading 1, Centered");
        await Assert.That(Paragraph("", "<w:pStyle w:val=\"Heading1\"/>", styles: headingStyles)).IsEqualTo("Normal");
    }

    // A list by its definition: bullets, or numbers with where they start and how they align.
    [Test]
    public async Task AList_ByItsDefinition()
    {
        await Assert.That(Paragraph("<w:pStyle w:val=\"ListParagraph\"/><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>", styles: headingStyles, numbering: lists))
            .IsEqualTo("List Paragraph, Bulleted + Level: 1 + Aligned at: 0.63 cm + Indent at: 1.27 cm");
        await Assert.That(Paragraph("<w:pStyle w:val=\"ListParagraph\"/><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"2\"/></w:numPr>", styles: headingStyles, numbering: lists))
            .IsEqualTo("List Paragraph, Numbered + Level: 1 + Numbering Style: A, B, C, … + Start at: 1 + Alignment: Left + Aligned at: 0.63 cm + Indent at: 1.27 cm");
    }

    // In a list the paragraph hangs as the list's level does; taken out of it, its indents go with it.
    [Test]
    public async Task AListsIndents()
    {
        await Assert.That(Paragraph("<w:pStyle w:val=\"ListParagraph\"/><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"2\"/></w:numPr><w:ind w:left=\"1080\"/>", styles: headingStyles, numbering: lists))
            .IsEqualTo("List Paragraph, Indent: Left: 1.27 cm, Numbered + Level: 1 + Numbering Style: A, B, C, … + Start at: 1 + Alignment: Left + Aligned at: 0.63 cm + Indent at: 1.27 cm");
        await Assert.That(Paragraph("", "<w:pStyle w:val=\"ListParagraph\"/><w:numPr><w:numId w:val=\"1\"/></w:numPr><w:ind w:hanging=\"360\"/>", styles: headingStyles, numbering: lists))
            .IsEqualTo("Normal, No bullets or numbering");
    }

    // Only a formatting change is described.
    [Test]
    public async Task AnInsertion_HasNoDescription()
    {
        var docx = Build(P(R("Kept "), Ins("Ann", 1, R("added"))));

        await Assert.That(DocumentReview.Read(docx).Changes.Single().Description).IsNull();
    }
}
