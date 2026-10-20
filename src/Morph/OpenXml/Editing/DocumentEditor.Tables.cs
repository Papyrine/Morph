using OoxmlTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using OoxmlTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using OoxmlTableCellProperties = DocumentFormat.OpenXml.Wordprocessing.TableCellProperties;
using OoxmlTableProperties = DocumentFormat.OpenXml.Wordprocessing.TableProperties;
using OoxmlTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;

// Rows and columns added to the table a caret is in, or taken from it, and the table itself deleted.
// A table is a grid (w:tblGrid) its cells each span one or more columns of (w:gridSpan), so a column
// is a column of the grid: a cell merged across it is widened or narrowed rather than divided.
//
// Tracked, a row added or deleted is marked so in its properties (w:trPr/w:ins, w:del) and a cell in
// its own (w:cellIns, w:cellDel); what was in a deleted row or cell is struck out, and it all goes
// when the change is accepted.
static partial class DocumentEditor
{
    /// <summary>Changes the table the paragraph is in. The place returned is where the caret goes on from.</summary>
    public static EditResult EditTable(byte[] docx, int paragraph, TableEdit change, EditOptions options) =>
        Edit(docx, options, edit => edit.EditTable(paragraph, change));

    sealed partial class Editing
    {
        public (Paragraph Paragraph, int Offset) EditTable(int ordinal, TableEdit change)
        {
            var paragraph = Find(ordinal);
            var cell = paragraph.Ancestors<OoxmlTableCell>().FirstOrDefault() ??
                       throw new InvalidOperationException("Click in a table first.");
            var row = cell.Ancestors<OoxmlTableRow>().First();
            var table = row.Ancestors<OoxmlTable>().First();
            var (start, end) = Columns(row, cell);
            switch (change)
            {
                case TableEdit.InsertRowAbove:
                case TableEdit.InsertRowBelow:
                    var added = CopyRow(row);
                    if (change == TableEdit.InsertRowAbove)
                    {
                        row.InsertBeforeSelf(added);
                    }
                    else
                    {
                        row.InsertAfterSelf(added);
                    }

                    return (First(CellAt(added, start) ?? Cells(added)[0]), 0);

                case TableEdit.InsertColumnLeft:
                    return (First(InsertColumn(table, row, cell, start, start)), 0);

                case TableEdit.InsertColumnRight:
                    return (First(InsertColumn(table, row, cell, end, start)), 0);

                case TableEdit.DeleteRow:
                    var rows = Rows(table);
                    var index = rows.IndexOf(row);
                    DeleteRow(row);
                    if (options.Track)
                    {
                        return (paragraph, 0);
                    }

                    if (rows.Count == 1)
                    {
                        return After(Remove(table));
                    }

                    var next = rows[index == rows.Count - 1 ? index - 1 : index + 1];
                    return (First(CellAt(next, start) ?? Cells(next)[0]), 0);

                case TableEdit.DeleteColumn:
                    return DeleteColumn(table, row, paragraph, start, end);

                default:
                    if (options.Track)
                    {
                        foreach (var each in Rows(table))
                        {
                            DeleteRow(each);
                        }

                        return (paragraph, 0);
                    }

                    return After(Remove(table));
            }
        }

        // The grid columns a cell spans, from the first to the one after the last.
        static (int Start, int End) Columns(OoxmlTableRow row, OoxmlTableCell cell)
        {
            var column = row.TableRowProperties?.GetFirstChild<GridBefore>()?.Val?.Value ?? 0;
            foreach (var each in Cells(row))
            {
                var span = Span(each);
                if (ReferenceEquals(each, cell))
                {
                    return (column, column + span);
                }

                column += span;
            }

            return (column, column + 1);
        }

        static int Span(OoxmlTableCell cell) =>
            Math.Max(1, cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1);

        // A row's own cells, in order: those in a content control too, and none of a table nested in one.
        static List<OoxmlTableCell> Cells(OoxmlTableRow row) =>
            row.Descendants<OoxmlTableCell>()
                .Where(_ => ReferenceEquals(_.Ancestors<OoxmlTableRow>().First(), row))
                .ToList();

        static List<OoxmlTableRow> Rows(OoxmlTable table) =>
            table.Descendants<OoxmlTableRow>()
                .Where(_ => ReferenceEquals(_.Ancestors<OoxmlTable>().First(), table))
                .ToList();

        // The cell of a row that a grid column is in.
        static OoxmlTableCell? CellAt(OoxmlTableRow row, int column)
        {
            var at = row.TableRowProperties?.GetFirstChild<GridBefore>()?.Val?.Value ?? 0;
            foreach (var cell in Cells(row))
            {
                var span = Span(cell);
                if (column >= at &&
                    column < at + span)
                {
                    return cell;
                }

                at += span;
            }

            return null;
        }

        static Paragraph First(OoxmlTableCell cell) =>
            cell.Elements<Paragraph>().FirstOrDefault() ?? cell.Descendants<Paragraph>().First();

        // Rows

