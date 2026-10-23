using OoxmlTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;

// A section's page — its margins, its size, which way up it is — and section breaks. Word-probed
// 2026-10-19 (Word 365 over COM, a two-paragraph document from the blank template):
//
//   - A section is the paragraphs up to and including the one whose properties hold its w:sectPr, or
//     up to the end, whose w:sectPr is the body's last child. What the caret's section is, is the
//     first w:sectPr at or after the caret's paragraph.
//   - Margins are w:pgMar top, right, bottom and left: Normal 1440 all round, Narrow 720, Moderate
//     1440 above and below and 1080 either side, Wide 1440 and 2880.
//   - Landscape is the page's width and height exchanged and w:orient="landscape"; the margins turn
//     with it (portrait's top, right, bottom and left become its right, bottom, left and top).
//   - Letter is 12240 by 15840; A4 is Word's own 11906 by 16838.
//   - A section break after "First" puts a copy of the section's w:sectPr into the paragraph's
//     properties, ending the section there; the section after it keeps the w:sectPr it had, and
//     says in it how it starts — w:type="continuous" for a continuous break, nothing for one that
//     starts a new page.
//
// None of it is tracked: a section's properties are changed as they stand.
static partial class DocumentEditor
{
    /// <summary>The page of the section the paragraph is in.</summary>
    public static PageSetup PageSetupOf(byte[] docx, int paragraph)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var editing = new Editing(document, new("", DateTimeOffset.MinValue, false));
        return Setup(editing.SectionOf(paragraph));
    }

    /// <summary>Sets the margins of the section the paragraph is in, in twips.</summary>
    public static byte[] SetMargins(byte[] docx, int paragraph, int top, int right, int bottom, int left, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                var margins = Margins(edit.SectionOf(paragraph));
                margins.Top = top;
                margins.Right = (uint) right;
                margins.Bottom = bottom;
                margins.Left = (uint) left;
                return null;
            }).Document;

    /// <summary>Turns the page of the section the paragraph is in on its side, or back.</summary>
    public static byte[] SetOrientation(byte[] docx, int paragraph, bool landscape, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                var section = edit.SectionOf(paragraph);
                var size = Size(section);
                if (Landscape(size) == landscape)
                {
                    return null;
                }

                var (width, height) = ((int) (size.Width?.Value ?? 11906), (int) (size.Height?.Value ?? 16838));
                size.Width = (uint) height;
                size.Height = (uint) width;
                size.Orient = landscape ? PageOrientationValues.Landscape : null;

                var margins = Margins(section);
                var (top, right, bottom, left) = (margins.Top?.Value ?? 1440, (int) (margins.Right?.Value ?? 1440), margins.Bottom?.Value ?? 1440, (int) (margins.Left?.Value ?? 1440));
                if (landscape)
                {
                    (margins.Top, margins.Right, margins.Bottom, margins.Left) = (left, (uint) top, right, (uint) bottom);
                }
                else
                {
                    (margins.Top, margins.Right, margins.Bottom, margins.Left) = (right, (uint) bottom, left, (uint) top);
                }

                return null;
            }).Document;

    /// <summary>
    /// Sets the paper of the section the paragraph is in, given as it stands upright, in twips; a page
    /// on its side stays on its side.
    /// </summary>
    public static byte[] SetPaperSize(byte[] docx, int paragraph, int width, int height, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                var size = Size(edit.SectionOf(paragraph));
                if (Landscape(size))
                {
                    (width, height) = (height, width);
                }

                size.Width = (uint) width;
                size.Height = (uint) height;
                return null;
            }).Document;

    /// <summary>
    /// Sets the text of the section the paragraph is in in <paramref name="count"/> columns of one
    /// width, a line between them or not, as Layout > Columns does. Word-probed 2026-10-23:
    /// <c>&lt;w:cols w:num="2" w:space="708"/&gt;</c>, <c>w:sep="1"</c> for the line, and one column
    /// is the element with no <c>w:num</c> at all. Columns of their own widths go with it.
    /// </summary>
    public static byte[] SetColumns(byte[] docx, int paragraph, int count, bool lineBetween, EditOptions options)
    {
        if (count is < 1 or > 45)
        {
            throw new InvalidOperationException("A section has 1 to 45 columns.");
        }

        return Edit(
            docx,
            options,
            edit =>
            {
                // Made afresh, so nothing of a layout of its own widths is left on it.
                var section = edit.SectionOf(paragraph);
                var old = section.GetFirstChild<Columns>();
                var columns = new Columns();
                if (count > 1)
                {
                    columns.ColumnCount = (short) count;
                    if (lineBetween)
                    {
                        columns.Separator = Editing.Literal<OnOffValue>("1");
                    }
                }

                columns.Space = old?.Space?.Value ?? "708";
                if (old == null)
                {
                    section.AddChild(columns);
                }
                else
                {
                    old.InsertBeforeSelf(columns);
                    old.Remove();
                }

                return null;
            }).Document;
    }

    /// <summary>
    /// Ends a section at a place: what is before it is a section, and what is after starts on a new
    /// page or, <paramref name="continuous"/>, on the same one. The place returned is where the text
    /// after the break starts.
    /// </summary>
    public static EditResult InsertSectionBreak(byte[] docx, int paragraph, int offset, bool continuous, EditOptions options) =>
        Edit(docx, options, edit => edit.SectionBreak(paragraph, offset, continuous));

    static PageSetup Setup(SectionProperties section)
    {
        var size = section.GetFirstChild<PageSize>();
        var margins = section.GetFirstChild<PageMargin>();
        var columns = section.GetFirstChild<Columns>();
        var count = columns?.ColumnCount?.Value is > 1 and var number ? number : 1;
        if (columns?.EqualWidth?.Value == false &&
            columns.Elements<Column>().Count() is > 1 and var listed)
        {
            count = listed;
        }

        return new(
            (int) (size?.Width?.Value ?? 11906),
            (int) (size?.Height?.Value ?? 16838),
            margins?.Top?.Value ?? 1440,
            (int) (margins?.Right?.Value ?? 1440),
            margins?.Bottom?.Value ?? 1440,
            (int) (margins?.Left?.Value ?? 1440),
            size != null && Landscape(size))
        {
            Columns = (int) count,
            LineBetween = columns?.Separator?.Value == true
        };
    }

    static bool Landscape(PageSize size) =>
        size.Orient?.Value == PageOrientationValues.Landscape ||
        (size.Width?.Value ?? 0) > (size.Height?.Value ?? 0);

    static PageSize Size(SectionProperties section)
    {
        if (section.GetFirstChild<PageSize>() is { } size)
        {
            return size;
        }

        size = new()
        {
            Width = 11906,
            Height = 16838
        };
        section.AddChild(size);
        return size;
    }

    static PageMargin Margins(SectionProperties section)
    {
        if (section.GetFirstChild<PageMargin>() is { } margins)
        {
            return margins;
        }

        margins = new()
        {
            Top = 1440,
            Right = 1440,
            Bottom = 1440,
            Left = 1440,
            Header = 708,
            Footer = 708,
            Gutter = 0
        };
        section.AddChild(margins);
        return margins;
    }

    sealed partial class Editing
    {
        // The section a paragraph is in: the first w:sectPr at or after it, among the paragraphs of the
        // body, or the body's own.
        public SectionProperties SectionOf(int ordinal)
        {
            var paragraph = Find(ordinal);
            var body = main.Document!.Body!;

            // A paragraph beside the body — a header's, a note's — is taken as in the last section.
            var block = paragraph.Ancestors().LastOrDefault(_ => ReferenceEquals(_.Parent, body));
            if (block == null &&
                !ReferenceEquals(paragraph.Parent, body))
            {
                block = body.Elements<SectionProperties>().LastOrDefault();
            }

            for (block ??= paragraph; block != null; block = block.NextSibling())
            {
                if (block is Paragraph {ParagraphProperties.SectionProperties: { } own})
                {
                    return own;
                }

                if (block is SectionProperties last)
                {
                    return last;
                }
            }

            var section = new SectionProperties(
                new PageSize
                {
                    Width = 11906,
                    Height = 16838
                },
                new PageMargin
                {
                    Top = 1440,
                    Right = 1440,
                    Bottom = 1440,
                    Left = 1440,
                    Header = 708,
                    Footer = 708,
                    Gutter = 0
                });
            body.AppendChild(section);
            return section;
        }

        public (Paragraph Paragraph, int Offset) SectionBreak(int ordinal, int offset, bool continuous)
        {
            var paragraph = Find(ordinal);
            if (paragraph.Ancestors<OoxmlTableCell>().Any())
            {
                throw new InvalidOperationException("A section break cannot go in a table.");
            }

            var section = SectionOf(ordinal);
            var copy = (SectionProperties) section.CloneNode(true);
            copy.RemoveAllChildren<SectionPropertiesChange>();
            var length = Units(paragraph).Sum(Span);

            // The paragraph the section ends with: the first half, the paragraph itself when the
            // break is at its end, or an empty paragraph before it when the break is at its start.
            Paragraph ending;
            var after = paragraph;
            if (offset <= 0)
            {
                ending = NewParagraphLike(paragraph);
                paragraph.InsertBeforeSelf(ending);
            }
            else if (offset >= length)
            {
                ending = paragraph;
                if (paragraph.ParagraphProperties?.SectionProperties != null ||
                    paragraph.NextSibling() is not (Paragraph or DocumentFormat.OpenXml.Wordprocessing.Table))
                {
                    // Something has to follow the break for the section after it to hold.
                    after = NewParagraphLike(paragraph);
                    if (paragraph.ParagraphProperties?.SectionProperties is { } own)
                    {
                        own.Remove();
                        (after.ParagraphProperties ?? after.PrependChild(new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties())).AppendChild(own);
                    }

                    paragraph.InsertAfterSelf(after);
                }
                else
                {
                    after = (Paragraph?) NextBlockParagraph(paragraph) ?? paragraph;
                }
            }
            else
            {
                ending = Split(At(paragraph, offset));
            }

            var properties = ending.ParagraphProperties ?? ending.PrependChild(new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties());
            properties.SectionProperties = copy;
            if (options.Track &&
                !ReferenceEquals(ending, paragraph))
            {
                Mark(ending).PrependChild(Stamp(new Inserted()));
            }

            // How the section after the break starts is said in its own w:sectPr.
            if (continuous)
            {
                section.RemoveAllChildren<SectionType>();
                section.PrependChild(new SectionType
                {
                    Val = SectionMarkValues.Continuous
                });
            }
            else
            {
                section.RemoveAllChildren<SectionType>();
            }

            return (after, 0);
        }

        // An empty paragraph formatted as another, without the section it may end.
        Paragraph NewParagraphLike(Paragraph like)
        {
            var paragraph = new Paragraph();
            if (like.ParagraphProperties?.CloneNode(true) is DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties copy)
            {
                copy.RemoveAllChildren<SectionProperties>();
                copy.RemoveAllChildren<ParagraphPropertiesChange>();
                if (copy.HasChildren)
                {
                    paragraph.AppendChild(copy);
                }
            }

            if (options.Track)
            {
                Mark(paragraph).PrependChild(Stamp(new Inserted()));
            }

            return paragraph;
        }

        static OpenXmlElement? NextBlockParagraph(Paragraph paragraph) =>
            paragraph.ElementsAfter().FirstOrDefault(_ => _ is Paragraph);
    }
}
