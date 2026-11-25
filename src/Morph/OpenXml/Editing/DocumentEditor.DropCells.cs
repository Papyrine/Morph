using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using OoxmlTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using OoxmlTableCellProperties = DocumentFormat.OpenXml.Wordprocessing.TableCellProperties;
using OoxmlTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;

// Cells dragged and dropped on a cell. A selection from one cell into another is the rectangle of cells
// between them, as Word selects it (WordProbe T-07), and dropped on a cell Word pastes them as cells
// from there (T-33, a row's first two cells dropped on (3,3) of a 3x3 table):
//
//   - each cell's content takes the place of the content of the cell as far across and down from the one
//     dropped on, and a row too short for them is given cells to hold them (row 3 became four cells);
//   - moved, the cells they came from are left empty; copied (Ctrl), as they were;
//   - tracked, each cell's new text is inserted ahead of its old text, which is struck, and the cells moved
//     from are struck (five revisions); undo calls it Move.
//
// Not recorded, and so refused: a block that would run past the table's last row, and cells dropped
// outside a table. Whole rows and columns could not be dragged by the harness (T-33's other rows).
static partial class DocumentEditor
{
    static DropResult DropCells(byte[] docx, IReadOnlyList<EditParagraph> paragraphs, int fromParagraph, int toParagraph, int atParagraph, bool copy, EditOptions options)
    {
        DropResult Unchanged() =>
            new(docx, atParagraph, 0, atParagraph, 0);

        if (paragraphs[atParagraph].Cell == null ||
            paragraphs[atParagraph].Root != paragraphs[fromParagraph].Root)
        {
            return Unchanged();
        }

        var placed = -1;
        var edited = DocumentReading.Edit(
            docx,
            document =>
            {
                var editing = new Editing(document, options);
                if (editing.DropCells(fromParagraph, toParagraph, atParagraph, copy) is { } landed)
                {
                    var all = SourceRuns.Stories(document).SelectMany(_ => _.Descendants<Paragraph>()).ToList();
                    placed = all.FindIndex(_ => ReferenceEquals(_, landed));
                }
            });

        if (placed < 0)
        {
            return Unchanged();
        }

        return new(edited, placed, 0, placed, 0);
    }

    sealed partial class Editing
    {
        // The cells between two paragraphs, put down from the cell a third is in; the paragraph the caret
        // goes to, at its start, as Word's does (T-33: an insertion point at the dropped cell's start), or
        // null where the block will not go.
        public Paragraph? DropCells(int fromOrdinal, int toOrdinal, int atOrdinal, bool copy)
        {
            var all = AllParagraphs().ToList();
            var (_, block, _, _) = Rectangle([all[fromOrdinal], all[toOrdinal]]);
            var target = CellOf(all[atOrdinal]);
            var table = target.Ancestors<OoxmlTable>().First();
            var rows = Rows(table);
            var row = rows.IndexOf(target.Ancestors<OoxmlTableRow>().First());
            var column = Cells(rows[row]).IndexOf(target);
            if (row + block.Count > rows.Count ||
                block.SelectMany(_ => _.Cells).Contains(target))
            {
                return null;
            }

            // Every cell's content read before any is written, since the block can overlap where it goes.
            var contents = block
                .Select(_ => _.Cells.Select(cell => cell.Elements().Where(child => child is not OoxmlTableCellProperties).Select(child => child.CloneNode(true)).ToList()).ToList())
                .ToList();

            Paragraph? first = null;
            for (var down = 0; down < block.Count; down++)
            {
                var cells = Cells(rows[row + down]);
                for (var across = 0; across < block[down].Cells.Count; across++)
                {
                    // A row too short is given a cell, as the last it has.
                    while (cells.Count <= column + across)
                    {
                        var added = new OoxmlTableCell();
                        if (cells[^1].TableCellProperties is { } properties)
                        {
                            added.AppendChild(properties.CloneNode(true));
                        }

                        added.AppendChild(new Paragraph());
                        cells[^1].InsertAfterSelf(added);
                        cells.Add(added);
                    }

                    var cell = cells[column + across];
                    Fill(cell, contents[down][across]);
                    if (down == 0 &&
                        across == 0)
                    {
                        first = cell.Elements<Paragraph>().First();
                    }
                }
            }

            if (!copy)
            {
                foreach (var cell in block.SelectMany(_ => _.Cells))
                {
                    EmptyOut(cell);
                }
            }

            return first;
        }

        // A cell given what another held: untracked, in place of what it held; tracked, what came goes in
        // ahead of what was there, which is struck.
        void Fill(OoxmlTableCell cell, List<OpenXmlElement> content)
        {
            foreach (var paragraph in content.OfType<Paragraph>().Concat(content.SelectMany(_ => _.Descendants<Paragraph>())))
            {
                // A paragraph's id is its own (ParagraphIds), and the copy is a paragraph of its own.
                paragraph.ParagraphId = null;
                paragraph.TextId = null;
            }

            if (!options.Track)
            {
                foreach (var child in cell.Elements().Where(_ => _ is not OoxmlTableCellProperties).ToList())
                {
                    child.Remove();
                }

                foreach (var child in content)
                {
                    cell.AppendChild(child);
                }

                if (!cell.Elements<Paragraph>().Any())
                {
                    cell.AppendChild(new Paragraph());
                }

                return;
            }

            var old = cell.Elements<Paragraph>().First();
            foreach (var run in cell.Descendants<OoxmlRun>().Where(_ => !_.Ancestors<DeletedRun>().Any()).ToList())
            {
                Strike(run);
            }

            // The first paragraph's text into the cell's first paragraph, ahead of its own; any after it as
            // paragraphs of their own after that, their marks inserted.
            var paragraphs = content.OfType<Paragraph>().ToList();
            var at = old.GetFirstChild<OoxmlParagraphProperties>() is { } properties ? Point.Behind(properties) : new(old, old.FirstChild);
            if (paragraphs.Count > 0)
            {
                foreach (var child in paragraphs[0].Elements().Where(_ => _ is not OoxmlParagraphProperties).ToList())
                {
                    child.Remove();
                    foreach (var made in Tracked(child))
                    {
                        at.Insert(made);
                        at = Point.Behind(made);
                    }
                }
            }

            OpenXmlElement after = old;
            foreach (var paragraph in paragraphs.Skip(1))
            {
                foreach (var child in paragraph.Elements().Where(_ => _ is not OoxmlParagraphProperties).ToList())
                {
                    child.Remove();
                    foreach (var made in Tracked(child))
                    {
                        paragraph.AppendChild(made);
                    }
                }

                Mark(paragraph).PrependChild(Stamp(new Inserted()));
                after.InsertAfterSelf(paragraph);
                after = paragraph;
            }
        }

        // A cell the cells were moved out of, left empty: untracked, one empty paragraph in its first's
        // look; tracked, its text struck where it stands.
        void EmptyOut(OoxmlTableCell cell)
        {
            if (options.Track)
            {
                foreach (var run in cell.Descendants<OoxmlRun>().Where(_ => !_.Ancestors<DeletedRun>().Any()).ToList())
                {
                    Strike(run);
                }

                return;
            }

            var kept = cell.Elements<Paragraph>().First();
            foreach (var child in kept.Elements().Where(_ => _ is not OoxmlParagraphProperties).ToList())
            {
                child.Remove();
            }

            foreach (var child in cell.Elements().Where(_ => _ is not OoxmlTableCellProperties && !ReferenceEquals(_, kept)).ToList())
            {
                child.Remove();
            }
        }
    }
}
