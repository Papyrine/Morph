using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// The edits made to a workbook: cells set to what was typed, cleared, copied, formatted, and columns
/// and rows sized. Each takes a workbook's bytes and returns the edited workbook's; the input is never
/// touched, which is what makes undo a matter of keeping the old array, as it is for Word documents.
///
/// Every edit that changes what a cell holds works the workbook's formulas out again
/// (<see cref="FormulaWorkbook"/>) and writes their values into the file, so a formula's cell shows its
/// new value wherever the file is read — in the grid, on Morph's pages, in a reader that does not
/// calculate. The calculation chain (<c>calcChain.xml</c>), which names the cells that hold formulas,
/// is dropped, as a stale one makes Excel repair the file; Excel builds it again. Where a formula used a
/// function the engine has not got, the file asks Excel to work everything out when it opens.
///
/// Sheets are named by their place in the workbook's list of sheets, hidden ones included.
/// </summary>
static partial class WorkbookEditor
{
    /// <summary>Sets cells to what was typed into them.</summary>
    public static byte[] SetCells(byte[] xlsx, int sheet, IReadOnlyList<CellEdit> edits, DateTime now) =>
        Edit(xlsx, now, _ =>
        {
            var worksheet = Worksheet(_, sheet);
            var styles = new StyleBook(_.WorkbookPart!);
            var strings = new StringTable(_.WorkbookPart!);
            var arrays = ArrayBlocks(worksheet);
            foreach (var edit in edits)
            {
                if (arrays.Any(block => Inside(block, edit.Row, edit.Column) &&
                                        block is not
                                        {
                                            RowCount: 1,
                                            ColumnCount: 1
                                        }))
                {
                    throw new InvalidOperationException("You can't change part of an array.");
                }
            }

            foreach (var edit in edits)
            {
                var cell = CellAt(worksheet, edit.Row, edit.Column);
                var style = cell.StyleIndex?.Value ?? 0;
                var entry = CellEntry.Read(edit.Input, styles.FormatCode(style));
                if (Tables.HeaderAt(worksheet, edit.Row, edit.Column) is { } header)
                {
                    Tables.Rename(_, header, entry, strings, cell);
                    continue;
                }

                ExpandShared(worksheet, cell);
                Write(cell, entry, strings);
                if (entry.ImpliedFormat is { } implied &&
                    styles.FormatCode(style) is null or "General")
                {
                    cell.StyleIndex = styles.Changed(style, new()
                    {
                        NumberFormat = implied
                    });
                }

                // Text typed over lines (Alt+Enter) wraps, as Excel turns wrapping on for it.
                if (entry.Kind == CellEntryKind.Text &&
                    entry.Text.Contains('\n'))
                {
                    cell.StyleIndex = styles.Changed(cell.StyleIndex?.Value ?? 0, new()
                    {
                        Wrap = true
                    });
                }

                Tidy(cell);
            }

            strings.Save();
            styles.Save();
        });

    /// <summary>
    /// Clears a range: what its cells hold (as Delete does), how they look, or both — the cells themselves
    /// then gone.
    /// </summary>
    public static byte[] Clear(byte[] xlsx, int sheet, SheetRange range, bool contents, bool formats, DateTime now) =>
        Edit(xlsx, now, _ =>
        {
            var worksheet = Worksheet(_, sheet);
            var arrays = ArrayBlocks(worksheet);
            if (contents &&
                arrays.Any(block => Overlaps(block, range) && !Covers(range, block)))
            {
                throw new InvalidOperationException("You can't change part of an array.");
            }

            var strings = new StringTable(_.WorkbookPart!);
            foreach (var cell in CellsIn(worksheet, range).ToList())
            {
                var row = CellReference.RowOf(cell.CellReference?.Value);
                var column = CellReference.ColumnOf(cell.CellReference?.Value);
                if (contents &&
                    Tables.HeaderAt(worksheet, row, column) is { } header)
                {
                    Tables.Rename(_, header, new(CellEntryKind.Clear), strings, cell);
                    if (formats)
                    {
                        cell.StyleIndex = null;
                    }

                    continue;
                }

                if (contents)
                {
                    ExpandShared(worksheet, cell);
                    Write(cell, new(CellEntryKind.Clear), strings);
                }

                if (formats)
                {
                    cell.StyleIndex = null;
                }

                if (contents &&
                    formats)
                {
                    cell.Remove();
                }
            }

            strings.Save();
        });

