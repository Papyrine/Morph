/// <summary>
/// The footnote/endnote appendix on the engine path. Word draws footnotes at the bottom of the page
/// where the reference appears; the shared <see cref="NotesAppendix"/> lists them at document end so
/// the content isn't lost — production's long-standing behaviour, previously triplicated across the
/// three page renderers, and silently missing from the engine path once coverage went total. These
/// pin the builder's rules and prove the <see cref="Fragmenter"/> flow actually carries the appendix.
/// </summary>
public class NotesAppendixTests
{
    static string DocumentCaptureFile => Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "document_capture", "01", "input.docx");

    [Test]
    public async Task Note_text_takes_the_FootnoteText_style_size_and_falls_back_to_Word_built_in_10pt()
    {
        var styled = new ParsedDocument
        {
            PageSettings = new(),
            Elements = [],
            Footnotes = [new() { Id = "2", Text = "a note" }],
            Endnotes = [new() { Id = "2", Text = "an endnote" }],
            FootnoteTextSizePoints = 8,
            EndnoteTextSizePoints = 12
        };
        var unstyled = new ParsedDocument
        {
            PageSettings = new(),
            Elements = [],
            Footnotes = [new() { Id = "2", Text = "a note" }],
            Endnotes = [new() { Id = "2", Text = "an endnote" }]
        };

        var styledParagraphs = NotesAppendix.BuildElements(styled);
        var unstyledParagraphs = NotesAppendix.BuildElements(unstyled);

        // [0] Footnotes heading, [1] the note, [2] Endnotes heading, [3] the endnote.
        await Assert.That(styledParagraphs[1].Runs[1].Properties.FontSizePoints).IsEqualTo(8d);
        await Assert.That(styledParagraphs[1].Runs[0].Properties.FontSizePoints).IsEqualTo(8d);
        await Assert.That(styledParagraphs[3].Runs[1].Properties.FontSizePoints).IsEqualTo(12d);
        await Assert.That(unstyledParagraphs[1].Runs[1].Properties.FontSizePoints).IsEqualTo(10d);
        await Assert.That(unstyledParagraphs[3].Runs[1].Properties.FontSizePoints).IsEqualTo(10d);
    }

    [Test]
    public async Task The_parser_reads_the_note_style_sizes()
    {
        using var stream = BuildDocumentWithNoteStyles(footnoteHalfPoints: 24, endnoteHalfPoints: 16);
        var document = new DocumentParser().Parse(stream);

        await Assert.That(document.FootnoteTextSizePoints).IsEqualTo(12d);
        await Assert.That(document.EndnoteTextSizePoints).IsEqualTo(8d);

        using var bare = BuildDocumentWithNoteStyles(null, null);
        var plain = new DocumentParser().Parse(bare);

        await Assert.That(plain.FootnoteTextSizePoints).IsNull();
        await Assert.That(plain.EndnoteTextSizePoints).IsNull();
    }

    static MemoryStream BuildDocumentWithNoteStyles(int? footnoteHalfPoints, int? endnoteHalfPoints)
    {
        var body = new DocumentFormat.OpenXml.Wordprocessing.Body(
            new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("x"))));

        var stream = new MemoryStream();
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = [with(body)];

            var styles = new DocumentFormat.OpenXml.Wordprocessing.Styles();
            if (footnoteHalfPoints is { } footnote)
            {
                styles.Append(NoteStyle("FootnoteText", footnote));
            }

            if (endnoteHalfPoints is { } endnote)
            {
                styles.Append(NoteStyle("EndnoteText", endnote));
            }

            mainPart.AddNewPart<DocumentFormat.OpenXml.Packaging.StyleDefinitionsPart>().Styles = styles;
        }

        stream.Position = 0;
        return stream;
    }

    static DocumentFormat.OpenXml.Wordprocessing.Style NoteStyle(string id, int halfPoints) => new()
    {
        Type = DocumentFormat.OpenXml.Wordprocessing.StyleValues.Paragraph,
        StyleId = id,
        StyleName = new DocumentFormat.OpenXml.Wordprocessing.StyleName { Val = id },
        StyleRunProperties = new DocumentFormat.OpenXml.Wordprocessing.StyleRunProperties(
            new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = halfPoints.ToString() })
    };

    [Test]
    public async Task Separator_stubs_are_skipped_and_numbering_is_sequential()
    {
        var document = new ParsedDocument
        {
            PageSettings = new(),
            Elements = [],
            Footnotes =
            [
                new() { Id = "0", Text = "separator stub" },
                new() { Id = "-1", Text = "continuation stub" },
                new() { Id = "2", Text = "first real note" },
                new() { Id = "3", Text = "second real note" },
                new() { Id = "4", Text = "   " },
            ],
        };

        var paragraphs = NotesAppendix.BuildElements(document);

        // Heading + the two real notes; the stubs and the whitespace-only note drop.
        await Assert.That(paragraphs.Count).IsEqualTo(3);
        await Assert.That(paragraphs[0].Runs[0].Text).IsEqualTo("Footnotes");
        await Assert.That(paragraphs[1].Runs[0].Text).IsEqualTo("1. ");
        await Assert.That(paragraphs[1].Runs[1].Text).IsEqualTo("first real note");
        await Assert.That(paragraphs[2].Runs[0].Text).IsEqualTo("2. ");
        await Assert.That(paragraphs[2].Runs[1].Text).IsEqualTo("second real note");
    }

    [Test]
    public async Task A_document_without_notes_appends_nothing()
    {
        var document = new ParsedDocument
        {
            PageSettings = new(),
            Elements = [new ParagraphElement { Runs = [], Properties = new() }],
        };

        await Assert.That(NotesAppendix.BuildElements(document)).IsEmpty();
        // Same list instance — no per-render allocation for the common case.
        await Assert.That(ReferenceEquals(NotesAppendix.AppendTo(document), document.Elements)).IsTrue();
    }

    [Test]
    public async Task The_engine_flow_carries_the_appendix()
    {
        var document = new DocumentParser().Parse(DocumentCaptureFile);
        await Assert.That(document.Footnotes.Count).IsGreaterThan(0);

        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout(
            NotesAppendix.AppendTo(document),
            document.PageSettings);

        var text = string.Join("\n", laidOut.Pages
            .SelectMany(_ => _.Items)
            .OfType<PlacedLine>()
            .SelectMany(_ => _.Runs)
            .Select(_ => _.Text));

        await Assert.That(text).Contains("Footnotes");
        await Assert.That(text).Contains("This is a footnote.");
        await Assert.That(text).Contains("Endnotes");
        await Assert.That(text).Contains("This is an endnote.");
    }
}
