namespace Morph;

// Rows and columns inserted and deleted — Excel's Home › Cells group, Ctrl+Plus and Ctrl+Minus, and the
// grid's right-click menu — through WorkbookEditor.InsertRows and its kind: the file in, the file out, as
// every edit of the grid is.
public partial class SheetEditor
{
    internal const string insertMenu = "insert";
    internal const string deleteMenu = "delete";
    internal const string contextMenu = "context";

    // Where the right-click menu opens, in pixels from the grid's own top left corner.
    internal (double X, double Y)? contextAt;
    bool placePending;

    /// <summary>Whether the selection is whole rows, every column of them.</summary>
    internal bool WholeRows => Selection is {FirstColumn: 1, LastColumn: CellReference.MaxColumn};

    /// <summary>Whether the selection is whole columns, every row of them.</summary>
    internal bool WholeColumns => Selection is {FirstRow: 1, LastRow: CellReference.MaxRow};

    internal Task InsertRowsAsync() =>
        ShiftAsync(true, false);

    internal Task InsertColumnsAsync() =>
        ShiftAsync(false, false);

    internal Task DeleteRowsAsync() =>
        ShiftAsync(true, true);

    internal Task DeleteColumnsAsync() =>
        ShiftAsync(false, true);

    // As many rows (or columns) as the selection spans, at its first, as Excel's Insert Sheet Rows and
    // Delete Sheet Rows do. The selection stays where it was: on the rows inserted, or on those that moved
    // up into the place of the rows deleted.
    async Task ShiftAsync(bool rows, bool delete)
    {
        menu = null;
        if (!await HandInAsync() ||
            !Editable ||
            Sheet is not { } sheet)
        {
            return;
        }

        var range = Selection;
        var index = sheet.Index;
        var at = range.FirstColumn;
        var count = range.ColumnCount;
        if (rows)
        {
            at = range.FirstRow;
            count = range.RowCount;
        }

        await ApplyAsync(_ => Shift(_, index, rows, delete, at, count));
        await FocusAsync();
    }

    static byte[] Shift(byte[] bytes, int sheet, bool rows, bool delete, int at, int count)
    {
        var now = DateTime.Now;
        if (rows && delete)
        {
            return WorkbookEditor.DeleteRows(bytes, sheet, at, count, now);
        }

        if (rows)
        {
            return WorkbookEditor.InsertRows(bytes, sheet, at, count, now);
        }

        if (delete)
        {
            return WorkbookEditor.DeleteColumns(bytes, sheet, at, count, now);
        }

        return WorkbookEditor.InsertColumns(bytes, sheet, at, count, now);
    }

    // Ctrl+Plus and Ctrl+Minus: whole rows or columns selected are inserted or deleted at once; for cells,
    // the menu asks which, where Excel's dialog asks.
    Task ShiftKeyAsync(bool delete)
    {
        if (!Editable)
        {
            return Task.CompletedTask;
        }

        if (WholeRows)
        {
            return ShiftAsync(true, delete);
        }

        if (WholeColumns)
        {
            return ShiftAsync(false, delete);
        }

        menu = insertMenu;
        if (delete)
        {
            menu = deleteMenu;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called from JavaScript for a right-click on the grid — on a cell, a column's letter or a row's
    /// number — at a place in pixels from the grid's top left corner. What is under it is selected first,
    /// unless it is in the selection already, as Excel's right-click does, and the menu opens there.
    /// </summary>
    [JSInvokable]
    public Task OnSheetContextMenu(string kind, int row, int column, double x, double y) =>
        Serially(() =>
        {
            var selection = Selection;
            switch (kind)
            {
                case "column":
                    if (!WholeColumns ||
                        column < selection.FirstColumn ||
                        column > selection.LastColumn)
                    {
                        Select(1, column, CellReference.MaxRow, column);
                    }

                    break;
                case "row":
                    if (!WholeRows ||
                        row < selection.FirstRow ||
                        row > selection.LastRow)
                    {
                        Select(row, 1, row, CellReference.MaxColumn);
                    }

                    break;
                default:
                    if (row < selection.FirstRow ||
                        row > selection.LastRow ||
                        column < selection.FirstColumn ||
                        column > selection.LastColumn)
                    {
                        Select(row, column, row, column);
                    }

                    break;
            }

            reveal = null;
            contextAt = (x, y);
            menu = contextMenu;
            placePending = true;
            StateHasChanged();
        });

    internal string ContextStyle
    {
        get
        {
            var (x, y) = contextAt ?? (0, 0);
            return FormattableString.Invariant($"left:{x:0.#}px;top:{y:0.#}px");
        }
    }

    // The menu just drawn, moved in from the grid's edges where it would run past them.
    async Task PlaceMenuAsync()
    {
        if (!placePending ||
            controller == null)
        {
            return;
        }

        placePending = false;
        try
        {
            await controller.InvokeVoidAsync("placeMenu");
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
    }
}
