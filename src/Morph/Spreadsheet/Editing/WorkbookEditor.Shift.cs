using S = DocumentFormat.OpenXml.Spreadsheet;
using XDR = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using XM = DocumentFormat.OpenXml.Office.Excel;
using TC = DocumentFormat.OpenXml.Office2019.Excel.ThreadedComments;

static partial class WorkbookEditor
{
    /// <summary>
    /// Inserts rows before a row, as Excel's Insert Sheet Rows does: what is at and below it moves down,
    /// and everything that names those cells moves with them (<see cref="SheetShift"/> has the rules) —
    /// formulas on every sheet, defined names, charts' series, merges, tables, validations, conditional
    /// formats, links, comments and pictures. The new rows look as the row above them does, its height
    /// included, and a table they land inside grows, its calculated columns' formulas filled into them.
    /// </summary>
    public static byte[] InsertRows(byte[] xlsx, int sheet, int at, int count, DateTime now) =>
        Shift(xlsx, sheet, true, at, count, false, now);

    /// <summary>
    /// Deletes rows, as Excel's Delete Sheet Rows does: what is below moves up, and a reference to a cell
    /// that went is <c>#REF!</c>.
    /// </summary>
    public static byte[] DeleteRows(byte[] xlsx, int sheet, int at, int count, DateTime now) =>
        Shift(xlsx, sheet, true, at, count, true, now);

    /// <summary>
    /// Inserts columns before a column, as Excel's Insert Sheet Columns does, the new ones looking as the
    /// column on their left does. A table they land inside gains columns, named Column1 and on.
    /// </summary>
    public static byte[] InsertColumns(byte[] xlsx, int sheet, int at, int count, DateTime now) =>
        Shift(xlsx, sheet, false, at, count, false, now);

    /// <summary>
    /// Deletes columns, as Excel's Delete Sheet Columns does; a structured reference to a table's column
    /// that went is <c>#REF!</c>.
    /// </summary>
    public static byte[] DeleteColumns(byte[] xlsx, int sheet, int at, int count, DateTime now) =>
        Shift(xlsx, sheet, false, at, count, true, now);

    static byte[] Shift(byte[] xlsx, int sheet, bool rows, int at, int count, bool delete, DateTime now) =>
        Edit(xlsx, now, _ =>
        {
            var worksheet = Worksheet(_, sheet);
            var max = CellReference.MaxColumn;
            if (rows)
            {
                max = CellReference.MaxRow;
            }

            if (count < 1 ||
                at < 1 ||
                at > max)
            {
                throw new InvalidOperationException("There are no such rows or columns.");
            }

            var shift = new SheetShift(SheetName(_, sheet), rows, at, Math.Min(count, max - at + 1), delete);
            Refuse(worksheet, shift);
            var tables = TableChanges(worksheet, shift);
            WriteOutShared(_, worksheet, shift);
            var anchored = tables
                .Where(_ => _.AtFirstDataRow)
                .Select(_ => _.Table)
                .ToHashSet();
            ShiftFormulas(_, worksheet, shift, anchored);
            if (rows)
            {
                ShiftRows(worksheet, shift);
            }
            else
            {
                ShiftColumns(worksheet, shift);
            }

            ShiftRanges(worksheet, shift);
            ApplyTables(_, worksheet, shift, tables);
            var part = worksheet.WorksheetPart!;
            ShiftDrawing(part, shift);
            ShiftComments(part, shift);
            foreach (var pivot in part.PivotTableParts)
            {
                if (pivot.PivotTableDefinition?.Location is {Reference.Value: { } reference} location &&
                    shift.Reference(reference) is { } moved &&
                    moved != reference)
                {
                    location.Reference = moved;
                }
            }
        });

    static string SheetName(SpreadsheetDocument document, int sheet) =>
        document.WorkbookPart?.Workbook?.Sheets?.Elements<S.Sheet>().ElementAtOrDefault(sheet)?.Name?.Value ?? string.Empty;

    // Each sheet of cells, by its name.
    static IEnumerable<(S.Worksheet Worksheet, string Name)> Sheets(SpreadsheetDocument document)
    {
        var workbookPart = document.WorkbookPart!;
        foreach (var sheet in workbookPart.Workbook?.Sheets?.Elements<S.Sheet>() ?? [])
        {
            if (sheet.Id?.Value is { } id &&
                workbookPart.TryGetPartById(id, out var part) &&
                part is WorksheetPart {Worksheet: { } worksheet})
            {
                yield return (worksheet, sheet.Name?.Value ?? string.Empty);
            }
        }
    }

    static (int First, int Last) Axis(SheetRange range, bool rows)
    {
        if (rows)
        {
            return (range.FirstRow, range.LastRow);
        }

        return (range.FirstColumn, range.LastColumn);
    }

    static string RangeText(SheetRange range) =>
        $"{CellReference.Format(range.FirstColumn, range.FirstRow)}:{CellReference.Format(range.LastColumn, range.LastRow)}";

    static int RowOf(S.Cell cell) =>
        CellReference.RowOf(cell.CellReference?.Value);

    static int ColumnOf(S.Cell cell) =>
        CellReference.ColumnOf(cell.CellReference?.Value);

    // What Excel will not do: insert inside an array formula's block or delete part of one, change part of
    // a PivotTable, or push a cell that holds something — or is only formatted — off the sheet's end.
    static void Refuse(S.Worksheet worksheet, SheetShift shift)
    {
        foreach (var block in ArrayBlocks(worksheet))
        {
            if (Splits(Axis(block, shift.Rows), shift))
            {
                throw new InvalidOperationException("You can't change part of an array.");
            }
        }

        foreach (var pivot in worksheet.WorksheetPart?.PivotTableParts ?? [])
        {
            if (CellReference.ParseRange(pivot.PivotTableDefinition?.Location?.Reference?.Value) is not { } range)
            {
                continue;
            }

            var (first, last) = Axis(range, shift.Rows);
            if ((shift.Delete && first <= shift.Last && last >= shift.At) ||
                (!shift.Delete && first < shift.At && shift.At <= last))
            {
                throw new InvalidOperationException("You can't change part of a PivotTable.");
            }
        }

        if (shift.Delete)
        {
            return;
        }

        var limit = shift.Max - shift.Count;
        var data = worksheet.GetFirstChild<S.SheetData>()!;
        bool pushed;
        if (shift.Rows)
        {
            pushed = data.Elements<S.Row>()
                .Any(_ => _.RowIndex?.Value > limit &&
                          (_.Elements<S.Cell>().Any() || _.CustomFormat?.Value == true));
        }
        else
        {
            pushed = data.Descendants<S.Cell>().Any(_ => ColumnOf(_) > limit);
        }

        if (pushed)
        {
            throw new InvalidOperationException("The cells can't be inserted: they would push cells that hold something, or are formatted, off the end of the sheet.");
        }
    }

