using static ReviewDocuments;

// A document read for editing: every paragraph of the main part with what it holds, in the terms an
// edit is later made in, and what the document allows.
public class DocumentOutlineTests
{
    static string Units(EditParagraph paragraph) =>
        string.Join(" | ", paragraph.Units.Select(_ => $"{_.Kind}@{_.Run}+{_.Start} '{_.Text.Replace("\n", "\\n").Replace("\t", "\\t")}'"));

    [Test]
    public async Task EveryParagraph_IsRead_WithItsTextRunByRun()
    {
        var docx = Build(P(R("Hello "), R("world", bold: true)) + "<w:p/>" + P(R("again")));

        var outline = DocumentOutline.Read(docx);

        await Assert.That(outline.Paragraphs.Count).IsEqualTo(3);
        await Assert.That(Units(outline.Paragraphs[0])).IsEqualTo("Text@0+0 'Hello ' | Text@1+0 'world'");
        await Assert.That(outline.Paragraphs[1].Units).IsEmpty();
        await Assert.That(Units(outline.Paragraphs[2])).IsEqualTo("Text@2+0 'again'");
        await Assert.That(outline.Paragraphs[0].Length).IsEqualTo(11);
        await Assert.That(outline.ParagraphOf(1)?.Ordinal).IsEqualTo(0);
        await Assert.That(outline.ParagraphOf(2)?.Ordinal).IsEqualTo(2);
        await Assert.That(outline.ParagraphOf(9)).IsNull();
    }

    // A tab and a line break are text, a character each. What is not text is a unit of its own, and
    // the text either side of it a unit each, though all three be one run.
    [Test]
    public async Task ARun_IsAsManyUnitsAsItHasThingsInIt()
    {
        var docx = Build(
            P(
                "<w:r><w:t>one</w:t><w:tab/><w:t>two</w:t><w:br/><w:t>three</w:t><w:footnoteReference w:id=\"2\"/><w:t>four</w:t>" +
                "<w:br w:type=\"page\"/><w:t>five</w:t></w:r>"));

        var paragraph = DocumentOutline.Read(docx).Paragraphs[0];

        await Assert.That(Units(paragraph)).IsEqualTo("Text@0+0 'one\\ttwo\\nthree' | Note@0+13 '' | Text@0+14 'four' | Break@0+18 '' | Text@0+18 'five'");
        await Assert.That(paragraph.Length).IsEqualTo(13 + 1 + 4 + 1 + 4);
        await Assert.That(paragraph.OffsetOf(new(0, 5))).IsEqualTo(5);
        await Assert.That(paragraph.OffsetOf(new(0, 14))).IsEqualTo(14);
        await Assert.That(paragraph.OffsetOf(new(0, 18))).IsEqualTo(18);
        await Assert.That(paragraph.OffsetOf(new(3, 0))).IsNull();
    }

    [Test]
    public async Task AField_IsOneUnit_FromItsStartToItsEnd_ShowingItsResult()
    {
        const string complex =
            "<w:r><w:t xml:space=\"preserve\">Page </w:t><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            "<w:r><w:instrText> PAGE </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
            "<w:r><w:t>3</w:t></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"end\"/><w:t xml:space=\"preserve\"> of </w:t></w:r>";
        var docx = Build(P(complex, "<w:fldSimple w:instr=\" NUMPAGES \">" + R("12") + "</w:fldSimple>", R(".")));

        var paragraph = DocumentOutline.Read(docx).Paragraphs[0];

        await Assert.That(Units(paragraph)).IsEqualTo("Text@0+0 'Page ' | Field@0+5 '3' | Text@4+0 ' of ' | Field@5+0 '12' | Text@6+0 '.'");
    }

    // A table of contents is one field over many paragraphs: none of their text is text to edit.
    [Test]
    public async Task AFieldOverSeveralParagraphs_TakesInAllOfThem()
    {
        var docx = Build(
            P("<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> TOC </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>", R("One")) +
            P(R("Two")) +
            P(R("Three"), "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>", R(" and after")) +
            P(R("Plain")));

        var outline = DocumentOutline.Read(docx);

        await Assert.That(Units(outline.Paragraphs[0])).IsEqualTo("Field@0+0 'One'");
        await Assert.That(Units(outline.Paragraphs[1])).IsEqualTo("Field@4+0 'Two'");
        await Assert.That(Units(outline.Paragraphs[2])).IsEqualTo("Field@5+0 'Three' | Text@7+0 ' and after'");
        await Assert.That(Units(outline.Paragraphs[3])).IsEqualTo("Text@8+0 'Plain'");
    }

    [Test]
    public async Task ATextBoxesParagraphs_AreTheirOwn_AndNoneOfTheParagraphTheyAreAnchoredIn()
    {
        var docx = Build(
            P(
                R("before "),
                "<w:r><w:pict><v:shape><v:textbox><w:txbxContent>" + P(R("inside")) + "</w:txbxContent></v:textbox></v:shape></w:pict></w:r>",
                R(" after")));

        var outline = DocumentOutline.Read(docx);

        await Assert.That(outline.Paragraphs.Count).IsEqualTo(2);
        await Assert.That(Units(outline.Paragraphs[0])).IsEqualTo("Text@0+0 'before ' | Drawing@1+0 '' | Text@3+0 ' after'");
        await Assert.That(Units(outline.Paragraphs[1])).IsEqualTo("Text@2+0 'inside'");
        await Assert.That(outline.ParagraphOf(2)?.Ordinal).IsEqualTo(1);
    }

