using System.Text.RegularExpressions;
using static EditDocuments;
using static ReviewDocuments;

// What a paragraph is rather than what it says — its style, its place in a list, its indent, its
// spacing — set by DocumentEditor, asserted on the markup it leaves.
public class ParagraphFormatTests
{
    const string styles =
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>";

    static string Styles(byte[] docx) =>
        Bare(Read(docx, _ => _.MainDocumentPart!.StyleDefinitionsPart?.Styles?.OuterXml ?? ""));

    static string Numbering(byte[] docx) =>
        Bare(Read(docx, _ => _.MainDocumentPart!.NumberingDefinitionsPart?.Numbering?.OuterXml ?? ""));

    static string StyleIds(byte[] docx) =>
        string.Join(',', Read(docx, _ => _.MainDocumentPart!.StyleDefinitionsPart?.Styles?.Elements<DocumentFormat.OpenXml.Wordprocessing.Style>().Select(style => style.StyleId!.Value).ToList() ?? []));

    // Styles

    [Test]
    public async Task AStyle_IsNamed_AndWordsDefinitionAdded_WithWhatItNames()
    {
        var docx = Build(P(R("Chapter one")), styles: styles);

        var edited = DocumentEditor.SetStyle(docx, [0], "Heading1", Plain);

        await Assert.That(Xml(edited)).IsEqualTo("<w:p><w:pPr><w:pStyle w:val=\"Heading1\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Chapter one</w:t></w:r></w:p>");
        await Assert.That(StyleIds(edited)).IsEqualTo("Normal,Heading1,Heading1Char,DefaultParagraphFont");
        await Assert.That(Styles(edited)).Contains("<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\" /><w:basedOn w:val=\"Normal\" /><w:next w:val=\"Normal\" /><w:link w:val=\"Heading1Char\" /><w:uiPriority w:val=\"9\" /><w:qFormat />");
    }

    [Test]
    public async Task AStyleTheDocumentDefines_IsLeftAsTheDocumentDefinesIt()
    {
        const string own = "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:rPr><w:sz w:val=\"72\"/></w:rPr></w:style>";
        var docx = Build(P(R("Chapter one")), styles: styles + own);

        var edited = DocumentEditor.SetStyle(docx, [0], "Heading1", Plain);

        await Assert.That(Styles(edited)).IsEqualTo(Styles(docx));
    }

    // Word-probed (WordProbe E-82): Ctrl+Q takes the paragraph's own list, centring, indents and space
    // after off, and leaves its style.
    [Test]
    public async Task ResettingAParagraph_LeavesWhatItsStyleSays()
    {
        var docx = Build(P("<w:pPr><w:pStyle w:val=\"Quote\"/><w:spacing w:after=\"240\"/><w:ind w:left=\"720\"/><w:jc w:val=\"center\"/></w:pPr>", R("Centred")), styles: styles);

        var edited = DocumentEditor.ResetParagraphs(docx, [0], Plain);

        await Assert.That(Xml(edited)).IsEqualTo("<w:p><w:pPr><w:pStyle w:val=\"Quote\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Centred</w:t></w:r></w:p>");
    }

    [Test]
    public async Task ResettingAParagraph_Tracked_IsAPropertyChange()
    {
        var docx = Build(P("<w:pPr><w:jc w:val=\"center\"/></w:pPr>", R("Centred")), styles: styles);

        var edited = DocumentEditor.ResetParagraphs(docx, [0], Tracked);

        await Assert.That(Xml(edited)).Contains("<w:pPrChange ");
        await Assert.That(Xml(edited)).Contains("<w:jc w:val=\"center\" /></w:pPr></w:pPrChange>");
    }

    // A document with no styles part of its own still gets the definition, and what it is based on.
    [Test]
    public async Task ADocumentWithNoStyles_IsGivenThem()
    {
        var docx = Build(P(R("Quote me")));

        var edited = DocumentEditor.SetStyle(docx, [0], "Quote", Plain);

        await Assert.That(StyleIds(edited)).IsEqualTo("Quote,Normal,QuoteChar,DefaultParagraphFont");
    }