    /// <summary>
    /// Copies a range's cells — what they hold and how they look — to another place, a formula moved as a
    /// copied formula moves: its relative references by as far as the cell went. A cut moves them instead,
    /// formulas as they were, and clears where they came from.
    /// </summary>
    public static byte[] Copy(byte[] xlsx, int fromSheet, SheetRange from, int toSheet, int toRow, int toColumn, bool cut, DateTime now) =>
        Edit(xlsx, now, _ =>
        {
            var source = Worksheet(_, fromSheet);
            var target = Worksheet(_, toSheet);
            var rows = toRow - from.FirstRow;
            var columns = toColumn - from.FirstColumn;
            if (toRow + from.RowCount - 1 > CellReference.MaxRow ||
                toColumn + from.ColumnCount - 1 > CellReference.MaxColumn)
            {
                throw new InvalidOperationException("The cells would go off the edge of the sheet.");
            }

            // Read whole before anything is written, as the two places may overlap.
            var copied = new List<(int Row, int Column, S.Cell Cell, string? Formula)>();
            foreach (var cell in CellsIn(source, from))
            {
                var row = CellReference.RowOf(cell.CellReference?.Value);
                var column = CellReference.ColumnOf(cell.CellReference?.Value);
                string? formula = null;
                if (FormulaOf(source, cell) is { } text)
                {
                    formula = text;
                    if (!cut)
                    {
                        formula = FormulaShifter.Shift(text, rows, columns);
                    }
                }

                copied.Add((row, column, (S.Cell) cell.CloneNode(true), formula));
            }

            if (cut)
            {
                foreach (var cell in CellsIn(source, from).ToList())
                {
                    ExpandShared(source, cell);
                    cell.Remove();
                }
            }

            var landing = new SheetRange(toRow, toColumn, toRow + from.RowCount - 1, toColumn + from.ColumnCount - 1);
            foreach (var cell in CellsIn(target, landing).ToList())
            {
                ExpandShared(target, cell);
                cell.Remove();
            }

            foreach (var (row, column, clone, formula) in copied)
            {
                var cell = CellAt(target, row + rows, column + columns);
                cell.StyleIndex = clone.StyleIndex;
                cell.DataType = clone.DataType;
                cell.RemoveAllChildren();
                if (formula != null)
                {
                    cell.CellFormula = new(formula);
                }

                if (clone.CellValue is { } value)
                {
                    cell.CellValue = (S.CellValue) value.CloneNode(true);
                }

                if (clone.InlineString is { } inline)
                {
                    cell.InlineString = (S.InlineString) inline.CloneNode(true);
                }

                Tidy(cell);
            }
        });

    /// <summary>Works a workbook's formulas out again and writes their values in.</summary>
    public static byte[] Recalculate(byte[] xlsx, DateTime now) =>
        Edit(xlsx, now, _ => { });

    // Opens a copy of the workbook for writing, makes the edit, works the formulas out (unless now is
    // null, for an edit that changes no value) and returns the edited file.
    static byte[] Edit(byte[] xlsx, DateTime? now, Action<SpreadsheetDocument> edit)
    {
        using var input = new MemoryStream(xlsx, false);
        var normalized = StrictToTransitional.Normalize(input);
        var stream = new MemoryStream();
        try
        {
            normalized.CopyTo(stream);
        }
        finally
        {
            if (!ReferenceEquals(normalized, input))
            {
                normalized.Dispose();
            }
        }

        stream.Position = 0;
        FastDeflate.Mark(stream.GetBuffer().AsSpan(0, (int) stream.Length));
        using (stream)
        {
            using (var document = SpreadsheetDocument.Open(stream, true))
            {
                edit(document);
                if (now is { } time)
                {
                    WriteValues(document, time);
                }
            }

            return stream.ToArray();
        }
    }

