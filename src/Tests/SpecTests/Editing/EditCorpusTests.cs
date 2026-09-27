using static EditDocuments;

// The editor against every document of the corpus rather than the ones written to test it: real
// documents hold content controls, fields, text boxes, tables in tables, and markup nobody would
// think to write a fixture for. Each takes two edits to a sample of its paragraphs — one that changes
// nothing, which has to leave the document's text exactly as it was, and one that types at a
// paragraph's end, which has to change that paragraph and no other — and has to be a document the
// parser still reads afterwards.
public class EditCorpusTests
{
    const int sampled = 8;
    const string typed = " — edited";

    public static IEnumerable<Func<string>> Documents() =>
        Directory
            .EnumerateFiles(ScenarioInputs.Root(ScenarioFormat.Word), "input.docx", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(_ => Path.GetRelativePath(ScenarioInputs.Root(ScenarioFormat.Word), Path.GetDirectoryName(_)!).Replace('\\', '/'))
            .Select(_ => (Func<string>) (() => _));

    [Test]
    [MethodDataSource(nameof(Documents))]
    public async Task EveryDocument_TakesAnEditToAnyParagraph(string scenario)
    {
        var docx = File.ReadAllBytes(Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), scenario, "input.docx"));
        var outline = DocumentOutline.Read(docx);
        if (!outline.AllowsEditing ||
            outline.Paragraphs.Count == 0)
        {
            return;
        }

        var before = Texts(outline);
        var step = Math.Max(1, outline.Paragraphs.Count / sampled);
        var edited = docx;
        var expected = before.ToList();
        for (var index = 0; index < outline.Paragraphs.Count; index += step)
        {
            var paragraph = outline.Paragraphs[index];

            // As it is: nothing may change.
            var same = DocumentEditor.Rewrite(docx, index, [new(Items(paragraph, ""))], Plain).Document;
            await Assert.That(Texts(DocumentOutline.Read(same)).SequenceEqual(before)).IsTrue()
                .Because($"rewriting paragraph {index} of {scenario} as it is changed the document");

            // With something typed at its end, on top of the edits before it.
            edited = DocumentEditor.Rewrite(edited, index, [new(Items(paragraph, typed))], index % 2 == 0 ? Plain : Tracked).Document;
            expected[index] += typed;
        }

        var after = DocumentOutline.Read(edited);
        await Assert.That(string.Join('\n', Texts(after))).IsEqualTo(string.Join('\n', expected));

        using var stream = new MemoryStream(edited);
        var parsed = DocumentConverter.Parse(stream, null, null, captureSources: true);
        await Assert.That(parsed.Elements.Count).IsGreaterThan(0);
    }

    // A paragraph as it is, with something typed after the last of its text.
    static List<NewItem> Items(EditParagraph paragraph, string more)
    {
        var items = new List<NewItem>();
        var last = paragraph.Units.ToList().FindLastIndex(_ => _.Editable);
        for (var index = 0; index < paragraph.Units.Count; index++)
        {
            var unit = paragraph.Units[index];
            if (!unit.Editable)
            {
                items.Add(K(index));
                continue;
            }

            var text = unit.Text;
            if (index == last)
            {
                text += more;
            }

            items.Add(T(index, text));
        }

        if (last < 0 &&
            more.Length > 0)
        {
            items.Add(T(-1, more));
        }

        return items;
    }

    // Each paragraph's text, with a mark where something that is not text stands. Typed after the
    // last text of a paragraph that ends in a field, the text stands before the field.
    static List<string> Texts(DocumentOutline outline) =>
        outline.Paragraphs
            .Select(_ => string.Concat(_.Units.Where(unit => unit.Editable).Select(unit => unit.Text)))
            .ToList();
}