        // A row like another: its cells as wide and as merged across, each empty and formatted as the
        // other's first paragraph is. What the row's properties record of revisions is not copied,
        // and nor is a row repeated as the table's header.
        OoxmlTableRow CopyRow(OoxmlTableRow row)
        {
            var copy = new OoxmlTableRow();
            if (row.TableRowProperties?.CloneNode(true) is TableRowProperties properties)
            {
                foreach (var child in properties.ChildElements.Where(_ => _ is Inserted or Deleted or TableRowPropertiesChange or TableHeader).ToList())
                {
                    child.Remove();
                }

                if (properties.HasChildren)
                {
                    copy.AppendChild(properties);
                }
            }

            if (options.Track)
            {
                var revised = copy.TableRowProperties ?? copy.PrependChild(new TableRowProperties());
                revised.AppendChild(Stamp(new Inserted()));
            }

            foreach (var cell in Cells(row))
            {
                copy.AppendChild(CopyCell(cell));
            }

            return copy;
        }

        OoxmlTableCell CopyCell(OoxmlTableCell cell)
        {
            var copy = NewCell(0, First(cell));
            copy.TableCellProperties!.Remove();
            if (cell.TableCellProperties?.CloneNode(true) is OoxmlTableCellProperties properties)
            {
                foreach (var child in properties.ChildElements.Where(_ => _ is VerticalMerge or CellInsertion or CellDeletion or CellMerge or TableCellPropertiesChange).ToList())
                {
                    child.Remove();
                }

                copy.PrependChild(properties);
            }
            else
            {
                copy.PrependChild(new OoxmlTableCellProperties());
            }

            return copy;
        }

        void DeleteRow(OoxmlTableRow row)
        {
            if (!options.Track)
            {
                row.Remove();
                return;
            }

            var properties = row.TableRowProperties ?? row.PrependChild(new TableRowProperties());
            if (properties.GetFirstChild<Deleted>() == null)
            {
                properties.AppendChild(Stamp(new Deleted()));
            }

            foreach (var cell in Cells(row))
            {
                Strike(cell);
            }
        }

        // Tracked, what is in a cell being deleted is struck out: its text, and each paragraph's mark.
        void Strike(OoxmlTableCell cell)
        {
            foreach (var paragraph in cell.Descendants<Paragraph>().ToList())
            {
                var units = Units(paragraph);
                Follow(units);
                foreach (var unit in units.AsEnumerable().Reverse())
                {
                    if (unit is { Editable: true, Cells.Count: > 0 })
                    {
                        Remove(unit.Cells);
                    }
                }

                var mark = Mark(paragraph);
                if (!mark.ChildElements.Any(_ => _ is Deleted or Inserted))
                {
                    mark.PrependChild(Stamp(new Deleted()));
                }
            }
        }

        // Columns

        // Adds a grid column at a place of the grid, as wide as the column the caret is in, and
        // narrows every column alike to keep the table as wide as it was.
        OoxmlTableCell InsertColumn(OoxmlTable table, OoxmlTableRow current, OoxmlTableCell caret, int at, int like)
        {
            var grid = table.GetFirstChild<TableGrid>() ??
                       table.InsertAfter(new TableGrid(), table.GetFirstChild<OoxmlTableProperties>());
            var widths = grid.Elements<GridColumn>().Select(Width).ToList();
            var total = widths.Sum();
            var width = 1440;
            if (like < widths.Count)
            {
                width = widths[like];
            }

            widths.Insert(Math.Min(at, widths.Count), width);
            if (total > 0)
            {
                var scale = total / (double) (total + width);
                for (var index = 0; index < widths.Count; index++)
                {
                    widths[index] = (int) Math.Round(widths[index] * scale);
                }
            }

            OoxmlTableCell? added = null;
            foreach (var row in Rows(table))
            {
                var placed = Place(row, at);
                if (ReferenceEquals(row, current))
                {
                    added = placed;
                }
            }

            Regrid(table, grid, widths);
            return added ?? caret;
        }

        // Makes room in a row for a grid column at a place: a new cell there, or — where a cell is
        // merged across the place — that cell a column wider.
        OoxmlTableCell? Place(OoxmlTableRow row, int at)
        {
            var cells = Cells(row);
            var column = row.TableRowProperties?.GetFirstChild<GridBefore>()?.Val?.Value ?? 0;
            if (column > at)
            {
                return null;
            }

            foreach (var cell in cells)
            {
                var span = Span(cell);
                if (column == at)
                {
                    var added = Fresh(cell);
                    cell.InsertBeforeSelf(added);
                    return added;
                }

                if (at < column + span)
                {
                    Widen(cell, 1);
                    return cell;
                }

                column += span;
            }

            if (cells.Count > 0 &&
                column == at)
            {
                var added = Fresh(cells[^1]);
                cells[^1].InsertAfterSelf(added);
                return added;
            }

            return null;
        }

        // A cell like another, for a column being added. Tracked, it is an insertion.
        OoxmlTableCell Fresh(OoxmlTableCell like)
        {
            var cell = CopyCell(like);
            if (options.Track)
            {
                Revise(cell, new CellInsertion());
            }

            return cell;
        }

