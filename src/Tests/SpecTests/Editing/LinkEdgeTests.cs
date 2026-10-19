using static EditDocuments;
using static ReviewDocuments;

// Text typed at either end of a link. Word-probed 2026-10-19 by keys sent to Word 365: it is not the
// link's, and goes outside the w:hyperlink, formatted as the link's text but without the Hyperlink
// style. Typed inside the link, or in place of its text, it is the link's.
//
// The paragraph is "See ", a bold link "the site", and " today" — units 0, 1 and 2.
public class LinkEdgeTests
{
    const string link =
        "<w:hyperlink w:anchor=\"top\"><w:r><w:rPr><w:rStyle w:val=\"Hyperlink\"/><w:b/></w:rPr><w:t>the site</w:t></w:r></w:hyperlink>";

    static byte[] Linked(bool before = true, bool after = true)
    {
        var content = new List<string>();
        if (before)
        {
            content.Add(R("See "));
        }

        content.Add(link);
        if (after)
        {
            content.Add(R(" today"));
        }

        return Build(P([.. content]));
    }

    [Test]
    public async Task TypedAtItsEnd_IsAfterTheLink_FormattedAsItsTextIs()
    {
        var edited = Rewrite(Linked(), 0, Plain, Reads(T(0, "See "), T(1, "the siteXY"), T(2, " today")));

        await Assert.That(Shape(edited)).IsEqualTo("[See ]<[b:the site]>[b:XY][ today] ¶");
        await Assert.That(Xml(edited)).Contains("</w:hyperlink><w:r><w:rPr><w:b /></w:rPr><w:t xml:space=\"preserve\">XY</w:t></w:r>");
    }

    [Test]
    public async Task TypedAtItsEnd_AtTheEndOfTheParagraph_IsAfterTheLink()
    {
        var edited = Rewrite(Linked(after: false), 0, Plain, Reads(T(0, "See "), T(1, "the siteXY")));

        await Assert.That(Shape(edited)).IsEqualTo("[See ]<[b:the site]>[b:XY] ¶");
    }

    [Test]
    public async Task TypedAtItsStart_IsBeforeTheLink()
    {
        var edited = Rewrite(Linked(), 0, Plain, Reads(T(0, "See "), T(1, "XYthe site"), T(2, " today")));

        await Assert.That(Shape(edited)).IsEqualTo("[See ][b:XY]<[b:the site]>[ today] ¶");
    }

    [Test]
    public async Task TypedAtItsStart_AtTheStartOfTheParagraph_IsBeforeTheLink()
    {
        var edited = Rewrite(Linked(before: false), 0, Plain, Reads(T(0, "XYthe site"), T(1, " today")));

        await Assert.That(Shape(edited)).IsEqualTo("[b:XY]<[b:the site]>[ today] ¶");
    }

    [Test]
    public async Task TypedInsideIt_IsTheLinks()
    {
        var edited = Rewrite(Linked(), 0, Plain, Reads(T(0, "See "), T(1, "the XYsite"), T(2, " today")));

        await Assert.That(Shape(edited)).IsEqualTo("[See ]<[b:the XYsite]>[ today] ¶");
    }

    // Its text replaced whole, the link keeps what replaced it.
    [Test]
    public async Task TypedInPlaceOfItsText_IsTheLinks()
    {
        var edited = Rewrite(Linked(), 0, Plain, Reads(T(0, "See "), T(1, "here"), T(2, " today")));

        await Assert.That(Shape(edited)).IsEqualTo("[See ]<[b:here]>[ today] ¶");
    }

    // Typed after the link but formatted as the text after it, it is that text's, as it always was.
    [Test]
    public async Task TypedAtItsEnd_FormattedAsTheTextAfter_JoinsThatText()
    {
        var edited = Rewrite(Linked(), 0, Plain, Reads(T(0, "See "), T(1, "the site"), T(2, "XY today")));

        await Assert.That(Shape(edited)).IsEqualTo("[See ]<[b:the site]>[XY today] ¶");
    }

    [Test]
    public async Task TypedAtItsEnd_Tracked_IsAnInsertionAfterTheLink()
    {
        var docx = Linked();

        var edited = Rewrite(docx, 0, Tracked, Reads(T(0, "See "), T(1, "the siteXY"), T(2, " today")));

        await Assert.That(Shape(edited)).IsEqualTo("[See ]<[b:the site]>{+[b:XY]+}[ today] ¶");
        await Assert.That(Shape(ReviewEditor.ResolveAll(edited, accept: false))).IsEqualTo(Shape(docx));
    }
}
