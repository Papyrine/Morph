namespace Morph;

// The sheets' tabs: a sheet added (the + button, Shift+F11, the tab's menu), renamed (a double-click on
// its tab, or the menu), deleted and moved, through WorkbookEditor.AddSheet and its kind — the file in, the
// file out, as every edit of the grid is.
public partial class SheetEditor
{
    internal const string tabMenu = "tab";

    // The sheet whose tab was right-clicked.
    internal int tabSheet;

    // The sheet whose tab is being renamed, and what its box holds.
    internal int? renaming;
    internal string renameText = string.Empty;
    bool renamePending;

    /// <summary>Whether sheets can be added, renamed, deleted or moved.</summary>
    internal bool CanChangeSheets => !ReadOnly && grid != null;

    internal bool CanDelete(int sheet) =>
        CanChangeSheets &&
        grid!.Sheets.Any(_ => _.Index != sheet && !_.Hidden);

    // The visible sheet before (or after) one, which Move left (or right) puts it in the place of.
    internal int? Neighbour(int sheet, int direction)
    {
        if (grid == null)
        {
            return null;
        }

        for (var index = sheet + direction; index >= 0 && index < grid.Sheets.Count; index += direction)
        {
            if (!grid.Sheets[index].Hidden)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>A new sheet at a place in the list, shown, as Excel's New Sheet makes one.</summary>
    internal async Task AddSheetAsync(int at)
    {
        menu = null;
        if (!CanChangeSheets ||
            !await HandInAsync())
        {
            return;
        }

        if (await ApplyAsync(_ => WorkbookEditor.AddSheet(_, at)))
        {
            ShowActive();
        }

        await FocusAsync();
    }

    internal async Task DeleteSheetAsync(int sheet)
    {
        menu = null;
        if (!CanDelete(sheet) ||
            !await HandInAsync())
        {
            return;
        }

        if (await ApplyAsync(_ => WorkbookEditor.DeleteSheet(_, sheet, DateTime.Now)))
        {
            ShowActive();
        }

        await FocusAsync();
    }

    internal async Task MoveSheetAsync(int sheet, int direction)
    {
        menu = null;
        if (!CanChangeSheets ||
            Neighbour(sheet, direction) is not { } to ||
            !await HandInAsync())
        {
            return;
        }

        if (await ApplyAsync(_ => WorkbookEditor.MoveSheet(_, sheet, to)))
        {
            sheetIndex = to;
            Measure();
        }

        await FocusAsync();
    }

    // The sheet the edit made active — a new one, or the one in a deleted one's place — shown from its start.
    void ShowActive()
    {
        if (grid == null)
        {
            return;
        }

        sheetIndex = -1;
        SwitchTo(Math.Clamp(grid.ActiveSheet, 0, Math.Max(0, grid.Sheets.Count - 1)));
    }

    internal Task BeginRenameAsync(int sheet)
    {
        menu = null;
        if (!CanChangeSheets ||
            grid?.Sheets.ElementAtOrDefault(sheet) is not { } target)
        {
            return Task.CompletedTask;
        }

        renaming = sheet;
        renameText = target.Name;
        renamePending = true;
        return Task.CompletedTask;
    }

    internal Task OnRenameKey(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "Enter":
                return CommitRenameAsync();
            case "Escape":
                renaming = null;
                note = null;
                return FocusAsync();
        }

        return Task.CompletedTask;
    }

    // What the box holds becomes the sheet's name, unless Excel would refuse it: then the box stays, and the
    // status bar says why.
    internal async Task CommitRenameAsync()
    {
        if (renaming is not { } sheet ||
            grid?.Sheets.ElementAtOrDefault(sheet) is not { } target)
        {
            renaming = null;
            return;
        }

        var name = renameText;
        if (name == target.Name)
        {
            renaming = null;
            await FocusAsync();
            return;
        }

        if (await ApplyAsync(_ => WorkbookEditor.RenameSheet(_, sheet, name)))
        {
            renaming = null;
            await FocusAsync();
            return;
        }

        renamePending = true;
    }

    /// <summary>
    /// Called from JavaScript for a right-click on a sheet's tab, at a place in pixels from the grid's top
    /// left corner: the tab's menu opens there.
    /// </summary>
    [JSInvokable]
    public Task OnSheetTabMenu(int sheet, double x, double y) =>
        Serially(async () =>
        {
            if (!await HandInAsync())
            {
                return;
            }

            tabSheet = sheet;
            contextAt = (x, y);
            menu = tabMenu;
            placePending = true;
            StateHasChanged();
        });

    // The box just drawn takes the keyboard, its text selected, as Excel's tab does.
    async Task SelectRenameAsync()
    {
        if (!renamePending ||
            controller == null)
        {
            return;
        }

        renamePending = false;
        try
        {
            await controller.InvokeVoidAsync("selectRename");
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
    }
}