    [Test]
    public async Task WhichParagraphsCanBeJoined_IsWhichFollowOneAnother()
    {
        var docx = Build(
            P(R("one")) +
            "<w:bookmarkStart w:id=\"1\" w:name=\"between\"/>" +
            P(R("two")) +
            "<w:tbl><w:tr><w:tc>" + P(R("cell")) + "</w:tc></w:tr></w:tbl>" +
            P(R("three")));

        var paragraphs = DocumentOutline.Read(docx).Paragraphs;

        await Assert.That(string.Join(' ', paragraphs.Select(_ => $"{_.HasPrevious}/{_.HasNext}")))
            .IsEqualTo("False/True True/False False/False False/False");
    }

    // Each cell's paragraphs know the cell, of the innermost table when one nests in another.
    [Test]
    public async Task AParagraphInACell_KnowsTheCell()
    {
        var docx = Build(
            P(R("before")) +
            "<w:tbl><w:tr><w:tc>" + P(R("a1")) + "</w:tc><w:tc>" + P(R("b1")) + P(R("b1 again")) + "</w:tc></w:tr>" +
            "<w:tr><w:tc>" + P(R("a2")) + "</w:tc><w:tc>" +
            "<w:tbl><w:tr><w:tc>" + P(R("inner")) + "</w:tc></w:tr></w:tbl>" + P(R("b2")) + "</w:tc></w:tr></w:tbl>" +
            P(R("after")));

        var cells = DocumentOutline.Read(docx).Paragraphs.Select(_ => _.Cell is { } cell ? $"{cell.Table}:{cell.Row}:{cell.Column}" : "-");

        await Assert.That(string.Join(' ', cells)).IsEqualTo("- 0:0:0 0:0:1 0:0:1 0:1:0 1:0:0 0:1:1 -");
    }

    [Test]
    [Arguments(null, true, false)]
    [Arguments("<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"1\"/>", false, false)]
    [Arguments("<w:documentProtection w:edit=\"comments\" w:enforcement=\"1\"/>", false, false)]
    [Arguments("<w:documentProtection w:edit=\"forms\" w:enforcement=\"1\"/>", false, false)]
    [Arguments("<w:documentProtection w:edit=\"trackedChanges\" w:enforcement=\"1\"/>", true, true)]
    [Arguments("<w:documentProtection w:edit=\"readOnly\" w:enforcement=\"0\"/>", true, false)]
    public async Task ProtectionIsHonoured_WhileItIsEnforced(string? settings, bool allows, bool forces)
    {
        var outline = DocumentOutline.Read(Build(P(R("Hello")), settings: settings));

        await Assert.That(outline.AllowsEditing).IsEqualTo(allows);
        await Assert.That(outline.ForcesTracking).IsEqualTo(forces);
    }

    [Test]
    [Arguments("<w:trackRevisions/>", true)]
    [Arguments("<w:trackRevisions w:val=\"0\"/>", false)]
    [Arguments("<w:zoom w:percent=\"100\"/>", false)]
    public async Task Tracking_IsTheDocumentsOwnSetting(string settings, bool tracking) =>
        await Assert.That(DocumentOutline.Read(Build(P(R("Hello")), settings: settings)).Tracking).IsEqualTo(tracking);

    // A place of a paragraph back as a position in one of its runs: a start at the first character at
    // or after it, an end after the last character before it.
    [Test]
    public async Task APlace_IsAPositionInARun_EitherSideOfWhatIsNotText()
    {
        const string field =
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> DATE </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1 May</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";
        var docx = Build(P(R("On "), field, R("we"), R(" met", bold: true)));
        var paragraph = DocumentOutline.Read(docx).Paragraphs[0];

        // "On " is places 0-2, the field place 3, "we" 4-5, " met" 6-9.
        await Assert.That(paragraph.PositionAt(1, ending: false)).IsEqualTo(new SourcePosition(0, 1));
        await Assert.That(paragraph.PositionAt(3, ending: true)).IsEqualTo(new SourcePosition(0, 3));
        await Assert.That(paragraph.PositionAt(3, ending: false)?.Offset).IsEqualTo(0);
        await Assert.That(paragraph.PositionAt(6, ending: true)?.Offset).IsEqualTo(2);
        await Assert.That(paragraph.PositionAt(6, ending: false)?.Offset).IsEqualTo(0);
        await Assert.That(paragraph.PositionAt(10, ending: true)?.Offset).IsEqualTo(4);
        await Assert.That(paragraph.PositionAt(10, ending: false)).IsNull();

        foreach (var place in new[] {0, 2, 5, 8})
        {
            await Assert.That(paragraph.OffsetOf(paragraph.PositionAt(place, ending: false)!.Value)).IsEqualTo(place);
        }
    }

    [Test]
    public async Task AParagraph_SaysWhetherItIsInATable_AndAUnitWhetherItIsALink()
    {
        var docx = Build(
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
            "<w:p><w:hyperlink w:anchor=\"x\"><w:r><w:t>link</w:t></w:r></w:hyperlink><w:r><w:t> plain</w:t></w:r></w:p>");

        var outline = DocumentOutline.Read(docx);

        await Assert.That(outline.Paragraphs[0].InTable).IsTrue();
        await Assert.That(outline.Paragraphs[1].InTable).IsFalse();
        await Assert.That(outline.Paragraphs[1].Units.Select(_ => _.Linked)).IsEquivalentTo([true, false]);
    }
}
