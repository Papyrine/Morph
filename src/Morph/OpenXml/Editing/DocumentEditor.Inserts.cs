using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using DrawingMl = DocumentFormat.OpenXml.Drawing;
using Pictures = DocumentFormat.OpenXml.Drawing.Pictures;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using OoxmlTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using OoxmlTableCellProperties = DocumentFormat.OpenXml.Wordprocessing.TableCellProperties;
using OoxmlTableProperties = DocumentFormat.OpenXml.Wordprocessing.TableProperties;
using OoxmlTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;
using Unit = ParagraphContent.Unit;

// What is put into a document's text rather than typed: a page break, a table, a picture, a link —
// and text found and replaced. Each goes in at a place a caret stands, a paragraph's ordinal and a
// count of places into it, as EditParagraph.Length counts them; the place returned is where a caret
// goes on from.
static partial class DocumentEditor
{
    /// <summary>
    /// Breaks the page at a place: the paragraph is divided there, and the break is a paragraph of
    /// its own between the two halves.
    ///
    /// Word-probed 2026-10-18: a page break inserted in the middle of a paragraph is a paragraph in
    /// Normal holding only the break, between the two halves; at a paragraph's start it goes before
    /// it, and at its end an empty paragraph formatted as the first follows it.
    /// </summary>
    public static EditResult InsertPageBreak(byte[] docx, int paragraph, int offset, EditOptions options) =>
        Edit(docx, options, edit => edit.PageBreak(paragraph, offset));

    /// <summary>
    /// Inserts a table of empty cells at a place, in the Table Grid style and as wide as the text, as
    /// Word's Insert Table does: the paragraph is divided there and the table stands between the
    /// halves — before the paragraph, at its start. The place returned is the first cell.
    /// </summary>
    public static EditResult InsertTable(byte[] docx, int paragraph, int offset, int rows, int columns, EditOptions options)
    {
        if (rows < 1 ||
            columns < 1 ||
            rows > 100 ||
            columns > 63)
        {
            throw new InvalidOperationException("A table has 1 to 100 rows and 1 to 63 columns.");
        }

        return Edit(docx, options, edit => edit.Table(paragraph, offset, rows, columns));
    }

    /// <summary>Puts a picture in the line at a place, made no wider than the text.</summary>
    public static EditResult InsertPicture(byte[] docx, int paragraph, int offset, NewPicture picture, EditOptions options) =>
        Edit(docx, options, edit => edit.Picture(paragraph, offset, picture));

