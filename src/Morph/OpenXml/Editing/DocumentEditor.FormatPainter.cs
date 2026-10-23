using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;

// Home > Format Painter. Word-probed 2026-10-23 (Selection.CopyFormat and PasteFormat over COM, the
// painter's own commands; the source a caret in a bold red 16pt word of a centred, indented paragraph):
//
//   - Painted onto part of a paragraph, the text takes the character formatting of the run the caret
//     was in, in place of its own (w:rPr <b/><color FF0000/><sz 32/>), and the paragraph is untouched.
//   - Painted onto a whole paragraph, mark and all, the paragraph takes the source paragraph's
//     formatting too (w:ind, w:jc), and its mark the character formatting.
//
// A source of a whole paragraph painted onto part of another changed nothing in Word; it is not
// modelled, a copy here being always the caret's run and its paragraph.
static partial class DocumentEditor
{
    /// <summary>
    /// What Format Painter picks up at a place: the character formatting of the run the place is in
    /// (the paragraph's first run where there is none) and the paragraph's formatting.
    /// </summary>
    public static FormatCopy CopyFormat(byte[] docx, int paragraph, SourcePosition? at)
    {
        using var stream = new MemoryStream(docx, false);
        using var document = WordprocessingDocument.Open(stream, false);
        var editing = new Editing(document, new("", DateTimeOffset.MinValue, false));
        return editing.Copy(paragraph, at);
    }

    /// <summary>
    /// Paints what <see cref="CopyFormat"/> picked up onto the text between two places; with
    /// <paramref name="whole"/>, the paragraphs it spans are taken whole, and take its paragraph
    /// formatting too.
    /// </summary>
    public static byte[] PasteFormat(byte[] docx, SourcePosition start, SourcePosition end, IReadOnlyList<int> whole, FormatCopy copy, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Paint(start, end, edit.FindAll(whole), copy);
                return null;
            }).Document;

    sealed partial class Editing
    {
        public FormatCopy Copy(int ordinal, SourcePosition? at)
        {
            var paragraph = Find(ordinal);
            OoxmlRun? run = null;
            if (at is { } place)
            {
                run = SourceRuns.IndexAll(Stories()).FirstOrDefault(_ => _.Value == place.Run).Key;
            }

            run ??= paragraph.Descendants<OoxmlRun>().FirstOrDefault(_ => _.GetFirstChild<Text>() != null);

            var characters = new OoxmlRunProperties(
                (run?.RunProperties?.ChildElements ?? []).Where(Painted).Select(_ => _.CloneNode(true)));
            var paragraphs = new OoxmlParagraphProperties(
                (paragraph.ParagraphProperties?.ChildElements ?? [])
                .Where(_ => _ is not (SectionProperties or ParagraphPropertiesChange or ParagraphMarkRunProperties))
                .Select(_ => _.CloneNode(true)));
            return new(characters, paragraphs);
        }

        // Formatting a painter carries: the run's own and its character style, not its revisions.
        static bool Painted(OpenXmlElement property) =>
            property is not (RunPropertiesChange or Inserted or Deleted or MoveFrom or MoveTo);

        public void Paint(SourcePosition start, SourcePosition end, List<Paragraph> whole, FormatCopy copy)
        {
            var characters = copy.Run;
            foreach (var cells in Between(start, end).AsEnumerable().Reverse())
            {
                var run = Isolate(cells);
                if (run.RunProperties?.ChildElements.Any(Painted) != true &&
                    !characters.HasChildren)
                {
                    continue;
                }

                var properties = Rerun(run);
                foreach (var child in properties.ChildElements.Where(Painted).ToList())
                {
                    child.Remove();
                }

                foreach (var child in characters.ChildElements)
                {
                    properties.AddChild(child.CloneNode(true));
                }

                if (!properties.HasChildren)
                {
                    properties.Remove();
                }
            }

            var formatting = copy.Paragraph;
            foreach (var paragraph in whole)
            {
                var properties = Reformat(paragraph);
                foreach (var child in properties.ChildElements.Where(_ => _ is not (SectionProperties or ParagraphPropertiesChange or ParagraphMarkRunProperties)).ToList())
                {
                    child.Remove();
                }

                foreach (var child in formatting.ChildElements)
                {
                    properties.AddChild(child.CloneNode(true));
                }

                var mark = Mark(paragraph);
                foreach (var child in mark.ChildElements.Where(Painted).ToList())
                {
                    child.Remove();
                }

                foreach (var child in characters.ChildElements)
                {
                    mark.AddChild(child.CloneNode(true));
                }

                if (!mark.HasChildren)
                {
                    mark.Remove();
                }

                if (!properties.HasChildren)
                {
                    properties.Remove();
                }
            }
        }
    }
}