    // Whether the change falls inside a span without taking it whole: an insert inside it, or a delete of
    // part of it.
    static bool Splits((int First, int Last) span, SheetShift shift)
    {
        if (!shift.Delete)
        {
            return span.First < shift.At &&
                   shift.At <= span.Last;
        }

        var overlaps = span.First <= shift.Last &&
                       span.Last >= shift.At;
        var covered = span.First >= shift.At &&
                      span.Last <= shift.Last;
        return overlaps && !covered;
    }

    // Shared formulas

    /// <summary>
    /// A shared formula's text depends on where its cells are, so a group whose cells move, or whose text
    /// changes, is written out per cell before anything moves: on the sheet changed, those with a cell at
    /// or past the change; on the others, those whose cells name the sheet's moved cells.
    /// </summary>
    static void WriteOutShared(SpreadsheetDocument document, S.Worksheet edited, SheetShift shift)
    {
        foreach (var (worksheet, name) in Sheets(document))
        {
            StateReferences(worksheet);
            var own = ReferenceEquals(worksheet, edited);
            WriteOutShared(worksheet, (text, row, column) =>
            {
                var at = column;
                if (shift.Rows)
                {
                    at = row;
                }

                return (own && at >= shift.At) ||
                       shift.Formula(text, name) != text;
            });
        }
    }

    static void WriteOutShared(S.Worksheet worksheet, Func<string, int, int, bool> needed)
    {
        var masters = new Dictionary<uint, (string Text, int Row, int Column)>();
        var members = new List<(S.Cell Cell, uint Index)>();
        foreach (var cell in worksheet.Descendants<S.Cell>())
        {
            if (cell.CellFormula is not {FormulaType.Value: var type, SharedIndex.Value: var index} formula ||
                type != S.CellFormulaValues.Shared)
            {
                continue;
            }

            members.Add((cell, index));
            if (!string.IsNullOrEmpty(formula.Text))
            {
                masters[index] = (formula.Text, RowOf(cell), ColumnOf(cell));
            }
        }

        if (members.Count == 0)
        {
            return;
        }

        var texts = new Dictionary<S.Cell, string?>();
        var groups = new HashSet<uint>();
        foreach (var (cell, index) in members)
        {
            string? text = null;
            if (masters.TryGetValue(index, out var master))
            {
                text = FormulaShifter.Shift(master.Text, RowOf(cell) - master.Row, ColumnOf(cell) - master.Column);
            }

            texts[cell] = text;
            if (text == null ||
                needed(text, RowOf(cell), ColumnOf(cell)))
            {
                groups.Add(index);
            }
        }

        foreach (var (cell, index) in members)
        {
            if (!groups.Contains(index))
            {
                continue;
            }

            var formula = cell.CellFormula!;
            formula.FormulaType = null;
            formula.SharedIndex = null;
            formula.Reference = null;
            formula.Text = texts[cell] ?? string.Empty;
        }
    }

    // Formulas

    /// <summary>
    /// Every formula in the workbook that names the sheet's cells: in cells, conditional formats,
    /// validations and links on every sheet, in Excel 2010's extensions to those, in tables' calculated
    /// columns and totals, in defined names, in charts' series and in PivotTables' sources.
    ///
    /// A calculated column's formula is the formula of its first data row (Excel 16 over COM, 2026-11-17),
    /// and moves as that cell's does — but for a table inserted into at its first data row, whose new first
    /// row takes the formula as it was, and keeps it: <paramref name="anchored"/>.
    /// </summary>
    static void ShiftFormulas(SpreadsheetDocument document, S.Worksheet edited, SheetShift shift, HashSet<S.Table> anchored)
    {
        var workbookPart = document.WorkbookPart!;
        foreach (var (worksheet, name) in Sheets(document))
        {
            // A structured reference without a table's name stands in its cell's table, which matters only
            // where a table loses columns.
            List<(SheetRange Range, string Name)> tables = [];
            if (ReferenceEquals(worksheet, edited) &&
                shift.Lost.Count > 0)
            {
                tables = TablesOf(worksheet);
            }

            var own = ReferenceEquals(worksheet, edited);
            foreach (var cell in worksheet.Descendants<S.Cell>())
            {
                if (cell.CellFormula is not {Text: {Length: > 0} text} formula)
                {
                    continue;
                }

                // An array formula's block moves with it; one cut into was refused.
                if (own &&
                    formula.FormulaType?.Value == S.CellFormulaValues.Array &&
                    shift.Reference(formula.Reference?.Value) is { } block &&
                    block != formula.Reference?.Value)
                {
                    formula.Reference = block;
                }

                var table = TableAt(tables, RowOf(cell), ColumnOf(cell));
                var moved = shift.Formula(text, name, table);
                if (moved != text)
                {
                    formula.Text = moved;
                }
            }

            foreach (var format in worksheet.Elements<S.ConditionalFormatting>())
            {
                foreach (var formula in format.Descendants<S.Formula>())
                {
                    Rewrite(formula, shift, name);
                }
            }

            foreach (var validation in worksheet.Descendants<S.DataValidation>())
            {
                Rewrite(validation.Formula1, shift, name);
                Rewrite(validation.Formula2, shift, name);
            }

            foreach (var link in worksheet.Descendants<S.Hyperlink>())
            {
                if (link.Location?.Value is {Length: > 0} location &&
                    shift.Formula(location, name) is var moved &&
                    moved != location)
                {
                    link.Location = moved;
                }
            }

            // Excel 2010's conditional formats, validations and sparklines keep their formulas as xm:f.
            foreach (var formula in worksheet.Descendants<XM.Formula>())
            {
                Rewrite(formula, shift, name);
            }

            foreach (var tablePart in worksheet.WorksheetPart?.TableDefinitionParts ?? [])
            {
                if (tablePart.Table is not { } table)
                {
                    continue;
                }

                var tableName = table.DisplayName?.Value ?? table.Name?.Value;
                foreach (var column in table.TableColumns?.Elements<S.TableColumn>() ?? [])
                {
                    if (!anchored.Contains(table))
                    {
                        Rewrite(column.CalculatedColumnFormula, shift, name, tableName);
                    }

                    Rewrite(column.TotalsRowFormula, shift, name, tableName);
                }
            }
        }

        foreach (var defined in workbookPart.Workbook?.DefinedNames?.Elements<S.DefinedName>() ?? [])
        {
            Rewrite(defined, shift, null);
        }

        foreach (var chart in Charts(workbookPart))
        {
            foreach (var leaf in chart.Descendants<OpenXmlLeafTextElement>().ToList())
            {
                if (leaf.LocalName == "f")
                {
                    Rewrite(leaf, shift, null);
                }
            }
        }

        foreach (var cache in workbookPart.PivotTableCacheDefinitionParts)
        {
            if (cache.PivotCacheDefinition?.CacheSource?.WorksheetSource is {Reference.Value: { } reference} source &&
                string.Equals(source.Sheet?.Value, shift.Sheet, StringComparison.OrdinalIgnoreCase) &&
                shift.Reference(reference) is { } moved &&
                moved != reference)
            {
                source.Reference = moved;
            }
        }
    }

