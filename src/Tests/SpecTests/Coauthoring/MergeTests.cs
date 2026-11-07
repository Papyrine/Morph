using TUnit.Assertions.Enums;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using static EditDocuments;
using static ReviewDocuments;

// Coauthoring merges an edit made to a version that is no longer the latest with what the others did
// since (DocumentMerge): two edits to different paragraphs are both kept, an edit is refused whole where
// both changed one paragraph, and ids both made afresh are remapped in mine. Every edit here is the
// editor's own, made under ParagraphIds.Stamping as the editor makes them when coauthoring.
public class MergeTests
{
    const string section = "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr>";

    // A, B, C and D, each with its id, as a session holds a document.
    static byte[] Base() =>
        ParagraphIds.StampAll(Build(P(R("A")) + P(R("B")) + P(R("C")) + P(R("D")) + section));

    static byte[] Edit(Func<byte[]> edit)
    {
        using var stamping = ParagraphIds.Stamping();
        return edit();
    }

    // The paragraph's first stretch of text typed over; an empty paragraph's typed into, as new text.
    static byte[] Typed(byte[] docx, int paragraph, string text)
    {
        var unit = 0;
        if (DocumentOutline.Read(docx).Paragraphs[paragraph].Units.Count == 0)
        {
            unit = -1;
        }

        return Edit(() => Rewrite(docx, paragraph, Plain, Reads(T(unit, text))));
    }

    static List<string> Texts(byte[] docx) =>
        ParagraphIdTests.Texts(docx);

    static string Id(byte[] docx, int paragraph) =>
        DocumentOutline.Read(docx).Paragraphs[paragraph].Id!;

    static async Task<byte[]> Merged(MergeOutcome outcome)
    {
        await Assert.That(outcome.Refused).IsFalse().Because(outcome.Reason ?? "");
        return outcome.Document!;
    }

    [Test]
    public async Task EditsToDifferentParagraphs_AreBothKept()
    {
        var original = Base();

        var outcome = DocumentMerge.Merge(original, Typed(original, 1, "B mine"), Typed(original, 3, "D theirs"));

        await Assert.That(Texts(await Merged(outcome))).IsEquivalentTo(["A", "B mine", "C", "D theirs"], CollectionOrdering.Matching);
        await Assert.That(outcome.Rebased).IsTrue();
    }

    [Test]
    public async Task WhenTheirsIsTheBase_MineIsKeptAsItIs()
    {
        var original = Base();
        var mine = Typed(original, 1, "B mine");

        var outcome = DocumentMerge.Merge(original, mine, original);

        await Assert.That(outcome.Document).IsSameReferenceAs(mine);
        await Assert.That(outcome.Rebased).IsFalse();
    }

    [Test]
    public async Task EditsToTheSameParagraph_AreRefused_NamingIt()
    {
        var original = Base();

        var outcome = DocumentMerge.Merge(original, Typed(original, 1, "B mine"), Typed(original, 1, "B theirs"));

        await Assert.That(outcome.Refused).IsTrue();
        await Assert.That(outcome.Paragraph).IsEqualTo(Id(original, 1));
        await Assert.That(outcome.Locked).IsFalse();
    }

    [Test]
    public async Task TheSameEditOnBothSides_IsKept()
    {
        var original = Base();

        var outcome = DocumentMerge.Merge(original, Typed(original, 1, "B both"), Typed(original, 1, "B both"));

        await Assert.That(Texts(await Merged(outcome))).IsEquivalentTo(["A", "B both", "C", "D"], CollectionOrdering.Matching);
    }

