namespace Morph;

/// <summary>
/// An Excel workbook edited the way Excel edits one: a grid of cells with column letters and row numbers,
/// a name box and a formula bar over it, the sheets' tabs and a status bar under it. Typing into a cell, a
/// formula with its <c>=</c> included, sets it when the reader moves on; every formula in the workbook is
/// then worked out again by Morph's own formula engine (<see cref="FormulaWorkbook"/>), so what depends on
/// the cell changes with it, and the file saved holds the new values. The toolbar formats the selected
/// cells — font, fill, alignment, wrapping, borders, number format — and clears, copies and pastes them.
///
/// It is <see cref="MorphEditor"/>'s view of a workbook, handed the file in <see cref="Bytes"/> and handing
/// back each edited file through <see cref="OnEdited"/>, as the editor's own edits are file in, file out
/// (<see cref="WorkbookEditor"/>): the grid is read again from what the edit made, and never patched, so
/// what it shows is what Save saves.
///
/// The cells drawn are only those in view, read off the scroll position the script reports
/// (morph-sheet.js), so a long sheet costs no more to show than a short one.
/// </summary>
public partial class SheetEditor : IAsyncDisposable
{
    const double headerHeight = 20;
    const int overscanRows = 6;
    const int overscanColumns = 2;
    const int moreRows = 60;
    const int moreColumns = 12;
    const int undoDepth = 100;
    const double pointsPerPixel = 0.75;

    internal const string fillMenu = "fill";
    internal const string colorMenu = "color";
    internal const string bordersMenu = "borders";
    internal const string clearMenu = "clear";
    internal const string formatMenu = "format";

    internal static readonly (string Name, string Code)[] NumberFormats =
    [
        ("General", "General"),
        ("Number", "0.00"),
        ("Currency", "\"$\"#,##0.00"),
        ("Accounting", "_(\"$\"* #,##0.00_);_(\"$\"* (#,##0.00);_(\"$\"* \"-\"??_);_(@_)"),
        ("Short date", "m/d/yyyy"),
        ("Long date", "[$-F800]dddd, mmmm dd, yyyy"),
        ("Time", "[$-F400]h:mm:ss AM/PM"),
        ("Percentage", "0.00%"),
        ("Fraction", "# ?/?"),
        ("Scientific", "0.00E+00"),
        ("Text", "@")
    ];

