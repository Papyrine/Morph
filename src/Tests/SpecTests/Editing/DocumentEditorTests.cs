using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using static EditDocuments;
using static ReviewDocuments;

// The edits made to a document's text. Each is asserted on the document it leaves behind: its runs in
// the notation EditDocuments.Shape documents, or its text and revisions in ReviewDocuments.Describe's.
public class DocumentEditorTests
{
    const string field =
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        "<w:r><w:instrText xml:space=\"preserve\"> DATE </w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        "<w:r><w:t>1 May</w:t></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    // Typing and deleting

    [Test]
    public async Task TextTypedIntoARun_IsThatRunsText()
    {
        var docx = Build(P(R("Hello world", bold: true)));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Hello brave world")));

        await Assert.That(Shape(edited)).IsEqualTo("[b:Hello brave world] ¶");
    }

    [Test]
    public async Task TextTyped_AtEitherEndOfARun()
    {
        var docx = Build(P(R("one "), R("two", bold: true)));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "zero one "), T(1, "two three")));

        await Assert.That(Shape(edited)).IsEqualTo("[zero one ][b:two three] ¶");
    }

    [Test]
    public async Task TextDeleted_GoesFromItsRun_AndARunLeftEmptyGoesToo()
    {
        var docx = Build(P(R("Hello "), R("brave ", bold: true), R("world")));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Hello "), T(2, "wor")));

        await Assert.That(Shape(edited)).IsEqualTo("[Hello ][wor] ¶");
    }

    // The editor says which run each stretch of text is like. What was typed is formatted as the run
    // it was typed into, whichever side of a boundary the characters it replaced were on.
    [Test]
    public async Task TextReplaced_AcrossTwoRuns_IsFormattedAsTheRunItWasTypedInto()
    {
        var docx = Build(P(R("Hello "), R("world", bold: true)));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "HellX"), T(1, "orld")));

        await Assert.That(Shape(edited)).IsEqualTo("[HellX][b:orld] ¶");
    }

    [Test]
    public async Task ACharacterRetypedInAnotherRun_TakesThatRunsFormatting()
    {
        var docx = Build(P(R("abc"), R("def", bold: true)));

        // The same text, but the d is now the first run's.
        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "abcd"), T(1, "ef")));

        await Assert.That(Shape(edited)).IsEqualTo("[abcd][b:ef] ¶");
    }

    // A paragraph often ends in a space that is a run of its own. Retyped as part of the text before
    // it, it is the same space formatted the same way, and is left where it is.
    [Test]
    public async Task ACharacterRetypedInARunFormattedAlike_Stays()
    {
        var docx = Build(P("<w:sdt><w:sdtPr><w:id w:val=\"7\"/></w:sdtPr><w:sdtContent>" + R("6:00 PM") + "</w:sdtContent></w:sdt>", R(" ")));

        var edited = Rewrite(docx, 0, Tracked, Reads(T(0, "7:30 PM sharp")));

        await Assert.That(Shape(edited)).IsEqualTo("(|{-[6:00]-}{+[7:30]+}[ PM]|)[ ]{+[sharp]+} ¶");
    }

    [Test]
    public async Task NothingChanged_ChangesNothing()
    {
        var docx = Build(P(R("Hello "), R("world", bold: true)));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Hello "), T(1, "world")));

        await Assert.That(Xml(edited)).IsEqualTo(Xml(docx));
    }

    [Test]
    public async Task AnEmptyParagraph_TakesTextFormattedAsItsMarkIs()
    {
        var docx = Build("<w:p><w:pPr><w:jc w:val=\"center\"/><w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr></w:pPr></w:p>");

        var edited = Rewrite(docx, 0, Plain, Reads(T(-1, "Title")));

        await Assert.That(Shape(edited)).IsEqualTo("[b:Title] ¶");
        await Assert.That(Xml(edited)).Contains("<w:r><w:rPr><w:b /><w:sz w:val=\"32\" /></w:rPr><w:t xml:space=\"preserve\">Title</w:t></w:r>");
    }

    [Test]
    public async Task TabsAndLineBreaks_AreElements_BothWays()
    {
        var docx = Build(P("<w:r><w:t>one</w:t><w:br/><w:t>two</w:t><w:tab/><w:t>three</w:t></w:r>"));
        await Assert.That(Shape(docx)).IsEqualTo("[one\\ntwo\\tthree] ¶");

        // The break goes, and another is typed, with a tab, further on.
        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "one two\tthr\n\tee")));

        await Assert.That(Shape(edited)).IsEqualTo("[one two\\tthr\\n\\tee] ¶");
        await Assert.That(Xml(edited)).Contains("<w:t xml:space=\"preserve\">one two</w:t><w:tab /><w:t xml:space=\"preserve\">thr</w:t><w:br /><w:tab /><w:t xml:space=\"preserve\">ee</w:t>");
    }

    // Text without xml:space="preserve" is read without its edge spaces, and is edited as it is read.
    [Test]
    public async Task UnpreservedText_IsEditedAsItIsRead()
    {
        var docx = Build(P("<w:r><w:t>  Hello world  </w:t></w:r>"));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Hello, world")));

        await Assert.That(Xml(edited)).Contains("<w:t xml:space=\"preserve\">Hello, world</w:t>");
    }

    [Test]
    public async Task WhatXmlCannotHold_IsDropped()
    {
        var docx = Build(P(R("ab")));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "a\u0001\u0007b\r\nc")));

        await Assert.That(Shape(edited)).IsEqualTo("[ab\\nc] ¶");
    }

    // What the editor never showed stays where it was

    [Test]
    public async Task BookmarksAndCommentMarks_StayWhereTheyWere()
    {
        var docx = Build(
            P(
                "<w:bookmarkStart w:id=\"5\" w:name=\"here\"/>",
                R("Hello "),
                "<w:commentRangeStart w:id=\"0\"/>",
                R("world"),
                "<w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r>",
                "<w:bookmarkEnd w:id=\"5\"/>"),
            comments: "<w:comment w:id=\"0\" w:author=\"Bob\"><w:p><w:r><w:t>Note</w:t></w:r></w:p></w:comment>");

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Hello, "), T(1, "wide world")));

        await Assert.That(Describe(edited)).IsEqualTo("Hello, [0:wide world:0](0)¶");
        await Assert.That(Xml(edited)).Contains("<w:bookmarkStart w:name=\"here\" w:id=\"5\" /><w:r>");
        await Assert.That(DocumentReview.Read(edited).Comments.Single().Quote).IsEqualTo("wide world");
    }

    [Test]
    public async Task AField_APicture_ANote_StayAsTheyAre()
    {
        var docx = Build(
            P(
                R("Dated "),
                field,
                R(" by "),
                "<w:r><w:t>Ann</w:t><w:footnoteReference w:id=\"2\"/><w:t xml:space=\"preserve\"> and Bob</w:t></w:r>"));
        var units = DocumentOutline.Read(docx).Paragraphs[0].Units;
        await Assert.That(string.Join(" | ", units.Select(_ => $"{_.Kind} '{_.Text}'")))
            .IsEqualTo("Text 'Dated ' | Field '1 May' | Text ' by ' | Text 'Ann' | Note '' | Text ' and Bob'");

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Signed and dated "), K(1), T(2, ", by "), T(3, "Anne"), K(4), T(5, " alone")));

        await Assert.That(Shape(edited)).IsEqualTo("[Signed and dated ][«][][|][1 May][»][, by ][Anne￼ alone] ¶");
    }

    [Test]
    public async Task TextTyped_BetweenTwoThingsThatAreNotText()
    {
        var docx = Build(P(field, field));

        var edited = Rewrite(docx, 0, Plain, Reads(T(-1, "From "), K(0), T(-1, " to "), K(1), T(-1, ".")));

        await Assert.That(Shape(edited)).IsEqualTo("[From ][«][][|][1 May][»][ to ][«][][|][1 May][»][.] ¶");
    }

    [Test]
    public async Task WhatIsNotText_HasToStay_AndInItsOrder()
    {
        var docx = Build(P(R("Dated "), field, R(" by "), field));

        await Assert.That(() => Rewrite(docx, 0, Plain, Reads(T(0, "Dated by ")))).Throws<InvalidOperationException>();
        await Assert.That(() => Rewrite(docx, 0, Plain, Reads(T(0, "Dated "), K(3), T(2, " by "), K(1)))).Throws<InvalidOperationException>();
        await Assert.That(() => Rewrite(docx, 0, Plain, Reads(T(1, "Dated "), K(1), K(3)))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TextDeletedByATrackedChange_IsNotTextToEdit()
    {
        var docx = Build(P(R("Hello "), Del("Bob", 1, "cruel "), R("world")));
        var units = DocumentOutline.Read(docx).Paragraphs[0].Units;
        await Assert.That(string.Join(" | ", units.Select(_ => $"{_.Kind} '{_.Text}'")))
            .IsEqualTo("Text 'Hello ' | Deleted 'cruel ' | Text 'world'");

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Hello, "), K(1), T(2, "World")));

        await Assert.That(Describe(edited)).IsEqualTo("Hello, {-cruel -}World¶");
    }

    // Links and content controls

    [Test]
    public async Task TextTypedInALink_IsPartOfTheLink()
    {
        var docx = Build(P(R("See "), "<w:hyperlink w:anchor=\"top\">" + R("the top") + "</w:hyperlink>", R(".")));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "See "), T(1, "the very top"), T(2, "!")));

        await Assert.That(Shape(edited)).IsEqualTo("[See ]<[the very top]>[!] ¶");
    }

    [Test]
    public async Task TextFormattedInALink_StaysInTheLink()
    {
        var docx = Build(P("<w:hyperlink w:anchor=\"top\">" + R("the top") + "</w:hyperlink>"));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "the "), T(0, "very", new(Bold: true)), T(0, " top")));

        await Assert.That(Shape(edited)).IsEqualTo("<[the ][b:very][ top]> ¶");
    }

    // Word stops showing a control's prompt once something is typed there.
    [Test]
    public async Task TypingInAContentControlsPrompt_EndsThePrompt()
    {
        var docx = Build(
            P(
                "<w:sdt><w:sdtPr><w:id w:val=\"7\"/><w:showingPlcHdr/></w:sdtPr><w:sdtContent>" +
                "<w:r><w:rPr><w:rStyle w:val=\"PlaceholderText\"/></w:rPr><w:t>Click here</w:t></w:r>" +
                "</w:sdtContent></w:sdt>"));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Minutes")));

        await Assert.That(Shape(edited)).IsEqualTo("(|[Minutes]|) ¶");
        await Assert.That(Xml(edited)).DoesNotContain("showingPlcHdr");
        await Assert.That(Xml(edited)).DoesNotContain("PlaceholderText");
        await Assert.That(Xml(edited)).Contains("<w:id w:val=\"7\" />");
    }

    // Formatting

    [Test]
    public async Task FormattingPartOfARun_DividesIt()
    {
        var docx = Build(P(R("Hello brave world")));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Hello "), T(0, "brave", new(Bold: true, Italic: true)), T(0, " world")));

        await Assert.That(Shape(edited)).IsEqualTo("[Hello ][bi:brave][ world] ¶");
    }

    // Set outright: a style could be what makes the text bold, and leaving w:b out would leave it bold.
    [Test]
    public async Task FormattingTurnedOff_IsWrittenAsOff()
    {
        var docx = Build(P(R("Hello world", bold: true)));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Hello "), T(0, "world", new(Bold: false, Underline: false, Strike: true))));

        await Assert.That(Shape(edited)).IsEqualTo("[b:Hello ][!bs!u:world] ¶");
    }

    [Test]
    public async Task NewTextFormattedOtherwise_IsARunOfItsOwn()
    {
        var docx = Build(P(R("Hello world")));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Hello "), T(0, "brave ", new(Underline: true)), T(0, "world")));

        await Assert.That(Shape(edited)).IsEqualTo("[Hello ][u:brave ][world] ¶");
    }

    [Test]
    public async Task Format_SetsTheTextBetweenTwoPlaces_AcrossRunsAndParagraphs()
    {
        var docx = Build(P(R("Hello "), R("brave", bold: true)) + P(R("new world")));

        var edited = DocumentEditor.Format(docx, new(0, 3), new(2, 3), new(Italic: true), Plain);

        await Assert.That(Shape(edited)).IsEqualTo("[Hel][i:lo ][bi:brave] ¶ [i:new][ world] ¶");
    }

    [Test]
    public async Task Format_LeavesWhatIsNotTextAlone()
    {
        var docx = Build(P(R("Dated "), field, R(" by Ann")));

        var edited = DocumentEditor.Format(docx, new(0, 0), new(6, 3), new(Bold: true), Plain);

        await Assert.That(Shape(edited)).IsEqualTo("[b:Dated ][«][][|][1 May][»][b: by][ Ann] ¶");
    }

    [Test]
    public async Task Align_SetsTheParagraphsAlignment()
    {
        var docx = Build(P(R("one")) + "<w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr>" + R("two") + "</w:p>");

        var edited = DocumentEditor.Align(docx, [0, 1], TextAlignment.Justify, Plain);

        await Assert.That(Xml(edited)).IsEqualTo(
            "<w:p><w:pPr><w:jc w:val=\"both\" /></w:pPr><w:r><w:t xml:space=\"preserve\">one</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:jc w:val=\"both\" /></w:pPr><w:r><w:t xml:space=\"preserve\">two</w:t></w:r></w:p>");
    }

    [Test]
    public async Task ARewrittenParagraph_CanBeAlignedInTheSameEdit()
    {
        var docx = Build(P(R("Hello")));

        var edited = DocumentEditor.Rewrite(docx, 0, [new([T(0, "Hello")], TextAlignment.Center)], Plain).Document;

        await Assert.That(Xml(edited)).Contains("<w:pPr><w:jc w:val=\"center\" /></w:pPr>");
    }

    // Splitting and joining

    [Test]
    public async Task AParagraphSplit_IsTwoParagraphsFormattedAlike()
    {
        var docx = Build(
            "<w:p><w:pPr><w:pStyle w:val=\"Quote\"/><w:jc w:val=\"center\"/></w:pPr>" + R("Hello world", bold: true) + "</w:p>" +
            P(R("after")));

        var result = DocumentEditor.Rewrite(docx, 0, [Reads(T(0, "Hello ")), Reads(T(0, "world"))], Plain);

        await Assert.That(Shape(result.Document)).IsEqualTo("[b:Hello ] ¶ [b:world] ¶ [after] ¶");
        await Assert.That(Xml(result.Document).Split("<w:pPr><w:pStyle w:val=\"Quote\" /><w:jc w:val=\"center\" /></w:pPr>").Length - 1).IsEqualTo(2);
        await Assert.That(result.Paragraph).IsEqualTo(0);
    }

    [Test]
    public async Task AParagraphSplit_AtItsStart_AtItsEnd_AndTwiceOver()
    {
        var docx = Build(P(R("Hello")));

        await Assert.That(Shape(Rewrite(docx, 0, Plain, Reads(), Reads(T(0, "Hello"))))).IsEqualTo("¶ [Hello] ¶");
        await Assert.That(Shape(Rewrite(docx, 0, Plain, Reads(T(0, "Hello")), Reads()))).IsEqualTo("[Hello] ¶ ¶");
        await Assert.That(Shape(Rewrite(docx, 0, Plain, Reads(T(0, "He")), Reads(), Reads(T(0, "llo!"))))).IsEqualTo("[He] ¶ ¶ [llo!] ¶");
    }

    // The section's end hangs off the mark of its last paragraph, and the last paragraph is the one
    // that keeps the mark.
    [Test]
    public async Task ASectionsLastParagraph_Split_KeepsTheSectionsEnd()
    {
        var docx = Build("<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr></w:pPr>" + R("Hello world") + "</w:p>" + P(R("next")));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Hello ")), Reads(T(0, "world")));

        await Assert.That(Xml(edited)).StartsWith(
            "<w:p><w:r><w:t xml:space=\"preserve\">Hello </w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:sectPr>");
    }

    [Test]
    public async Task AParagraphSplit_InsideALink_DividesTheLink()
    {
        var docx = Build(P(R("See "), "<w:hyperlink w:anchor=\"top\">" + R("the top") + "</w:hyperlink>", R(".")));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "See "), T(1, "the ")), Reads(T(1, "top"), T(2, ".")));

        await Assert.That(Shape(edited)).IsEqualTo("[See ]<[the ]> ¶ <[top]>[.] ¶");
    }

    // Enter at the end of a heading starts body text: the style names what follows it.
    [Test]
    public async Task EnterAtTheEndOfAHeading_StartsTheStyleThatFollowsIt()
    {
        const string styles =
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:next w:val=\"Normal\"/></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";
        var docx = Build("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:jc w:val=\"center\"/></w:pPr>" + R("Title") + "</w:p>", styles: styles);

        var atTheEnd = Rewrite(docx, 0, Plain, Reads(T(0, "Title")), Reads());
        var inTheMiddle = Rewrite(docx, 0, Plain, Reads(T(0, "Ti")), Reads(T(0, "tle")));

        await Assert.That(Xml(atTheEnd)).EndsWith("<w:p><w:pPr><w:pStyle w:val=\"Normal\" /></w:pPr></w:p>");
        await Assert.That(Xml(inTheMiddle).Split("<w:pStyle w:val=\"Heading1\" />").Length - 1).IsEqualTo(2);
    }

    [Test]
    public async Task Join_TheFirstParagraphTakesInTheSecond()
    {
        var docx = Build(
            "<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr>" + R("first ") + "</w:p>" +
            "<w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr>" + R("second", bold: true) + "</w:p>");

        var result = DocumentEditor.Join(docx, 0, Plain);

        await Assert.That(Shape(result.Document)).IsEqualTo("[first ][b:second] ¶");
        await Assert.That(Xml(result.Document)).Contains("<w:jc w:val=\"center\" />");
        await Assert.That(Xml(result.Document)).DoesNotContain("right");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((0, 6));
    }

    [Test]
    public async Task Join_AnEmptyParagraphGoes_AndTheSecondStandsAsItIs()
    {
        var docx = Build(
            "<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr></w:p>" +
            "<w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr>" + R("second") + "</w:p>");

        var result = DocumentEditor.Join(docx, 0, Plain);

        await Assert.That(Xml(result.Document)).IsEqualTo("<w:p><w:pPr><w:jc w:val=\"right\" /></w:pPr><w:r><w:t xml:space=\"preserve\">second</w:t></w:r></w:p>");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task Join_WhereNothingFollows_ChangesNothing()
    {
        var docx = Build(P(R("only")) + "<w:tbl><w:tr><w:tc><w:p>" + R("cell") + "</w:p></w:tc></w:tr></w:tbl>");

        await Assert.That(Xml(DocumentEditor.Join(docx, 0, Plain).Document)).IsEqualTo(Xml(docx));
        await Assert.That(Xml(DocumentEditor.Join(docx, 1, Plain).Document)).IsEqualTo(Xml(docx));
    }

    [Test]
    public async Task ARewrittenParagraph_CanThenBeJoinedToANeighbour()
    {
        var docx = Build(P(R("one")) + P(R("two")) + P(R("three")));

        var previous = DocumentEditor.Rewrite(docx, 1, [Reads(T(0, "TWO"))], Plain, JoinDirection.Previous);
        var next = DocumentEditor.Rewrite(docx, 1, [Reads(T(0, "TWO"))], Plain, JoinDirection.Next);

        await Assert.That(Shape(previous.Document)).IsEqualTo("[one][TWO] ¶ [three] ¶");
        await Assert.That((previous.Paragraph, previous.Offset)).IsEqualTo((0, 3));
        await Assert.That(Shape(next.Document)).IsEqualTo("[one] ¶ [TWO][three] ¶");
        await Assert.That((next.Paragraph, next.Offset)).IsEqualTo((1, 3));
    }

    [Test]
    public async Task Delete_WithinAParagraph_AndAcrossParagraphs()
    {
        var docx = Build(P(R("Hello "), R("brave", bold: true)) + P(R("new")) + P(R("wide world")));

        var within = DocumentEditor.Delete(docx, new(0, 2), new(1, 3), Plain);
        var across = DocumentEditor.Delete(docx, new(1, 2), new(3, 5), Plain);

        await Assert.That(Shape(within.Document)).IsEqualTo("[He][b:ve] ¶ [new] ¶ [wide world] ¶");
        await Assert.That((within.Paragraph, within.Offset)).IsEqualTo((0, 2));
        await Assert.That(Shape(across.Document)).IsEqualTo("[Hello ][b:br][world] ¶");
        await Assert.That((across.Paragraph, across.Offset)).IsEqualTo((0, 8));
    }

    [Test]
    public async Task Delete_AcrossATable_IsRefused()
    {
        var docx = Build(P(R("before")) + "<w:tbl><w:tr><w:tc><w:p>" + R("cell") + "</w:p></w:tc></w:tr></w:tbl>" + P(R("after")));

        await Assert.That(() => DocumentEditor.Delete(docx, new(0, 2), new(2, 3), Plain)).Throws<InvalidOperationException>();
    }

    // Tracked

    [Test]
    public async Task Tracked_WhatIsTypedIsAnInsertion_AndWhatGoesADeletion()
    {
        var docx = Build(P(R("Hello cruel world", bold: true)));

        var edited = Rewrite(docx, 0, Tracked, Reads(T(0, "Hello brave new world")));

        await Assert.That(Shape(edited)).IsEqualTo("[b:Hello ]{-[b:cruel]-}{+[b:brave new]+}[b: world] ¶");
        await Assert.That(Xml(edited)).Contains("<w:del w:author=\"Ann\" w:date=\"2026-09-27T14:30:45Z\" w:id=\"1\"><w:r><w:rPr><w:b /></w:rPr><w:delText xml:space=\"preserve\">cruel</w:delText></w:r></w:del>");

        var changes = DocumentReview.Read(edited).Changes;
        await Assert.That(string.Join("; ", changes.Select(_ => $"{_.Kind} '{_.Text}' {_.Author}")))
            .IsEqualTo("Deletion 'cruel' Ann; Insertion 'brave new' Ann");
    }

    [Test]
    public async Task Tracked_RejectingEverything_GivesBackTheDocument()
    {
        var docx = Build(
            "<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr>" + R("Hello ") + R("cruel", bold: true) + R(" world") + "</w:p>" +
            P(R("after")));

        var edited = DocumentEditor.Rewrite(
            docx,
            0,
            [Reads(T(0, "Hello, "), T(1, "most ", new(Italic: true))), new([T(1, "brave"), T(2, " world!")], TextAlignment.Right)],
            Tracked).Document;
        await Assert.That(Describe(edited)).IsEqualTo("Hello{- -}{-cruel-}{+, +}{+most +}¶+{+brave+} world{+!+}¶after¶");

        var rejected = ReviewEditor.ResolveAll(edited, accept: false);
        var accepted = ReviewEditor.ResolveAll(edited, accept: true);

        await Assert.That(Describe(rejected)).IsEqualTo("Hello cruel world¶after¶");
        await Assert.That(Xml(rejected)).Contains("<w:jc w:val=\"center\" />");
        await Assert.That(Xml(rejected)).DoesNotContain("right");
        await Assert.That(Describe(accepted)).IsEqualTo("Hello, most ¶brave world!¶after¶");
        await Assert.That(Shape(accepted)).IsEqualTo("[Hello][, ][bi:most ] ¶ [b:brave][ world][!] ¶ [after] ¶");
    }

    [Test]
    public async Task Tracked_TextTypedIntoOnesOwnInsertion_JoinsIt_AndDeletedFromIt_IsGone()
    {
        var docx = Build(P(R("Hello "), Ins("Ann", 1, R("brave new ")), R("world")));

        var edited = Rewrite(docx, 0, Tracked, Reads(T(0, "Hello "), T(1, "brave old "), T(2, "world")));

        await Assert.That(Shape(edited)).IsEqualTo("[Hello ]{+[brave old ]+}[world] ¶");
    }

    [Test]
    public async Task Tracked_InSomeoneElsesInsertion_ADeletionIsKept_AndAnInsertionDividesIt()
    {
        var docx = Build(P(R("Hello "), Ins("Bob", 1, R("brave new ")), R("world")));

        var edited = Rewrite(docx, 0, Tracked, Reads(T(0, "Hello "), T(1, "brave old "), T(2, "world")));

        await Assert.That(Shape(edited)).IsEqualTo("[Hello ]{+[brave ]{-[new]-}+}{+[old]+}{+[ ]+}[world] ¶");
        var authors = Read(edited, _ => string.Join(' ', _.MainDocumentPart!.Document!.Descendants<InsertedRun>().Select(RevisionElements.Author)));
        await Assert.That(authors).IsEqualTo("Bob Ann Bob");

        // Every revision is one of a kind.
        var ids = Read(edited, _ => RevisionElements.Enumerate(_.MainDocumentPart!.Document!).Select(RevisionElements.Id).ToList());
        await Assert.That(ids.Distinct().Count()).IsEqualTo(ids.Count);
    }

    [Test]
    public async Task Tracked_Formatting_KeepsARecordOfWhatItWas()
    {
        var docx = Build(P(R("Hello world", bold: true)));

        var edited = Rewrite(docx, 0, Tracked, Reads(T(0, "Hello "), T(0, "world", new(Bold: false, Italic: true))));

        await Assert.That(Shape(edited)).IsEqualTo("[b:Hello ][!bi~:world] ¶");
        await Assert.That(Xml(edited)).Contains("<w:rPrChange w:author=\"Ann\" w:date=\"2026-09-27T14:30:45Z\" w:id=\"0\"><w:rPr><w:b /></w:rPr></w:rPrChange>");
        await Assert.That(Shape(ReviewEditor.ResolveAll(edited, accept: false))).IsEqualTo("[b:Hello ][b:world] ¶");
    }

    [Test]
    public async Task Tracked_Alignment_KeepsARecordOfWhatItWas()
    {
        var docx = Build("<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr>" + R("one") + "</w:p>");

        var edited = DocumentEditor.Align(docx, [0], TextAlignment.Right, Tracked);

        await Assert.That(Xml(edited)).Contains(
            "<w:pPr><w:jc w:val=\"right\" /><w:pPrChange w:author=\"Ann\" w:date=\"2026-09-27T14:30:45Z\" w:id=\"0\"><w:pPr><w:jc w:val=\"center\" /></w:pPr></w:pPrChange></w:pPr>");
        await Assert.That(Xml(ReviewEditor.ResolveAll(edited, accept: false))).Contains("<w:pPr><w:jc w:val=\"center\" /></w:pPr>");
    }

    [Test]
    public async Task Tracked_Join_StrikesOutTheBreak_AndLeavesTwoParagraphs()
    {
        var docx = Build(P(R("first ")) + P(R("second")));

        var result = DocumentEditor.Join(docx, 0, Tracked);

        await Assert.That(Describe(result.Document)).IsEqualTo("first ¶-second¶");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((0, 6));
        await Assert.That(Describe(ReviewEditor.ResolveAll(result.Document, accept: true))).IsEqualTo("first second¶");
        await Assert.That(Describe(ReviewEditor.ResolveAll(result.Document, accept: false))).IsEqualTo("first ¶second¶");
    }

    [Test]
    public async Task Tracked_JoiningAtABreakOfOnesOwn_TakesTheBreakBack()
    {
        var docx = Rewrite(Build(P(R("Hello world"))), 0, Tracked, Reads(T(0, "Hello ")), Reads(T(0, "world")));
        await Assert.That(Describe(docx)).IsEqualTo("Hello ¶+world¶");

        var result = DocumentEditor.Join(docx, 0, Tracked);

        await Assert.That(Describe(result.Document)).IsEqualTo("Hello world¶");
        await Assert.That(DocumentReview.Read(result.Document).Changes).IsEmpty();
    }

    [Test]
    public async Task Tracked_Delete_AcrossParagraphs()
    {
        var docx = Build(P(R("Hello brave")) + P(R("new")) + P(R("wide world")));

        var result = DocumentEditor.Delete(docx, new(0, 6), new(2, 5), Tracked);

        await Assert.That(Describe(result.Document)).IsEqualTo("Hello {-brave-}¶-{-new-}¶-{-wide -}world¶");
        await Assert.That(Describe(ReviewEditor.ResolveAll(result.Document, accept: true))).IsEqualTo("Hello world¶");
        await Assert.That(DocumentReview.Read(result.Document).Changes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RevisionIds_FollowOnFromTheDocumentsOwn()
    {
        var docx = Build(P(R("Hello "), Ins("Bob", 41, R("brave ")), R("world")));

        var edited = Rewrite(docx, 0, Tracked, Reads(T(0, "Hello "), T(1, "brave "), T(2, "world!")));

        await Assert.That(Xml(edited)).Contains("<w:ins w:author=\"Ann\" w:date=\"2026-09-27T14:30:45Z\" w:id=\"42\">");
    }

    // Settings

    [Test]
    public async Task SetTracking_TurnsTheDocumentsOwnTrackingOnAndOff()
    {
        var docx = Build(P(R("Hello")));
        await Assert.That(DocumentOutline.Read(docx).Tracking).IsFalse();

        var on = DocumentEditor.SetTracking(docx, true);
        var off = DocumentEditor.SetTracking(on, false);

        await Assert.That(DocumentOutline.Read(on).Tracking).IsTrue();
        await Assert.That(DocumentOutline.Read(off).Tracking).IsFalse();
        await Assert.That(Describe(on)).IsEqualTo(Describe(docx));
    }

    [Test]
    public async Task SetTracking_PutsTheSettingWhereTheSchemaHasIt()
    {
        var docx = Build(P(R("Hello")), settings: "<w:zoom w:percent=\"100\"/><w:defaultTabStop w:val=\"720\"/>");

        var on = DocumentEditor.SetTracking(docx, true);

        var settings = Read(on, _ => Bare(_.MainDocumentPart!.DocumentSettingsPart!.Settings!.InnerXml));
        await Assert.That(settings).IsEqualTo("<w:zoom w:percent=\"100\" /><w:trackRevisions /><w:defaultTabStop w:val=\"720\" />");
    }

    // The same edit to the same file writes the same bytes.
    [Test]
    public async Task Edits_AreReproducible()
    {
        var docx = Build(P(R("Hello cruel world")));

        var first = Rewrite(docx, 0, Tracked, Reads(T(0, "Hello ")), Reads(T(0, "brave world")));
        var second = Rewrite(docx, 0, Tracked, Reads(T(0, "Hello ")), Reads(T(0, "brave world")));

        await Assert.That(Xml(first)).IsEqualTo(Xml(second));
    }

    // What the viewer does after every edit: parse the result and lay it out.
    [Test]
    public async Task AnEditedDocument_StillParses_AndItsParagraphsAreStamped()
    {
        var docx = Rewrite(Corpus("tracked_changes", "01"), 0, Tracked, Reads(T(0, "Hello, "), T(1, "inserted "), T(2, "world ")), Reads(K(3), T(-1, "More.")));

        using var stream = new MemoryStream(docx);
        var parsed = DocumentConverter.Parse(stream, null, null, captureSources: true);

        var paragraphs = parsed.Elements.OfType<ParagraphElement>().ToList();
        await Assert.That(string.Join('|', paragraphs.Select(_ => string.Concat(_.Runs.Select(run => run.Text))))).IsEqualTo("Hello, inserted world |removed.More.");
        await Assert.That(paragraphs.Select(_ => _.Source).SequenceEqual([0, 1])).IsTrue();
    }
}