    // A document whose paragraphs default to a style of another name is not given a second default:
    // what Word's definition is based on is that style instead.
    [Test]
    public async Task ADefinitionBasedOnNormal_IsBasedOnTheDocumentsOwnDefault()
    {
        var docx = Build(P(R("Chapter one")), styles: "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Standard\"><w:name w:val=\"Standard\"/></w:style>");

        var edited = DocumentEditor.SetStyle(docx, [0], "Heading2", Plain);

        await Assert.That(StyleIds(edited)).IsEqualTo("Standard,Heading2,Heading2Char,DefaultParagraphFont");
        await Assert.That(Styles(edited)).Contains("<w:basedOn w:val=\"Standard\" /><w:next w:val=\"Standard\" />");
    }

    // Word-probed 2026-10-18: Heading 1 applied to a centred paragraph with space after it leaves a
    // Heading 1, neither centred nor spaced but as Heading 1 is.
    [Test]
    public async Task AStyle_ReplacesTheParagraphsOwnFormatting()
    {
        var docx = Build("<w:p><w:pPr><w:spacing w:after=\"600\"/><w:ind w:left=\"720\"/><w:jc w:val=\"center\"/></w:pPr>" + R("Chapter one") + "</w:p>", styles: styles);

        var edited = DocumentEditor.SetStyle(docx, [0], "Heading1", Plain);

        await Assert.That(Xml(edited)).StartsWith("<w:p><w:pPr><w:pStyle w:val=\"Heading1\" /></w:pPr>");
    }

    // Word-probed 2026-10-18: bold set on the whole of a paragraph goes when a heading style is
    // applied to it; bold set on one word of it stays.
    [Test]
    public async Task AStyle_TakesCharacterFormattingCoveringTheWholeParagraph_AndLeavesEmphasis()
    {
        var docx = Build(P(R("All ", bold: true), R("bold", bold: true)) + P(R("Bold", bold: true), R(" word")), styles: styles);

        var edited = DocumentEditor.SetStyle(docx, [0, 1], "Heading2", Plain);

        await Assert.That(Shape(edited)).IsEqualTo("[All ][bold] ¶ [b:Bold][ word] ¶");
    }

    [Test]
    public async Task Normal_NamesNoStyle()
    {
        var docx = Build("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>" + R("Body") + "</w:p>", styles: styles);

        var edited = DocumentEditor.SetStyle(docx, [0], "Normal", Plain);

        await Assert.That(Xml(edited)).IsEqualTo("<w:p><w:r><w:t xml:space=\"preserve\">Body</w:t></w:r></w:p>");
    }

    [Test]
    public async Task AStyle_Tracked_KeepsARecordOfWhatTheParagraphWas()
    {
        var docx = Build("<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr>" + R("Chapter one") + "</w:p>", styles: styles);

        var edited = DocumentEditor.SetStyle(docx, [0], "Title", Tracked);

        await Assert.That(Xml(edited)).StartsWith("<w:p><w:pPr><w:pStyle w:val=\"Title\" /><w:pPrChange w:author=\"Ann\" w:date=\"2026-09-27T14:30:45Z\" w:id=\"0\"><w:pPr><w:jc w:val=\"center\" /></w:pPr></w:pPrChange></w:pPr>");
        await Assert.That(DocumentReview.Read(edited).Changes.Select(_ => _.Kind)).IsEquivalentTo([ReviewChangeKind.ParagraphFormatting]);
    }

    [Test]
    public async Task AStyle_Rejected_GivesTheParagraphBack()
    {
        var docx = Build("<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr>" + R("Chapter one") + "</w:p>", styles: styles);
        var edited = DocumentEditor.SetStyle(docx, [0], "Title", Tracked);

        var rejected = ReviewEditor.ResolveAll(edited, accept: false);

        await Assert.That(Xml(rejected)).StartsWith("<w:p><w:pPr><w:jc w:val=\"center\" /></w:pPr>");
    }

    // Lists

