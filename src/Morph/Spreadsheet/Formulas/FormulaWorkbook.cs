using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// A workbook's cells, tables and defined names, worked out as Excel works them out: every formula from
/// the values it reads, the cells it reads first. What the file cached is kept beside each value
/// (<see cref="FormulaCell.Cached"/>), so a formula the engine cannot work out — a function it lacks,
/// a data table — keeps the value Excel last gave it, and says so (<see cref="NeedsExcel"/>).
///
/// The order is found before anything is worked out, by walking the references each formula makes,
/// without recursion: a running balance down a check register is a chain thousands of cells long, and
/// the browser's WebAssembly stack is small. A reference the walk cannot see ahead of time — what
/// OFFSET or INDIRECT makes — is worked out when it is read, to a bounded depth. A circular reference
/// keeps the value its cell had, as Excel's does with iteration off (and Excel then warns).
/// </summary>
sealed class FormulaWorkbook(DateTime now, CultureInfo? culture = null)
{
    const int onDemandDepth = 64;
    const int nameDepth = 16;

    Dictionary<string, int> sheetsByName = [with(StringComparer.OrdinalIgnoreCase)];
    Dictionary<string, FormulaTable> tables = [with(StringComparer.OrdinalIgnoreCase)];
    Dictionary<(int Scope, string Name), string> names = [];
    Dictionary<string, FormulaNode?> parsed = [with(StringComparer.Ordinal)];
    List<FormulaSheet> sheets = [];
    int depth;

    // Formula cells by sheet and column, sorted by row, for finding the formulas an area holds.
    Dictionary<int, Dictionary<int, List<FormulaCell>>> formulaIndex = [];
    List<(int Sheet, FormulaCell Cell)> arrayMasters = [];

    public IReadOnlyList<FormulaSheet> Sheets => sheets;

    public IEnumerable<FormulaTable> Tables => tables.Values;

    /// <summary>What TODAY and NOW say.</summary>
    public DateTime Now { get; } = now;

    /// <summary>The culture TEXT's format codes are read in, as the grid shows them.</summary>
    public CultureInfo Culture { get; } = culture ?? CultureInfo.InvariantCulture;

    /// <summary>
    /// Whether some formula was left at the value the file had, because the engine cannot work it out:
    /// the file then asks Excel to work everything out again when it opens (<c>fullCalcOnLoad</c>).
    /// </summary>
    public bool NeedsExcel { get; private set; }

    public FormulaSheet AddSheet(string name)
    {
        var sheet = new FormulaSheet(name, sheets.Count);
        sheets.Add(sheet);
        sheetsByName.TryAdd(name, sheet.Index);
        return sheet;
    }

    public void AddTable(FormulaTable table) =>
        tables.TryAdd(table.Name, table);

    /// <summary>A defined name: scope -1 for the workbook's, or the index of the sheet it is local to.</summary>
    public void AddName(int scope, string name, string formula) =>
        names.TryAdd((scope, name.ToUpperInvariant()), formula);

    public int SheetIndex(string name) =>
        sheetsByName.GetValueOrDefault(name, -1);

    public FormulaTable? Table(string name) =>
        tables.GetValueOrDefault(name);

    /// <summary>The table a cell stands in, for a structured reference that names none.</summary>
    public FormulaTable? TableAt(int sheet, int row, int column) =>
        tables.Values.FirstOrDefault(_ => _.Sheet == sheet &&
                                          _.Range.ContainsRow(row) &&
                                          column >= _.Range.FirstColumn &&
                                          column <= _.Range.LastColumn);

    /// <summary>A defined name's formula, the sheet's own before the workbook's.</summary>
    public string? Name(int sheet, string name)
    {
        var key = name.ToUpperInvariant();
        if (names.TryGetValue((sheet, key), out var local))
        {
            return local;
        }

        return names.GetValueOrDefault((-1, key));
    }

    /// <summary>A formula's tree, read once however many cells hold the same text; null when it cannot be read.</summary>
    public FormulaNode? Parse(string formula)
    {
        if (parsed.TryGetValue(formula, out var known))
        {
            return known;
        }

        FormulaNode? node;
        try
        {
            node = FormulaParser.Parse(formula);
        }
        catch (FormulaSyntaxException)
        {
            node = null;
        }

        parsed[formula] = node;
        return node;
    }