    // Enter puts the text before the caret in a new paragraph ahead of the one it was in, which keeps
    // its id: the new one is placed before what follows it in mine, so the two stay together.
    [Test]
    public async Task Enter_KeepsTheSplitParagraphsTogether()
    {
        var original = Base();
        var mine = Edit(() => Rewrite(original, 1, Plain, Reads(T(0, "B1")), Reads(T(0, "B2"))));

        var outcome = DocumentMerge.Merge(original, mine, Typed(original, 2, "C theirs"));

        await Assert.That(Texts(await Merged(outcome))).IsEquivalentTo(["A", "B1", "B2", "C theirs", "D"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AParagraphAddedAtTheEnd_GoesBeforeTheSection()
    {
        var original = Base();
        var mine = Edit(() => Rewrite(original, 3, Plain, Reads(T(0, "D")), Reads(T(0, "E"))));

        var merged = await Merged(DocumentMerge.Merge(original, mine, Typed(original, 0, "A theirs")));

        await Assert.That(Texts(merged)).IsEquivalentTo(["A theirs", "B", "C", "D", "E"], CollectionOrdering.Matching);
        using var stream = new MemoryStream(merged);
        using var package = WordprocessingDocument.Open(stream, false);
        await Assert.That(package.MainDocumentPart!.Document!.Body!.LastChild).IsTypeOf<SectionProperties>();
    }

    [Test]
    public async Task AJoin_IsKept_AlongsideAnEditElsewhere()
    {
        var original = Base();
        var mine = Edit(() => DocumentEditor.Join(original, 1, Plain).Document);

        var outcome = DocumentMerge.Merge(original, mine, Typed(original, 3, "D theirs"));

        await Assert.That(Texts(await Merged(outcome))).IsEquivalentTo(["A", "BC", "D theirs"], CollectionOrdering.Matching);
    }

    // A join changes one paragraph and deletes the other: refused whole where theirs changed either.
    [Test]
    public async Task AJoin_OfAParagraphTheyChanged_IsRefused()
    {
        var original = Base();
        var mine = Edit(() => DocumentEditor.Join(original, 1, Plain).Document);

        var outcome = DocumentMerge.Merge(original, mine, Typed(original, 2, "C theirs"));

        await Assert.That(outcome.Refused).IsTrue();
    }

    [Test]
    public async Task AnEditToAParagraphTheyDeleted_IsRefused()
    {
        var original = Base();
        var theirs = Edit(() => DocumentEditor.Join(original, 1, Plain).Document);

        var outcome = DocumentMerge.Merge(original, Typed(original, 2, "C mine"), theirs);

        await Assert.That(outcome.Refused).IsTrue();
    }

    [Test]
    public async Task AnEditToAParagraphSomeoneElseHasOpen_IsRefused()
    {
        var original = Base();
        var locked = new HashSet<string> { Id(original, 1) };

        var outcome = DocumentMerge.Merge(original, Typed(original, 1, "B mine"), original, locked);

        await Assert.That(outcome.Refused).IsTrue();
        await Assert.That(outcome.Locked).IsTrue();
        await Assert.That(outcome.Paragraph).IsEqualTo(Id(original, 1));
    }

    [Test]
    public async Task AnEditBesideAParagraphSomeoneElseHasOpen_IsKept()
    {
        var original = Base();
        var locked = new HashSet<string> { Id(original, 1) };

        var outcome = DocumentMerge.Merge(original, Typed(original, 3, "D mine"), Typed(original, 2, "C theirs"), locked);

        await Assert.That(Texts(await Merged(outcome))).IsEquivalentTo(["A", "B", "C theirs", "D mine"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task EditsToDifferentCellsOfATable_AreBothKept()
    {
        var table = Edit(() => DocumentEditor.InsertTable(Base(), 0, 1, 2, 2, Plain).Document);
        var original = ParagraphIds.StampAll(table);
        var cells = DocumentOutline.Read(original).Paragraphs.Where(_ => _.InTable).ToList();

        var mine = Typed(original, cells[0].Ordinal, "first");
        var theirs = Typed(original, cells[3].Ordinal, "last");
        var merged = await Merged(DocumentMerge.Merge(original, mine, theirs));

        var texts = DocumentOutline.Read(merged).Paragraphs.Where(_ => _.InTable).Select(_ => string.Concat(_.Units.Select(unit => unit.Text))).ToList();
        await Assert.That(texts).IsEquivalentTo(["first", "", "", "last"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task EditsToTheHeaderAndTheBody_AreBothKept()
    {
        var original = ParagraphIds.StampAll(Build(P(R("body")), header: P(R("head"))));
        var head = DocumentOutline.Read(original).Paragraphs.Single(_ => _.Part == ReviewPart.Header).Ordinal;

        var outcome = DocumentMerge.Merge(original, Typed(original, head, "head mine"), Typed(original, 0, "body theirs"));

        await Assert.That(Texts(await Merged(outcome))).IsEquivalentTo(["body theirs", "head mine"], CollectionOrdering.Matching);
    }

    // Both add Heading 1 to the styles, the same definition: one style, both paragraphs in it.
    [Test]
    public async Task AStyleBothAdded_IsKeptOnce()
    {
        var original = Base();
        var mine = Edit(() => DocumentEditor.SetStyle(original, [0], "Heading1", Plain));
        var theirs = Edit(() => DocumentEditor.SetStyle(original, [3], "Heading1", Plain));

        var merged = await Merged(DocumentMerge.Merge(original, mine, theirs));

        using var stream = new MemoryStream(merged);
        using var package = WordprocessingDocument.Open(stream, false);
        var main = package.MainDocumentPart!;
        var paragraphs = main.Document!.Body!.Elements<Paragraph>().ToList();
        await Assert.That(paragraphs[0].ParagraphProperties?.ParagraphStyleId?.Val?.Value).IsEqualTo("Heading1");
        await Assert.That(paragraphs[3].ParagraphProperties?.ParagraphStyleId?.Val?.Value).IsEqualTo("Heading1");
        await Assert.That(main.StyleDefinitionsPart!.Styles!.Elements<Style>().Count(_ => _.StyleId == "Heading1")).IsEqualTo(1);
        await Assert.That(main.Parts.Count(_ => _.OpenXmlPart is StyleDefinitionsPart)).IsEqualTo(1);
    }

    // A bulleted list and a numbered one, made at once: both took list 1 and definition 0, so mine's are
    // renumbered, and each paragraph keeps the list it was given.
    [Test]
    public async Task ListsBothMade_AreToldApart()
    {
        var original = Base();
        var mine = Edit(() => DocumentEditor.SetList(original, [0], ListKind.Bullet, Plain));
        var theirs = Edit(() => DocumentEditor.SetList(original, [3], ListKind.Number, Plain));

        var merged = await Merged(DocumentMerge.Merge(original, mine, theirs));

        await Assert.That(DocumentEditor.ListOf(merged, 0)).IsEqualTo(ListKind.Bullet);
        await Assert.That(DocumentEditor.ListOf(merged, 3)).IsEqualTo(ListKind.Number);
    }

    [Test]
    public async Task CommentsBothAdded_AreBothKept()
    {
        var original = Base();
        var mine = Edit(() => ReviewEditor.AddComment(original, new(1, 0), new(1, 1), "Ann", "mine", Now));
        var theirs = Edit(() => ReviewEditor.AddComment(original, new(3, 0), new(3, 1), "Bob", "theirs", Now));

        var merged = await Merged(DocumentMerge.Merge(original, mine, theirs));

        var comments = DocumentReview.Read(merged).Comments;
        await Assert.That(comments.Select(_ => _.Text)).IsEquivalentTo(["mine", "theirs"], CollectionOrdering.Any);
        await Assert.That(comments.Select(_ => _.Id).Distinct().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task PicturesBothPutIn_AreBothKept()
    {
        var original = Base();
        var mine = Edit(() => DocumentEditor.InsertPicture(original, 0, 1, new([0x89, 0x50, 0x4E, 0x47, 1], "image/png", 10, 10), Plain).Document);
        var theirs = Edit(() => DocumentEditor.InsertPicture(original, 3, 1, new([0x89, 0x50, 0x4E, 0x47, 2, 2], "image/png", 10, 10), Plain).Document);

        var merged = await Merged(DocumentMerge.Merge(original, mine, theirs));

        using var stream = new MemoryStream(merged);
        using var package = WordprocessingDocument.Open(stream, false);
        var main = package.MainDocumentPart!;
        var embedded = main.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Blip>()
            .Select(_ => main.GetPartById(_.Embed!.Value!))
            .Select(_ =>
            {
                using var data = _.GetStream();
                return data.Length;
            })
            .ToList();
        await Assert.That(embedded).IsEquivalentTo([5L, 6L], CollectionOrdering.Any);
        var drawings = main.Document.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().Select(_ => _.Id!.Value).ToList();
        await Assert.That(drawings.Distinct().Count()).IsEqualTo(2);
    }

    // Undo when others have edited since: the edit's own change is taken back, theirs stay.
    [Test]
    public async Task UndoAsAMerge_TakesBackTheEditAlone()
    {
        var before = Base();
        var after = Typed(before, 1, "B mine");
        var current = Typed(after, 3, "D theirs");

        var undone = await Merged(DocumentMerge.Merge(after, before, current));

        await Assert.That(Texts(undone)).IsEquivalentTo(["A", "B", "C", "D theirs"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task TheParagraphsAnEditChanged_AreNamedByTheirIds()
    {
        var original = Base();

        var changed = DocumentMerge.Changed(original, Typed(original, 2, "C!")).ToList();

        await Assert.That(changed).IsEquivalentTo([Id(original, 2)]);
    }
}
