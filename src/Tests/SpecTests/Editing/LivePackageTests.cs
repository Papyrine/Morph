using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using static EditDocuments;

// The viewer's edits keep the package of the file each returns open (DocumentReading): the next edit
// to that file starts from its tree, and the parse of it reads that tree rather than the file. Both
// have to come out exactly as from the file itself, over every document of the corpus: what the parse
// lays out, and what the next edit writes, which is where a change parsing makes to the tree and fails
// to undo (a style defined twice dropped, a relative size folded into an extent) would show.
public class LivePackageTests
{
    static readonly ImageExportOptions options = new()
    {
        Dpi = 96,
        FontDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"))
    };

    [Test]
    [MethodDataSource(typeof(EditCorpusTests), nameof(EditCorpusTests.Documents))]
    public async Task EveryDocument_ReadsAndEditsTheSameThroughItsLivePackage(string scenario)
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

        // The package lives on the thread that made the edit, so everything that relies on it runs
        // here, before the first await.
        var (borrowed, first) = EditAndBorrow(docx, paragraph);
        using var stream = new MemoryStream(first);
        var read = DocumentConverter.Parse(stream, null, captureSources: true);
        var (live, fresh) = EditAgain(first, paragraph.Ordinal);

        await Assert.That(borrowed).IsNotNull()
            .Because($"{scenario}'s edit left no package to read");
        await Assert.That(Drawn(borrowed!)).IsEqualTo(Drawn(read))
            .Because($"{scenario} parsed from its live package lays out otherwise than from its file");
        await Assert.That(Parts(live)).IsEqualTo(Parts(fresh))
            .Because($"{scenario}'s next edit wrote otherwise through its live package than through its file");
    }

    // The viewer opens a file for editing before it parses it (DocumentReading.Prepare), and the parse,
    // the reading of its review and outline, and its first edit all read that package.
    [Test]
    [MethodDataSource(typeof(EditCorpusTests), nameof(EditCorpusTests.Documents))]
    public async Task EveryDocument_OpensReadsAndEditsTheSameThroughAPreparedPackage(string scenario)
    {
        var docx = await File.ReadAllBytesAsync(Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), scenario, "input.docx"));
        var (review, outline) = DocumentReading.Read(docx);
        var paragraph = outline.AllowsEditing
            ? outline.Paragraphs.Where(_ => _.Units.Any(unit => unit.Editable)).MaxBy(_ => _.Length)
            : null;
        if (paragraph == null)
        {
            return;
        }

        // As above, everything that relies on the package runs before the first await.
        var (borrowed, reading, prepared) = PrepareReadAndEdit(docx, paragraph);
        using var stream = new MemoryStream(docx);
        var read = DocumentConverter.Parse(stream, null, captureSources: true);
        byte[] fresh;
        using (DocumentReading.Capture())
        {
            fresh = DocumentEditor.Rewrite(docx.ToArray(), paragraph.Ordinal, [Typed(paragraph, "x")], Plain).Document;
        }

        await Assert.That(borrowed).IsNotNull()
            .Because($"{scenario} was parsed without its prepared package");
        await Assert.That(Drawn(borrowed!)).IsEqualTo(Drawn(read))
            .Because($"{scenario} parsed from its prepared package lays out otherwise than from its file");
        await Assert.That(Outlined(reading)).IsEqualTo(Outlined((review, outline)))
            .Because($"{scenario}'s review and outline read otherwise off its prepared package than off its file");
        await Assert.That(Parts(prepared)).IsEqualTo(Parts(fresh))
            .Because($"{scenario}'s first edit wrote otherwise through its prepared package than through its file");
    }

    [Test]
    public async Task AReleasedPackage_IsNotBorrowed()
    {
        var docx = await File.ReadAllBytesAsync(Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), "letters", "01", "input.docx"));
        DocumentReading.Prepare(docx);
        var prepared = DocumentReading.Borrow(docx, _ => 0, out _);
        DocumentReading.Release(docx);
        var released = DocumentReading.Borrow(docx, _ => 0, out _);
        await Assert.That(prepared).IsTrue();
        await Assert.That(released).IsFalse();
    }

    // An edit that fails part-way may leave the tree no longer the file's: its package is let go, and
    // the next edit to that file opens it afresh.
    [Test]
    public async Task AFailedEdit_LetsThePackageGo()
    {
        var docx = await File.ReadAllBytesAsync(Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), "letters", "01", "input.docx"));
        var paragraph = DocumentOutline.Read(docx).Paragraphs.First(_ => _.Units.Any(unit => unit.Editable));
        byte[] first;
        using (DocumentReading.Capture())
        {
            first = DocumentEditor.Rewrite(docx, paragraph.Ordinal, [Typed(paragraph, "x")], Plain).Document;
        }

        var failed = false;
        try
        {
            using (DocumentReading.Capture())
            {
                DocumentEditor.Rewrite(first, int.MaxValue, [Typed(paragraph, "y")], Plain);
            }
        }
        catch (Exception)
        {
            failed = true;
        }

        var kept = DocumentReading.Borrow(first, _ => 0, out _);
        await Assert.That(failed).IsTrue();
        await Assert.That(kept).IsFalse();
    }

    static (ParsedDocument? Borrowed, byte[] First) EditAndBorrow(byte[] docx, EditParagraph paragraph)
    {
        byte[] first;
        using (DocumentReading.Capture())
        {
            first = DocumentEditor.Rewrite(docx, paragraph.Ordinal, [Typed(paragraph, "x")], Plain).Document;
        }

        return (DocumentReading.Borrow(first, _ => DocumentConverter.Parse(_, null, captureSources: true), out var parsed) ? parsed : null, first);
    }

    static (ParsedDocument? Borrowed, (DocumentReview, DocumentOutline) Reading, byte[] Edited) PrepareReadAndEdit(byte[] docx, EditParagraph paragraph)
    {
        DocumentReading.Prepare(docx);
        var borrowed = DocumentReading.Borrow(docx, _ => DocumentConverter.Parse(_, null, captureSources: true), out var parsed) ? parsed : null;
        var reading = DocumentReading.Read(docx);
        using (DocumentReading.Capture())
        {
            return (borrowed, reading, DocumentEditor.Rewrite(docx, paragraph.Ordinal, [Typed(paragraph, "x")], Plain).Document);
        }
    }

    // What the viewer takes from the review and the outline: the comments and changes, and every
    // paragraph's units.
    static string Outlined((DocumentReview Review, DocumentOutline Outline) reading) =>
        $"{reading.Review.Comments.Count} comments, {reading.Review.Changes.Count} changes, " +
        $"{reading.Outline.AllowsEditing} {reading.Outline.Tracking} {reading.Outline.ForcesTracking}\n" +
        string.Join('\n', reading.Outline.Paragraphs.Select(_ => $"{_.Ordinal}: {string.Join(" | ", _.Units)}"));

    // The same second edit through the live package, and through a copy of the file it is of.
    static (byte[] Live, byte[] Fresh) EditAgain(byte[] first, int ordinal)
    {
        var again = DocumentReading.Read(first).Outline.Paragraphs[ordinal];
        using (DocumentReading.Capture())
        {
            var live = DocumentEditor.Rewrite(first, ordinal, [Typed(again, "y")], Plain).Document;
            var fresh = DocumentEditor.Rewrite(first.ToArray(), ordinal, [Typed(again, "y")], Plain).Document;
            return (live, fresh);
        }
    }

    // The paragraph as it reads, with text typed at the end of its last editable unit.
    static NewParagraph Typed(EditParagraph paragraph, string typed)
    {
        var items = paragraph.Units
            .Select((unit, index) => new NewItem(index, unit.Editable ? unit.Text : null))
            .ToList();
        var last = items.FindLastIndex(_ => _.Text != null);
        items[last] = items[last] with
        {
            Text = items[last].Text + typed
        };
        return new(items);
    }

    // Every page as the SVG painter writes it, ids aside, and the pictures it carries.
    static string Drawn(ParsedDocument parsed)
    {
        var laidOut = ImageSharpDocumentConverter.Layout(parsed, options);
        using var context = ImageSharpDocumentConverter.CreateContext(parsed, options);
        var drawn = new StringBuilder();
        foreach (var page in laidOut.Pages)
        {
            var svg = SvgPainter.PaintPage(page, context, "Morph Text");
            drawn.AppendLine(Regex.Replace(svg.Markup, @"\bm\d+x", "m"));
            foreach (var image in svg.Images)
            {
                drawn.AppendLine(Convert.ToHexString(SHA256.HashData(image)));
            }
        }

        return drawn.ToString();
    }

    // Every part of a file: its tree, or a hash of its bytes. Where a namespace is declared is left out:
    // a tree written again keeps declarations the SDK added the first time (numbering.xml's root gains
    // an unused xmlns:p3 for Word 2012's namespace), which one read back from the file has elsewhere.
    static string Parts(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var document = WordprocessingDocument.Open(stream, false);
        var parts = new StringBuilder();
        foreach (var part in document.GetAllParts().OrderBy(_ => _.Uri.OriginalString, StringComparer.Ordinal))
        {
            parts.Append(part.Uri.OriginalString).Append(": ");
            if (part.RootElement is { } root)
            {
                var tree = XElement.Parse(root.OuterXml);
                foreach (var element in tree.DescendantsAndSelf())
                {
                    element.Attributes().Where(_ => _.IsNamespaceDeclaration).Remove();
                }

                parts.AppendLine(tree.ToString(SaveOptions.DisableFormatting));
                continue;
            }

            using var data = part.GetStream(FileMode.Open, FileAccess.Read);
            parts.AppendLine(Convert.ToHexString(SHA256.HashData(data)));
        }

        return parts.ToString();
    }
}