    /// <summary>
    /// Makes the text between two places of one paragraph a link: to a web address, or — written
    /// <c>#name</c> — to a bookmark of the document. Text already linked is linked anew.
    /// </summary>
    public static byte[] Link(byte[] docx, SourcePosition start, SourcePosition end, string target, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Link(start, end, target);
                return null;
            }).Document;

    /// <summary>
    /// Removes the links the text between two places is in, whole, keeping their text. With the two
    /// places the same, the link the character either side of it is in.
    /// </summary>
    public static byte[] Unlink(byte[] docx, SourcePosition start, SourcePosition end, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Unlink(start, end);
                return null;
            }).Document;

    /// <summary>
    /// Replaces stretches of text with <paramref name="text"/>, formatted as the first character it
    /// replaces. A stretch cannot take in a picture, a field or a note: those stay where they are.
    /// </summary>
    public static byte[] Replace(byte[] docx, IReadOnlyList<TextRange> ranges, string text, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                foreach (var paragraph in ranges.GroupBy(_ => _.Paragraph))
                {
                    edit.Replace(paragraph.Key, [.. paragraph], text);
                }

                return null;
            }).Document;

    // The widths Word gives an inserted table's columns add up to the text's width less the 0.5pt
    // of Table Grid's border, so its outside edges stand on the margins.
    const int gridBorder = 10;

    const long emuPerPoint = 12700;

    sealed partial class Editing
    {
        // The place a caret stands at, offset places into a paragraph.
        Point At(Paragraph paragraph, int offset)
        {
            var units = Units(paragraph);
            Follow(units);
            var at = 0;
            foreach (var unit in units)
            {
                if (offset <= at)
                {
                    return Before(unit);
                }

                var span = Span(unit);
                if (offset < at + span)
                {
                    return After(unit.Cells[offset - at - 1]);
                }

                at += span;
            }

            if (units.Count > 0)
            {
                return After(units[^1]);
            }

            return Point.EndOf(paragraph);
        }

        static int Span(Unit unit)
        {
            if (unit.Editable)
            {
                return unit.Cells.Count;
            }

            return 1;
        }

        // Page breaks

        public (Paragraph Paragraph, int Offset) PageBreak(int ordinal, int offset)
        {
            var paragraph = Find(ordinal);
            if (offset > 0)
            {
                Split(At(paragraph, offset));
            }

            var breaking = new Paragraph();
            paragraph.InsertBeforeSelf(breaking);
            Add(
                Point.EndOf(breaking),
                new(
                    new Break
                    {
                        Type = BreakValues.Page
                    }));
            if (options.Track)
            {
                Mark(breaking).PrependChild(Stamp(new Inserted()));
            }

            return (paragraph, 0);
        }

        // Tables

        public (Paragraph Paragraph, int Offset) Table(int ordinal, int offset, int rows, int columns)
        {
            var paragraph = Find(ordinal);
            if (offset > 0)
            {
                Split(At(paragraph, offset));
            }

            Define("TableGrid");
            var total = TextWidth(paragraph) - gridBorder;
            var widths = new int[columns];
            for (var column = 0; column < columns; column++)
            {
                widths[column] = total / columns;
            }

            widths[^1] = total - total / columns * (columns - 1);

            var table = new Table(
                new OoxmlTableProperties(
                    new TableStyle
                    {
                        Val = "TableGrid"
                    },
                    new TableWidth
                    {
                        Width = "0",
                        Type = TableWidthUnitValues.Auto
                    },
                    new TableLook
                    {
                        Val = "04A0",
                        FirstRow = Literal<OnOffValue>("1"),
                        LastRow = Literal<OnOffValue>("0"),
                        FirstColumn = Literal<OnOffValue>("1"),
                        LastColumn = Literal<OnOffValue>("0"),
                        NoHorizontalBand = Literal<OnOffValue>("0"),
                        NoVerticalBand = Literal<OnOffValue>("1")
                    }),
                new TableGrid(widths.Select(_ => new GridColumn
                {
                    Width = _.ToString(CultureInfo.InvariantCulture)
                })));
            for (var row = 0; row < rows; row++)
            {
                table.AppendChild(NewRow(widths));
            }

            paragraph.InsertBeforeSelf(table);
            return (table.Descendants<Paragraph>().First(), 0);
        }

        // A row of empty cells. Tracked, the row is an insertion, and so is each cell's paragraph mark.
        OoxmlTableRow NewRow(IEnumerable<int> widths)
        {
            var row = new OoxmlTableRow();
            if (options.Track)
            {
                row.AppendChild(new TableRowProperties(Stamp(new Inserted())));
            }

            foreach (var width in widths)
            {
                row.AppendChild(NewCell(width));
            }

            return row;
        }

        OoxmlTableCell NewCell(int width, Paragraph? like = null)
        {
            var paragraph = new Paragraph();
            if (like?.ParagraphProperties is { } properties)
            {
                var copy = (DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties) properties.CloneNode(true);
                copy.RemoveAllChildren<SectionProperties>();
                copy.RemoveAllChildren<ParagraphPropertiesChange>();
                if (copy.ParagraphMarkRunProperties is { } mark)
                {
                    Unrevise(mark);
                    if (!mark.HasChildren)
                    {
                        mark.Remove();
                    }
                }

                if (copy.HasChildren)
                {
                    paragraph.AppendChild(copy);
                }
            }

            if (options.Track)
            {
                Mark(paragraph).PrependChild(Stamp(new Inserted()));
            }

            return
            [
                with(new OoxmlTableCellProperties(
                        new TableCellWidth
                        {
                            Width = width.ToString(CultureInfo.InvariantCulture),
                            Type = TableWidthUnitValues.Dxa
                        }),
                    paragraph)
            ];
        }

        // How wide the text a paragraph is in runs, in twips: the cell's, in a table; otherwise the
        // page's less its margins, and a column's where the section has several.
        int TextWidth(Paragraph paragraph)
        {
            if (paragraph.Ancestors<OoxmlTableCell>().FirstOrDefault() is { } cell)
            {
                if (cell.TableCellProperties?.TableCellWidth is {Type.Value: var type, Width.Value: { } value} &&
                    type == TableWidthUnitValues.Dxa &&
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var twips))
                {
                    // Less the cell's margins, Normal Table's 0.075" either side.
                    return Math.Max(twips - 216, 720);
                }

                return 2880;
            }

            var section = Section(paragraph);
            var page = section?.GetFirstChild<PageSize>();
            var margins = section?.GetFirstChild<PageMargin>();
            var width = (long) (page?.Width?.Value ?? 12240) -
                        (margins?.Left?.Value ?? 1440) -
                        (margins?.Right?.Value ?? 1440) -
                        (margins?.Gutter?.Value ?? 0);
            if (section?.GetFirstChild<Columns>() is {ColumnCount.Value: > 1 and var count} columns)
            {
                var space = 720;
                if (int.TryParse(columns.Space?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var spacing))
                {
                    space = spacing;
                }

                width = (width - space * (count - 1)) / count;
            }

            return (int) Math.Max(width, 720);
        }

        // The section a paragraph is in: the one ended by the first paragraph from it on that ends
        // one, or else the body's last.
        SectionProperties? Section(OpenXmlElement element)
        {
            var body = main.Document!.Body!;
            var block = element.Ancestors().FirstOrDefault(_ => ReferenceEquals(_.Parent, body)) ?? element;
            for (var current = block; current != null; current = current.NextSibling())
            {
                if (current is Paragraph {ParagraphProperties.SectionProperties: { } ending})
                {
                    return ending;
                }
            }

            return body.Elements<SectionProperties>().LastOrDefault();
        }

        // Pictures

        public (Paragraph Paragraph, int Offset) Picture(int ordinal, int offset, NewPicture picture)
        {
            var paragraph = Find(ordinal);
            var point = At(paragraph, offset);

            // A picture is no part of a link's text.
            while (point.Parent is Hyperlink)
            {
                point = Divide(point);
            }

            // A picture's image belongs to the part it is drawn in: a header's own, in a header.
            var owner = PartOf(paragraph);
            var relationship = NextRelationship(owner);
            var image = owner switch
            {
                HeaderPart header => header.AddImagePart(picture.ContentType, relationship),
                FooterPart footer => footer.AddImagePart(picture.ContentType, relationship),
                FootnotesPart footnotes => footnotes.AddImagePart(picture.ContentType, relationship),
                EndnotesPart endnotes => endnotes.AddImagePart(picture.ContentType, relationship),
                _ => main.AddImagePart(picture.ContentType, relationship)
            };
            using (var data = new MemoryStream(picture.Data, writable: false))
            {
                image.FeedData(data);
            }

            var width = Math.Max(picture.WidthPoints, 1);
            var height = Math.Max(picture.HeightPoints, 1);
            var room = TextWidth(paragraph) / 20d;
            if (width > room)
            {
                height *= room / width;
                width = room;
            }

            var id = NextDrawing();
            var run = new OoxmlRun(
                new OoxmlRunProperties(new NoProof()),
                new Drawing(Inline(relationship, id, (long) Math.Round(width * emuPerPoint), (long) Math.Round(height * emuPerPoint), picture.Description)));
            Add(point, run);
            return (paragraph, offset + 1);
        }

        // One more than the highest id a drawing in the document has: every drawing's has to be its own.
        uint NextDrawing()
        {
            var highest = 0u;
            foreach (var part in new OpenXmlPart[] {main}.Concat(main.HeaderParts).Concat(main.FooterParts))
            {
                foreach (var properties in part.RootElement?.Descendants<DocProperties>() ?? [])
                {
                    highest = Math.Max(highest, properties.Id?.Value ?? 0);
                }
            }

            return highest + 1;
        }

        // A picture in the line, as Word writes one: no space around it, its aspect locked.
        static Inline Inline(string relationship, uint id, long cx, long cy, string? description) =>
            new(
                new Extent
                {
                    Cx = cx,
                    Cy = cy
                },
                new EffectExtent
                {
                    LeftEdge = 0,
                    TopEdge = 0,
                    RightEdge = 0,
                    BottomEdge = 0
                },
                new DocProperties
                {
                    Id = id,
                    Name = $"Picture {id}",
                    Description = description
                },
                new NonVisualGraphicFrameDrawingProperties(
                    new DrawingMl.GraphicFrameLocks
                    {
                        NoChangeAspect = Literal<BooleanValue>("1")
                    }),
                new DrawingMl.Graphic(
                    new DrawingMl.GraphicData(
                        new Pictures.Picture(
                            new Pictures.NonVisualPictureProperties(
                                new Pictures.NonVisualDrawingProperties
                                {
                                    Id = id,
                                    Name = ""
                                },
                                new Pictures.NonVisualPictureDrawingProperties()),
                            new Pictures.BlipFill(
                                new DrawingMl.Blip
                                {
                                    Embed = relationship
                                },
                                new DrawingMl.Stretch(new DrawingMl.FillRectangle())),
                            new Pictures.ShapeProperties(
                                new DrawingMl.Transform2D(
                                    new DrawingMl.Offset
                                    {
                                        X = 0,
                                        Y = 0
                                    },
                                    new DrawingMl.Extents
                                    {
                                        Cx = cx,
                                        Cy = cy
                                    }),
                                new DrawingMl.PresetGeometry(new DrawingMl.AdjustValueList())
                                {
                                    Preset = DrawingMl.ShapeTypeValues.Rectangle
                                })))
                    {
                        Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture"
                    }))
            {
                DistanceFromTop = 0,
                DistanceFromBottom = 0,
                DistanceFromLeft = 0,
                DistanceFromRight = 0
            };

        // A value written as Word writes it: an on-off as 1 or 0, where the SDK would write true.
        public static T Literal<T>(string text)
            where T : OpenXmlSimpleType, new() =>
            new()
            {
                InnerText = text
            };

        // Word numbers a part's relationships rId1, rId2 and on, where the SDK draws an id at
        // random: the next number free keeps an edit making the same file each time.
        string NextRelationship() =>
            NextRelationship(main);

        static string NextRelationship(OpenXmlPart part)
        {
            var taken = part.Parts.Select(_ => _.RelationshipId)
                .Concat(part.ExternalRelationships.Select(_ => _.Id))
                .Concat(part.HyperlinkRelationships.Select(_ => _.Id))
                .Concat(part.DataPartReferenceRelationships.Select(_ => _.Id))
                .ToHashSet(StringComparer.Ordinal);
            var number = 1;
            while (taken.Contains($"rId{number}"))
            {
                number++;
            }

            return $"rId{number}";
        }

        // Links

        public void Link(SourcePosition start, SourcePosition end, string target)
        {
            target = target.Trim();
            if (target.Length == 0)
            {
                throw new InvalidOperationException("Give the link an address.");
            }

            var runs = Runs(start, end);
            if (runs.Count == 0)
            {
                throw new InvalidOperationException("Select the text to make a link.");
            }

            var paragraph = runs[0].Ancestors<Paragraph>().First();
            if (runs.Any(_ => !ReferenceEquals(_.Ancestors<Paragraph>().First(), paragraph)))
            {
                throw new InvalidOperationException("A link has to be within one paragraph.");
            }

            foreach (var run in runs)
            {
                Unwrap(run);
            }

            var link = new Hyperlink
            {
                History = Literal<OnOffValue>("1")
            };
            if (target.StartsWith('#'))
            {
                link.Anchor = target[1..];
            }
            else
            {
                if (!Uri.TryCreate(target, UriKind.Absolute, out var address))
                {
                    throw new InvalidOperationException($"'{target}' is not a web address.");
                }

                var owner = PartOf(runs[0]);
                link.Id = owner.AddHyperlinkRelationship(address, true, NextRelationship(owner)).Id;
            }

            // Runs that follow one another in one place are one link; where the text is divided —
            // part of it a tracked insertion — each part is a link to the same place.
            var follows = runs
                .Select((run, index) => index > 0 &&
                                        ReferenceEquals(Following(runs[index - 1]), run)).ToList();
            Hyperlink? open = null;
            for (var index = 0; index < runs.Count; index++)
            {
                var run = runs[index];
                if (open == null ||
                    !follows[index])
                {
                    open = (Hyperlink) link.CloneNode(true);
                    run.InsertBeforeSelf(open);
                }

                run.Remove();
                open.AppendChild(run);
            }

            Define("Hyperlink");
            foreach (var run in runs)
            {
                Rerun(run).RunStyle = new()
                {
                    Val = "Hyperlink"
                };
            }
        }

        // The run after a run, past the marks that can stand between two.
        static OpenXmlElement? Following(OoxmlRun run)
        {
            var next = run.NextSibling();
            while (next is BookmarkStart or BookmarkEnd or ProofError)
            {
                next = next.NextSibling();
            }

            return next;
        }

        public void Unlink(SourcePosition start, SourcePosition end)
        {
            var cells = Between(start, end).SelectMany(_ => _).ToList();
            if (cells.Count == 0 &&
                start == end)
            {
                cells = [.. Between(start, start with {Offset = start.Offset + 1}).SelectMany(_ => _)];
                if (cells.Count == 0 &&
                    start.Offset > 0)
                {
                    cells = [.. Between(start with {Offset = start.Offset - 1}, start).SelectMany(_ => _)];
                }
            }

            var links = cells
                .Select(_ => _.Run.Ancestors<Hyperlink>().FirstOrDefault())
                .OfType<Hyperlink>()
                .Distinct()
                .ToList();
            if (links.Count == 0)
            {
                throw new InvalidOperationException("There is no link there.");
            }

            foreach (var link in links)
            {
                foreach (var run in link.Descendants<OoxmlRun>().ToList())
                {
                    if (run.RunProperties?.RunStyle?.Val?.Value == "Hyperlink")
                    {
                        var properties = Rerun(run);
                        properties.RunStyle = null;
                        if (!properties.HasChildren)
                        {
                            properties.Remove();
                        }
                    }
                }

                var owner = PartOf(link);
                foreach (var child in link.ChildElements.ToList())
                {
                    child.Remove();
                    link.InsertBeforeSelf(child);
                }

                link.Remove();
                Release(link.Id?.Value, owner);
            }
        }

        // A relationship no link of its part uses any longer goes with the last one.
        static void Release(string? relationship, OpenXmlPart part)
        {
            if (relationship == null ||
                part.RootElement?.Descendants<Hyperlink>().Any(_ => _.Id?.Value == relationship) == true)
            {
                return;
            }

            if (part.HyperlinkRelationships.FirstOrDefault(_ => _.Id == relationship) is { } found)
            {
                part.DeleteReferenceRelationship(found);
            }
        }

        // The runs holding exactly the text between two places, divided off from the text around it.
        List<OoxmlRun> Runs(SourcePosition start, SourcePosition end)
        {
            var runs = new List<OoxmlRun>();
            foreach (var cells in Between(start, end).AsEnumerable().Reverse())
            {
                runs.Insert(0, Isolate(cells));
            }

            return runs;
        }

        // Takes a run out of the link it is in, dividing the link where the run is only part of it.
        void Unwrap(OoxmlRun run)
        {
            if (run.Parent is not Hyperlink)
            {
                return;
            }

            // The run begins a link of its own, and then ends it.
            Divide(Point.Ahead(run));
            Divide(Point.Behind(run));
            if (run.Parent is Hyperlink link)
            {
                run.Remove();
                link.InsertBeforeSelf(run);
                if (!link.HasChildren)
                {
                    link.Remove();
                }
            }
        }

        // Replacing

        public void Replace(int ordinal, List<TextRange> ranges, string text)
        {
            var paragraph = Find(ordinal);
            var units = Units(paragraph);
            ranges.Sort((first, second) => first.From.CompareTo(second.From));
            for (var index = 1; index < ranges.Count; index++)
            {
                if (ranges[index].From < ranges[index - 1].To)
                {
                    throw new InvalidOperationException("Text replaced in one place cannot be replaced again in another.");
                }
            }

            const string fixedInPlace = "Text can be replaced only where it is text: not across a picture, a field or a note.";
            var items = new List<NewItem>();
            var place = 0;
            for (var index = 0; index < units.Count; index++)
            {
                var unit = units[index];
                if (!unit.Editable)
                {
                    if (ranges.Any(_ => _.From <= place &&
                                        place < _.To))
                    {
                        throw new InvalidOperationException(fixedInPlace);
                    }

                    items.Add(new(index, null));
                    place++;
                    continue;
                }

                var builder = new StringBuilder();
                for (var character = 0; character < unit.Cells.Count; character++)
                {
                    if (ranges.Any(_ => _.From == place))
                    {
                        builder.Append(text);
                    }

                    if (!ranges.Any(_ => _.From <= place &&
                                         place < _.To))
                    {
                        builder.Append(unit.Text[character]);
                    }

                    place++;
                }

                if (builder.Length > 0)
                {
                    items.Add(new(index, builder.ToString()));
                }
            }

            if (ranges.Any(_ => _.To > place))
            {
                throw new InvalidOperationException($"Paragraph {ordinal} is not that long.");
            }

            if (items.Count == 0)
            {
                items.Add(new(-1, ""));
            }

            Rewrite(ordinal, [new(items)]);
        }
    }
}
