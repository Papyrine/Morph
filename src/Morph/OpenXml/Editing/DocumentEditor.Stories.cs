using OoxmlFootnote = DocumentFormat.OpenXml.Wordprocessing.Footnote;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;

// Headers, footers and footnotes: the parts of a document beside its body that an editor writes in
// too. Their paragraphs are numbered after the body's (SourceRuns.Stories), so everything that edits
// a paragraph by its ordinal edits theirs. Word-probed 2026-10-19 (Word 365 over COM, a header, a
// footer and a footnote added to a blank document):
//
//   - A header is a part of its own holding a paragraph in the Header style
//     (<w:hdr><w:p><w:pPr><w:pStyle w:val="Header"/></w:pPr>…), named by the section's properties as
//     <w:headerReference w:type="default" r:id="…"/>, ahead of the page size. A footer likewise, in
//     Footer. A section that names none shows the one before it.
//   - A footnote's reference is a run in the Footnote Reference style holding
//     <w:footnoteReference w:id="1"/>. The note is a w:footnote of that id in the footnotes part, whose
//     first two are the separator (id -1) and the continuation separator (id 0); its paragraph is in
//     Footnote Text, and starts with the note's own mark (w:footnoteRef, in Footnote Reference) and a
//     space before its text.
static partial class DocumentEditor
{
    /// <summary>
    /// The header — or, <paramref name="footer"/>, the footer — of the section the paragraph is in, made
    /// if the section shows none. The place returned is the start of its first paragraph.
    /// </summary>
    public static EditResult EditHeader(byte[] docx, int paragraph, bool footer, EditOptions options) =>
        Edit(docx, options, edit => edit.Band(paragraph, footer));

    /// <summary>
    /// Puts a footnote at a place: its reference there, and its note at the end of the footnotes. The
    /// place returned is the end of the note's paragraph, where its text is typed.
    /// </summary>
    public static EditResult InsertFootnote(byte[] docx, int paragraph, int offset, EditOptions options) =>
        Edit(docx, options, edit => edit.Footnote(paragraph, offset));

    sealed partial class Editing
    {
        public (Paragraph Paragraph, int Offset) Band(int ordinal, bool footer)
        {
            var section = SectionOf(ordinal);
            if (Shown(section, footer) is { } existing)
            {
                var first = existing.Descendants<Paragraph>().FirstOrDefault();
                if (first == null)
                {
                    first = new();
                    existing.AppendChild(first);
                }

                return (first, 0);
            }

            var style = footer ? "Footer" : "Header";
            Define(style);
            var paragraph = new Paragraph(new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(new ParagraphStyleId
            {
                Val = style
            }));
            var id = NextRelationship(main);
            if (footer)
            {
                var part = main.AddNewPart<FooterPart>(id);
                part.Footer = [with(paragraph)];
                section.PrependChild(new FooterReference
                {
                    Type = HeaderFooterValues.Default,
                    Id = id
                });
            }
            else
            {
                var part = main.AddNewPart<HeaderPart>(id);
                part.Header = [with(paragraph)];
                section.PrependChild(new HeaderReference
                {
                    Type = HeaderFooterValues.Default,
                    Id = id
                });
            }

            return (paragraph, 0);
        }

        // The header or footer a section shows on its pages: the one it names, or else the one the
        // section before it shows.
        OpenXmlPartRootElement? Shown(SectionProperties section, bool footer)
        {
            var sections = main.Document!.Body!.Descendants<SectionProperties>().ToList();
            for (var index = sections.IndexOf(section); index >= 0; index--)
            {
                var named = footer
                    ? sections[index].Elements<FooterReference>().FirstOrDefault(_ => _.Type?.Value is null || _.Type.Value == HeaderFooterValues.Default)?.Id?.Value
                    : sections[index].Elements<HeaderReference>().FirstOrDefault(_ => _.Type?.Value is null || _.Type.Value == HeaderFooterValues.Default)?.Id?.Value;
                if (named != null &&
                    main.GetPartById(named) is { RootElement: OpenXmlPartRootElement root })
                {
                    return root;
                }
            }

            return null;
        }

        public (Paragraph Paragraph, int Offset) Footnote(int ordinal, int offset)
        {
            var paragraph = Find(ordinal);
            if (!ReferenceEquals(PartOf(paragraph), main))
            {
                throw new InvalidOperationException("A footnote goes in the body of the document.");
            }

            var point = At(paragraph, offset);
            while (point.Parent is Hyperlink)
            {
                point = Divide(point);
            }

            Define("FootnoteReference");
            Define("FootnoteText");
            var notes = Notes();
            var id = notes.Elements<OoxmlFootnote>().Select(_ => _.Id?.Value ?? 0).DefaultIfEmpty(0).Max() + 1;
            if (id < 1)
            {
                id = 1;
            }

            Add(
                point,
                new(Referenced(),
                    new FootnoteReference
                    {
                        Id = id
                    }));

            var note = new Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(new ParagraphStyleId
                {
                    Val = "FootnoteText"
                }));
            var runs = new OpenXmlElement[]
            {
                new OoxmlRun(Referenced(), new FootnoteReferenceMark()),
                new OoxmlRun(new Text(" ")
                {
                    Space = SpaceProcessingModeValues.Preserve
                })
            };
            if (options.Track)
            {
                var inserted = Stamp(new InsertedRun());
                inserted.Append(runs);
                note.AppendChild(inserted);
                Mark(note).PrependChild(Stamp(new Inserted()));
            }
            else
            {
                note.Append(runs);
            }

            notes.AppendChild(new OoxmlFootnote(note)
            {
                Id = id
            });
            return (note, int.MaxValue);

            static DocumentFormat.OpenXml.Wordprocessing.RunProperties Referenced() =>
            [
                with(new RunStyle
                {
                    Val = "FootnoteReference"
                })
            ];
        }

        // The footnotes, made with Word's two separators where the document has none.
        Footnotes Notes()
        {
            var part = main.FootnotesPart ?? main.AddNewPart<FootnotesPart>(NextRelationship(main));
            if (part.Footnotes is { } existing)
            {
                return existing;
            }

            part.Footnotes =
            [
                with(Separator(-1, FootnoteEndnoteValues.Separator, new SeparatorMark()),
                    Separator(0, FootnoteEndnoteValues.ContinuationSeparator, new ContinuationSeparatorMark()))
            ];
            return part.Footnotes;

            static OoxmlFootnote Separator(int id, FootnoteEndnoteValues type, OpenXmlElement mark) =>
                new(new Paragraph(
                    new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(new SpacingBetweenLines
                    {
                        After = "0",
                        Line = "240",
                        LineRule = LineSpacingRuleValues.Auto
                    }),
                    new OoxmlRun(mark)))
                {
                    Type = type,
                    Id = id
                };
        }
    }
}