    [Test]
    public async Task Bullets_StartAListOfWordsBullets_InListParagraph()
    {
        var docx = Build(P(R("Milk")) + P(R("Eggs")), styles: styles);

        var edited = DocumentEditor.SetList(docx, [0, 1], ListKind.Bullet, Plain);

        await Assert.That(Xml(edited)).IsEqualTo(
            "<w:p><w:pPr><w:pStyle w:val=\"ListParagraph\" /><w:numPr><w:ilvl w:val=\"0\" /><w:numId w:val=\"1\" /></w:numPr></w:pPr><w:r><w:t xml:space=\"preserve\">Milk</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"ListParagraph\" /><w:numPr><w:ilvl w:val=\"0\" /><w:numId w:val=\"1\" /></w:numPr></w:pPr><w:r><w:t xml:space=\"preserve\">Eggs</w:t></w:r></w:p>");
        var numbering = Numbering(edited);
        await Assert.That(numbering).Contains("<w:abstractNum w:abstractNumId=\"0\"><w:nsid w:val=\"4D4F0000\" /><w:multiLevelType w:val=\"hybridMultilevel\" />");
        await Assert.That(numbering).Contains("<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\" /><w:numFmt w:val=\"bullet\" /><w:lvlText w:val=\"\" /><w:lvlJc w:val=\"left\" /><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\" /></w:pPr><w:rPr><w:rFonts w:hint=\"default\" w:ascii=\"Symbol\" w:hAnsi=\"Symbol\" /></w:rPr></w:lvl>");
        await Assert.That(numbering).Contains("<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\" /><w:numFmt w:val=\"bullet\" /><w:lvlText w:val=\"o\" />");
        await Assert.That(numbering).EndsWith("<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\" /></w:num></w:numbering>");
        await Assert.That(StyleIds(edited)).Contains("ListParagraph");
    }

    [Test]
    public async Task Numbering_IsDecimal_ThenLetters_ThenRomanNumerals()
    {
        var docx = Build(P(R("First")), styles: styles);

        var edited = DocumentEditor.SetList(docx, [0], ListKind.Number, Plain);

        var numbering = Numbering(edited);
        await Assert.That(numbering).Contains("<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\" /><w:numFmt w:val=\"decimal\" /><w:lvlText w:val=\"%1.\" /><w:lvlJc w:val=\"left\" /><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\" /></w:pPr></w:lvl>");
        await Assert.That(numbering).Contains("<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\" /><w:numFmt w:val=\"lowerLetter\" /><w:lvlText w:val=\"%2.\" />");
        await Assert.That(numbering).Contains("<w:lvl w:ilvl=\"2\"><w:start w:val=\"1\" /><w:numFmt w:val=\"lowerRoman\" /><w:lvlText w:val=\"%3.\" /><w:lvlJc w:val=\"right\" /><w:pPr><w:ind w:left=\"2160\" w:hanging=\"180\" /></w:pPr></w:lvl>");
        await Assert.That(DocumentEditor.ListOf(edited, 0)).IsEqualTo(ListKind.Number);
    }

    // A paragraph put in a list just after one of the same kind carries it on, rather than starting
    // its own at 1.
    [Test]
    public async Task AParagraphAfterAList_JoinsIt()
    {
        var docx = Build(P(R("One")) + P(R("Two")), styles: styles);
        var listed = DocumentEditor.SetList(docx, [0], ListKind.Number, Plain);

        var edited = DocumentEditor.SetList(listed, [1], ListKind.Number, Plain);

        await Assert.That(Xml(edited)).Contains("<w:numId w:val=\"1\" /></w:numPr></w:pPr><w:r><w:t xml:space=\"preserve\">Two</w:t>");
        await Assert.That(Numbering(edited)).DoesNotContain("w:numId=\"2\"");
    }

    // After a list of the other kind, a list of its own.
    [Test]
    public async Task AParagraphAfterAListOfTheOtherKind_StartsItsOwn()
    {
        var docx = Build(P(R("One")) + P(R("Two")), styles: styles);
        var listed = DocumentEditor.SetList(docx, [0], ListKind.Number, Plain);

        var edited = DocumentEditor.SetList(listed, [1], ListKind.Bullet, Plain);

        await Assert.That(Xml(edited)).Contains("<w:numId w:val=\"2\" /></w:numPr></w:pPr><w:r><w:t xml:space=\"preserve\">Two</w:t>");
        await Assert.That(DocumentEditor.ListOf(edited, 0)).IsEqualTo(ListKind.Number);
        await Assert.That(DocumentEditor.ListOf(edited, 1)).IsEqualTo(ListKind.Bullet);
    }

    // Word-probed 2026-10-18: a bulleted paragraph's numbers removed, it is in Normal again.
    [Test]
    public async Task OutOfAList_IsNormalAgain()
    {
        var docx = Build(P(R("Milk")), styles: styles);
        var listed = DocumentEditor.SetList(docx, [0], ListKind.Bullet, Plain);

        var edited = DocumentEditor.SetList(listed, [0], ListKind.None, Plain);

        await Assert.That(Xml(edited)).IsEqualTo("<w:p><w:r><w:t xml:space=\"preserve\">Milk</w:t></w:r></w:p>");
    }