    static void Rewrite(OpenXmlLeafTextElement? element, SheetShift shift, string? sheet, string? table = null)
    {
        if (element?.Text is not {Length: > 0} text)
        {
            return;
        }

        var moved = shift.Formula(text, sheet, table);
        if (moved != text)
        {
            element.Text = moved;
        }
    }

    // The charts drawn on worksheets and chart sheets, Excel 2016's kinds included.
    static IEnumerable<OpenXmlPartRootElement> Charts(WorkbookPart workbookPart)
    {
        var drawings = workbookPart.WorksheetParts
            .Select(_ => _.DrawingsPart)
            .Concat(workbookPart.ChartsheetParts.Select(_ => _.DrawingsPart));
        foreach (var drawing in drawings)
        {
            if (drawing == null)
            {
                continue;
            }

            foreach (var chart in drawing.ChartParts)
            {
                if (chart.ChartSpace is { } space)
                {
                    yield return space;
                }
            }

            foreach (var chart in drawing.ExtendedChartParts)
            {
                if (chart.ChartSpace is { } space)
                {
                    yield return space;
                }
            }
        }
    }

    static List<(SheetRange Range, string Name)> TablesOf(S.Worksheet worksheet)
    {
        var tables = new List<(SheetRange Range, string Name)>();
        foreach (var tablePart in worksheet.WorksheetPart?.TableDefinitionParts ?? [])
        {
            if (tablePart.Table is { } table &&
                CellReference.ParseRange(table.Reference?.Value) is { } range)
            {
                tables.Add((range, table.DisplayName?.Value ?? table.Name?.Value ?? string.Empty));
            }
        }

        return tables;
    }

    static string? TableAt(List<(SheetRange Range, string Name)> tables, int row, int column)
    {
        foreach (var (range, name) in tables)
        {
            if (Inside(range, row, column))
            {
                return name;
            }
        }

        return null;
    }

    // Cells

    static void ShiftRows(S.Worksheet worksheet, SheetShift shift)
    {
        var data = worksheet.GetFirstChild<S.SheetData>()!;
        S.Row? above = null;
        foreach (var row in data.Elements<S.Row>().ToList())
        {
            var index = (int) (row.RowIndex?.Value ?? 0);
            if (index == shift.At - 1)
            {
                above = row;
            }

            if (index < shift.At)
            {
                continue;
            }

            if (shift.Index(index) is not { } moved)
            {
                row.Remove();
                continue;
            }

            row.RowIndex = (uint) moved;
            foreach (var cell in row.Elements<S.Cell>())
            {
                cell.CellReference = CellReference.Format(ColumnOf(cell), moved);
            }
        }

        if (!shift.Delete &&
            above != null)
        {
            FormatLikeAbove(data, above, shift);
        }
    }

    /// <summary>
    /// The rows inserted look as the row above them does — its height, its style and each of its cells'
    /// styles — as Excel's Insert does unless told otherwise (its Insert Options' Format Same As Above).
    /// Above the first row there is nothing to look like, and they are plain.
    /// </summary>
    static void FormatLikeAbove(S.SheetData data, S.Row above, SheetShift shift)
    {
        var styled = above.Elements<S.Cell>()
            .Where(_ => _.StyleIndex?.Value > 0)
            .Select(_ => (Column: ColumnOf(_), Style: _.StyleIndex!.Value))
            .ToList();
        var height = above.Height?.Value;
        var style = above.StyleIndex?.Value;
        if (height == null &&
            style == null &&
            styled.Count == 0)
        {
            return;
        }

        for (var index = shift.At; index <= shift.Last; index++)
        {
            var row = RowAt(data, index);
            if (height is { } points)
            {
                row.Height = points;
                if (above.CustomHeight?.Value == true)
                {
                    row.CustomHeight = true;
                }
            }

            if (style is { } rowStyle)
            {
                row.StyleIndex = rowStyle;
                if (above.CustomFormat?.Value == true)
                {
                    row.CustomFormat = true;
                }
            }

            if (above.OutlineLevel?.Value is > 0 and var level)
            {
                row.OutlineLevel = level;
            }

            foreach (var (column, cellStyle) in styled)
            {
                row.AppendChild(new S.Cell
                {
                    CellReference = CellReference.Format(column, index),
                    StyleIndex = cellStyle
                });
            }
        }
    }

    // Columns move within each row; the new ones look as the column on their left does, cell by cell
    // (Excel's Format Same As Left).
    static void ShiftColumns(S.Worksheet worksheet, SheetShift shift)
    {
        var data = worksheet.GetFirstChild<S.SheetData>()!;
        foreach (var row in data.Elements<S.Row>())
        {
            // Which columns a row's cells span is only a hint to a reader, and would be stale.
            row.Spans = null;
            var rowIndex = (int) (row.RowIndex?.Value ?? 0);
            S.Cell? left = null;
            foreach (var cell in row.Elements<S.Cell>().ToList())
            {
                var column = ColumnOf(cell);
                if (column == shift.At - 1)
                {
                    left = cell;
                }

                if (column < shift.At)
                {
                    continue;
                }

                if (shift.Index(column) is not { } moved)
                {
                    cell.Remove();
                    continue;
                }

                cell.CellReference = CellReference.Format(moved, rowIndex);
            }

            if (shift.Delete ||
                left?.StyleIndex?.Value is not (> 0 and var style))
            {
                continue;
            }

            OpenXmlElement after = left;
            for (var column = shift.At; column <= shift.Last; column++)
            {
                var made = new S.Cell
                {
                    CellReference = CellReference.Format(column, rowIndex),
                    StyleIndex = style
                };
                row.InsertAfter(made, after);
                after = made;
            }
        }
    }

