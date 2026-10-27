using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;

// Typing over text selected across paragraphs (WordProbe E-10, E-10b, E-10c, R-E-4). Unlike Backspace and
// Delete, which leave the paragraphs at either end apart (Delete), a typed character joins them: what is
// left is one paragraph with the FIRST one's properties, and the character goes where the selection
// began, formatted as the first character it replaced. Tracked, the selection is struck, its marks too,
// and what is typed is an insertion where the selection began.
static partial class DocumentEditor
{
    /// <summary>
    /// Replaces the text between two places with typed text, the paragraphs the places are in joined into
    /// the first. The place returned is after what was typed.
    /// </summary>
    public static EditResult TypeOver(byte[] docx, SourcePosition start, SourcePosition end, string text, EditOptions options) =>
        Edit(docx, options, edit => edit.TypeOver(start, end, text));

    sealed partial class Editing
    {
        public (Paragraph Paragraph, int Offset)? TypeOver(SourcePosition start, SourcePosition end, string text)
        {
            var stretches = Between(start, end);
            if (stretches.Count == 0)
            {
                return null;
            }

            var first = stretches[0][0].Run.Ancestors<Paragraph>().First();
            var last = stretches[^1][0].Run.Ancestors<Paragraph>().First();
            var properties = first.ParagraphProperties?.CloneNode(true);
            var look = (OoxmlRunProperties?) stretches[0][0].Run.RunProperties?.CloneNode(true);
            if (Delete(start, end) is not var (paragraph, offset))
            {
                return null;
            }

            if (!options.Track &&
                !ReferenceEquals(first, last))
            {
                (paragraph, offset) = Join(first);
                // Emptied, the first paragraph went into the last and left it the last's properties.
                if (!ReferenceEquals(paragraph, first))
                {
                    paragraph.ParagraphProperties?.Remove();
                    if (properties != null)
                    {
                        paragraph.PrependChild(properties);
                    }
                }
            }

            var run = new OoxmlRun(new Text(text)
            {
                Space = SpaceProcessingModeValues.Preserve
            });
            if (look != null)
            {
                foreach (var revision in look.ChildElements.Where(_ => _ is Inserted or Deleted or MoveFrom or MoveTo or RunPropertiesChange).ToList())
                {
                    revision.Remove();
                }

                run.PrependChild(look);
            }

            Put(paragraph, offset, [run]);
            return (paragraph, offset + text.Length);
        }
    }
}
