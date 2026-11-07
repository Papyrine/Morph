using TUnit.Assertions.Enums;
using static EditDocuments;

// The merge over every document of the corpus, as a session holds it (every part stamped with ids): one
// person types into the body's first paragraph with text and another into its last, at once. The merged
// document reads as both edits made one after the other.
public class MergeCorpusTests
{
    [Test]
    [MethodDataSource(typeof(EditCorpusTests), nameof(EditCorpusTests.Documents))]
    public async Task EveryDocument_MergesEditsToItsFirstAndLastParagraphs(string scenario)
    {
        var docx = await File.ReadAllBytesAsync(Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), scenario, "input.docx"));
        if (!DocumentOutline.Read(docx).AllowsEditing)
        {
            return;
        }

        var original = ParagraphIds.StampAll(docx);
        var editable = DocumentOutline.Read(original).Paragraphs
            .Where(_ => _.Part == ReviewPart.Body && _.Units.Any(unit => unit.Editable))
            .ToList();
        if (editable.Count < 2)
        {
            return;
        }

        var first = editable[0];
        var last = editable[^1];
        var mine = Typed(original, first, " — mine");
        var theirs = Typed(original, last, " — theirs");

        var outcome = DocumentMerge.Merge(original, mine, theirs);

        await Assert.That(outcome.Refused).IsFalse()
            .Because($"{scenario}: {outcome.Reason}");
        var both = Typed(mine, DocumentOutline.Read(mine).Paragraphs[last.Ordinal], " — theirs");
        await Assert.That(ParagraphIdTests.Texts(outcome.Document!)).IsEquivalentTo(ParagraphIdTests.Texts(both), CollectionOrdering.Matching)
            .Because($"{scenario} merged otherwise than its two edits made in turn");
    }

    // The paragraph with the text of its first stretch of text added to.
    static byte[] Typed(byte[] docx, EditParagraph paragraph, string added)
    {
        var first = paragraph.Units.First(_ => _.Editable);
        var content = Reads([.. paragraph.Units.Select(Item)]);
        using var stamping = ParagraphIds.Stamping();
        return Rewrite(docx, paragraph.Ordinal, Plain, content);

        NewItem Item(EditUnit unit, int index)
        {
            if (!unit.Editable)
            {
                return K(index);
            }

            if (ReferenceEquals(unit, first))
            {
                return T(index, unit.Text + added);
            }

            return T(index, unit.Text);
        }
    }
}