    // The sheet's own ranges

    /// <summary>
    /// What else on the sheet names its cells by range: merges, conditional formats, validations, links,
    /// the used range, the views' selection and scroll, the filter, page breaks, ignored errors, protected
    /// ranges, Excel 2010's extensions and — for columns — the columns' widths and styles. A merge left one
    /// cell is no merge; anything else whose cells all went goes too.
    /// </summary>
    static void ShiftRanges(S.Worksheet worksheet, SheetShift shift)
    {
        if (worksheet.GetFirstChild<S.MergeCells>() is { } merges)
        {
            foreach (var merge in merges.Elements<S.MergeCell>().ToList())
            {
                if (shift.Reference(merge.Reference?.Value) is not { } moved ||
                    CellReference.ParseRange(moved) is not { } range ||
                    range is {RowCount: 1, ColumnCount: 1})
                {
                    merge.Remove();
                    continue;
                }

                merge.Reference = moved;
            }

            var count = merges.Elements<S.MergeCell>().Count();
            merges.Count = (uint) count;
            if (count == 0)
            {
                merges.Remove();
            }
        }

        foreach (var format in worksheet.Elements<S.ConditionalFormatting>().ToList())
        {
            var sqref = format.SequenceOfReferences?.InnerText;
            if (shift.Ranges(sqref) is not { } moved)
            {
                format.Remove();
                continue;
            }

            if (moved != sqref)
            {
                format.SequenceOfReferences = new()
                {
                    InnerText = moved
                };
            }
        }

        if (worksheet.GetFirstChild<S.DataValidations>() is { } validations)
        {
            foreach (var validation in validations.Elements<S.DataValidation>().ToList())
            {
                var sqref = validation.SequenceOfReferences?.InnerText;
                if (shift.Ranges(sqref) is not { } moved)
                {
                    validation.Remove();
                    continue;
                }

                if (moved != sqref)
                {
                    validation.SequenceOfReferences = new()
                    {
                        InnerText = moved
                    };
                }
            }

            var count = validations.Elements<S.DataValidation>().Count();
            validations.Count = (uint) count;
            if (count == 0)
            {
                validations.Remove();
            }
        }

        ShiftLinks(worksheet, shift);

        if (worksheet.GetFirstChild<S.SheetDimension>() is {Reference.Value: { } dimension} used)
        {
            used.Reference = shift.Reference(dimension) ?? "A1";
        }

        // A view's cells stay where they are when theirs went, as Excel's selection does.
        foreach (var view in worksheet.Descendants<S.SheetView>())
        {
            if (view.TopLeftCell?.Value is { } top)
            {
                view.TopLeftCell = shift.Reference(top) ?? top;
            }

            foreach (var pane in view.Elements<S.Pane>())
            {
                if (pane.TopLeftCell?.Value is { } paneTop)
                {
                    pane.TopLeftCell = shift.Reference(paneTop) ?? paneTop;
                }
            }

            foreach (var selection in view.Elements<S.Selection>())
            {
                if (selection.ActiveCell?.Value is { } active)
                {
                    selection.ActiveCell = shift.Reference(active) ?? active;
                }

                if (selection.SequenceOfReferences?.InnerText is { Length: > 0 } sqref)
                {
                    selection.SequenceOfReferences = new()
                    {
                        InnerText = shift.Ranges(sqref) ?? selection.ActiveCell?.Value ?? "A1"
                    };
                }
            }
        }

        if (worksheet.GetFirstChild<S.AutoFilter>() is { } filter)
        {
            if (CellReference.ParseRange(filter.Reference?.Value) is not { } range ||
                shift.Range(range) is not { } moved)
            {
                filter.Remove();
            }
            else
            {
                ShiftFilterColumns(filter, range, moved, shift);
                filter.Reference = RangeText(moved);
                ShiftSort(filter.GetFirstChild<S.SortState>(), shift);
            }
        }

        ShiftSort(worksheet.GetFirstChild<S.SortState>(), shift);

        if (shift.Rows &&
            worksheet.GetFirstChild<S.RowBreaks>() is { } rowBreaks)
        {
            var (count, manual) = ShiftBreaks(rowBreaks, shift);
            rowBreaks.Count = count;
            rowBreaks.ManualBreakCount = manual;
        }

        if (!shift.Rows &&
            worksheet.GetFirstChild<S.ColumnBreaks>() is { } columnBreaks)
        {
            var (count, manual) = ShiftBreaks(columnBreaks, shift);
            columnBreaks.Count = count;
            columnBreaks.ManualBreakCount = manual;
        }

        foreach (var ignored in worksheet.Descendants<S.IgnoredError>().ToList())
        {
            if (shift.Ranges(ignored.SequenceOfReferences?.InnerText) is { } moved)
            {
                ignored.SequenceOfReferences = new()
                {
                    InnerText = moved
                };
            }
            else
            {
                Prune(ignored);
            }
        }

        foreach (var range in worksheet.Descendants<S.ProtectedRange>().ToList())
        {
            if (shift.Ranges(range.SequenceOfReferences?.InnerText) is { } moved)
            {
                range.SequenceOfReferences = new()
                {
                    InnerText = moved
                };
            }
            else
            {
                Prune(range);
            }
        }

        // Excel 2010's conditional formats, validations and sparklines say where they stand as xm:sqref.
        foreach (var sqref in worksheet.Descendants<XM.ReferenceSequence>().ToList())
        {
            if (shift.Ranges(sqref.Text) is { } moved)
            {
                if (moved != sqref.Text)
                {
                    sqref.Text = moved;
                }
            }
            else if (sqref.Parent is { } owner)
            {
                Prune(owner);
            }
        }

        if (!shift.Rows)
        {
            ShiftColumnSpans(worksheet, shift);
        }
    }

    // An element taken out, and the lists it leaves empty with it.
    static void Prune(OpenXmlElement element)
    {
        var parent = element.Parent;
        element.Remove();
        while (parent is {HasChildren: false} and not S.Worksheet)
        {
            var next = parent.Parent;
            parent.Remove();
            parent = next;
        }
    }