    /// <summary>What is in a cell now, its formula worked out first if it has not been yet.</summary>
    public FormulaValue Value(int sheet, int row, int column, FormulaEvaluator? reader = null)
    {
        if (sheet < 0 ||
            sheet >= sheets.Count ||
            sheets[sheet].Find(row, column) is not { } cell)
        {
            return FormulaValue.Blank;
        }

        if (cell is { HasFormula: true, Fixed: false, State: CellState.Pending } &&
            depth < onDemandDepth)
        {
            Compute(sheet, cell);
        }

        if (reader != null &&
            cell.Volatile)
        {
            reader.Volatile = true;
        }

        return cell.Value;
    }

    public FormulaCell? Cell(int sheet, int row, int column)
    {
        if (sheet < 0 ||
            sheet >= sheets.Count)
        {
            return null;
        }

        return sheets[sheet].Find(row, column);
    }

    public bool RowHidden(int sheet, int row) =>
        sheet >= 0 &&
        sheet < sheets.Count &&
        sheets[sheet].HiddenRows.Contains(row);

    /// <summary>Works out every formula in the workbook again.</summary>
    public void Recalculate()
    {
        NeedsExcel = false;
        var formulas = new List<(int Sheet, FormulaCell Cell)>();
        foreach (var sheet in sheets)
        {
            foreach (var cell in sheet.Cells)
            {
                if (!cell.HasFormula)
                {
                    continue;
                }

                if (cell.Fixed)
                {
                    cell.Value = cell.Cached;
                    NeedsExcel = true;
                    continue;
                }

                cell.State = CellState.Pending;
                cell.Unsupported = false;
                cell.Volatile = false;
                cell.Precedents = null;
                cell.Node = Parse(cell.Formula!);
                cell.Subtotal = cell.Node != null && CallsSubtotal(cell.Node);
                formulas.Add((sheet.Index, cell));
            }
        }

        Index(formulas);
        foreach (var (sheet, cell) in Ordered(formulas))
        {
            Compute(sheet, cell);
        }
    }

    void Index(List<(int Sheet, FormulaCell Cell)> formulas)
    {
        formulaIndex = [];
        arrayMasters = [];
        foreach (var (sheet, cell) in formulas)
        {
            if (!formulaIndex.TryGetValue(sheet, out var columns))
            {
                columns = [];
                formulaIndex[sheet] = columns;
            }

            if (!columns.TryGetValue(cell.Column, out var list))
            {
                list = [];
                columns[cell.Column] = list;
            }

            list.Add(cell);
            if (cell.ArrayRange != null)
            {
                arrayMasters.Add((sheet, cell));
            }
        }

        foreach (var columns in formulaIndex.Values)
        {
            foreach (var list in columns.Values)
            {
                list.Sort((left, right) => left.Row.CompareTo(right.Row));
            }
        }
    }

    // The formulas in the order they can be worked out in: each after the formulas it reads.
    List<(int Sheet, FormulaCell Cell)> Ordered(List<(int Sheet, FormulaCell Cell)> formulas)
    {
        var sheetOf = new Dictionary<FormulaCell, int>(formulas.Count);
        foreach (var (sheet, cell) in formulas)
        {
            sheetOf[cell] = sheet;
        }

        var order = new List<(int Sheet, FormulaCell Cell)>(formulas.Count);
        var visited = new HashSet<FormulaCell>();
        var stack = new Stack<(FormulaCell Cell, int Next)>();
        foreach (var (_, root) in formulas)
        {
            if (!visited.Add(root))
            {
                continue;
            }

            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                var (cell, next) = stack.Pop();
                var precedents = cell.Precedents ??= Precedents(sheetOf[cell], cell);
                if (next < precedents.Count)
                {
                    stack.Push((cell, next + 1));
                    var precedent = precedents[next];
                    if (visited.Add(precedent))
                    {
                        stack.Push((precedent, 0));
                    }

                    continue;
                }

                order.Add((sheetOf[cell], cell));
            }
        }