        // A cell's revision goes where its properties say a revision goes: before a record of what
        // they used to be.
        void Revise(OoxmlTableCell cell, OpenXmlElement revision)
        {
            var properties = cell.TableCellProperties ?? cell.PrependChild(new OoxmlTableCellProperties());
            properties.InsertBefore(Stamp(revision), properties.GetFirstChild<TableCellPropertiesChange>());
        }

        void DeleteColumnCells(OoxmlTable table, int start, int end)
        {
            foreach (var row in Rows(table))
            {
                var column = row.TableRowProperties?.GetFirstChild<GridBefore>()?.Val?.Value ?? 0;
                foreach (var cell in Cells(row))
                {
                    var span = Span(cell);
                    var overlap = Math.Min(end, column + span) - Math.Max(start, column);
                    if (overlap > 0)
                    {
                        if (overlap >= span)
                        {
                            if (options.Track)
                            {
                                Revise(cell, new CellDeletion());
                                Strike(cell);
                            }
                            else
                            {
                                cell.Remove();
                            }
                        }
                        else if (!options.Track)
                        {
                            Widen(cell, -overlap);
                        }
                    }

                    column += span;
                }
            }
        }

        (Paragraph Paragraph, int Offset) DeleteColumn(OoxmlTable table, OoxmlTableRow row, Paragraph paragraph, int start, int end)
        {
            var grid = table.GetFirstChild<TableGrid>();
            var widths = grid?.Elements<GridColumn>().Select(Width).ToList() ?? [];
            if (!options.Track &&
                widths.Count > 0 &&
                end - start >= widths.Count)
            {
                return After(Remove(table));
            }

            DeleteColumnCells(table, start, end);
            if (options.Track)
            {
                return (paragraph, 0);
            }

            if (grid != null)
            {
                widths.RemoveRange(start, Math.Min(end, widths.Count) - start);
                Regrid(table, grid, widths);
            }

            // Rows left with no cells go too.
            foreach (var each in Rows(table).Where(_ => Cells(_).Count == 0))
            {
                each.Remove();
            }

            if (Rows(table).Count == 0)
            {
                return After(Remove(table));
            }

            var near = CellAt(row, Math.Max(0, start - 1)) ?? CellAt(row, start) ?? Cells(row).FirstOrDefault();
            if (near == null)
            {
                return After(Remove(table));
            }

            return (First(near), 0);
        }

        static void Widen(OoxmlTableCell cell, int columns)
        {
            var properties = cell.TableCellProperties ?? cell.PrependChild(new OoxmlTableCellProperties());
            var span = Span(cell) + columns;
            if (span <= 1)
            {
                properties.GridSpan = null;
                return;
            }

            properties.GridSpan = new()
            {
                Val = span
            };
        }

        static int Width(GridColumn column)
        {
            if (int.TryParse(column.Width?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width))
            {
                return width;
            }

            return 0;
        }

        // The grid's columns set to widths, and each cell to the width of the columns it spans.
        static void Regrid(OoxmlTable table, TableGrid grid, List<int> widths)
        {
            grid.RemoveAllChildren<GridColumn>();
            foreach (var width in widths)
            {
                grid.AppendChild(new GridColumn
                {
                    Width = width.ToString(CultureInfo.InvariantCulture)
                });
            }

            foreach (var row in Rows(table))
            {
                var column = row.TableRowProperties?.GetFirstChild<GridBefore>()?.Val?.Value ?? 0;
                foreach (var cell in Cells(row))
                {
                    var span = Span(cell);
                    var width = 0;
                    for (var index = column; index < column + span && index < widths.Count; index++)
                    {
                        width += widths[index];
                    }

                    if (cell.TableCellProperties?.TableCellWidth is {Type.Value: var type} cellWidth &&
                        type == TableWidthUnitValues.Dxa)
                    {
                        cellWidth.Width = width.ToString(CultureInfo.InvariantCulture);
                    }

                    column += span;
                }
            }
        }

        // The table goes, and what stood after it is where the caret goes on: the block that took its
        // place, if it is a paragraph, or the paragraph before it.
        static OpenXmlElement? Remove(OoxmlTable table)
        {
            var next = NextBlock(table);
            var previous = PreviousBlock(table);
            var parent = table.Parent;
            table.Remove();

            // A cell ends with a paragraph, and a cell left with nothing in it has to be given one.
            if (parent is OoxmlTableCell cell &&
                !cell.Elements<Paragraph>().Any())
            {
                var empty = new Paragraph();
                cell.AppendChild(empty);
                return empty;
            }

            if (next is Paragraph)
            {
                return next;
            }

            return previous;
        }

        (Paragraph Paragraph, int Offset) After(OpenXmlElement? standing)
        {
            if (standing is Paragraph paragraph)
            {
                return (paragraph, 0);
            }

            if (standing?.Descendants<Paragraph>().FirstOrDefault() is { } inside)
            {
                return (inside, 0);
            }

            return (main.Document!.Descendants<Paragraph>().First(), 0);
        }
    }
}
