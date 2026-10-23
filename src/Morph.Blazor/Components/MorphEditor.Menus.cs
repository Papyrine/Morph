namespace Morph;

// The editor's menus, as data (MenuLine): the ribbon's dropdowns that are lists of lines, and the
// right-click menu. Each is made here, where what it offers is known, and drawn by the one fragment
// (Menu); the dropdowns that are pictures rather than lists — the palettes, the table grid, the list
// libraries, the fonts and styles drawn as themselves — keep markup of their own.
//
// Choosing a line closes the menu first, so the pages are what the reader sees while it runs.
public partial class MorphEditor
{
    const string contextMenu = "context";

    // The submenu open in a menu, by its line's label: it opens on hover too, but a touch has no hover.
    string? openSubmenu;

    // Where the right-click menu opens, in pixels from the editor's top left, and that the script is
    // still to keep it inside the editor once it is drawn.
    (double X, double Y) contextAt;
    bool placeMenu;

    Task ChooseAsync(MenuLine line)
    {
        if (line.Command is { } command)
        {
            // The script carries out what it handles itself from the press; here the menu only closes.
            if (command.Handler == CommandHandler.Script)
            {
                CloseMenu();
                return Task.CompletedTask;
            }

            return ChooseAsync(command);
        }

        if (line.Disabled)
        {
            return Task.CompletedTask;
        }

        if (!line.StaysOpen)
        {
            CloseMenu();
        }

        return line.Choice?.Invoke() ?? Task.CompletedTask;
    }

    bool LineOff(MenuLine line)
    {
        if (line.Command is { } command)
        {
            return Off(command);
        }

        return line.Disabled;
    }

    static string RoleOf(MenuLine line)
    {
        if (line.Checked is null)
        {
            return "menuitem";
        }

        if (line.Toggle)
        {
            return "menuitemcheckbox";
        }

        return "menuitemradio";
    }

    void ToggleSubmenu(string label) =>
        openSubmenu = openSubmenu == label ? null : label;

    internal override void OpenContextMenu(double x, double y)
    {
        menu = contextMenu;
        openSubmenu = null;
        contextAt = (x, y);
        placeMenu = true;
    }

    // Escape: the menu open closes, or else the dialog.
    internal override void CloseMenus()
    {
        if (menu is not null)
        {
            CloseMenu();
            return;
        }

        if (dialog is not null)
        {
            CloseDialog();
        }
    }

    string ContextStyle =>
        string.Create(CultureInfo.InvariantCulture, $"left: {contextAt.X:0.#}px; top: {contextAt.Y:0.#}px");

