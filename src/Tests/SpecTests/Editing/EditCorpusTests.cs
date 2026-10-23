using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
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
        var docx = await File.ReadAllBytesAsync(Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), scenario, "input.docx"));
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
        var parsed = DocumentConverter.Parse(stream, null, captureSources: true);
        await Assert.That(parsed.Elements.Count).IsGreaterThan(0);
    }

    // The ribbon's edits to a sample of each document's paragraphs — a style, a list, an indent, a
    // spacing, alternately plain and tracked — and a page break, a table and a picture put in. None
    // may change a paragraph's text; the document has to be one the parser still reads, and one the
    // OpenXML schema finds nothing new wrong with: what Word refuses to open is the failure that matters.
    [Test]
    [MethodDataSource(nameof(Documents))]
    public async Task EveryDocument_TakesTheRibbonsEdits(string scenario)
    {
        var docx = await File.ReadAllBytesAsync(Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), scenario, "input.docx"));
        var outline = DocumentOutline.Read(docx);
        if (!outline.AllowsEditing ||
            outline.Paragraphs.Count == 0)
        {
            return;
        }

        var before = Texts(outline);
        var step = Math.Max(1, outline.Paragraphs.Count / sampled);
        var edited = docx;
        var turn = 0;
        for (var index = 0; index < outline.Paragraphs.Count; index += step)
        {
            var options = turn % 2 == 0 ? Plain : Tracked;
            edited = (turn++ % 4) switch
            {
                0 => DocumentEditor.SetStyle(edited, [index], "Heading2", options),
                1 => DocumentEditor.SetList(edited, [index], ListKind.Bullet, options),
                2 => DocumentEditor.Indent(edited, [index], 1, options),
                _ => DocumentEditor.Space(edited, [index], new(Line: 1.5, BeforePoints: 12), options)
            };
        }

        await Assert.That(string.Join('\n', Texts(DocumentOutline.Read(edited)))).IsEqualTo(string.Join('\n', before))
            .Because($"a paragraph edit to {scenario} changed its text");

        // Put in at the end of the last paragraph that is not in a table, so what follows is the body's.
        var last = DocumentOutline.Read(edited).Paragraphs.LastOrDefault(_ => !_.InTable);
        if (last != null)
        {
            edited = DocumentEditor.InsertPageBreak(edited, last.Ordinal, last.Length, Tracked).Document;
            edited = DocumentEditor.InsertTable(edited, last.Ordinal, 0, 2, 2, Plain).Document;
            edited = DocumentEditor.InsertPicture(edited, last.Ordinal, 0, new(Png, "image/png", 20, 10), Plain).Document;
        }

        using var stream = new MemoryStream(edited);
        var parsed = DocumentConverter.Parse(stream, null, captureSources: true);
        await Assert.That(parsed.Elements.Count).IsGreaterThan(0);

        var added = Errors(edited).Except(Errors(docx)).ToList();
        await Assert.That(added).IsEmpty()
            .Because($"the ribbon's edits made {scenario} invalid: {string.Join("; ", added.Take(3))}");
    }

    // A 1×1 PNG: the picture's bytes are only carried, never decoded.
    static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // What the schema finds wrong with the parts an edit writes: the body, the styles, the numbering.
    static HashSet<string> Errors(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = WordprocessingDocument.Open(stream, false);
        var validator = new DocumentFormat.OpenXml.Validation.OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(package)
            .Where(_ => _.Part?.Uri.OriginalString is "/word/document.xml" or "/word/styles.xml" or "/word/numbering.xml")
            .Select(_ => $"{_.Part!.Uri} {_.Description}")
            .ToHashSet(StringComparer.Ordinal);
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

    // What the viewer reads off an edit's own package (DocumentReading.Capture) has to be what reading
    // the edited file afresh gives, and capturing must not change a byte of the file.
    [Test]
    [MethodDataSource(nameof(Documents))]
    public async Task EveryDocument_ReadsTheSameCapturedAsAfresh(string scenario)
    {
        var docx = await File.ReadAllBytesAsync(Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), scenario, "input.docx"));
        var outline = DocumentOutline.Read(docx);
        if (!outline.AllowsEditing ||
            outline.Paragraphs.FirstOrDefault(_ => _.Units.Any(unit => unit.Editable)) is not { } paragraph)
        {
            return;
        }

        var content = new NewParagraph[] { new(Items(paragraph, typed)) };
        var plain = DocumentEditor.Rewrite(docx, paragraph.Ordinal, content, Tracked).Document;
        byte[] captured;
        using (DocumentReading.Capture())
        {
            captured = DocumentEditor.Rewrite(docx, paragraph.Ordinal, content, Tracked).Document;
        }

        await Assert.That(Differing(captured, plain)).IsEmpty()
            .Because($"capturing changed parts of {scenario}'s edit");

        // A copy of the array is not remembered, so it is read from the bytes.
        var (review, read) = DocumentReading.Read(captured);
        var (freshReview, freshRead) = DocumentReading.Read(captured.ToArray());
        await Assert.That(Json(review)).IsEqualTo(Json(freshReview));
        await Assert.That(Json(read)).IsEqualTo(Json(freshRead));
    }

    // FastDeflate changes how hard the parts an edit writes back are compressed, never what they hold:
    // every part loaded and saved holds the same marked as not.
    [Test]
    [MethodDataSource(nameof(Documents))]
    public async Task EveryDocument_SavesTheSamePartsMarkedFast(string scenario)
    {
        var docx = await File.ReadAllBytesAsync(Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), scenario, "input.docx"));
        var marked = docx.ToArray();
        FastDeflate.Mark(marked);

        await Assert.That(Differing(Resaved(marked), Resaved(docx))).IsEmpty()
            .Because($"marking {scenario} fast changed what its parts hold");
    }

    // An edit writes the parts it changed at Fastest, which is what the marking is for: a runtime that
    // stopped honouring an entry's declared level would leave edits at Optimal, silently.
    [Test]
    public async Task AnEdit_WritesTheBodyAtFastest()
    {
        var docx = await File.ReadAllBytesAsync(Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), "letters", "01", "input.docx"));
        var paragraph = DocumentOutline.Read(docx).Paragraphs.First(_ => _.Units.Any(unit => unit.Editable));
        var content = new NewParagraph[] { new(Items(paragraph, typed)) };
        var edited = DocumentEditor.Rewrite(docx, paragraph.Ordinal, content, Tracked).Document;

        await using var archive = new ZipArchive(new MemoryStream(edited));
        var body = archive.GetEntry("word/document.xml")!;
        using var fastest = new MemoryStream();
        await using (var deflate = new DeflateStream(fastest, CompressionLevel.Fastest, true))
        {
            deflate.Write(Entries(edited)["word/document.xml"]);
        }

        await Assert.That(body.CompressedLength).IsEqualTo(fastest.Length);
    }

    // The package with every part's DOM loaded and saved back, as an edit that touched them all would.
    static byte[] Resaved(byte[] docx)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        stream.Position = 0;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            foreach (var part in document.GetAllParts())
            {
                _ = part.RootElement;
            }
        }

        return stream.ToArray();
    }

    // The parts whose content differs between two packages, or that one has and the other not.
    static List<string> Differing(byte[] first, byte[] second)
    {
        var one = Entries(first);
        var other = Entries(second);
        return one.Keys.Union(other.Keys)
            .Where(_ => !one.TryGetValue(_, out var a) || !other.TryGetValue(_, out var b) || !a.SequenceEqual(b))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    static Dictionary<string, byte[]> Entries(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package));
        return archive.Entries.ToDictionary(
            _ => _.FullName,
            _ =>
            {
                using var stream = _.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            });
    }

    static string Json(object value) =>
        JsonSerializer.Serialize(value, value.GetType());
}