    static void ShiftLinks(S.Worksheet worksheet, SheetShift shift)
    {
        if (worksheet.GetFirstChild<S.Hyperlinks>() is not { } links)
        {
            return;
        }

        var part = worksheet.WorksheetPart;
        foreach (var link in links.Elements<S.Hyperlink>().ToList())
        {
            if (shift.Reference(link.Reference?.Value) is { } moved)
            {
                if (moved != link.Reference?.Value)
                {
                    link.Reference = moved;
                }

                continue;
            }

            if (link.Id?.Value is { } id &&
                part?.HyperlinkRelationships.FirstOrDefault(_ => _.Id == id) is { } relationship)
            {
                part.DeleteReferenceRelationship(relationship);
            }

            link.Remove();
        }

        if (!links.HasChildren)
        {
            links.Remove();
        }
    }

    // A filter's columns are counted from its first: those after the change move, and those deleted go.
    static void ShiftFilterColumns(S.AutoFilter filter, SheetRange old, SheetRange moved, SheetShift shift)
    {
        if (shift.Rows)
        {
            return;
        }

        foreach (var column in filter.Elements<S.FilterColumn>().ToList())
        {
            if (shift.Index(old.FirstColumn + (int) (column.ColumnId?.Value ?? 0)) is not { } at)
            {
                column.Remove();
                continue;
            }

            column.ColumnId = (uint) (at - moved.FirstColumn);
        }
    }

    static void ShiftSort(S.SortState? sort, SheetShift shift)
    {
        if (sort == null)
        {
            return;
        }

        if (shift.Reference(sort.Reference?.Value) is not { } moved)
        {
            sort.Remove();
            return;
        }

        sort.Reference = moved;
        foreach (var condition in sort.Elements<S.SortCondition>().ToList())
        {
            if (shift.Reference(condition.Reference?.Value) is { } by)
            {
                condition.Reference = by;
            }
            else
            {
                condition.Remove();
            }
        }

        if (!sort.Elements<S.SortCondition>().Any())
        {
            sort.Remove();
        }
    }

    /// <summary>
    /// A page break stands above the row (left of the column) after the one its zero-based id names
    /// (ECMA-376 §18.3.1.0): it moves with that row, and one whose row was deleted stands where the
    /// deleted rows were.
    /// </summary>
    static (uint Count, uint Manual) ShiftBreaks(OpenXmlCompositeElement breaks, SheetShift shift)
    {
        var seen = new HashSet<uint>();
        foreach (var each in breaks.Elements<S.Break>().ToList())
        {
            var first = (int) (each.Id?.Value ?? 0) + 1;
            int moved;
            if (shift.Index(first) is { } index)
            {
                moved = index;
            }
            else if (shift.Delete)
            {
                moved = shift.At;
            }
            else
            {
                each.Remove();
                continue;
            }

            var id = (uint) (moved - 1);
            if (!seen.Add(id))
            {
                each.Remove();
                continue;
            }

            each.Id = id;
        }

        var kept = breaks.Elements<S.Break>().ToList();
        return ((uint) kept.Count, (uint) kept.Count(_ => _.ManualPageBreak?.Value == true));
    }

    /// <summary>
    /// The columns' spans (<c>col</c>, their width and style) cut and moved: a span the change falls
    /// inside grows or shrinks, as a range does, and columns inserted after a span take its look.
    /// </summary>
    static void ShiftColumnSpans(S.Worksheet worksheet, SheetShift shift)
    {
        if (worksheet.GetFirstChild<S.Columns>() is not { } columns)
        {
            return;
        }

        S.Column? left = null;
        foreach (var span in columns.Elements<S.Column>().ToList())
        {
            var min = (int) (span.Min?.Value ?? 0);
            var max = (int) (span.Max?.Value ?? 0);
            if (min < 1 ||
                max < min)
            {
                continue;
            }

            if (max == shift.At - 1)
            {
                left = span;
            }

            if (shift.Span(min, max) is not { } moved)
            {
                span.Remove();
                continue;
            }

            span.Min = (uint) moved.First;
            span.Max = (uint) moved.Last;
        }

        if (!shift.Delete &&
            left != null)
        {
            columns.InsertAfter(Span(left, shift.At, shift.Last), left);
        }

        if (!columns.HasChildren)
        {
            columns.Remove();
        }
    }

    // Tables

    sealed class TableChange(TableDefinitionPart part, S.Table table, SheetRange old)
    {
        public TableDefinitionPart Part { get; } = part;

        public S.Table Table { get; } = table;

        public SheetRange Old { get; } = old;

        /// <summary>Where it is now; null when it was deleted whole.</summary>
        public SheetRange? New { get; set; }

        public bool TotalsOff { get; set; }

        /// <summary>Rows inserted inside it, which its calculated columns fill.</summary>
        public bool Grown { get; set; }

        /// <summary>
        /// Rows inserted at its first data row, which then hold its calculated columns' formulas as they
        /// were, so those are left as they were.
        /// </summary>
        public bool AtFirstDataRow { get; set; }

        /// <summary>Where, among its columns, columns were inserted; -1 for none.</summary>
        public int InsertedAt { get; set; } = -1;

        /// <summary>Its columns deleted, by their place among its columns.</summary>
        public List<int> Deleted { get; } = [];
    }

    /// <summary>
    /// What the change does to each of the sheet's tables, worked out before anything moves (Excel 16 over
    /// COM, 2026-11-17): rows or columns inserted inside one make it larger — a column so made named after
    /// the one on its left (<see cref="NewColumnName"/>) — and one deleted from it make it smaller. Its
    /// header row can't be deleted on its own, though its totals row can (the totals are then off); every
    /// data row deleted leaves the header with one row under it; and its every row (or column) deleted
    /// deletes it. A table can't be made larger or smaller while another table shares the rows (or
    /// columns) changed — two tables side by side, or one above the other — though both can move whole.
    /// </summary>
    static List<TableChange> TableChanges(S.Worksheet worksheet, SheetShift shift)
    {
        var changes = new List<TableChange>();
        var touched = 0;
        var resized = false;
        foreach (var tablePart in worksheet.WorksheetPart?.TableDefinitionParts ?? [])
        {
            if (tablePart.Table is not { } table ||
                CellReference.ParseRange(table.Reference?.Value) is not { } range)
            {
                continue;
            }

            var name = table.DisplayName?.Value ?? table.Name?.Value ?? string.Empty;
            var change = new TableChange(tablePart, table, range)
            {
                New = shift.Range(range)
            };
            changes.Add(change);
            var (first, last) = Axis(range, shift.Rows);
            if ((shift.Delete && first <= shift.Last && last >= shift.At) ||
                (!shift.Delete && first <= shift.At && shift.At <= last))
            {
                touched++;
            }

            if (Splits((first, last), shift))
            {
                resized = true;
            }

            if (change.New == null)
            {
                shift.Lost[name] = null;
                continue;
            }

            if (!shift.Delete)
            {
                if (first < shift.At &&
                    shift.At <= last)
                {
                    if (shift.Rows)
                    {
                        change.Grown = true;
                        Inserted(change, shift);
                    }
                    else
                    {
                        change.InsertedAt = shift.At - first;
                    }
                }

                continue;
            }

            if (shift.Rows)
            {
                RowsDeleted(change, shift);
                continue;
            }

            var columns = table.TableColumns?.Elements<S.TableColumn>().ToList() ?? [];
            var lost = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var column = Math.Max(first, shift.At); column <= Math.Min(last, shift.Last); column++)
            {
                var index = column - first;
                change.Deleted.Add(index);
                if (index < columns.Count)
                {
                    lost.Add(Tables.Decode(columns[index].Name?.Value ?? string.Empty));
                }
            }

            if (lost.Count > 0)
            {
                shift.Lost[name] = lost;
            }
        }