    static void WriteValues(SpreadsheetDocument document, DateTime now)
    {
        var workbookPart = document.WorkbookPart!;
        var book = FormulaWorkbook.Read(workbookPart, now);
        book.Recalculate();
        foreach (var sheet in book.Sheets)
        {
            foreach (var cell in sheet.Cells)
            {
                if (cell is not {HasFormula: true, Fixed: false, Unsupported: false, Element: { } element})
                {
                    continue;
                }

                WriteValue(element, cell.Value);
            }

            // An array formula's other cells hold its results, as Excel caches them.
            var blocks = sheet.Cells
                .Where(_ => _ is {ArrayRange: not null, Unsupported: false})
                .Select(_ => _.ArrayRange!.Value)
                .ToList();
            foreach (var block in blocks)
            {
                foreach (var cell in sheet.In(block.FirstRow, block.FirstColumn, block.LastRow, block.LastColumn))
                {
                    if (cell is {HasFormula: false, Element: { } element})
                    {
                        WriteValue(element, cell.Value);
                    }
                }
            }
        }

        if (workbookPart.CalculationChainPart is { } chain)
        {
            workbookPart.DeletePart(chain);
        }

        if (book.NeedsExcel &&
            workbookPart.Workbook is { } workbook)
        {
            workbook.CalculationProperties ??= new();
            workbook.CalculationProperties.FullCalculationOnLoad = true;
        }
    }

    // A formula's value as Excel caches it: text as t="str", a logical as t="b", an error as t="e".
    static void WriteValue(S.Cell element, FormulaValue value)
    {
        switch (value.Kind)
        {
            case FormulaValueKind.Text:
                element.DataType = S.CellValues.String;
                element.CellValue = new(value.Text);
                break;
            case FormulaValueKind.Boolean:
                element.DataType = S.CellValues.Boolean;
                element.CellValue = new(value.Boolean ? "1" : "0");
                break;
            case FormulaValueKind.Error:
                element.DataType = S.CellValues.Error;
                element.CellValue = new(FormulaErrors.Text(value.Error));
                break;
            case FormulaValueKind.Number:
                element.DataType = null;
                element.CellValue = new(Number(value.Number));
                break;
            default:
                element.DataType = null;
                element.CellValue = new("0");
                break;
        }
    }

