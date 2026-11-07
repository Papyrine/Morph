using TUnit.Assertions.Enums;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using static EditDocuments;
using static ReviewDocuments;

// Coauthoring names a paragraph by its w14:paraId (ParagraphIds), since an ordinal moves with every
// paragraph added above. An edit made under Stamping leaves every paragraph and row of the parts it
// loaded with an id of its own, keeps the ids it found, and touches no part it did not load.
public class ParagraphIdTests
{
    static byte[] Stamped(Func<byte[]> edit)
    {
        using var stamping = ParagraphIds.Stamping();
        return edit();
    }

    static string Paragraph(string id, string text) =>
        $"<w:p w14:paraId=\"{id}\">{R(text)}</w:p>";

    static List<string?> Ids(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = WordprocessingDocument.Open(stream, false);
        return [.. package.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().Select(_ => _.ParagraphId?.Value)];
    }

    public static List<string> Texts(byte[] docx) =>
        [.. DocumentOutline.Read(docx).Paragraphs.Select(_ => string.Concat(_.Units.Select(unit => unit.Text)))];

    static async Task AssertValid(IReadOnlyList<string?> ids)
    {
        await Assert.That(ids.Distinct().Count()).IsEqualTo(ids.Count);
        foreach (var id in ids)
        {
            await Assert.That(id).IsNotNull();
            await Assert.That(id!).Matches("^[0-9A-F]{8}$");
            await Assert.That(int.Parse(id!, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task AnEdit_GivesEveryParagraphAnId()
    {
        var docx = Build(P(R("one")) + P(R("two")) + P(R("three")));

        var edited = Stamped(() => Rewrite(docx, 1, Plain, Reads(T(0, "two!"))));

        var ids = Ids(edited);
        await Assert.That(ids.Count).IsEqualTo(3);
        await AssertValid(ids);
    }

    [Test]
    public async Task TheIdsADocumentHas_AreKept()
    {
        var docx = Build(Paragraph("0000000A", "one") + Paragraph("0000000B", "two"));

        var edited = Stamped(() => Rewrite(docx, 1, Plain, Reads(T(0, "two!"))));

        await Assert.That(Ids(edited).Select(_ => _ ?? "")).IsEquivalentTo(["0000000A", "0000000B"], CollectionOrdering.Matching);
    }

    // DocumentEditor.Split puts the text before the caret in a new paragraph ahead, and the paragraph
    // that was there becomes the last piece: the id stays with the tail.
    [Test]
    public async Task Enter_LeavesTheIdOnTheTail_AndGivesTheHeadANewOne()
    {
        var docx = Build(Paragraph("0000000A", "onetwo"));

        var edited = Stamped(() => Rewrite(docx, 0, Plain, Reads(T(0, "one")), Reads(T(0, "two"))));

        var ids = Ids(edited);
        await Assert.That(ids.Count).IsEqualTo(2);
        await AssertValid(ids);
        await Assert.That(ids[1]).IsEqualTo("0000000A");
        await Assert.That(Texts(edited)).IsEquivalentTo(["one", "two"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnIdTwoParagraphsShare_StaysWithTheFirst()
    {
        var docx = Build(Paragraph("0000000A", "one") + Paragraph("0000000A", "two"));

        var edited = Stamped(() => Rewrite(docx, 0, Plain, Reads(T(0, "one!"))));

        var ids = Ids(edited);
        await AssertValid(ids);
        await Assert.That(ids[0]).IsEqualTo("0000000A");
    }

    [Test]
    public async Task WithoutStamping_AnEditAddsNoId()
    {
        var docx = Build(P(R("one")) + P(R("two")));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "one!")));

        await Assert.That(Ids(edited).All(_ => _ == null)).IsTrue();
    }

    [Test]
    public async Task ThePartsRoot_MarksW14Ignorable()
    {
        var docx = Build(P(R("one")));

        var edited = Stamped(() => Rewrite(docx, 0, Plain, Reads(T(0, "one!"))));

        using var stream = new MemoryStream(edited);
        using var package = WordprocessingDocument.Open(stream, false);
        var root = package.MainDocumentPart!.Document!;
        await Assert.That(root.MCAttributes?.Ignorable?.Value).IsNotNull();
        await Assert.That(root.MCAttributes!.Ignorable!.Value!.Split(' ')).Contains("w14");
        await Assert.That(root.LookupNamespace("w14")).IsEqualTo("http://schemas.microsoft.com/office/word/2010/wordml");
    }

    [Test]
    public async Task TheRowsOfATableAnEditMakes_GetIds()
    {
        var docx = Build(P(R("before")));

        var edited = Stamped(() => DocumentEditor.InsertTable(docx, 0, 6, 2, 2, Plain).Document);

        using var stream = new MemoryStream(edited);
        using var package = WordprocessingDocument.Open(stream, false);
        var body = package.MainDocumentPart!.Document!.Body!;
        var rows = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.TableRow>().Select(_ => _.ParagraphId?.Value).ToList();
        await Assert.That(rows.Count).IsEqualTo(2);
        await AssertValid([.. rows, .. Ids(edited)]);
    }

    // A session stamps every part when it starts, so the parts an edit reads and does not change already
    // have their ids, and stamping them changes nothing.
    [Test]
    public async Task APartWhoseParagraphsHaveIds_IsNotChanged()
    {
        var docx = Build(Paragraph("0000000A", "body"), header: Paragraph("0000000B", "head"));

        var edited = Stamped(() => Rewrite(docx, 0, Plain, Reads(T(0, "body!"))));

        var changed = DocumentPatch.Diff(docx, edited).Changed.Keys;
        await Assert.That(changed).IsEquivalentTo(["word/document.xml"]);
    }

    [Test]
    public async Task StampAll_StampsEveryPartWithParagraphs()
    {
        var docx = Build(
            P(R("body")),
            comments: "<w:comment w:id=\"0\" w:author=\"Ann\"><w:p><w:r><w:t>note</w:t></w:r></w:p></w:comment>",
            header: P(R("head")));

        var stamped = ParagraphIds.StampAll(docx);

        using var stream = new MemoryStream(stamped);
        using var package = WordprocessingDocument.Open(stream, false);
        var main = package.MainDocumentPart!;
        List<string?> ids =
        [
            .. main.Document!.Descendants<Paragraph>().Select(_ => _.ParagraphId?.Value),
            .. main.HeaderParts.Single().Header!.Descendants<Paragraph>().Select(_ => _.ParagraphId?.Value),
            .. main.WordprocessingCommentsPart!.Comments!.Descendants<Paragraph>().Select(_ => _.ParagraphId?.Value)
        ];
        await Assert.That(ids.Count).IsEqualTo(3);
        await AssertValid(ids);
    }

    [Test]
    public async Task TheOutline_ReadsEachParagraphsId()
    {
        var docx = Build(Paragraph("0000000A", "one") + P(R("two")));

        var paragraphs = DocumentOutline.Read(docx).Paragraphs;

        await Assert.That(paragraphs[0].Id).IsEqualTo("0000000A");
        await Assert.That(paragraphs[1].Id).IsNull();
    }
}