        if (resized &&
            touched > 1)
        {
            var across = "columns";
            if (shift.Rows)
            {
                across = "rows";
            }

            throw new InvalidOperationException($"This can't be done: it would shift cells in a table, and another table shares those {across}.");
        }

        return changes;
    }

    // Rows inserted at a table's first data row: its calculated columns' formulas are the new first row's
    // as they were, and a range over its data rows grows with it.
    static void Inserted(TableChange change, SheetShift shift)
    {
        var range = change.Old;
        var headers = (int) (change.Table.HeaderRowCount?.Value ?? 1);
        var totals = (int) (change.Table.TotalsRowCount?.Value ?? 0);
        var dataFirst = range.FirstRow + headers;
        if (headers == 0 ||
            shift.At != dataFirst)
        {
            return;
        }

        change.AtFirstDataRow = true;
        var dataLast = range.LastRow - totals;
        if (dataLast >= dataFirst)
        {
            shift.Bodies.Add(new(dataFirst, range.FirstColumn, dataLast, range.LastColumn));
        }
    }

    static void RowsDeleted(TableChange change, SheetShift shift)
    {
        var range = change.Old;
        var headers = (int) (change.Table.HeaderRowCount?.Value ?? 1);
        var totals = (int) (change.Table.TotalsRowCount?.Value ?? 0);
        if (headers > 0 &&
            range.FirstRow >= shift.At &&
            range.FirstRow <= shift.Last)
        {
            throw new InvalidOperationException("A table's header row can't be deleted without the rest of the table.");
        }

        var totalsGone = totals > 0 &&
                         range.LastRow >= shift.At &&
                         range.LastRow <= shift.Last;
        change.TotalsOff = totalsGone;
        var dataFirst = range.FirstRow + headers;
        var dataLast = range.LastRow - totals;
        var gone = Math.Max(0, Math.Min(dataLast, shift.Last) - Math.Max(dataFirst, shift.At) + 1);
        if (gone < dataLast - dataFirst + 1)
        {
            return;
        }

        if (totals > 0 &&
            !totalsGone)
        {
            throw new InvalidOperationException("A table must keep a row of data above its totals.");
        }

        change.New = range with
        {
            LastRow = range.FirstRow + headers
        };
    }

    static void ApplyTables(SpreadsheetDocument document, S.Worksheet worksheet, SheetShift shift, List<TableChange> changes)
    {
        StringTable? strings = null;
        foreach (var change in changes)
        {
            var table = change.Table;
            if (change.New is not { } range)
            {
                DeleteTable(worksheet, change.Part);
                continue;
            }

            table.Reference = RangeText(range);
            if (change.TotalsOff)
            {
                table.TotalsRowCount = null;
            }

            var list = table.TableColumns ??= new();
            var columns = list.Elements<S.TableColumn>().ToList();
            foreach (var index in change.Deleted)
            {
                if (index < columns.Count)
                {
                    columns[index].Remove();
                }
            }

            if (change.InsertedAt >= 0)
            {
                strings ??= new(document.WorkbookPart!);
                InsertTableColumns(worksheet, table, columns, change.InsertedAt, range, shift, strings);
            }

            list.Count = (uint) list.Elements<S.TableColumn>().Count();
            if (table.AutoFilter is { } filter)
            {
                var old = CellReference.ParseRange(filter.Reference?.Value) ?? change.Old;
                var totals = (int) (table.TotalsRowCount?.Value ?? 0);
                var filtered = range with
                {
                    LastRow = range.LastRow - totals
                };
                ShiftFilterColumns(filter, old, filtered, shift);
                filter.Reference = RangeText(filtered);
            }

            ShiftSort(table.SortState, shift);
            if (change.Grown)
            {
                FillCalculated(worksheet, table, range, shift);
            }
        }

        strings?.Save();
    }

    // Columns inserted inside a table are its columns too, in its header row as in its list, each named
    // after the one on its left (NewColumnName).
    static void InsertTableColumns(S.Worksheet worksheet, S.Table table, List<S.TableColumn> columns, int at, SheetRange range, SheetShift shift, StringTable strings)
    {
        var list = table.TableColumns!;
        var names = columns
            .Select(_ => Tables.Decode(_.Name?.Value ?? string.Empty))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var id = columns.Count == 0 ? 0 : columns.Max(_ => _.Id?.Value ?? 0);
        S.TableColumn? before = null;
        if (at < columns.Count)
        {
            before = columns[at];
        }

        string? left = null;
        if (at > 0 &&
            at <= columns.Count)
        {
            left = Tables.Decode(columns[at - 1].Name?.Value ?? string.Empty);
        }

        var header = (table.HeaderRowCount?.Value ?? 1) != 0;
        for (var index = 0; index < shift.Count; index++)
        {
            var name = NewColumnName(left, names);
            names.Add(name);
            left = name;
            var made = new S.TableColumn
            {
                Id = ++id,
                Name = name
            };
            if (before == null)
            {
                list.AppendChild(made);
            }
            else
            {
                list.InsertBefore(made, before);
            }

            if (!header)
            {
                continue;
            }

            var cell = CellAt(worksheet, range.FirstRow, shift.At + index);
            cell.CellFormula = null;
            cell.InlineString = null;
            cell.DataType = S.CellValues.SharedString;
            cell.CellValue = new(strings.Index(name).ToString(CultureInfo.InvariantCulture));
            Tidy(cell);
        }
    }

    /// <summary>
    /// A new table column's name, from the column on its left, as Excel makes it (Excel 16 over COM,
    /// 2026-11-17): a name ending in a number takes the next number (Item 7 → Item 8), or failing that the
    /// name with 2, 3 and on after it (Column 1 → Column 12 where Column 2 is taken, Total2 → Total22);
    /// any other name gives Column1, Column2 and on. The first such name no column has.
    /// </summary>
    internal static string NewColumnName(string? left, ISet<string> names)
    {
        if (left != null &&
            TrailingNumber(left) is var (prefix, number))
        {
            var next = prefix + (number + 1).ToString(CultureInfo.InvariantCulture);
            if (!names.Contains(next))
            {
                return next;
            }

            for (var suffix = 2; ; suffix++)
            {
                var candidate = left + suffix.ToString(CultureInfo.InvariantCulture);
                if (!names.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        for (var suffix = 1; ; suffix++)
        {
            var candidate = "Column" + suffix.ToString(CultureInfo.InvariantCulture);
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    // The digits a name ends in, as a number, and what is before them.
    static (string Prefix, long Number)? TrailingNumber(string name)
    {
        var start = name.Length;
        while (start > 0 &&
               char.IsAsciiDigit(name[start - 1]))
        {
            start--;
        }

        if (start == name.Length ||
            !long.TryParse(name.AsSpan(start), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
            number == long.MaxValue)
        {
            return null;
        }

        return (name[..start], number);
    }

    // Rows inserted inside a table take its calculated columns' formulas, as Excel fills them: the formula
    // of the first data row, copied down to theirs — its relative references moved by as many rows.
    static void FillCalculated(S.Worksheet worksheet, S.Table table, SheetRange range, SheetShift shift)
    {
        var first = range.FirstRow + (int) (table.HeaderRowCount?.Value ?? 1);
        var index = 0;
        foreach (var column in table.TableColumns?.Elements<S.TableColumn>() ?? [])
        {
            if (column.CalculatedColumnFormula is {Text: {Length: > 0} text} formula &&
                formula.Array?.Value != true)
            {
                for (var row = shift.At; row <= shift.Last; row++)
                {
                    var cell = CellAt(worksheet, row, range.FirstColumn + index);
                    cell.CellValue = null;
                    cell.InlineString = null;
                    cell.DataType = null;
                    cell.CellFormula = new(FormulaShifter.Shift(text, row - first, 0));
                    Tidy(cell);
                }
            }

            index++;
        }
    }

    static void DeleteTable(S.Worksheet worksheet, TableDefinitionPart tablePart)
    {
        var part = worksheet.WorksheetPart!;
        var id = part.GetIdOfPart(tablePart);
        if (worksheet.GetFirstChild<S.TableParts>() is { } list)
        {
            foreach (var each in list.Elements<S.TablePart>().Where(_ => _.Id?.Value == id).ToList())
            {
                each.Remove();
            }

            var count = list.Elements<S.TablePart>().Count();
            list.Count = (uint) count;
            if (count == 0)
            {
                list.Remove();
            }
        }

        part.DeletePart(tablePart);
    }

    // Drawings

    /// <summary>
    /// The pictures, shapes and charts over the sheet, by how each is anchored (Excel 16 over COM,
    /// 2026-11-17): one that moves and sizes with its cells grows with rows inserted through it, shrinks
    /// with rows deleted from it, and goes when every row it stood on does; one that moves but does not size
    /// moves with its first cell, keeping its size; one that does neither stays where it is.
    /// </summary>
    static void ShiftDrawing(WorksheetPart part, SheetShift shift)
    {
        if (part.DrawingsPart is not {WorksheetDrawing: { } drawing} drawings)
        {
            return;
        }

        var gone = new List<OpenXmlElement>();
        foreach (var anchor in drawing.ChildElements.ToList())
        {
            switch (anchor)
            {
                case XDR.TwoCellAnchor
                {
                    FromMarker: { } from,
                    ToMarker: { } to
                } two:
                    var mode = two.EditAs?.Value;
                    if (mode == XDR.EditAsValues.Absolute)
                    {
                        break;
                    }

                    if (mode == XDR.EditAsValues.OneCell)
                    {
                        MoveAnchor(from, to, shift);
                        break;
                    }

                    if (!SizeAnchor(from, to, shift))
                    {
                        gone.Add(two);
                    }

                    break;
                case XDR.OneCellAnchor
                {
                    FromMarker: { } start
                }:
                    MoveAnchor(start, null, shift);
                    break;
            }
        }

        foreach (var anchor in gone)
        {
            RemoveAnchor(drawings, drawing, anchor);
        }
    }

    // Moves and sizes with its cells; false when every row (or column) it stood on was deleted.
    static bool SizeAnchor(OpenXmlElement from, OpenXmlElement to, SheetShift shift)
    {
        var (start, startOffset) = ReadMarker(from, shift.Rows);
        var (end, endOffset) = ReadMarker(to, shift.Rows);
        if (shift.Delete)
        {
            // An end at the very top of a row stands on the row before it.
            var lastOn = end;
            if (endOffset == 0)
            {
                lastOn = end - 1;
            }

            if (start >= shift.At &&
                lastOn <= shift.Last)
            {
                return false;
            }
        }

        PlaceMarker(from, start, startOffset, shift);
        PlaceMarker(to, end, endOffset, shift);
        return true;
    }

    static void PlaceMarker(OpenXmlElement marker, int index, long offset, SheetShift shift)
    {
        if (shift.Index(index) is { } moved)
        {
            if (moved != index)
            {
                WriteMarker(marker, shift.Rows, moved, offset);
            }

            return;
        }

        // In the rows deleted: at the edge of what is left. Pushed past the sheet's end: at its end.
        if (shift.Delete)
        {
            WriteMarker(marker, shift.Rows, shift.At, 0);
        }
        else
        {
            WriteMarker(marker, shift.Rows, shift.Max, offset);
        }
    }

    // Moves with its first cell and keeps its size.
    static void MoveAnchor(OpenXmlElement from, OpenXmlElement? to, SheetShift shift)
    {
        var (start, startOffset) = ReadMarker(from, shift.Rows);
        var target = shift.Max;
        var targetOffset = startOffset;
        if (shift.Index(start) is { } moved)
        {
            target = moved;
        }
        else if (shift.Delete)
        {
            target = shift.At;
            targetOffset = 0;
        }

        if (target == start &&
            targetOffset == startOffset)
        {
            return;
        }

        WriteMarker(from, shift.Rows, target, targetOffset);
        if (to != null)
        {
            var (end, endOffset) = ReadMarker(to, shift.Rows);
            WriteMarker(to, shift.Rows, Math.Clamp(end + target - start, 1, shift.Max), endOffset);
        }
    }

    // A marker's row (or column), 1-based — the file's are zero-based — and its offset into it in EMU.
    static (int Index, long Offset) ReadMarker(OpenXmlElement marker, bool rows)
    {
        string? id;
        string? offset;
        if (rows)
        {
            id = marker.GetFirstChild<XDR.RowId>()?.Text;
            offset = marker.GetFirstChild<XDR.RowOffset>()?.Text;
        }
        else
        {
            id = marker.GetFirstChild<XDR.ColumnId>()?.Text;
            offset = marker.GetFirstChild<XDR.ColumnOffset>()?.Text;
        }

        var index = 1;
        if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zero))
        {
            index = zero + 1;
        }

        long emu = 0;
        if (long.TryParse(offset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            emu = parsed;
        }

        return (index, emu);
    }

    static void WriteMarker(OpenXmlElement marker, bool rows, int index, long offset)
    {
        var id = (index - 1).ToString(CultureInfo.InvariantCulture);
        var emu = offset.ToString(CultureInfo.InvariantCulture);
        if (rows)
        {
            if (marker.GetFirstChild<XDR.RowId>() is { } row)
            {
                row.Text = id;
            }

            if (marker.GetFirstChild<XDR.RowOffset>() is { } rowOffset)
            {
                rowOffset.Text = emu;
            }

            return;
        }

        if (marker.GetFirstChild<XDR.ColumnId>() is { } column)
        {
            column.Text = id;
        }

        if (marker.GetFirstChild<XDR.ColumnOffset>() is { } columnOffset)
        {
            columnOffset.Text = emu;
        }
    }

    // A drawing taken out, with the parts it alone used — its picture, its chart.
    static void RemoveAnchor(DrawingsPart drawings, XDR.WorksheetDrawing drawing, OpenXmlElement anchor)
    {
        var ids = RelationshipIds(anchor).ToHashSet(StringComparer.Ordinal);
        anchor.Remove();
        ids.ExceptWith(RelationshipIds(drawing));
        foreach (var id in ids)
        {
            if (drawings.Parts.FirstOrDefault(_ => _.RelationshipId == id) is var pair)
            {
                drawings.DeletePart(pair.OpenXmlPart);
                continue;
            }

            foreach (var reference in drawings.HyperlinkRelationships
                         .Cast<ReferenceRelationship>()
                         .Concat(drawings.ExternalRelationships)
                         .Where(_ => _.Id == id)
                         .ToList())
            {
                drawings.DeleteReferenceRelationship(reference);
            }
        }
    }

    const string relationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    static IEnumerable<string> RelationshipIds(OpenXmlElement element) =>
        element.Descendants()
            .Prepend(element)
            .SelectMany(_ => _.GetAttributes())
            .Where(_ => _.NamespaceUri == relationshipNamespace && !string.IsNullOrEmpty(_.Value))
            .Select(_ => _.Value!);

    // Comments

    /// <summary>
    /// Notes and threaded comments move with their cells, and go with them. A note's box (in the sheet's
    /// VML) moves as far as its cell did.
    /// </summary>
    static void ShiftComments(WorksheetPart part, SheetShift shift)
    {
        if (part.WorksheetCommentsPart?.Comments?.CommentList is { } list)
        {
            foreach (var comment in list.Elements<S.Comment>().ToList())
            {
                var reference = comment.Reference?.Value;
                if (shift.Reference(reference) is not { } moved)
                {
                    comment.Remove();
                    continue;
                }

                if (moved != reference)
                {
                    comment.Reference = moved;
                }
            }
        }

        foreach (var threads in part.WorksheetThreadedCommentsParts)
        {
            foreach (var comment in threads.ThreadedComments?.Elements<TC.ThreadedComment>().ToList() ?? [])
            {
                var reference = comment.Ref?.Value;
                if (shift.Reference(reference) is not { } moved)
                {
                    comment.Remove();
                    continue;
                }

                if (moved != reference)
                {
                    comment.Ref = moved;
                }
            }
        }

        foreach (var vml in part.VmlDrawingParts)
        {
            ShiftNotes(vml, shift);
        }
    }

    static void ShiftNotes(VmlDrawingPart vml, SheetShift shift)
    {
        XDocument notes;
        try
        {
            using var stream = vml.GetStream(FileMode.Open, FileAccess.Read);
            notes = XDocument.Load(stream);
        }
        catch (XmlException)
        {
            // Legacy VML that is not well-formed XML is left as it is.
            return;
        }

        XNamespace excel = "urn:schemas-microsoft-com:office:excel";
        var axis = excel + "Column";
        int[] edges = [0, 4];
        if (shift.Rows)
        {
            axis = excel + "Row";
            edges = [2, 6];
        }

        var changed = false;
        foreach (var data in notes.Descendants(excel + "ClientData").ToList())
        {
            if (!string.Equals((string?) data.Attribute("ObjectType"), "Note", StringComparison.OrdinalIgnoreCase) ||
                data.Element(axis) is not { } place ||
                !int.TryParse(place.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zero))
            {
                continue;
            }

            if (shift.Index(zero + 1) is not { } moved)
            {
                data.Parent?.Remove();
                changed = true;
                continue;
            }

            var by = moved - (zero + 1);
            if (by == 0)
            {
                continue;
            }

            place.Value = (moved - 1).ToString(CultureInfo.InvariantCulture);
            // LeftColumn, LeftOffset, TopRow, TopOffset, RightColumn, RightOffset, BottomRow, BottomOffset.
            if (data.Element(excel + "Anchor") is { } anchor &&
                anchor.Value.Split(',') is {Length: 8} parts)
            {
                foreach (var edge in edges)
                {
                    if (int.TryParse(parts[edge].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    {
                        parts[edge] = (value + by).ToString(CultureInfo.InvariantCulture);
                    }
                }

                anchor.Value = string.Join(", ", parts.Select(_ => _.Trim()));
            }

            changed = true;
        }

        if (!changed)
        {
            return;
        }

        using var output = vml.GetStream(FileMode.Create, FileAccess.Write);
        notes.Save(output);
    }
}