    // A paragraph listed by its style is taken out of the list by saying so: numbering 0.
    [Test]
    public async Task OutOfAListItsStyleMakes_SaysSo()
    {
        const string listStyle =
            "<w:style w:type=\"paragraph\" w:styleId=\"ListBullet\"><w:name w:val=\"List Bullet\"/><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style>";
        var docx = Build("<w:p><w:pPr><w:pStyle w:val=\"ListBullet\"/></w:pPr>" + R("Milk") + "</w:p>", styles: styles + listStyle);
        var numbered = DocumentEditor.SetList(Build(P(R("x")), styles: styles), [0], ListKind.Bullet, Plain);
        docx = WithNumbering(docx, Numbering(numbered));

        var edited = DocumentEditor.SetList(docx, [0], ListKind.None, Plain);

        await Assert.That(Xml(edited)).StartsWith("<w:p><w:pPr><w:pStyle w:val=\"ListBullet\" /><w:numPr><w:numId w:val=\"0\" /></w:numPr></w:pPr>");
        await Assert.That(DocumentEditor.ListOf(edited, 0)).IsEqualTo(ListKind.None);
    }

    static byte[] WithNumbering(byte[] docx, string numbering)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        stream.Position = 0;
        using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, true))
        {
            var part = package.MainDocumentPart!.AddNewPart<DocumentFormat.OpenXml.Packaging.NumberingDefinitionsPart>();
            part.Numbering = [with(numbering.Replace("<w:numbering>", "<w:numbering xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"))];
        }

        return stream.ToArray();
    }

    // A paragraph with a style of its own keeps it when it is listed.
    [Test]
    public async Task AHeadingListed_StaysAHeading()
    {
        var docx = Build("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>" + R("Chapter") + "</w:p>", styles: styles);

        var edited = DocumentEditor.SetList(docx, [0], ListKind.Number, Plain);

        await Assert.That(Xml(edited)).StartsWith("<w:p><w:pPr><w:pStyle w:val=\"Heading1\" /><w:numPr>");
    }

    // Indenting

    [Test]
    public async Task AListsParagraph_IsIndentedALevel()
    {
        var docx = DocumentEditor.SetList(Build(P(R("One")) + P(R("Two")), styles: styles), [0, 1], ListKind.Bullet, Plain);

        var deeper = DocumentEditor.Indent(docx, [1], 1, Plain);
        var back = DocumentEditor.Indent(deeper, [1], -1, Plain);
        var outermost = DocumentEditor.Indent(docx, [0], -1, Plain);

        await Assert.That(Xml(deeper)).Contains("<w:numPr><w:ilvl w:val=\"1\" /><w:numId w:val=\"1\" /></w:numPr></w:pPr><w:r><w:t xml:space=\"preserve\">Two</w:t>");
        await Assert.That(Xml(back)).IsEqualTo(Xml(docx));
        await Assert.That(Xml(outermost)).IsEqualTo(Xml(docx));
    }

    // Word's Increase Indent goes from one default tab stop to the next; probed 2026-10-18 twice on
    // a centred paragraph, which then stands 1440 twips in.
    [Test]
    public async Task AnotherParagraph_IsIndentedToTheNextTabStop()
    {
        var docx = Build(
            P(R("None")) +
            "<w:p><w:pPr><w:ind w:left=\"360\"/></w:pPr>" + R("Between") + "</w:p>" +
            "<w:p><w:pPr><w:ind w:left=\"1000\" w:hanging=\"200\"/></w:pPr>" + R("Hanging") + "</w:p>",
            styles: styles);

        var indented = DocumentEditor.Indent(docx, [0, 1, 2], 1, Plain);
        var outdented = DocumentEditor.Indent(docx, [0, 1, 2], -1, Plain);

        await Assert.That(Xml(indented)).IsEqualTo(
            "<w:p><w:pPr><w:ind w:left=\"720\" /></w:pPr><w:r><w:t xml:space=\"preserve\">None</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:ind w:left=\"720\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Between</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:ind w:left=\"1440\" w:hanging=\"200\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Hanging</w:t></w:r></w:p>");
        await Assert.That(Xml(outdented)).IsEqualTo(
            "<w:p><w:r><w:t xml:space=\"preserve\">None</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:ind w:left=\"0\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Between</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:ind w:left=\"720\" w:hanging=\"200\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Hanging</w:t></w:r></w:p>");
    }

    // An indent the style gives is where the next tab stop is counted from, and the document's own
    // default tab stop is what is counted in.
    [Test]
    public async Task TheIndent_IsCountedFromTheStyles_InTheDocumentsTabStops()
    {
        const string indentedStyle = "<w:style w:type=\"paragraph\" w:styleId=\"Indented\"><w:name w:val=\"Indented\"/><w:pPr><w:ind w:left=\"567\"/></w:pPr></w:style>";
        var docx = Build("<w:p><w:pPr><w:pStyle w:val=\"Indented\"/></w:pPr>" + R("Styled") + "</w:p>", styles: styles + indentedStyle, settings: "<w:defaultTabStop w:val=\"567\"/>");

        var edited = DocumentEditor.Indent(docx, [0], 1, Plain);

        await Assert.That(Xml(edited)).StartsWith("<w:p><w:pPr><w:pStyle w:val=\"Indented\" /><w:ind w:left=\"1134\" /></w:pPr>");
    }

    // Spacing

    [Test]
    public async Task LineSpacing_IsAMultipleOfSingle_AndSpaceIsInPoints()
    {
        var docx = Build("<w:p><w:pPr><w:spacing w:before=\"100\" w:beforeAutospacing=\"1\"/></w:pPr>" + R("Spaced") + "</w:p>", styles: styles);

        var lines = DocumentEditor.Space(docx, [0], new(Line: 1.5), Plain);
        var around = DocumentEditor.Space(docx, [0], new(BeforePoints: 12, AfterPoints: 0), Plain);

        await Assert.That(Xml(lines)).StartsWith("<w:p><w:pPr><w:spacing w:before=\"100\" w:beforeAutospacing=\"1\" w:line=\"360\" w:lineRule=\"auto\" /></w:pPr>");
        await Assert.That(Xml(around)).StartsWith("<w:p><w:pPr><w:spacing w:before=\"240\" w:after=\"0\" /></w:pPr>");
    }

    // The gallery

    [Test]
    public async Task TheGallery_OffersWordsStyles_AndTheDocumentsOwn_InWordsOrder()
    {
        const string own =
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:uiPriority w:val=\"9\"/><w:qFormat/><w:rPr><w:b/><w:sz w:val=\"36\"/><w:color w:val=\"2E74B5\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:customStyle=\"1\" w:styleId=\"Byline\"><w:name w:val=\"Byline\"/><w:uiPriority w:val=\"12\"/><w:qFormat/><w:rPr><w:i/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"TOC1\"><w:name w:val=\"toc 1\"/><w:uiPriority w:val=\"39\"/><w:semiHidden/></w:style>";
        var docx = Build(P(R("x")), styles: styles + own);

        var gallery = DocumentOutline.Read(docx).Styles;

        await Assert.That(string.Join(", ", gallery.Select(_ => $"{_.Name}{(_.Defined ? "" : "*")}"))).IsEqualTo(
            "Normal, No Spacing*, Heading 1, Heading 2*, Heading 3*, Title*, Subtitle*, Byline, Quote*, Intense Quote*, List Paragraph*");
        var heading = gallery.Single(_ => _.Id == "Heading1");
        await Assert.That((heading.SizePoints, heading.Bold, heading.ColorHex)).IsEqualTo((18d, true, "2E74B5"));
        await Assert.That(gallery.Single(_ => _.Id == "Byline").Italic).IsTrue();
    }

    // Tracked, every paragraph edit is a change to the paragraph's properties that rejecting puts back.

    [Test]
    public async Task AList_Tracked_IsAFormattingChange_ThatRejectingTakesAway()
    {
        var docx = Build(P(R("Milk")), styles: styles);

        var edited = DocumentEditor.SetList(docx, [0], ListKind.Bullet, Tracked);

        await Assert.That(Xml(edited)).Contains("<w:pPrChange w:author=\"Ann\" w:date=\"2026-09-27T14:30:45Z\" w:id=\"0\"><w:pPr /></w:pPrChange>");
        await Assert.That(DocumentReview.Read(edited).Changes.Select(_ => _.Kind)).IsEquivalentTo([ReviewChangeKind.ParagraphFormatting]);
        await Assert.That(Xml(ReviewEditor.ResolveAll(edited, accept: false))).IsEqualTo(Xml(docx));
    }

    [Test]
    public async Task AnIndent_Tracked_IsAFormattingChange_ThatRejectingTakesAway()
    {
        var docx = Build("<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr>" + R("Indented") + "</w:p>", styles: styles);

        var edited = DocumentEditor.Indent(docx, [0], 1, Tracked);

        await Assert.That(Xml(edited)).StartsWith("<w:p><w:pPr><w:ind w:left=\"720\" /><w:jc w:val=\"center\" /><w:pPrChange");
        await Assert.That(Xml(ReviewEditor.ResolveAll(edited, accept: false))).IsEqualTo(Xml(docx));
        await Assert.That(Xml(ReviewEditor.ResolveAll(edited, accept: true))).StartsWith("<w:p><w:pPr><w:ind w:left=\"720\" /><w:jc w:val=\"center\" /></w:pPr>");
    }

    [Test]
    public async Task Spacing_Tracked_IsAFormattingChange_ThatRejectingTakesAway()
    {
        var docx = Build(P(R("Spaced")), styles: styles);

        var edited = DocumentEditor.Space(docx, [0], new(Line: 2, AfterPoints: 12), Tracked);

        await Assert.That(Xml(edited)).StartsWith("<w:p><w:pPr><w:spacing w:after=\"240\" w:line=\"480\" w:lineRule=\"auto\" /><w:pPrChange");
        await Assert.That(Xml(ReviewEditor.ResolveAll(edited, accept: false))).IsEqualTo(Xml(docx));
    }

    // A second change to a paragraph already changed under tracking keeps the first record: what
    // rejecting gives back is the paragraph as it was before either.
    [Test]
    public async Task TwoChanges_Tracked_KeepOneRecordOfTheParagraphAsItWas()
    {
        var docx = Build("<w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr>" + R("Twice") + "</w:p>", styles: styles);

        var once = DocumentEditor.Indent(docx, [0], 1, Tracked);
        var twice = DocumentEditor.SetStyle(once, [0], "Quote", Tracked);

        await Assert.That(Regex.Matches(Xml(twice), "<w:pPrChange").Count).IsEqualTo(1);
        await Assert.That(Xml(ReviewEditor.ResolveAll(twice, accept: false))).IsEqualTo(Xml(docx));
    }

    // A paragraph indented by a list level of the style's list says only the level; the list is
    // still the style's.
    [Test]
    public async Task AParagraphListedByItsStyle_IsIndentedByLevelAlone()
    {
        const string listStyle =
            "<w:style w:type=\"paragraph\" w:styleId=\"ListBullet\"><w:name w:val=\"List Bullet\"/><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style>";
        var docx = Build("<w:p><w:pPr><w:pStyle w:val=\"ListBullet\"/></w:pPr>" + R("Milk") + "</w:p>", styles: styles + listStyle);

        var edited = DocumentEditor.Indent(docx, [0], 1, Plain);

        await Assert.That(Xml(edited)).StartsWith("<w:p><w:pPr><w:pStyle w:val=\"ListBullet\" /><w:numPr><w:ilvl w:val=\"1\" /></w:numPr></w:pPr>");
    }

    // A list's levels stop at nine.
    [Test]
    public async Task AListsDeepestLevel_GoesNoDeeper()
    {
        var docx = DocumentEditor.SetList(Build(P(R("Deep")), styles: styles), [0], ListKind.Number, Plain);
        var deepest = DocumentEditor.Indent(docx, [0], 20, Plain);

        var edited = DocumentEditor.Indent(deepest, [0], 1, Plain);

        await Assert.That(Xml(deepest)).Contains("<w:ilvl w:val=\"8\" />");
        await Assert.That(Xml(edited)).IsEqualTo(Xml(deepest));
    }

    // A document's numbering part keeps every definition ahead of every list, as the schema has it.
    [Test]
    public async Task ANewList_IsDefinedAheadOfTheListsAlreadyThere()
    {
        var docx = DocumentEditor.SetList(Build(P(R("One")) + P(R("Gap")) + P(R("Two")), styles: styles), [0], ListKind.Number, Plain);

        var edited = DocumentEditor.SetList(docx, [2], ListKind.Bullet, Plain);

        var numbering = Numbering(edited);
        await Assert.That(numbering.IndexOf("w:abstractNumId=\"1\"", StringComparison.Ordinal)).IsLessThan(numbering.IndexOf("<w:num ", StringComparison.Ordinal));
        await Assert.That(numbering).Contains("<w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\" /></w:num>");
        await Assert.That(numbering).Contains("<w:nsid w:val=\"4D4F9E37\" />");
    }
}