    // The shortest text that reads back as the same double.
    static string Number(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    // What a cell is to hold, written in. A formula's value is worked out once every edit is in.
    static void Write(S.Cell cell, CellEntry entry, StringTable strings)
    {
        cell.CellFormula = null;
        cell.CellValue = null;
        cell.InlineString = null;
        cell.DataType = null;
        switch (entry.Kind)
        {
            case CellEntryKind.Number:
                cell.CellValue = new(Number(entry.Number));
                break;
            case CellEntryKind.Text:
                cell.DataType = S.CellValues.SharedString;
                cell.CellValue = new(strings.Index(entry.Text).ToString(CultureInfo.InvariantCulture));
                break;
            case CellEntryKind.Boolean:
                cell.DataType = S.CellValues.Boolean;
                cell.CellValue = new(entry.Number != 0 ? "1" : "0");
                break;
            case CellEntryKind.Error:
                cell.DataType = S.CellValues.Error;
                cell.CellValue = new(FormulaErrors.Text(entry.Error));
                break;
            case CellEntryKind.Formula:
                cell.CellFormula = new(FormulaText.ForFile(entry.Text));
                break;
        }
    }

    // The children of a cell in the order the schema has them: f, v, is.
    static void Tidy(S.Cell cell)
    {
        var formula = cell.CellFormula;
        var value = cell.CellValue;
        var inline = cell.InlineString;
        formula?.Remove();
        value?.Remove();
        inline?.Remove();
        if (formula != null)
        {
            cell.PrependChild(formula);
        }

        if (value != null)
        {
            if (formula != null)
            {
                cell.InsertAfter(value, formula);
            }
            else
            {
                cell.PrependChild(value);
            }
        }

        if (inline != null)
        {
            cell.InsertAfter(inline, (OpenXmlElement?) value ?? formula);
        }
    }

    internal static S.Worksheet Worksheet(SpreadsheetDocument document, int sheet)
    {
        var workbookPart = document.WorkbookPart ??
                           throw new InvalidOperationException("The workbook has no sheets.");
        var sheets = workbookPart.Workbook?.Sheets?.Elements<S.Sheet>().ToList() ?? [];
        if (sheet < 0 ||
            sheet >= sheets.Count)
        {
            throw new InvalidOperationException("The workbook has no such sheet.");
        }

        if (sheets[sheet].Id?.Value is not { } id ||
            workbookPart.GetPartById(id) is not WorksheetPart part)
        {
            throw new InvalidOperationException("That sheet holds a chart, not cells.");
        }

        var worksheet = part.Worksheet ??= new(new S.SheetData());
        if (worksheet.GetFirstChild<S.SheetData>() == null)
        {
            worksheet.AppendChild(new S.SheetData());
        }

        StateReferences(worksheet);
        return worksheet;
    }

    // Every row and cell given its r, which a producer may leave out (ECMA-376 §18.3.1.73, §18.3.1.4):
    // what an edit finds cells by.
    static void StateReferences(S.Worksheet worksheet)
    {
        var data = worksheet.GetFirstChild<S.SheetData>()!;
        var row = 0;
        foreach (var element in data.Elements<S.Row>())
        {
            row = element.RowIndex?.Value is { } stated ? (int) stated : row + 1;
            element.RowIndex ??= (uint) row;
            var column = 0;
            foreach (var cell in element.Elements<S.Cell>())
            {
                var statedColumn = CellReference.ColumnOf(cell.CellReference?.Value);
                column = statedColumn == 0 ? column + 1 : statedColumn;
                if (statedColumn == 0)
                {
                    cell.CellReference = CellReference.Format(column, row);
                }
            }
        }
    }

    /// <summary>
    /// The cell at a place, made if the sheet has none there — in its place among its row's cells, its
    /// row among the sheet's — looking as its row or its column says a new cell there looks.
    /// </summary>
    internal static S.Cell CellAt(S.Worksheet worksheet, int row, int column)
    {
        var data = worksheet.GetFirstChild<S.SheetData>()!;
        var line = RowAt(data, row);
        S.Cell? after = null;
        foreach (var cell in line.Elements<S.Cell>())
        {
            var at = CellReference.ColumnOf(cell.CellReference?.Value);
            if (at == column)
            {
                return cell;
            }

            if (at > column)
            {
                break;
            }

            after = cell;
        }

        var made = new S.Cell
        {
            CellReference = CellReference.Format(column, row)
        };
        if (line.StyleIndex?.Value is { } rowStyle &&
            line.CustomFormat?.Value == true)
        {
            made.StyleIndex = rowStyle;
        }
        else if (ColumnStyle(worksheet, column) is { } columnStyle)
        {
            made.StyleIndex = columnStyle;
        }

        if (after == null)
        {
            line.PrependChild(made);
        }
        else
        {
            line.InsertAfter(made, after);
        }

        return made;
    }

    internal static S.Row RowAt(S.SheetData data, int row)
    {
        S.Row? before = null;
        foreach (var line in data.Elements<S.Row>())
        {
            var at = (int) (line.RowIndex?.Value ?? 0);
            if (at == row)
            {
                return line;
            }

            if (at > row)
            {
                before = line;
                break;
            }
        }

        var made = new S.Row
        {
            RowIndex = (uint) row
        };
        if (before == null)
        {
            data.AppendChild(made);
        }
        else
        {
            data.InsertBefore(made, before);
        }

        return made;
    }

    static uint? ColumnStyle(S.Worksheet worksheet, int column) =>
        worksheet.GetFirstChild<S.Columns>()?
            .Elements<S.Column>()
            .FirstOrDefault(_ => _.Min?.Value <= column &&
                                 _.Max?.Value >= column)?
            .Style?.Value;

    internal static IEnumerable<S.Cell> CellsIn(S.Worksheet worksheet, SheetRange range)
    {
        var data = worksheet.GetFirstChild<S.SheetData>()!;
        foreach (var line in data.Elements<S.Row>())
        {
            var row = (int) (line.RowIndex?.Value ?? 0);
            if (row < range.FirstRow)
            {
                continue;
            }

            if (row > range.LastRow)
            {
                yield break;
            }

            foreach (var cell in line.Elements<S.Cell>())
            {
                var column = CellReference.ColumnOf(cell.CellReference?.Value);
                if (column >= range.FirstColumn &&
                    column <= range.LastColumn)
                {
                    yield return cell;
                }
            }
        }
    }

    // A cell's formula as text in its own cell: a shared formula's moved there from the cell it is
    // written in.
    static string? FormulaOf(S.Worksheet worksheet, S.Cell cell)
    {
        if (cell.CellFormula is not { } formula)
        {
            return null;
        }

        if (formula.FormulaType?.Value == S.CellFormulaValues.Shared &&
            string.IsNullOrEmpty(formula.Text) &&
            formula.SharedIndex?.Value is { } index &&
            SharedMaster(worksheet, index) is var (text, row, column))
        {
            return FormulaShifter.Shift(
                text,
                CellReference.RowOf(cell.CellReference?.Value) - row,
                CellReference.ColumnOf(cell.CellReference?.Value) - column);
        }

        if (string.IsNullOrEmpty(formula.Text))
        {
            return null;
        }

        return formula.Text;
    }

    static (string Text, int Row, int Column)? SharedMaster(S.Worksheet worksheet, uint index)
    {
        foreach (var cell in worksheet.Descendants<S.Cell>())
        {
            if (cell.CellFormula is {FormulaType.Value: var type, SharedIndex.Value: var si} formula &&
                type == S.CellFormulaValues.Shared &&
                si == index &&
                !string.IsNullOrEmpty(formula.Text))
            {
                return (formula.Text, CellReference.RowOf(cell.CellReference?.Value), CellReference.ColumnOf(cell.CellReference?.Value));
            }
        }

        return null;
    }

    /// <summary>
    /// A shared formula written out in each of its cells, before the cell it is written in is changed:
    /// the others take their formula from that one, and would lose it.
    /// </summary>
    static void ExpandShared(S.Worksheet worksheet, S.Cell cell)
    {
        if (cell.CellFormula is not {FormulaType.Value: var type} formula ||
            type != S.CellFormulaValues.Shared ||
            formula.SharedIndex?.Value is not { } index)
        {
            return;
        }

        var members = worksheet.Descendants<S.Cell>()
            .Where(_ => _.CellFormula is {FormulaType.Value: var each, SharedIndex.Value: var si} &&
                        each == S.CellFormulaValues.Shared &&
                        si == index)
            .ToList();
        foreach (var member in members)
        {
            var text = FormulaOf(worksheet, member);
            var own = member.CellFormula!;
            own.FormulaType = null;
            own.SharedIndex = null;
            own.Reference = null;
            own.Text = text ?? string.Empty;
        }
    }

    // The blocks the sheet's array formulas fill.
    static List<SheetRange> ArrayBlocks(S.Worksheet worksheet)
    {
        var blocks = new List<SheetRange>();
        foreach (var cell in worksheet.Descendants<S.Cell>())
        {
            if (cell.CellFormula is {FormulaType.Value: var type} formula &&
                type == S.CellFormulaValues.Array &&
                CellReference.ParseRange(formula.Reference?.Value) is { } block)
            {
                blocks.Add(block);
            }
        }

        return blocks;
    }

    static bool Inside(SheetRange range, int row, int column) =>
        range.ContainsRow(row) &&
        column >= range.FirstColumn &&
        column <= range.LastColumn;

    static bool Overlaps(SheetRange a, SheetRange b) =>
        a.FirstRow <= b.LastRow &&
        a.LastRow >= b.FirstRow &&
        a.FirstColumn <= b.LastColumn &&
        a.LastColumn >= b.FirstColumn;

    static bool Covers(SheetRange outer, SheetRange inner) =>
        outer.FirstRow <= inner.FirstRow &&
        outer.LastRow >= inner.LastRow &&
        outer.FirstColumn <= inner.FirstColumn &&
        outer.LastColumn >= inner.LastColumn;
}