    static Task Done(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    // Word's right-click menu, for what was right-clicked — which the script selected first: the
    // clipboard's commands, then what that place offers — a picture's wrapping, the Font and Paragraph
    // dialogs, a numbered list's numbering, a table's rows and columns, a link — and a comment. Unlike
    // the ribbon it offers only what belongs to the place, as Word's does; what belongs but cannot be
    // done is greyed out.
    IReadOnlyList<MenuLine> ContextLines()
    {
        List<MenuLine> lines =
        [
            MenuLine.Of(Commands.Cut),
            MenuLine.Of(Commands.Copy),
            MenuLine.Of(Commands.Paste),
            MenuLine.Separator
        ];
        if (state.PictureSelected)
        {
            // An inline picture is in line with text; a floating one's wrap is read from the file.
            lines.Add(new("Wrap text") {Lines = WrapLines(CurrentWrap ?? PictureWrap.Inline)});
            lines.Add(MenuLine.Separator);
        }

        lines.Add(MenuLine.Of(Commands.FontDialog, "Font…"));
        lines.Add(MenuLine.Of(Commands.ParagraphDialog, "Paragraph…"));
        if (state.List == ListKind.Number)
        {
            lines.Add(MenuLine.Separator);
            lines.Add(MenuLine.Of(Commands.RestartNumbering));
            lines.Add(MenuLine.Of(Commands.ContinueNumbering));
        }

        if (state.InTable)
        {
            lines.Add(MenuLine.Separator);
            lines.Add(
                new("Insert")
                {
                    Lines =
                    [
                        MenuLine.Of(Commands.InsertRowAbove, "Row above"),
                        MenuLine.Of(Commands.InsertRowBelow, "Row below"),
                        MenuLine.Of(Commands.InsertColumnLeft, "Column to the left"),
                        MenuLine.Of(Commands.InsertColumnRight, "Column to the right")
                    ]
                });
            lines.Add(
                new("Delete")
                {
                    Lines =
                    [
                        MenuLine.Of(Commands.DeleteRow, "Row"),
                        MenuLine.Of(Commands.DeleteColumn, "Column"),
                        MenuLine.Of(Commands.DeleteTable, "Table")
                    ]
                });
            lines.Add(MenuLine.Of(Commands.MergeCells));
            lines.Add(MenuLine.Of(Commands.SplitCells, "Split cells…"));
        }

        lines.Add(MenuLine.Separator);
        lines.Add(state.InLink ? MenuLine.Of(Commands.RemoveLink) : MenuLine.Of(Commands.Link, "Link…"));
        if (Shown(Commands.NewComment))
        {
            lines.Add(MenuLine.Of(Commands.NewComment));
        }

        return lines;
    }

    // Home

    IReadOnlyList<MenuLine> SpacingLines()
    {
        var lines = LineSpacings
            .Select(_ => new MenuLine(_.ToString("0.0#", CultureInfo.InvariantCulture))
            {
                Checked = Math.Abs(CurrentLineSpacing - _) < 0.01,
                Choice = () => SpaceAsync(new(Line: _))
            })
            .ToList();
        lines.Add(MenuLine.Separator);
        lines.Add(new(HasSpaceBefore ? "Remove space before paragraph" : "Add space before paragraph") {Choice = ToggleSpaceBeforeAsync});
        lines.Add(new(HasSpaceAfter ? "Remove space after paragraph" : "Add space after paragraph") {Choice = ToggleSpaceAfterAsync});
        lines.Add(MenuLine.Separator);
        lines.Add(MenuLine.Of(Commands.ParagraphDialog, "Line spacing options…"));
        return lines;
    }

    IReadOnlyList<MenuLine> ParagraphBorderLines() =>
    [
        .. ParagraphBorders.Select(_ => new MenuLine(_.Name) {Choice = () => BorderParagraphsAsync(_.Border)}),
        MenuLine.Separator,
        .. WidthLines()
    ];

    // Under the Numbering library's looks.
    static IReadOnlyList<MenuLine> NumberingLines() =>
    [
        MenuLine.Separator,
        MenuLine.Of(Commands.RestartNumbering),
        MenuLine.Of(Commands.ContinueNumbering)
    ];

    // Word's border widths, chosen before the border they are for: the menu stays open.
    IEnumerable<MenuLine> WidthLines() =>
    [
        MenuLine.Heading("Width"),
        .. BorderSizes.Select(_ => new MenuLine(_.Name)
        {
            Checked = BorderSize == _.Size,
            StaysOpen = true,
            Choice = () => Done(() => BorderSize = _.Size)
        })
    ];

    // Insert

    IReadOnlyList<MenuLine> PageNumberLines() =>
    [
        MenuLine.Heading("At the caret"),
        new("Page number") {Choice = () => InsertPageNumberAsync(false)},
        new("Number of pages") {Choice = () => InsertPageNumberAsync(true)}
    ];

    // Layout: the caret's section is read only while one of its menus is open.

    IReadOnlyList<MenuLine> MarginLines()
    {
        var current = CurrentPage;
        return
        [
            .. MarginPresets.Select(_ => new MenuLine(_.Name)
            {
                Checked = current != null &&
                          (current.Top, current.Right, current.Bottom, current.Left) == (_.Top, _.Right, _.Bottom, _.Left),
                Detail = $"{Centimetres(_.Top)} top, {Centimetres(_.Left)} left, {Centimetres(_.Right)} right",
                Choice = () => SetMarginsAsync(_.Top, _.Right, _.Bottom, _.Left)
            }),
            MenuLine.Separator,
            new("Custom margins…") {Choice = () => Done(OpenMargins)}
        ];
    }

    IReadOnlyList<MenuLine> OrientationLines()
    {
        var current = CurrentPage;
        return
        [
            new("Portrait") {Checked = current is {Landscape: false}, Choice = () => SetOrientationAsync(false)},
            new("Landscape") {Checked = current is {Landscape: true}, Choice = () => SetOrientationAsync(true)}
        ];
    }

    IReadOnlyList<MenuLine> PaperLines()
    {
        var current = CurrentPage;
        return
        [
            .. PaperSizes.Select(_ => new MenuLine(_.Name)
            {
                Checked = current != null &&
                          Math.Min(current.Width, current.Height) == _.Width &&
                          Math.Max(current.Width, current.Height) == _.Height,
                Detail = $"{Centimetres(_.Width)} × {Centimetres(_.Height)}",
                Choice = () => SetPaperSizeAsync(_.Width, _.Height)
            })
        ];
    }

    IReadOnlyList<MenuLine> ColumnLines()
    {
        var current = CurrentPage;
        return
        [
            .. ColumnChoices.Select(_ => new MenuLine(_.Name)
            {
                Checked = current?.Columns == _.Count,
                Choice = () => SetColumnsAsync(_.Count, current?.LineBetween == true)
            }),
            MenuLine.Separator,
            new("Line between")
            {
                Toggle = true,
                Checked = current?.LineBetween == true,
                Disabled = current?.Columns is null or < 2,
                Choice = () => SetColumnsAsync(current?.Columns ?? 1, current?.LineBetween != true)
            }
        ];
    }

    IReadOnlyList<MenuLine> BreakLines() =>
    [
        MenuLine.Heading("Page breaks"),
        MenuLine.Of(Commands.PageBreak, "Page"),
        MenuLine.Heading("Section breaks"),
        new("Next page") {Choice = () => InsertSectionBreakAsync(false)},
        new("Continuous") {Choice = () => InsertSectionBreakAsync(true)}
    ];

    IReadOnlyList<MenuLine> WrapLines(PictureWrap? current) =>
    [
        .. WrapChoices.Select(_ => new MenuLine(_.Name) {Checked = current == _.Wrap, Choice = () => WrapPictureAsync(_.Wrap)})
    ];

    // Table

    IReadOnlyList<MenuLine> FitLines() =>
    [
        new("AutoFit contents")
        {
            Choice = () => FitTableAsync(TableFit.Contents)
        },
        new("AutoFit window")
        {
            Choice = () => FitTableAsync(TableFit.Window)
        },
        new("Fixed column width")
        {
            Choice = () => FitTableAsync(TableFit.Fixed)
        }
    ];

    IReadOnlyList<MenuLine> TableBorderLines() =>
    [
        new("All borders")
        {
            Choice = () => SetTableBordersAsync(BorderPreset.All)
        },
        new("Outside borders")
        {
            Choice = () => SetTableBordersAsync(BorderPreset.Outside)
        },
        new("Inside borders")
        {
            Choice = () => SetTableBordersAsync(BorderPreset.Inside)
        },
        new("No border")
        {
            Choice = () => SetTableBordersAsync(BorderPreset.None)
        },
        MenuLine.Separator,
        .. WidthLines()
    ];
}