    internal static readonly double[] FontSizes = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36, 48, 72];

    internal static readonly string[] Fonts = ["Aptos", "Aptos Narrow", "Arial", "Calibri", "Cambria", "Courier New", "Georgia", "Segoe UI", "Tahoma", "Times New Roman", "Verdana"];

    internal static IReadOnlyList<(string Hex, string Name)> Colors => MorphViewer.TextColors;

    [Inject]
    MorphInterop Interop { get; set; } = null!;

    /// <summary>The workbook. A different array is a different file, opened afresh, or this one edited elsewhere.</summary>
    [Parameter]
    public byte[]? Bytes { get; set; }

    /// <summary>Changes when another file is opened, which starts the undo history again.</summary>
    [Parameter]
    public int DocumentKey { get; set; }

    /// <summary>Where the bundled fonts are, whose digit sizes the columns.</summary>
    [Parameter]
    public string? FontDirectory { get; set; }

    /// <summary>Shows the workbook without letting it be edited.</summary>
    [Parameter]
    public bool ReadOnly { get; set; }

    /// <summary>Called with the file each edit makes.</summary>
    [Parameter]
    public EventCallback<byte[]> OnEdited { get; set; }

    /// <summary>Called for Ctrl+S, once what is being typed has been handed in.</summary>
    [Parameter]
    public EventCallback OnSave { get; set; }

    byte[]? bytes;
    int key = int.MinValue;
    internal WorkbookGrid? grid;
    internal int sheetIndex;
    internal string? note;

    // The selection: the cell it started at, and the active cell, which the formula bar shows.
    internal int endRow = 1;
    internal int endColumn = 1;
    internal int activeRow = 1;
    internal int activeColumn = 1;

    // A reference being pointed at in a formula, outlined as Excel outlines it.
    (int Row, int Column, int LastRow, int LastColumn)? pointed;

    // null, "enter" or "edit": Excel's modes, shown in the status bar.
    internal string? editMode;
    internal bool showFormulas;
    internal string? menu;
    internal string findText = "";
    int barVersion;

    double scrollTop;
    double scrollLeft;
    double viewWidth = 1200;
    double viewHeight = 600;
    internal double zoom = 1;
    internal double[] lefts = [0];
    internal double[] tops = [0];
    bool geometryChanged = true;
    bool focusPending;
    (int Row, int Column, int LastRow, int LastColumn)? reveal;

    readonly List<Kept> undo = [];
    readonly List<Kept> redo = [];
    (int Sheet, SheetRange Range, bool Cut, string Text)? clipboard;
    bool busy;

    ElementReference root;
    IJSObjectReference? controller;
    DotNetObjectReference<SheetEditor>? self;
    bool disposed;

    sealed record Kept(byte[] Bytes, int Sheet, int Row, int Column, int EndRow, int EndColumn);

    internal SheetGrid? Sheet => grid?.Sheets.ElementAtOrDefault(sheetIndex);

    internal bool CanUndo => undo.Count > 0 && !ReadOnly;

    internal bool CanRedo => redo.Count > 0 && !ReadOnly;

    internal bool Editable => !ReadOnly && Sheet is {IsChart: false};

    internal int ExtentRows => tops.Length - 1;

    internal int ExtentColumns => lefts.Length - 1;

    internal double RowHeaderWidth => Math.Max(32, ExtentRows.ToString(CultureInfo.InvariantCulture).Length * 8 + 14);

    protected override void OnParametersSet()
    {
        if (Bytes is not { } given)
        {
            return;
        }

        if (key != DocumentKey)
        {
            key = DocumentKey;
            undo.Clear();
            redo.Clear();
            clipboard = null;
            Load(given, true);
            return;
        }

        if (!ReferenceEquals(given, bytes))
        {
            Load(given, false);
        }
    }

    void Load(byte[] given, bool fresh)
    {
        bytes = given;
        try
        {
            grid = WorkbookGrid.Read(given, FontDirectory);
            note = null;
        }
        catch (Exception exception)
        {
            grid = null;
            note = $"The workbook could not be read: {exception.Message}";
            return;
        }

        if (fresh)
        {
            sheetIndex = Math.Clamp(grid.ActiveSheet, 0, Math.Max(0, grid.Sheets.Count - 1));
            if (Sheet is {Hidden: true})
            {
                sheetIndex = Math.Max(0, grid.Sheets.FindIndex(_ => !_.Hidden));
            }

            var (row, column) = Sheet?.Active ?? (1, 1);
            Select(row, column, row, column);
            scrollTop = 0;
            scrollLeft = 0;

            // A workbook opened takes the keyboard, as Excel's grid does: typing goes into its first cell.
            focusPending = true;
        }

        sheetIndex = Math.Clamp(sheetIndex, 0, Math.Max(0, grid.Sheets.Count - 1));
        Measure();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            self = DotNetObjectReference.Create(this);
            try
            {
                controller = await Interop.AttachSheetAsync(root, self);
                await controller.InvokeVoidAsync("report");
            }
            catch (JSDisconnectedException)
            {
                return;
            }
            catch (JSException)
            {
                // A test host without the script: the grid still draws.
                return;
            }
        }

        if (controller == null)
        {
            return;
        }

        try
        {
            if (geometryChanged)
            {
                geometryChanged = false;
                await controller.InvokeVoidAsync("setGeometry", lefts, tops, RowHeaderWidth, zoom);
            }

            if (reveal is var (row, column, lastRow, lastColumn))
            {
                reveal = null;
                await controller.InvokeVoidAsync("reveal", row, column, lastRow, lastColumn);
            }

            if (focusPending)
            {
                focusPending = false;
                await controller.InvokeVoidAsync("focus");
            }
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
    }

    // Geometry

    /// <summary>
    /// Each column's left and each row's top, as far as the grid reaches: past what the sheet holds and
    /// what is in view, by a margin, so there is always more of the sheet to scroll into, as in Excel.
    /// </summary>
    void Measure()
    {
        if (Sheet is not { } sheet)
        {
            lefts = [0];
            tops = [0];
            geometryChanged = true;
            return;
        }

        // The selection's reach, but for the far end of a whole column or row.
        var reachRow = activeRow;
        if (endRow != CellReference.MaxRow)
        {
            reachRow = Math.Max(reachRow, endRow);
        }

        var reachColumn = activeColumn;
        if (endColumn != CellReference.MaxColumn)
        {
            reachColumn = Math.Max(reachColumn, endColumn);
        }

        var rows = Math.Max(Math.Max(sheet.MaxRow, reachRow), 1) + moreRows;
        var columns = Math.Max(Math.Max(sheet.MaxColumn, reachColumn), 26) + moreColumns;
        tops = Edges(rows, CellReference.MaxRow, sheet.RowHeight, scrollTop + viewHeight);
        lefts = Edges(columns, CellReference.MaxColumn, sheet.ColumnWidth, scrollLeft + viewWidth);
        geometryChanged = true;
    }

    // The edges of the first rows (or columns): at least `count` of them, and past what is in view by
    // half as much again, never beyond the sheet's last. Only that far — a grid laid out to row 1,048,576
    // is a canvas tens of millions of pixels tall, which a browser does not paint at all.
    internal static double[] Edges(int count, int most, Func<int, double> size, double reach)
    {
        var edges = new List<double>(count + 1) { 0 };
        var index = 1;
        while (index <= most &&
               (index <= count || edges[^1] < reach * 1.5 + 200))
        {
            edges.Add(edges[^1] + size(index));
            index++;
        }

        return [.. edges];
    }

    internal static int Find(double[] edges, double at)
    {
        var low = 0;
        var high = edges.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (edges[middle] <= at)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Math.Clamp(low + 1, 1, Math.Max(1, edges.Length - 1));
    }

    /// <summary>The rows and columns drawn: those in view, and a few either side.</summary>
    internal (int FirstRow, int LastRow, int FirstColumn, int LastColumn) Window
    {
        get
        {
            var firstRow = Math.Max(1, Find(tops, scrollTop) - overscanRows);
            var lastRow = Math.Min(ExtentRows, Find(tops, scrollTop + viewHeight) + overscanRows);
            var firstColumn = Math.Max(1, Find(lefts, scrollLeft) - overscanColumns);
            var lastColumn = Math.Min(ExtentColumns, Find(lefts, scrollLeft + viewWidth) + overscanColumns);
            return (firstRow, lastRow, firstColumn, lastColumn);
        }
    }

    internal double Left(int column) =>
        lefts[Math.Clamp(column - 1, 0, lefts.Length - 1)];

    internal double Top(int row) =>
        tops[Math.Clamp(row - 1, 0, tops.Length - 1)];

    internal double Width(int first, int last) =>
        lefts[Math.Clamp(last, 0, lefts.Length - 1)] - Left(first);

    internal double Height(int first, int last) =>
        tops[Math.Clamp(last, 0, tops.Length - 1)] - Top(first);

    // The selection

    internal SheetRange Selection => Covering(new(
        Math.Min(endRow, activeRow),
        Math.Min(endColumn, activeColumn),
        Math.Max(endRow, activeRow),
        Math.Max(endColumn, activeColumn)));

    // A selection takes in the whole of every merged area it touches, as Excel's does.
    SheetRange Covering(SheetRange range)
    {
        if (Sheet is not { Merges.Count: > 0 } sheet)
        {
            return range;
        }

        var grown = true;
        while (grown)
        {
            grown = false;
            foreach (var merge in sheet.Merges)
            {
                if (merge.FirstRow <= range.LastRow &&
                    merge.LastRow >= range.FirstRow &&
                    merge.FirstColumn <= range.LastColumn &&
                    merge.LastColumn >= range.FirstColumn)
                {
                    var wider = new SheetRange(
                        Math.Min(range.FirstRow, merge.FirstRow),
                        Math.Min(range.FirstColumn, merge.FirstColumn),
                        Math.Max(range.LastRow, merge.LastRow),
                        Math.Max(range.LastColumn, merge.LastColumn));
                    if (wider != range)
                    {
                        range = wider;
                        grown = true;
                    }
                }
            }
        }

        return range;
    }

    // The active cell's area: itself, or the merged area it heads.
    internal SheetRange ActiveArea =>
        Sheet?.MergeAt(activeRow, activeColumn) ?? new(activeRow, activeColumn, activeRow, activeColumn);

    // A selection from the active cell, where it starts and what is typed goes into, to its far corner,
    // which Shift and a drag move, as Excel's does. The far corner is what is scrolled into view.
    void Select(int fromRow, int fromColumn, int toRow, int toColumn)
    {
        tabColumn = null;
        activeRow = Math.Clamp(fromRow, 1, CellReference.MaxRow);
        activeColumn = Math.Clamp(fromColumn, 1, CellReference.MaxColumn);
        endRow = Math.Clamp(toRow, 1, CellReference.MaxRow);
        endColumn = Math.Clamp(toColumn, 1, CellReference.MaxColumn);

        // The active cell of a merged area is its first.
        if (Sheet?.MergeAt(activeRow, activeColumn) is { } merge &&
            fromRow == toRow &&
            fromColumn == toColumn)
        {
            endRow = activeRow = merge.FirstRow;
            endColumn = activeColumn = merge.FirstColumn;
        }

        // Only a corner the reader moves to makes the grid reach further: a whole column selected ends at
        // the sheet's last row, which it would not do to lay the sheet out that far.
        var wholeColumns = activeRow == 1 && endRow == CellReference.MaxRow;
        var wholeRows = activeColumn == 1 && endColumn == CellReference.MaxColumn;
        var reachRow = wholeColumns ? activeRow : endRow;
        var reachColumn = wholeRows ? activeColumn : endColumn;
        if (Math.Max(activeRow, reachRow) > ExtentRows - 10 ||
            Math.Max(activeColumn, reachColumn) > ExtentColumns - 3)
        {
            Measure();
        }

        var row = Math.Min(reachRow, ExtentRows);
        var column = Math.Min(reachColumn, ExtentColumns);
        reveal = (row, column, row, column);
    }

    internal string ActiveName
    {
        get
        {
            var selection = Selection;
            if (selection is
                {
                    RowCount: 1,
                    ColumnCount: 1
                } ||
                ActiveArea == selection)
            {
                return CellReference.Format(activeColumn, activeRow);
            }

            if (selection is
                {
                    FirstRow: 1,
                    LastRow: CellReference.MaxRow
                })
            {
                return $"{Letters(selection.FirstColumn)}:{Letters(selection.LastColumn)}";
            }

            if (selection is
                {
                    FirstColumn: 1,
                    LastColumn: CellReference.MaxColumn
                })
            {
                return $"{selection.FirstRow}:{selection.LastRow}";
            }

            return $"{CellReference.Format(selection.FirstColumn, selection.FirstRow)}:{CellReference.Format(selection.LastColumn, selection.LastRow)}";
        }
    }

    internal static string Letters(int column)
    {
        var reference = CellReference.Format(column, 1);
        return reference[..^1];
    }

    internal GridCell? ActiveCell => Sheet?.Cell(activeRow, activeColumn);

    internal GridLook ActiveLook => Sheet?.Look(activeRow, activeColumn) ?? GridLook.Plain;

    internal string BarText => ActiveCell?.Input ?? string.Empty;

    internal int BarVersion => barVersion;

    internal string ModeName =>
        editMode switch
        {
            "enter" => "Enter",
            "edit" => "Edit",
            _ when pointed != null => "Point",
            _ => "Ready"
        };

    internal (int Row, int Column, int LastRow, int LastColumn)? Pointed => pointed;

    /// <summary>What the status bar says of the selection: its average, count and sum, as Excel's does.</summary>
    internal (double? Average, int Count, double? Sum)? Summary
    {
        get
        {
            if (Sheet is not { } sheet)
            {
                return null;
            }

            var selection = Selection;
            if (selection is
                {
                    RowCount: 1,
                    ColumnCount: 1
                })
            {
                return null;
            }

            var count = 0;
            var numbers = 0;
            var sum = 0d;
            foreach (var cell in sheet.Cells)
            {
                if (!selection.ContainsRow(cell.Row) ||
                    cell.Column < selection.FirstColumn ||
                    cell.Column > selection.LastColumn ||
                    (cell.Display.Length == 0 && cell.Input.Length == 0))
                {
                    continue;
                }

                count++;
                if (cell.Number is { } value)
                {
                    numbers++;
                    sum += value;
                }
            }

            if (count < 2)
            {
                return null;
            }

            if (numbers == 0)
            {
                return (null, count, null);
            }

            return (sum / numbers, count, sum);
        }
    }

    // Called from the script

    Task queue = Task.CompletedTask;

    // What the script hands over is done in the order it was handed over, each once the one before has
    // finished: a cell handed in is an edit that takes a moment, and the arrow pressed straight after it
    // moves on from where that edit left the selection, not from where it was before.
    Task Serially(Func<Task> work)
    {
        var next = After(queue, work);
        queue = next;
        return next;
    }

    Task Serially(Action work) =>
        Serially(() =>
        {
            work();
            return Task.CompletedTask;
        });

    async Task After(Task previous, Func<Task> work)
    {
        try
        {
            await previous;
        }
        catch (Exception)
        {
            // The one before failed; this one goes ahead regardless.
        }

        await InvokeAsync(work);
    }

    /// <summary>Called from JavaScript as the grid scrolls or resizes: what is in view, in pixels at 100%.</summary>
    [JSInvokable]
    public Task OnSheetScroll(double top, double left, double width, double height) =>
        InvokeAsync(() =>
        {
            var before = Window;
            var extentRows = ExtentRows;
            var extentColumns = ExtentColumns;
            scrollTop = top;
            scrollLeft = left;
            viewWidth = Math.Max(1, width);
            viewHeight = Math.Max(1, height);

            // Near the end of what the grid reaches, it reaches further.
            if (top + height > Top(extentRows) - 200 ||
                left + width > Left(extentColumns) - 200)
            {
                Measure();
            }

            if (Window != before ||
                ExtentRows != extentRows ||
                ExtentColumns != extentColumns)
            {
                StateHasChanged();
            }
        });

    /// <summary>Called from JavaScript for a key pressed on the grid while not editing, by its name.</summary>
    [JSInvokable]
    public Task OnSheetKey(string name, bool shift, bool ctrl) =>
        Serially(async () =>
        {
            await KeyAsync(name, shift, ctrl);
            StateHasChanged();
        });

    // The keys that move or change the selection, which wait while a cell is open: one that arrives
    // while .NET has started a cell's editing that the script had not yet heard of is the script's to
    // have, and the script handles it once it has.
    static readonly HashSet<string> moving = ["Up", "Down", "Left", "Right", "Home", "End", "PageUp", "PageDown", "Enter", "Tab", "Delete", "Backspace", "F2"];

    Task KeyAsync(string name, bool shift, bool ctrl)
    {
        if (editMode != null &&
            moving.Contains(name))
        {
            return Task.CompletedTask;
        }

        switch (name)
        {
            case "Up":
                Move(-1, 0, shift, ctrl);
                break;
            case "Down":
                Move(1, 0, shift, ctrl);
                break;
            case "Left":
                Move(0, -1, shift, ctrl);
                break;
            case "Right":
                Move(0, 1, shift, ctrl);
                break;
            case "Home":
                if (ctrl)
                {
                    Extend(shift, 1, 1);
                }
                else
                {
                    Extend(shift, shift ? endRow : activeRow, 1);
                }

                break;
            case "End":
                if (ctrl &&
                    Sheet is { } last)
                {
                    Extend(shift, Math.Max(1, last.MaxRow), Math.Max(1, last.MaxColumn));
                }

                break;
            case "PageUp":
            case "PageDown":
                var page = Math.Max(1, Find(tops, scrollTop + viewHeight) - Find(tops, scrollTop));
                if (ctrl)
                {
                    return StepSheetAsync(name == "PageUp" ? -1 : 1);
                }

                var from = shift ? endRow : activeRow;
                Extend(shift, Math.Max(1, from + (name == "PageUp" ? -page : page)), shift ? endColumn : activeColumn);
                break;
            case "Enter":
                Return(shift ? -1 : 1);
                break;
            case "Tab":
                Across(shift ? -1 : 1);
                break;
            case "Delete":
                return ClearAsync(contents: true, formats: false);
            case "Backspace":
                if (Editable)
                {
                    return BeginEditAsync("enter", string.Empty);
                }

                break;
            case "F2":
                if (Editable)
                {
                    return BeginEditAsync("edit", BarText);
                }

                break;
            case "Escape":
                clipboard = null;
                break;
            case "Undo":
                return UndoAsync();
            case "Redo":
                return RedoAsync();
            case "Bold":
                return ToggleAsync(_ => _.Bold, _ => new() { Bold = _ });
            case "Italic":
                return ToggleAsync(_ => _.Italic, _ => new() { Italic = _ });
            case "Underline":
                return ToggleAsync(_ => _.Underline, _ => new() { Underline = _ });
            case "SelectAll":
                Select(1, 1, CellReference.MaxRow, CellReference.MaxColumn);
                reveal = null;
                break;
            case "Save":
                return OnSave.InvokeAsync();
            case "Find":
                menu = null;
                return FocusFindAsync();
            case "Today":
                if (Editable)
                {
                    return BeginEditAsync("enter", DateTime.Today.ToString("M/d/yyyy", CultureInfo.InvariantCulture));
                }

                break;
            case "FillDown":
                return FillAsync(down: true);
            case "FillRight":
                return FillAsync(down: false);
            case "ShowFormulas":
                showFormulas = !showFormulas;
                break;
            case "AutoSum":
                return AutoSumAsync();
        }

        return Task.CompletedTask;
    }

    void Extend(bool shift, int row, int column)
    {
        if (shift)
        {
            Select(activeRow, activeColumn, row, column);
            return;
        }

        Select(row, column, row, column);
    }

    // An arrow: one cell on, or with Ctrl to the edge of the block of cells holding something, as Excel's.
    void Move(int rows, int columns, bool shift, bool ctrl)
    {
        // Shift moves the selection's far corner; otherwise the active cell moves, and the selection with it.
        var row = activeRow;
        var column = activeColumn;
        if (shift)
        {
            row = endRow;
            column = endColumn;
        }

        if (!shift &&
            Sheet?.MergeAt(row, column) is { } merge)
        {
            // Off a merged area, from its far side.
            if (rows > 0)
            {
                row = merge.LastRow;
            }

            if (columns > 0)
            {
                column = merge.LastColumn;
            }
        }

        if (ctrl)
        {
            (row, column) = Edge(row, column, rows, columns);
        }
        else
        {
            row += rows;
            column += columns;
        }

        row = Math.Clamp(row, 1, CellReference.MaxRow);
        column = Math.Clamp(column, 1, CellReference.MaxColumn);
        Extend(shift, row, column);
    }

    bool Filled(int row, int column) =>
        Sheet?.Cell(row, column) is { } cell &&
        (cell.Display.Length > 0 || cell.Input.Length > 0);

    (int Row, int Column) Edge(int row, int column, int rows, int columns)
    {
        var lastRow = Math.Max(Sheet?.MaxRow ?? 1, row);
        var lastColumn = Math.Max(Sheet?.MaxColumn ?? 1, column);
        bool Inside(int r, int c) => r >= 1 && c >= 1 && r <= lastRow && c <= lastColumn;

        var nextRow = row + rows;
        var nextColumn = column + columns;
        if (!Inside(nextRow, nextColumn))
        {
            return (Math.Clamp(nextRow, 1, lastRow), Math.Clamp(nextColumn, 1, lastColumn));
        }

        // Inside a block, to its end; otherwise to the next cell holding something, or the edge.
        if (Filled(row, column) &&
            Filled(nextRow, nextColumn))
        {
            while (Inside(nextRow + rows, nextColumn + columns) &&
                   Filled(nextRow + rows, nextColumn + columns))
            {
                nextRow += rows;
                nextColumn += columns;
            }

            return (nextRow, nextColumn);
        }

        while (Inside(nextRow, nextColumn) &&
               !Filled(nextRow, nextColumn))
        {
            nextRow += rows;
            nextColumn += columns;
        }

        return (Math.Clamp(nextRow, 1, lastRow), Math.Clamp(nextColumn, 1, lastColumn));
    }

    // The column a run of Tabs started from, which Enter goes back to on the row below, as Excel's does:
    // typing a row of a table, cell by cell, goes on at the start of the next row.
    int? tabColumn;

    void Across(int columns)
    {
        var single = Selection is {RowCount: 1, ColumnCount: 1} || Selection == ActiveArea;
        var from = tabColumn ?? activeColumn;
        Step(0, columns);
        if (single)
        {
            tabColumn = from;
        }
    }

    void Return(int rows)
    {
        if (tabColumn is { } column &&
            rows > 0 &&
            (Selection is {RowCount: 1, ColumnCount: 1} || Selection == ActiveArea))
        {
            var row = ActiveArea.LastRow + 1;
            Select(row, column, row, column);
            return;
        }

        Step(rows, 0);
    }

    // Enter and Tab: within a selection of more than one cell they go round it, as Excel's do.
    void Step(int rows, int columns)
    {
        var selection = Selection;
        if (selection is
            {
                RowCount: 1,
                ColumnCount: 1
            } ||
            selection == ActiveArea)
        {
            Move(rows, columns, false, false);
            return;
        }

        var row = activeRow;
        var column = activeColumn;
        if (rows != 0)
        {
            row += rows;
            if (row > selection.LastRow)
            {
                row = selection.FirstRow;
                column = column + 1 > selection.LastColumn ? selection.FirstColumn : column + 1;
            }
            else if (row < selection.FirstRow)
            {
                row = selection.LastRow;
                column = column - 1 < selection.FirstColumn ? selection.LastColumn : column - 1;
            }
        }
        else
        {
            column += columns;
            if (column > selection.LastColumn)
            {
                column = selection.FirstColumn;
                row = row + 1 > selection.LastRow ? selection.FirstRow : row + 1;
            }
            else if (column < selection.FirstColumn)
            {
                column = selection.LastColumn;
                row = row - 1 < selection.FirstRow ? selection.LastRow : row - 1;
            }
        }

        activeRow = row;
        activeColumn = column;
        reveal = (row, column, row, column);
    }

    /// <summary>Called from JavaScript for a press, a drag or a double click on a cell.</summary>
    [JSInvokable]
    public Task OnSheetPointer(int row, int column, bool shift, string phase) =>
        Serially(async () =>
        {
            menu = null;
            switch (phase)
            {
                case "down":
                    Extend(shift, row, column);
                    break;
                case "drag":
                    Select(activeRow, activeColumn, row, column);
                    break;
                case "double":
                    Select(row, column, row, column);
                    if (Editable)
                    {
                        await BeginEditAsync("edit", BarText);
                    }

                    break;
            }

            StateHasChanged();
        });

    /// <summary>Called from JavaScript for a press or a drag on a column's or a row's header.</summary>
    [JSInvokable]
    public Task OnSheetHeader(string kind, int index, bool extend, string phase) =>
        Serially(() =>
        {
            menu = null;

            // A whole column, its first cell active; with Shift or a drag, the columns from where it started.
            if (kind == "column")
            {
                var from = index;
                if (extend)
                {
                    from = activeColumn;
                }

                Select(1, from, CellReference.MaxRow, index);
            }
            else
            {
                var from = index;
                if (extend)
                {
                    from = activeRow;
                }

                Select(from, 1, index, CellReference.MaxColumn);
            }

            reveal = null;
            StateHasChanged();
        });

    /// <summary>Called from JavaScript as a reference is pointed at in a formula, to outline it.</summary>
    [JSInvokable]
    public Task OnSheetPoint(int row, int column, int lastRow, int lastColumn) =>
        Serially(() =>
        {
            pointed = (row, column, lastRow, lastColumn);
            reveal = (lastRow, lastColumn, lastRow, lastColumn);
            StateHasChanged();
        });

    /// <summary>Called from JavaScript when typing starts the cell's editing, or F2 switches its mode.</summary>
    [JSInvokable]
    public Task OnSheetEditStart(string mode) =>
        Serially(() =>
        {
            if (!Editable)
            {
                return;
            }

            editMode = mode;
            note = null;
            StateHasChanged();
        });

    /// <summary>Called from JavaScript when the formula bar takes the focus: the cell is edited there.</summary>
    [JSInvokable]
    public Task OnSheetBarFocus() =>
        Serially(async () =>
        {
            if (!Editable ||
                editMode != null)
            {
                return;
            }

            await BeginEditAsync("edit", BarText);
            StateHasChanged();
        });

    /// <summary>Called from JavaScript when the typing in a cell is abandoned with Escape.</summary>
    [JSInvokable]
    public Task OnSheetCancel() =>
        Serially(() =>
        {
            EndEdit();
            note = null;
            StateHasChanged();
        });

    void EndEdit()
    {
        editMode = null;
        pointed = null;
        barVersion++;
    }

    async Task BeginEditAsync(string mode, string text, int? caret = null)
    {
        if (controller == null)
        {
            return;
        }

        editMode = mode;
        note = null;
        pointed = null;
        StateHasChanged();
        try
        {
            await controller.InvokeVoidAsync("beginEdit", text, mode, caret ?? text.Length);
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
    }

    /// <summary>
    /// Called from JavaScript when a cell's typing is handed in: what it now holds, and where the
    /// selection goes after — on down, up, left or right, nowhere, or "fill" for Ctrl+Enter, which sets
    /// every selected cell. False keeps the cell open, with the reason in the status bar.
    /// </summary>
    [JSInvokable]
    public async Task<bool> OnSheetCommit(string text, string move)
    {
        var ok = false;
        await Serially(async () =>
        {
            ok = await CommitAsync(text, move);
            StateHasChanged();
        });
        return ok;
    }

    async Task<bool> CommitAsync(string text, string move)
    {
        if (!Editable ||
            Sheet is not { } sheet)
        {
            EndEdit();
            return true;
        }

        var edits = new List<CellEdit>();
        if (move == "fill")
        {
            // Ctrl+Enter: every selected cell, a formula moved to each as a copied one would be.
            var selection = Bounded(Selection);
            var formula = text.StartsWith('=');
            for (var row = selection.FirstRow; row <= selection.LastRow; row++)
            {
                for (var column = selection.FirstColumn; column <= selection.LastColumn; column++)
                {
                    var input = text;
                    if (formula)
                    {
                        try
                        {
                            input = "=" + FormulaShifter.Shift(text[1..], row - activeRow, column - activeColumn);
                        }
                        catch (FormulaSyntaxException exception)
                        {
                            note = exception.Message;
                            return false;
                        }
                    }

                    edits.Add(new(row, column, input));
                }
            }
        }
        else if (text != (ActiveCell?.Input ?? string.Empty))
        {
            edits.Add(new(activeRow, activeColumn, text));
        }

        if (edits.Count > 0)
        {
            var index = sheet.Index;
            var made = await ApplyAsync(_ => WorkbookEditor.SetCells(_, index, edits, DateTime.Now));
            if (!made)
            {
                return false;
            }
        }

        EndEdit();
        switch (move)
        {
            case "down":
                Return(1);
                break;
            case "up":
                Return(-1);
                break;
            case "right":
                Across(1);
                break;
            case "left":
                Across(-1);
                break;
        }

        return true;
    }

    // A selection of whole columns or rows, cut down to the cells the sheet has.
    SheetRange Bounded(SheetRange range)
    {
        var sheet = Sheet;
        var lastRow = Math.Max(sheet?.MaxRow ?? 1, range.FirstRow);
        var lastColumn = Math.Max(sheet?.MaxColumn ?? 1, range.FirstColumn);
        return range with
        {
            LastRow = Math.Min(range.LastRow, lastRow),
            LastColumn = Math.Min(range.LastColumn, lastColumn)
        };
    }

    /// <summary>Called from JavaScript for Ctrl+C or Ctrl+X: the selection as text, tab between cells.</summary>
    [JSInvokable]
    public async Task<string?> OnSheetCopy(bool cut)
    {
        string? text = null;
        await Serially(() =>
        {
            text = CopyText(cut);
            StateHasChanged();
        });
        return text;
    }

    // The toolbar's lists, as the reader picks from them.
    internal Task OnFontChosen(ChangeEventArgs args) =>
        FormatAsync(new() { FontFamily = args.Value?.ToString() });

    internal Task OnSizeChosen(ChangeEventArgs args)
    {
        if (!double.TryParse(args.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
        {
            return Task.CompletedTask;
        }

        return FormatAsync(new() { FontSizePoints = size });
    }

    internal Task OnFormatChosen(ChangeEventArgs args) =>
        NumberFormatAsync(args.Value?.ToString() ?? "General");

    internal string? CopyText(bool cut)
    {
        if (Sheet is not { } sheet)
        {
            return null;
        }

        var range = Bounded(Selection);
        var builder = new StringBuilder();
        for (var row = range.FirstRow; row <= range.LastRow; row++)
        {
            for (var column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                if (column > range.FirstColumn)
                {
                    builder.Append('\t');
                }

                var display = sheet.Cell(row, column)?.Display ?? string.Empty;
                if (display.Contains('\t') ||
                    display.Contains('\n') ||
                    display.Contains('"'))
                {
                    display = "\"" + display.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
                }

                builder.Append(display);
            }

            builder.Append("\r\n");
        }

        var text = builder.ToString();
        clipboard = (sheetIndex, range, cut && Editable, text);
        return text;
    }

    /// <summary>
    /// Called from JavaScript for a paste on the grid. What was copied here is pasted as it was — its
    /// formulas moved, its formats with it — and anything else as text, tab between cells.
    /// </summary>
    [JSInvokable]
    public Task OnSheetPaste(string text) =>
        Serially(async () =>
        {
            await PasteAsync(text);
            StateHasChanged();
        });

    internal async Task PasteAsync(string? text)
    {
        if (!Editable ||
            Sheet is not { } sheet ||
            text == null)
        {
            return;
        }

        if (clipboard is var (fromSheet, from, cut, copied) &&
            Normalize(copied) == Normalize(text) &&
            grid?.Sheets.ElementAtOrDefault(fromSheet) is { } source)
        {
            var toSheet = sheet.Index;
            var row = Selection.FirstRow;
            var column = Selection.FirstColumn;
            if (await ApplyAsync(_ => WorkbookEditor.Copy(_, source.Index, from, toSheet, row, column, cut, DateTime.Now)))
            {
                Select(row, column, row + from.RowCount - 1, column + from.ColumnCount - 1);
                if (cut)
                {
                    clipboard = null;
                }
            }

            return;
        }

        var lines = Rows(text);
        var edits = new List<CellEdit>();
        for (var r = 0; r < lines.Count; r++)
        {
            for (var c = 0; c < lines[r].Count; c++)
            {
                edits.Add(new(Selection.FirstRow + r, Selection.FirstColumn + c, lines[r][c]));
            }
        }

        if (edits.Count == 0)
        {
            return;
        }

        var index = sheet.Index;
        var top = Selection.FirstRow;
        var left = Selection.FirstColumn;
        if (await ApplyAsync(_ => WorkbookEditor.SetCells(_, index, edits, DateTime.Now)))
        {
            Select(top, left, top + lines.Count - 1, left + lines.Max(_ => _.Count) - 1);
        }
    }

    static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    // Tab-separated text as rows of cells; a field in quotes may hold tabs, line breaks and doubled quotes.
    internal static List<List<string>> Rows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var normalized = Normalize(text);
        for (var index = 0; index < normalized.Length; index++)
        {
            var ch = normalized[index];
            if (quoted)
            {
                if (ch == '"' &&
                    index + 1 < normalized.Length &&
                    normalized[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    break;
                case '\t':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 ||
            row.Count > 0 ||
            normalized.Length > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Called from JavaScript when a column's or a row's edge has been dragged: its new size in pixels.</summary>
    [JSInvokable]
    public Task OnSheetResize(string kind, int index, double pixels) =>
        Serially(async () =>
        {
            await ResizeAsync(kind, index, pixels);
            StateHasChanged();
        });

    async Task ResizeAsync(string kind, int index, double pixels)
    {
        if (!Editable ||
            Sheet is not { } sheet)
        {
            return;
        }

        var selection = Selection;
        var sheetNumber = sheet.Index;
        if (kind == "column")
        {
            var (first, last) = (index, index);
            if (selection is
                {
                    FirstRow: 1,
                    LastRow: CellReference.MaxRow
                } &&
                index >= selection.FirstColumn &&
                index <= selection.LastColumn)
            {
                (first, last) = (selection.FirstColumn, selection.LastColumn);
            }

            var width = WorkbookGrid.Characters(Math.Max(0, pixels), sheet.MaxDigitWidth);
            await ApplyAsync(_ => WorkbookEditor.SetColumnWidth(_, sheetNumber, first, last, width));
            return;
        }

        var (top, bottom) = (index, index);
        if (selection is
            {
                FirstColumn: 1,
                LastColumn: CellReference.MaxColumn
            } &&
            index >= selection.FirstRow &&
            index <= selection.LastRow)
        {
            (top, bottom) = (selection.FirstRow, selection.LastRow);
        }

        var points = Math.Round(Math.Max(0, pixels) * pointsPerPixel * 4) / 4;
        await ApplyAsync(_ => WorkbookEditor.SetRowHeight(_, sheetNumber, top, bottom, points));
    }

    /// <summary>Called from JavaScript for a double click on a column's edge: the column fitted to what it holds.</summary>
    [JSInvokable]
    public Task OnSheetFit(string kind, int index) =>
        Serially(async () =>
        {
            if (kind != "column" ||
                Sheet is not { } sheet)
            {
                return;
            }

            // The text's width estimated from its characters at the cell's size; Excel measures the glyphs.
            var widest = 0d;
            foreach (var cell in sheet.Cells.Where(_ => _.Column == index && _.Display.Length > 0))
            {
                var scale = (cell.Look.FontSizePoints ?? 11) / 11;
                var longest = cell.Display.Split('\n').Max(_ => _.Length);
                widest = Math.Max(widest, longest * sheet.MaxDigitWidth * scale * 1.05);
            }

            if (widest <= 0)
            {
                return;
            }

            await ResizeAsync("column", index, widest + 8);
            StateHasChanged();
        });

    /// <summary>Called from JavaScript for Enter in the name box: the cell, range or name typed there selected.</summary>
    [JSInvokable]
    public Task OnSheetGoTo(string text) =>
        Serially(() =>
        {
            GoTo(text.Trim());
            StateHasChanged();
        });

    void GoTo(string text)
    {
        if (grid == null)
        {
            return;
        }

        var target = text;
        var bang = text.LastIndexOf('!');
        if (bang > 0)
        {
            var sheetName = text[..bang].Trim('\'');
            var found = grid.Sheets.FindIndex(_ => string.Equals(_.Name, sheetName, StringComparison.OrdinalIgnoreCase));
            if (found < 0)
            {
                note = $"There is no sheet called {sheetName}.";
                return;
            }

            SwitchTo(found);
            target = text[(bang + 1)..];
        }

        if (FormulaReference.Parse(null, target.Replace(" ", string.Empty, StringComparison.Ordinal)) is { Shape: not ReferenceShape.Invalid } reference)
        {
            var (firstRow, firstColumn, lastRow, lastColumn) = reference.Bounds;
            Select(firstRow, firstColumn, lastRow, lastColumn);
            reveal = (firstRow, firstColumn, firstRow, firstColumn);
            return;
        }

        note = $"'{text}' is not a cell or a range.";
    }

    /// <summary>Called from JavaScript for Enter in the find box: the next cell that holds the text.</summary>
    [JSInvokable]
    public Task OnSheetFind(string text, bool forward) =>
        Serially(() =>
        {
            findText = text;
            FindNext(forward);
            StateHasChanged();
        });

    internal void FindNext(bool forward)
    {
        if (Sheet is not { } sheet ||
            findText.Length == 0)
        {
            return;
        }

        var matches = sheet.Cells
            .Where(_ => _.Display.Contains(findText, StringComparison.OrdinalIgnoreCase) ||
                        (_.HasFormula && _.Input.Contains(findText, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(_ => _.Row)
            .ThenBy(_ => _.Column)
            .ToList();
        if (matches.Count == 0)
        {
            note = $"No cell holds “{findText}”.";
            return;
        }

        note = null;
        GridCell next;
        if (forward)
        {
            next = matches.FirstOrDefault(_ => _.Row > activeRow || (_.Row == activeRow && _.Column > activeColumn)) ?? matches[0];
        }
        else
        {
            next = matches.LastOrDefault(_ => _.Row < activeRow || (_.Row == activeRow && _.Column < activeColumn)) ?? matches[^1];
        }

        Select(next.Row, next.Column, next.Row, next.Column);
    }

    async Task FocusFindAsync()
    {
        try
        {
            await root.FocusAsync();
        }
        catch (Exception)
        {
            // Nothing to focus in a test host.
        }

        findOpen = true;
    }

    internal bool findOpen;

    // Edits

    // Every edit ends here: the file in, the file out, the grid read again from what it made, and the
    // editor told. An edit Excel would refuse (part of an array, a formula that cannot be read) is said
    // in the status bar and changes nothing.
    internal async Task<bool> ApplyAsync(Func<byte[], byte[]> edit)
    {
        if (busy ||
            bytes is not { } before ||
            ReadOnly)
        {
            return false;
        }

        busy = true;
        try
        {
            var after = await Task.Run(() => edit(before));
            if (ReferenceEquals(after, before))
            {
                return true;
            }

            Keep(undo, new(before, sheetIndex, activeRow, activeColumn, endRow, endColumn));
            redo.Clear();
            await ShowAsync(after);
            note = null;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            note = exception.Message;
            return false;
        }
        catch (Exception exception)
        {
            note = $"That could not be done: {exception.Message}";
            return false;
        }
        finally
        {
            busy = false;
        }
    }

    Task ShowAsync(byte[] next)
    {
        Load(next, false);
        if (!disposed)
        {
            return OnEdited.InvokeAsync(next);
        }

        return Task.CompletedTask;
    }

    static void Keep(List<Kept> history, Kept step)
    {
        history.Add(step);
        if (history.Count > undoDepth)
        {
            history.RemoveAt(0);
        }
    }

    internal Task UndoAsync() =>
        StepAsync(undo, redo);

    internal Task RedoAsync() =>
        StepAsync(redo, undo);

    async Task StepAsync(List<Kept> from, List<Kept> to)
    {
        if (from.Count == 0 ||
            bytes is not { } current ||
            busy)
        {
            return;
        }

        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        Keep(to, new(current, sheetIndex, activeRow, activeColumn, endRow, endColumn));
        sheetIndex = step.Sheet;
        await ShowAsync(step.Bytes);
        Select(step.Row, step.Column, step.EndRow, step.EndColumn);
    }

    // Hands in what is being typed before a command acts, as a press on Excel's ribbon does.
    internal async Task<bool> HandInAsync()
    {
        if (controller == null ||
            editMode == null)
        {
            return true;
        }

        try
        {
            return await controller.InvokeAsync<bool>("handIn");
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
    }

    internal async Task FormatAsync(CellFormatChange change)
    {
        menu = null;
        if (!await HandInAsync() ||
            Sheet is not { } sheet)
        {
            return;
        }

        var index = sheet.Index;
        var range = Selection;
        await ApplyAsync(_ => WorkbookEditor.Format(_, index, range, change));
        await FocusAsync();
    }

    // Bold and its kind: on, unless the active cell has it already, as Excel's buttons do.
    internal Task ToggleAsync(Func<GridLook, bool> has, Func<bool, CellFormatChange> change) =>
        FormatAsync(change(!has(ActiveLook)));

    internal Task AlignAsync(string horizontal)
    {
        var current = ActiveLook.Horizontal switch
        {
            TextAlignment.Left => "left",
            TextAlignment.Center => "center",
            TextAlignment.Right => "right",
            _ => "general"
        };
        if (current == horizontal)
        {
            horizontal = "general";
        }

        return FormatAsync(new() { Horizontal = horizontal });
    }

    internal Task VerticalAsync(string vertical) =>
        FormatAsync(new() { Vertical = vertical });

    internal Task NumberFormatAsync(string code) =>
        FormatAsync(new() { NumberFormat = code });

    // The active cell's format with a decimal more or one fewer, in every section of it.
    internal Task DecimalsAsync(int step)
    {
        var code = ActiveLook.NumberFormat ?? "General";
        if (code == "General")
        {
            var shown = ActiveCell?.Display ?? string.Empty;
            var point = shown.IndexOf('.', StringComparison.Ordinal);
            var places = point < 0 ? 0 : shown.Length - point - 1;
            places = Math.Max(0, places + step);
            code = places == 0 ? "0" : "0." + new string('0', places);
            return NumberFormatAsync(code);
        }

        var sections = code.Split(';');
        for (var index = 0; index < sections.Length; index++)
        {
            sections[index] = Decimals(sections[index], step);
        }

        return NumberFormatAsync(string.Join(';', sections));
    }

    static readonly Regex digits = new(@"0(\.0*)?", RegexOptions.CultureInvariant);

    static string Decimals(string section, int step)
    {
        var matches = digits.Matches(section);
        if (matches.Count == 0)
        {
            return section;
        }

        var last = matches[^1];
        var places = last.Groups[1].Success ? last.Groups[1].Value.Length - 1 : 0;
        places = Math.Max(0, places + step);
        var replacement = places == 0 ? "0" : "0." + new string('0', places);
        return section[..last.Index] + replacement + section[(last.Index + last.Length)..];
    }

    internal Task BordersAsync(string kind) =>
        FormatAsync(new() { Borders = kind });

    internal async Task ClearAsync(bool contents, bool formats)
    {
        menu = null;
        if (!await HandInAsync() ||
            !Editable ||
            Sheet is not { } sheet)
        {
            return;
        }

        var index = sheet.Index;
        var range = Selection;
        await ApplyAsync(_ => WorkbookEditor.Clear(_, index, range, contents, formats, DateTime.Now));
        await FocusAsync();
    }

    // Ctrl+D and Ctrl+R: the selection's first row (or column) copied down (or across) the rest of it.
    async Task FillAsync(bool down)
    {
        if (!Editable ||
            Sheet is not { } sheet)
        {
            return;
        }

        var range = Bounded(Selection);
        var index = sheet.Index;
        if (down)
        {
            if (range.RowCount < 2)
            {
                range = range with { FirstRow = Math.Max(1, range.FirstRow - 1) };
            }

            var source = range with { LastRow = range.FirstRow };
            await ApplyAsync(_ =>
            {
                var result = _;
                for (var row = range.FirstRow + 1; row <= range.LastRow; row++)
                {
                    result = WorkbookEditor.Copy(result, index, source, index, row, range.FirstColumn, false, DateTime.Now);
                }

                return result;
            });
            return;
        }

        if (range.ColumnCount < 2)
        {
            range = range with { FirstColumn = Math.Max(1, range.FirstColumn - 1) };
        }

        var first = range with { LastColumn = range.FirstColumn };
        await ApplyAsync(_ =>
        {
            var result = _;
            for (var column = range.FirstColumn + 1; column <= range.LastColumn; column++)
            {
                result = WorkbookEditor.Copy(result, index, first, index, range.FirstRow, column, false, DateTime.Now);
            }

            return result;
        });
    }

    // AutoSum: a SUM of the numbers above the active cell, or else to its left, offered for typing.
    internal async Task AutoSumAsync()
    {
        menu = null;
        if (!Editable ||
            Sheet is not { } sheet ||
            !await HandInAsync())
        {
            return;
        }

        bool Number(int row, int column) =>
            sheet.Cell(row, column) is { Number: not null };

        var range = string.Empty;
        if (Number(activeRow - 1, activeColumn))
        {
            var first = activeRow - 1;
            while (first > 1 && Number(first - 1, activeColumn))
            {
                first--;
            }

            range = $"{CellReference.Format(activeColumn, first)}:{CellReference.Format(activeColumn, activeRow - 1)}";
        }
        else if (Number(activeRow, activeColumn - 1))
        {
            var first = activeColumn - 1;
            while (first > 1 && Number(activeRow, first - 1))
            {
                first--;
            }

            range = $"{CellReference.Format(first, activeRow)}:{CellReference.Format(activeColumn - 1, activeRow)}";
        }

        // With nothing to sum, the caret waits between the brackets for the reader's range, as in Excel.
        var text = $"=SUM({range})";
        await BeginEditAsync("enter", text, range.Length == 0 ? text.Length - 1 : text.Length);
    }

    // Sheets

    internal async Task SelectSheetAsync(int index)
    {
        menu = null;
        if (!await HandInAsync())
        {
            return;
        }

        SwitchTo(index);
        await FocusAsync();
    }

    void SwitchTo(int index)
    {
        if (grid == null ||
            index < 0 ||
            index >= grid.Sheets.Count ||
            index == sheetIndex)
        {
            return;
        }

        sheetIndex = index;
        scrollTop = 0;
        scrollLeft = 0;
        var (row, column) = Sheet?.Active ?? (1, 1);
        Measure();
        Select(row, column, row, column);
        if (controller != null)
        {
            _ = controller.InvokeVoidAsync("scrollTo", 0, 0).AsTask();
        }
    }

    Task StepSheetAsync(int direction)
    {
        if (grid == null)
        {
            return Task.CompletedTask;
        }

        var index = sheetIndex;
        do
        {
            index += direction;
        }
        while (index >= 0 &&
               index < grid.Sheets.Count &&
               grid.Sheets[index].Hidden);

        return SelectSheetAsync(index);
    }

    // The view

    internal async Task ZoomAsync(double by)
    {
        zoom = Math.Clamp(Math.Round((zoom + by) * 10) / 10, 0.5, 2);
        geometryChanged = true;
        StateHasChanged();
        await Task.Yield();
        if (controller != null)
        {
            await controller.InvokeVoidAsync("report");
        }
    }

    internal void ToggleMenu(string name) =>
        menu = menu == name ? null : name;

    internal async Task FocusAsync()
    {
        if (controller == null)
        {
            return;
        }

        try
        {
            await controller.InvokeVoidAsync("focus");
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
    }

    internal async Task CopyAsync(bool cut)
    {
        menu = null;
        if (!await HandInAsync() ||
            controller == null ||
            CopyText(cut) is not { } text)
        {
            return;
        }

        await controller.InvokeVoidAsync("copyText", text);
        await FocusAsync();
    }

    internal async Task PasteFromClipboardAsync()
    {
        menu = null;
        if (!await HandInAsync() ||
            controller == null)
        {
            return;
        }

        var text = await controller.InvokeAsync<string?>("readClipboard");
        if (text == null)
        {
            note = "The browser did not let the page read the clipboard: press Ctrl+V instead.";
            return;
        }

        await PasteAsync(text);
        await FocusAsync();
    }

    /// <summary>
    /// Hands in what is being typed in a cell, for what saves or exports the file: what is saved is the
    /// workbook as the reader sees it.
    /// </summary>
    internal Task<bool> FinishTypingAsync() =>
        HandInAsync();

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        if (controller != null)
        {
            try
            {
                await controller.InvokeVoidAsync("dispose");
                await controller.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is gone.
            }
        }

        self?.Dispose();
        GC.SuppressFinalize(this);
    }
}