        return order;
    }

    // The formula cells a formula reads, as far as can be told without working it out.
    List<FormulaCell> Precedents(int sheet, FormulaCell cell)
    {
        var found = new List<FormulaCell>();
        if (cell.Node == null)
        {
            return found;
        }

        var evaluator = new FormulaEvaluator(this, sheet, cell.Row, cell.Column);
        foreach (var area in evaluator.Areas(cell.Node))
        {
            FormulasIn(area, found);
        }

        found.Remove(cell);
        return found;
    }

    void FormulasIn(FormulaArea area, List<FormulaCell> found)
    {
        if (formulaIndex.TryGetValue(area.Sheet, out var columns))
        {
            if (area.LastColumn - area.FirstColumn < columns.Count)
            {
                for (var column = area.FirstColumn; column <= area.LastColumn; column++)
                {
                    if (columns.TryGetValue(column, out var list))
                    {
                        Rows(list, area, found);
                    }
                }
            }
            else
            {
                foreach (var (column, list) in columns)
                {
                    if (column >= area.FirstColumn &&
                        column <= area.LastColumn)
                    {
                        Rows(list, area, found);
                    }
                }
            }
        }

        // A cell of an array formula's block is the formula's, which writes it.
        foreach (var (sheet, master) in arrayMasters)
        {
            if (sheet == area.Sheet &&
                master.ArrayRange is { } block &&
                block.FirstRow <= area.LastRow &&
                block.LastRow >= area.FirstRow &&
                block.FirstColumn <= area.LastColumn &&
                block.LastColumn >= area.FirstColumn)
            {
                found.Add(master);
            }
        }
    }

    static void Rows(List<FormulaCell> list, FormulaArea area, List<FormulaCell> found)
    {
        var low = 0;
        var high = list.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (list[middle].Row < area.FirstRow)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (var index = low; index < list.Count && list[index].Row <= area.LastRow; index++)
        {
            found.Add(list[index]);
        }
    }

    void Compute(int sheet, FormulaCell cell)
    {
        if (cell.State != CellState.Pending)
        {
            return;
        }

        cell.State = CellState.Computing;
        depth++;
        try
        {
            if (cell.Node == null)
            {
                // A formula that cannot be read keeps the value Excel gave it.
                cell.Unsupported = true;
                cell.Value = cell.Cached;
                NeedsExcel = true;
                return;
            }

            var evaluator = new FormulaEvaluator(this, sheet, cell.Row, cell.Column)
            {
                ArrayFormula = cell.ArrayRange != null
            };
            FormulaValue result;
            try
            {
                result = evaluator.Evaluate(cell.Node);
                if (cell.ArrayRange is { } block)
                {
                    result = evaluator.Arrayed(result);
                    Spread(sheet, cell, block, result);
                }

                result = evaluator.Final(result);
            }
            catch (Exception)
            {
                // A formula the engine trips over keeps the value Excel gave it, rather than costing the
                // reader the edit.
                evaluator.Unsupported = true;
                result = cell.Cached;
            }

            cell.Volatile = evaluator.Volatile;
            if (evaluator.Unsupported)
            {
                cell.Unsupported = true;
                cell.Value = cell.Cached;
                NeedsExcel = true;
                return;
            }

            cell.Value = result;
        }
        finally
        {
            depth--;
            cell.State = CellState.Computed;
        }
    }

    // An array formula's result laid over its block: each cell its value, #N/A where the result is smaller.
    void Spread(int sheet, FormulaCell master, SheetRange block, FormulaValue result)
    {
        var values = result.IsArray
            ? result.Array
            : new[,]
            {
                {
                    result
                }
            };
        var rows = values.GetLength(0);
        var columns = values.GetLength(1);
        for (var row = block.FirstRow; row <= block.LastRow; row++)
        {
            for (var column = block.FirstColumn; column <= block.LastColumn; column++)
            {
                if (row == master.Row &&
                    column == master.Column)
                {
                    continue;
                }

                var r = row - block.FirstRow;
                var c = column - block.FirstColumn;
                var value = FormulaValue.FromError(FormulaError.NotAvailable);
                if (r < rows &&
                    c < columns)
                {
                    value = values[r, c];
                }
                else if (rows == 1 &&
                         c < columns)
                {
                    value = values[0, c];
                }
                else if (columns == 1 &&
                         r < rows)
                {
                    value = values[r, 0];
                }

                var member = sheets[sheet].Find(row, column) ?? sheets[sheet].Add(row, column);
                member.Value = Cellular(value);
            }
        }
    }

    // What a cell holds of a value: a blank result is a zero, as =A1 of an empty A1 shows.
    static FormulaValue Cellular(FormulaValue value)
    {
        if (value.IsBlank)
        {
            return FormulaValue.FromNumber(0);
        }

        return value;
    }

    static bool CallsSubtotal(FormulaNode node) =>
        node switch
        {
            CallNode { Name: "SUBTOTAL" or "AGGREGATE" } => true,
            CallNode call => call.Arguments.Any(CallsSubtotal),
            UnaryNode unary => CallsSubtotal(unary.Operand),
            PercentNode percent => CallsSubtotal(percent.Operand),
            BinaryNode binary => CallsSubtotal(binary.Left) || CallsSubtotal(binary.Right),
            _ => false
        };

    // Guards a defined name that names itself, directly or through another.
    internal bool EnterName()
    {
        if (depth >= nameDepth + onDemandDepth)
        {
            return false;
        }

        depth++;
        return true;
    }

    internal void LeaveName() =>
        depth--;

    /// <summary>
    /// Reads a workbook's cells, tables and defined names. Values come from the file as Excel cached
    /// them; <see cref="Recalculate"/> works the formulas out again.
    /// </summary>
    public static FormulaWorkbook Read(WorkbookPart workbookPart, DateTime now, CultureInfo? culture = null)
    {
        var book = new FormulaWorkbook(now, culture);
        var strings = new SharedStrings(workbookPart);
        var sheetElements = workbookPart.Workbook?.Sheets?.Elements<S.Sheet>().ToList() ?? [];
        foreach (var element in sheetElements)
        {
            var sheet = book.AddSheet(element.Name?.Value ?? $"Sheet{book.sheets.Count + 1}");
            if (element.Id?.Value is not { } id ||
                workbookPart.GetPartById(id) is not WorksheetPart
                {
                    Worksheet: { } worksheet
                } part)
            {
                continue;
            }

            ReadCells(sheet, worksheet, strings);
            foreach (var tablePart in part.TableDefinitionParts)
            {
                if (tablePart.Table is { } table &&
                    CellReference.ParseRange(table.Reference?.Value) is { } range)
                {
                    var columns = table.TableColumns?.Elements<S.TableColumn>()
                        .Select(_ => Decode(_.Name?.Value ?? string.Empty))
                        .ToList() ?? [];
                    var name = table.DisplayName?.Value ?? table.Name?.Value;
                    if (name != null)
                    {
                        book.AddTable(new(
                            name,
                            sheet.Index,
                            range,
                            (int)(table.HeaderRowCount?.Value ?? 1),
                            (int)(table.TotalsRowCount?.Value ?? 0),
                            columns));
                    }
                }
            }
        }

        foreach (var name in workbookPart.Workbook?.DefinedNames?.Elements<S.DefinedName>() ?? [])
        {
            if (name.Name?.Value is not { } key ||
                string.IsNullOrWhiteSpace(name.Text))
            {
                continue;
            }

            var scope = -1;
            if (name.LocalSheetId?.Value is { } local)
            {
                scope = (int)local;
            }

            book.AddName(scope, key, name.Text);
        }

        return book;
    }

    static void ReadCells(FormulaSheet sheet, S.Worksheet worksheet, SharedStrings strings)
    {
        if (worksheet.GetFirstChild<S.SheetData>() is not { } data)
        {
            return;
        }

        var shared = new Dictionary<uint, (string Formula, int Row, int Column)>();
        var members = new List<(FormulaCell Cell, uint Index)>();
        var rowNumber = 0;
        foreach (var row in data.Elements<S.Row>())
        {
            rowNumber = row.RowIndex?.Value is { } stated ? (int)stated : rowNumber + 1;
            if (row.Hidden?.Value == true)
            {
                sheet.HiddenRows.Add(rowNumber);
            }

            var columnNumber = 0;
            foreach (var element in row.Elements<S.Cell>())
            {
                var statedColumn = CellReference.ColumnOf(element.CellReference?.Value);
                columnNumber = statedColumn == 0 ? columnNumber + 1 : statedColumn;
                var value = CachedValue(element, strings);
                var formula = element.CellFormula;
                if (value.IsBlank &&
                    formula == null)
                {
                    continue;
                }

                var cell = sheet.Add(rowNumber, columnNumber);
                cell.Element = element;
                cell.Value = value;
                cell.Cached = value;
                if (formula == null)
                {
                    continue;
                }

                var type = formula.FormulaType?.Value;
                var text = formula.Text;
                if (type == S.CellFormulaValues.DataTable)
                {
                    cell.Formula = text;
                    cell.Fixed = true;
                    continue;
                }

                if (type == S.CellFormulaValues.Shared &&
                    formula.SharedIndex?.Value is { } index)
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        members.Add((cell, index));
                    }
                    else
                    {
                        shared[index] = (text, rowNumber, columnNumber);
                        cell.Formula = text;
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                cell.Formula = text;
                if (type == S.CellFormulaValues.Array &&
                    CellReference.ParseRange(formula.Reference?.Value) is { } block &&
                    (block.RowCount > 1 || block.ColumnCount > 1))
                {
                    cell.ArrayRange = block;
                }
            }
        }

        foreach (var (cell, index) in members)
        {
            if (shared.TryGetValue(index, out var master))
            {
                try
                {
                    cell.Formula = FormulaShifter.Shift(master.Formula, cell.Row - master.Row, cell.Column - master.Column);
                }
                catch (FormulaSyntaxException)
                {
                    cell.Formula = master.Formula;
                    cell.Fixed = true;
                }
            }
        }
    }

    /// <summary>The value a cell element holds, as Excel cached it.</summary>
    public static FormulaValue CachedValue(S.Cell element, SharedStrings strings)
    {
        var raw = element.CellValue?.Text;
        var type = element.DataType?.Value;
        if (type == S.CellValues.InlineString)
        {
            if (element.InlineString is { } inline)
            {
                return FormulaValue.FromText(SharedStrings.Flatten(inline));
            }

            return FormulaValue.Blank;
        }

        if (raw == null)
        {
            return FormulaValue.Blank;
        }

        if (type == S.CellValues.SharedString)
        {
            if (int.TryParse(raw, CultureInfo.InvariantCulture, out var index))
            {
                return FormulaValue.FromText(strings.Get(index));
            }

            return FormulaValue.Blank;
        }

        if (type == S.CellValues.String)
        {
            return FormulaValue.FromText(raw);
        }

        if (type == S.CellValues.Boolean)
        {
            return FormulaValue.FromBoolean(raw.Trim() is "1" or "true");
        }

        if (type == S.CellValues.Error)
        {
            if (FormulaErrors.TryParse(raw.Trim(), out var error))
            {
                return FormulaValue.FromError(error);
            }

            return FormulaValue.FromError(FormulaError.Value);
        }

        if (type == S.CellValues.Date)
        {
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
            {
                return FormulaValue.FromNumber(FormulaCoercion.ToSerial(date));
            }

            return FormulaValue.FromText(raw);
        }

        if (raw.Length == 0)
        {
            return FormulaValue.Blank;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return FormulaValue.FromNumber(number);
        }

        return FormulaValue.FromText(raw);
    }

    static readonly Regex escape = new("_x([0-9A-Fa-f]{4})_", RegexOptions.CultureInvariant);

    // A table column's name keeps characters XML cannot hold as _xHHHH_.
    static string Decode(string name)
    {
        if (!name.Contains("_x", StringComparison.Ordinal))
        {
            return name;
        }

        return escape.Replace(name, _ => ((char)int.Parse(_.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
    }
}
