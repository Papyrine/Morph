using static EditDocuments;
using static ReviewDocuments;

// A DocumentPatch is what coauthoring sends for an edit or a version: the entries that changed, whole.
// Applied to the package it was taken from, it gives the package it was taken to, entry for entry.
public class DocumentPatchTests
{
    static async Task AssertSameEntries(byte[] actual, byte[] expected)
    {
        var actualEntries = PackageEntries.Read(actual);
        var expectedEntries = PackageEntries.Read(expected);
        await Assert.That(actualEntries.Keys).IsEquivalentTo(expectedEntries.Keys);
        foreach (var (name, content) in expectedEntries)
        {
            await Assert.That(actualEntries[name].AsSpan().SequenceEqual(content)).IsTrue()
                .Because($"{name} differs");
        }
    }

    [Test]
    [MethodDataSource(typeof(EditCorpusTests), nameof(EditCorpusTests.Documents))]
    public async Task EveryDocument_PatchedWithAnEdit_IsTheEditedDocument(string scenario)
    {
        var docx = await File.ReadAllBytesAsync(Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), scenario, "input.docx"));
        var outline = DocumentOutline.Read(docx);
        var paragraph = outline.AllowsEditing
            ? outline.Paragraphs.Where(_ => _.Units.Any(unit => unit.Editable)).MaxBy(_ => _.Length)
            : null;
        if (paragraph == null)
        {
            return;
        }

        var first = paragraph.Units.First(_ => _.Editable);
        var content = Reads([.. paragraph.Units.Select(Item)]);
        byte[] edited;
        using (ParagraphIds.Stamping())
        {
            edited = Rewrite(docx, paragraph.Ordinal, Plain, content);
        }

        var patch = DocumentPatch.FromBytes(DocumentPatch.Diff(docx, edited).ToBytes());

        await AssertSameEntries(patch.Apply(docx), edited);

        // The paragraph as it was, with the first stretch of its text typed onto.
        NewItem Item(EditUnit unit, int index)
        {
            if (!unit.Editable)
            {
                return K(index);
            }

            if (ReferenceEquals(unit, first))
            {
                return T(index, unit.Text + " — edited");
            }

            return T(index, unit.Text);
        }
    }

    [Test]
    public async Task AnEditToTheBody_ChangesTheMainPartAlone()
    {
        var docx = Build(P(R("body")), styles: "", header: P(R("head")));

        var patch = DocumentPatch.Diff(docx, Rewrite(docx, 0, Plain, Reads(T(0, "body!"))));

        await Assert.That(patch.Changed.Keys).IsEquivalentTo(["word/document.xml"]);
        await Assert.That(patch.Removed).IsEmpty();
    }

    [Test]
    public async Task AnEntryAddedAndOneTakenOut_AreBothInThePatch()
    {
        var docx = Build(P(R("body")));
        var entries = PackageEntries.Read(docx);
        entries["word/extra.xml"] = "<extra/>"u8.ToArray();
        var withExtra = PackageEntries.Write(entries);

        var added = DocumentPatch.Diff(docx, withExtra);
        var removed = DocumentPatch.Diff(withExtra, docx);

        await Assert.That(added.Changed.Keys).IsEquivalentTo(["word/extra.xml"]);
        await AssertSameEntries(added.Apply(docx), withExtra);
        await Assert.That(removed.Removed).IsEquivalentTo(["word/extra.xml"]);
        await AssertSameEntries(removed.Apply(withExtra), docx);
    }

    [Test]
    public async Task NoChange_IsAnEmptyPatch_ThatGivesBackTheSameFile()
    {
        var docx = Build(P(R("body")));

        var patch = DocumentPatch.FromBytes(DocumentPatch.Diff(docx, docx).ToBytes());

        await Assert.That(patch.IsEmpty).IsTrue();
        await Assert.That(patch.Apply(docx)).IsSameReferenceAs(docx);
    }

    [Test]
    public async Task BytesThatAreNotAPatch_AreRefused() =>
        await Assert.That(() => DocumentPatch.FromBytes("not a patch"u8.ToArray())).Throws<InvalidDataException>();
}
