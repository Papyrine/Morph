using OoxmlFootnote = DocumentFormat.OpenXml.Wordprocessing.Footnote;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlEndnote = DocumentFormat.OpenXml.Wordprocessing.Endnote;

// Headers, footers and footnotes: the parts of a document beside its body that an editor writes in
// too. Their paragraphs are numbered after the body's (SourceRuns.Stories), so everything that edits
// a paragraph by its ordinal edits theirs. Word-probed 2026-10-19 (Word 365 over COM, a header, a
// footer and a footnote added to a blank document):
//
//   - A header is a part of its own holding a paragraph in the Header style
//     (<w:hdr><w:p><w:pPr><w:pStyle w:val="Header"/></w:pPr>…), named by the section's properties as
//     <w:headerReference w:type="default" r:id="…"/>, ahead of the page size. A footer likewise, in
//     Footer. A section that names none shows the one before it.
//   - An endnote is written as a footnote is, in the endnotes part and the Endnote Reference and
//     Endnote Text styles (w:endnoteReference, w:endnoteRef). Word makes both notes parts with their
//     separators, and names the separators in the settings (<w:footnotePr>, <w:endnotePr>).
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
        Edit(docx, options, edit => edit.Note(paragraph, offset, false));

    /// <summary>
    /// Puts an endnote at a place, as <see cref="InsertFootnote"/> puts a footnote: its reference there,
    /// its note at the end of the endnotes, and the place returned the end of the note's paragraph.
    /// </summary>
    public static EditResult InsertEndnote(byte[] docx, int paragraph, int offset, EditOptions options) =>
        Edit(docx, options, edit => edit.Note(paragraph, offset, true));

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

        public (Paragraph Paragraph, int Offset) Note(int ordinal, int offset, bool endnote)
        {
            var paragraph = Find(ordinal);
            if (!ReferenceEquals(PartOf(paragraph), main))
            {
                throw new InvalidOperationException("A note goes in the body of the document.");
            }

            var point = At(paragraph, offset);
            while (point.Parent is Hyperlink)
            {
                point = Divide(point);
            }

            var kind = endnote ? "Endnote" : "Footnote";
            Define($"{kind}Reference");
            Define($"{kind}Text");
            var notes = Notes(endnote);
            var id = notes.Elements<FootnoteEndnoteType>().Select(_ => _.Id?.Value ?? 0).DefaultIfEmpty(0).Max() + 1;
            if (id < 1)
            {
                id = 1;
            }

            FootnoteEndnoteReferenceType reference = endnote
                ? new EndnoteReference
                {
                    Id = id
                }
                : new FootnoteReference
                {
                    Id = id
                };
            Add(point, new(Referenced(kind), reference));

            var note = new Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(new ParagraphStyleId
                {
                    Val = $"{kind}Text"
                }));
            OpenXmlElement mark = endnote ? new EndnoteReferenceMark() : new FootnoteReferenceMark();
            var runs = new OpenXmlElement[]
            {
                new OoxmlRun(Referenced(kind), mark),
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

            FootnoteEndnoteType made = endnote ? new OoxmlEndnote(note) : new OoxmlFootnote(note);
            made.Id = id;
            notes.AppendChild(made);
            return (note, int.MaxValue);

            static DocumentFormat.OpenXml.Wordprocessing.RunProperties Referenced(string kind) =>
            [
                with(new RunStyle
                {
                    Val = $"{kind}Reference"
                })
            ];
        }

        // The footnotes or the endnotes, made with Word's two separators where the document has none,
        // and named in the settings as Word names them (<w:footnotePr><w:footnote w:id="-1"/>…).
        OpenXmlCompositeElement Notes(bool endnote)
        {
            if (endnote)
            {
                var endnotes = main.EndnotesPart ?? main.AddNewPart<EndnotesPart>(NextRelationship(main));
                if (endnotes.Endnotes is { } existing)
                {
                    return existing;
                }

                endnotes.Endnotes =
                [
                    with(Separator<OoxmlEndnote>(-1, FootnoteEndnoteValues.Separator, new SeparatorMark()),
                        Separator<OoxmlEndnote>(0, FootnoteEndnoteValues.ContinuationSeparator, new ContinuationSeparatorMark()))
                ];
                Name(new EndnoteDocumentWideProperties(new EndnoteSpecialReference
                {
                    Id = -1
                }, new EndnoteSpecialReference
                {
                    Id = 0
                }));
                return endnotes.Endnotes;
            }

            var footnotes = main.FootnotesPart ?? main.AddNewPart<FootnotesPart>(NextRelationship(main));
            if (footnotes.Footnotes is { } current)
            {
                return current;
            }

            footnotes.Footnotes =
            [
                with(Separator<OoxmlFootnote>(-1, FootnoteEndnoteValues.Separator, new SeparatorMark()),
                    Separator<OoxmlFootnote>(0, FootnoteEndnoteValues.ContinuationSeparator, new ContinuationSeparatorMark()))
            ];
            Name(new FootnoteDocumentWideProperties(new FootnoteSpecialReference
            {
                Id = -1
            }, new FootnoteSpecialReference
            {
                Id = 0
            }));
            return footnotes.Footnotes;

            static T Separator<T>(int id, FootnoteEndnoteValues type, OpenXmlElement mark)
                where T : FootnoteEndnoteType, new()
            {
                var separator = new T
                {
                    Type = type,
                    Id = id
                };
                separator.AppendChild(new Paragraph(
                    new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(new SpacingBetweenLines
                    {
                        After = "0",
                        Line = "240",
                        LineRule = LineSpacingRuleValues.Auto
                    }),
                    new OoxmlRun(mark)));
                return separator;
            }

            // In the settings' schema order, footnotePr then endnotePr, both before compat.
            void Name(OpenXmlElement properties)
            {
                if (main.DocumentSettingsPart?.Settings is not { } settings ||
                    settings.ChildElements.Any(_ => _.GetType() == properties.GetType()))
                {
                    return;
                }

                OpenXmlElement? before = settings.GetFirstChild<EndnoteDocumentWideProperties>();
                if (properties is EndnoteDocumentWideProperties || before == null)
                {
                    before = settings.GetFirstChild<Compatibility>();
                }

                if (before == null)
                {
                    return;
                }

                settings.InsertBefore(properties, before);
            }
        }
    }
}
