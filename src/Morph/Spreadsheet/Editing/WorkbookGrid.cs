using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// A workbook read for the grid the editor shows it in: each sheet's cells as they read and look, sized
/// as Excel sizes them on screen — a column's width from its character count and the body font's digit
/// (<c>width * maxDigitWidth</c>, Excel's own rounding), a row's height from its points at 96 dpi.
///
/// A cell looks as the pages draw it (<c>SheetGridBuilder</c>): its own style over its table's — the
/// table's stripes and header where the cell keeps the Normal style's fill and colour — and a conditional
/// format's over both. Values are what the file holds, which every edit has worked out
/// (<see cref="WorkbookEditor"/>), so reading the grid works nothing out.
/// </summary>
sealed class WorkbookGrid
{
    const double pixelsPerPoint = 96.0 / 72.0;

    public List<SheetGrid> Sheets { get; } = [];

    /// <summary>The sheet the workbook was saved showing.</summary>
    public int ActiveSheet { get; private set; }

    public static WorkbookGrid Read(byte[] xlsx, string? fontDirectory, CultureInfo? culture = null)
    {
        using var input = new MemoryStream(xlsx, false);
        var normalized = StrictToTransitional.Normalize(input);
        try
        {
            using var document = SpreadsheetDocument.Open(normalized, false);
            return Read(document.WorkbookPart ?? throw new InvalidOperationException("The package has no workbook part."), fontDirectory, culture ?? CultureInfo.InvariantCulture);
        }
        finally
        {
            if (!ReferenceEquals(normalized, input))
            {
                normalized.Dispose();
            }
        }
    }

    static WorkbookGrid Read(WorkbookPart workbookPart, string? fontDirectory, CultureInfo culture)
    {
        var grid = new WorkbookGrid();
        var themeColors = ThemeParser.ExtractThemeColors(workbookPart.ThemePart);
        var styles = new CellStyles(workbookPart, themeColors);
        var stylesheet = workbookPart.WorkbookStylesPart?.Stylesheet;
        var (family, size) = styles.DefaultFont;
        var digit = Digit(fontDirectory, family, size);

        var book = FormulaWorkbook.Read(workbookPart, DateTime.Now, culture);
        var normal = styles.Resolve(0);
        var looks = new Dictionary<uint, GridLook>();
        var elements = workbookPart.Workbook?.Sheets?.Elements<S.Sheet>().ToList() ?? [];
        grid.ActiveSheet = (int) (workbookPart.Workbook?.BookViews?.GetFirstChild<S.WorkbookView>()?.ActiveTab?.Value ?? 0);
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var hidden = element.State?.Value is { } state &&
                         state != S.SheetStateValues.Visible;
            var part = element.Id?.Value is { } id ? workbookPart.GetPartById(id) as WorksheetPart : null;
            var sheet = new SheetGrid(element.Name?.Value ?? $"Sheet{index + 1}", index)
            {
                Hidden = hidden,
                IsChart = part?.Worksheet == null,
                MaxDigitWidth = digit
            };
            grid.Sheets.Add(sheet);
            if (part?.Worksheet is not { } worksheet)
            {
                continue;
            }

            ReadLayout(sheet, worksheet, digit);
            var tables = new TableStyles(part, stylesheet, styles);
            var conditional = new ConditionalFormats(worksheet, stylesheet, themeColors);

            GridLook LookOf(uint style, int row, int column, string text, double? number)
            {
                var resolved = styles.Resolve(style);
                var table = tables.IsEmpty ? null : tables.For(row, column);
                var overlay = conditional.IsEmpty ? null : conditional.For(row, column, text, number);
                if (table == null &&
                    overlay == null &&
                    looks.TryGetValue(style, out var known))
                {
                    return known;
                }

                var color = resolved.ColorHex;
                if (table?.ColorHex is { } tableColor &&
                    resolved.ColorHex == normal.ColorHex)
                {
                    color = tableColor;
                }

                var fill = resolved.BackgroundColorHex;
                if (table?.BackgroundColorHex is { } tableFill &&
                    resolved.BackgroundColorHex == normal.BackgroundColorHex)
                {
                    fill = tableFill;
                }

                var borders = resolved.Borders;
                if (table is { } drawn)
                {
                    borders = borders with
                    {
                        Left = borders.Left.IsVisible ? borders.Left : drawn.Left ?? borders.Left,
                        Right = borders.Right.IsVisible ? borders.Right : drawn.Right ?? borders.Right,
                        Top = borders.Top.IsVisible ? borders.Top : drawn.Top ?? borders.Top,
                        Bottom = borders.Bottom.IsVisible ? borders.Bottom : drawn.Bottom ?? borders.Bottom
                    };
                }

                var look = new GridLook
                {
                    FontFamily = resolved.FontFamily ?? family,
                    FontSizePoints = resolved.FontSizePoints ?? size,
                    Bold = resolved.Bold || table?.Bold == true || overlay?.Bold == true,
                    Italic = resolved.Italic || table?.Italic == true || overlay?.Italic == true,
                    Underline = resolved.Underline,
                    Strikethrough = resolved.Strikethrough,
                    ColorHex = overlay?.ColorHex ?? color,
                    FillHex = overlay?.BackgroundColorHex ?? fill,
                    Horizontal = resolved.HorizontalAlignment,
                    Vertical = resolved.VerticalAlignment,
                    Wrap = resolved.WrapText,
                    Indent = resolved.IndentLevel,
                    NumberFormat = resolved.EffectiveFormatCode,
                    Borders = borders
                };
                if (table == null &&
                    overlay == null)
                {
                    looks[style] = look;
                }

                return look;
            }

            var data = worksheet.GetFirstChild<S.SheetData>();
            var rowNumber = 0;
            foreach (var line in data?.Elements<S.Row>() ?? [])
            {
                rowNumber = line.RowIndex?.Value is { } stated ? (int) stated : rowNumber + 1;
                var columnNumber = 0;
                foreach (var cellElement in line.Elements<S.Cell>())
                {
                    var statedColumn = CellReference.ColumnOf(cellElement.CellReference?.Value);
                    columnNumber = statedColumn == 0 ? columnNumber + 1 : statedColumn;
                    var style = cellElement.StyleIndex?.Value ?? 0;
                    var formulaCell = book.Cell(index, rowNumber, columnNumber);
                    if (formulaCell == null)
                    {
                        var empty = LookOf(style, rowNumber, columnNumber, string.Empty, null);
                        if (!empty.IsPlain ||
                            style != 0)
                        {
                            sheet.AddLook(rowNumber, columnNumber, empty);
                        }

                        continue;
                    }

                    var code = NumberFormat.Localize(styles.Resolve(style).EffectiveFormatCode, culture);
                    var (display, formatColor) = Display(formulaCell.Value, code);
                    double? number = formulaCell.Value.IsNumber ? formulaCell.Value.Number : null;
                    var look = LookOf(style, rowNumber, columnNumber, display, number);
                    sheet.Add(new(rowNumber, columnNumber, display, Input(formulaCell, look.NumberFormat), look)
                    {
                        Numeric = formulaCell.Value.IsNumber,
                        Centred = formulaCell.Value.IsBoolean || formulaCell.Value.IsError,
                        HasFormula = formulaCell.HasFormula,
                        Number = number,
                        FormatColorHex = formatColor
                    });
                }
            }

            // A table's stripes reach its rows that hold nothing.
            if (!tables.IsEmpty)
            {
                foreach (var table in book.Tables.Where(_ => _.Sheet == index))
                {
                    for (var row = table.Range.FirstRow; row <= table.Range.LastRow; row++)
                    {
                        for (var column = table.Range.FirstColumn; column <= table.Range.LastColumn; column++)
                        {
                            if (sheet.Cell(row, column) == null)
                            {
                                sheet.AddLook(row, column, LookOf(0, row, column, string.Empty, null));
                            }
                        }
                    }
                }
            }
        }

        return grid;
    }

    static readonly ConcurrentDictionary<(string?, string, double), double> digits = new();

    // The body font's widest digit in whole pixels, as Excel measures it (Calibri 11's is 7): measured
    // once per font, since the grid is read again after every edit.
    static double Digit(string? fontDirectory, string family, double size) =>
        digits.GetOrAdd((fontDirectory, family, size), _ =>
        {
            using var resolver = LayoutFonts.CreateResolver(_.Item1, null);
            return Math.Max(1, Math.Round(SheetGridBuilder.MaxDigitWidth(LayoutFonts.ToDelegate(resolver), _.Item2, _.Item3)));
        });

    static void ReadLayout(SheetGrid sheet, S.Worksheet worksheet, double digit)
    {
        var format = worksheet.SheetFormatProperties;
        if (format?.DefaultRowHeight?.Value is { } rowPoints)
        {
            sheet.DefaultRowHeight = Math.Round(rowPoints * pixelsPerPoint);
        }

        if (format?.DefaultColumnWidth?.Value is { } columnWidth)
        {
            sheet.DefaultColumnWidth = Pixels(columnWidth, digit);
        }
        else
        {
            // baseColWidth is in characters without the padding; Excel rounds the column up to 8 pixels.
            var characters = format?.BaseColumnWidth?.Value ?? 8;
            var pixels = Math.Truncate(characters * digit + 5);
            sheet.DefaultColumnWidth = Math.Ceiling(pixels / 8) * 8;
        }

        foreach (var column in worksheet.GetFirstChild<S.Columns>()?.Elements<S.Column>() ?? [])
        {
            var min = (int) (column.Min?.Value ?? 0);
            var max = Math.Min((int) (column.Max?.Value ?? 0), CellReference.MaxColumn);
            var width = column.Hidden?.Value == true ? 0 : column.Width?.Value is { } stated ? Pixels(stated, digit) : sheet.DefaultColumnWidth;
            for (var each = min; each <= max && each >= 1; each++)
            {
                sheet.ColumnWidths[each] = width;
            }
        }

        var rowNumber = 0;
        foreach (var row in worksheet.GetFirstChild<S.SheetData>()?.Elements<S.Row>() ?? [])
        {
            rowNumber = row.RowIndex?.Value is { } stated ? (int) stated : rowNumber + 1;
            if (row.Hidden?.Value == true)
            {
                sheet.RowHeights[rowNumber] = 0;
            }
            else if (row.Height?.Value is { } height)
            {
                sheet.RowHeights[rowNumber] = Math.Round(height * pixelsPerPoint);
            }
        }

        foreach (var merge in worksheet.GetFirstChild<S.MergeCells>()?.Elements<S.MergeCell>() ?? [])
        {
            if (CellReference.ParseRange(merge.Reference?.Value) is { } range &&
                (range.RowCount > 1 || range.ColumnCount > 1))
            {
                sheet.Merges.Add(range);
            }
        }

        if (worksheet.SheetViews?.GetFirstChild<S.SheetView>() is { } view)
        {
            sheet.ShowGridLines = view.ShowGridLines?.Value ?? true;
            if (view.GetFirstChild<S.Pane>() is
                {
                    State.Value: var paneState
                } pane &&
                (paneState == S.PaneStateValues.Frozen ||
                 paneState == S.PaneStateValues.FrozenSplit))
            {
                sheet.FrozenColumns = (int) (pane.HorizontalSplit?.Value ?? 0);
                sheet.FrozenRows = (int) (pane.VerticalSplit?.Value ?? 0);
            }

            var active = view.Elements<S.Selection>().LastOrDefault()?.ActiveCell?.Value;
            if (CellReference.RowOf(active) is > 0 and var activeRow &&
                CellReference.ColumnOf(active) is > 0 and var activeColumn)
            {
                sheet.Active = (activeRow, activeColumn);
            }
        }
    }

    /// <summary>A column's stated width (characters, padding included) in pixels, as Excel draws it.</summary>
    public static double Pixels(double width, double digit) =>
        Math.Truncate((256 * width + Math.Truncate(128 / digit)) / 256 * digit);

    /// <summary>The width the file states for a column drawn so many pixels wide: <see cref="Pixels"/> undone.</summary>
    public static double Characters(double pixels, double digit) =>
        Math.Truncate(pixels / digit * 256) / 256;

    static (string Text, string? ColorHex) Display(FormulaValue value, string? code)
    {
        switch (value.Kind)
        {
            case FormulaValueKind.Number:
                var formatted = NumberFormat.Format(value.Number, code);
                return (formatted.Text, formatted.ColorHex);
            case FormulaValueKind.Text:
                var text = NumberFormat.FormatText(value.Text, code);
                return (text.Text, text.ColorHex);
            case FormulaValueKind.Boolean when value.Boolean:
                return ("TRUE", null);
            case FormulaValueKind.Boolean:
                return ("FALSE", null);
            case FormulaValueKind.Error:
                return (FormulaErrors.Text(value.Error), null);
            default:
                return (string.Empty, null);
        }
    }

    /// <summary>
    /// What the formula bar shows for a cell, and editing starts from: its formula, or its value as it
    /// would be typed — a date as a date, a percentage with its sign, any other number in full.
    /// </summary>
    static string Input(FormulaCell cell, string? code)
    {
        if (cell.Formula is { } formula)
        {
            return FormulaText.ForDisplay(formula);
        }

        var value = cell.Value;
        switch (value.Kind)
        {
            case FormulaValueKind.Number:
                return NumberInput(value.Number, code);
            case FormulaValueKind.Text:
                return value.Text;
            case FormulaValueKind.Boolean when value.Boolean:
                return "TRUE";
            case FormulaValueKind.Boolean:
                return "FALSE";
            case FormulaValueKind.Error:
                return FormulaErrors.Text(value.Error);
            default:
                return string.Empty;
        }
    }

    static string NumberInput(double number, string? code)
    {
        if (code is { } format &&
            IsDate(format))
        {
            var date = FormulaCoercion.FromSerial(number);
            var day = date.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
            var time = number - Math.Floor(number);
            if (number < 1)
            {
                return date.ToString("h:mm:ss tt", CultureInfo.InvariantCulture);
            }

            if (time > 0)
            {
                return $"{day} {date.ToString("h:mm:ss tt", CultureInfo.InvariantCulture)}";
            }

            return day;
        }

        if (code is { } percent &&
            percent.Contains('%') &&
            !percent.Contains('"'))
        {
            return FormulaCoercion.NumberText(number * 100) + "%";
        }

        return FormulaCoercion.NumberText(number);
    }

    // A code with day, month or year letters or an hour outside quotes and brackets is a date's.
    static bool IsDate(string code)
    {
        var inQuotes = false;
        var inBrackets = false;
        foreach (var ch in code)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
            {
                continue;
            }

            if (ch == '[')
            {
                inBrackets = true;
                continue;
            }

            if (ch == ']')
            {
                inBrackets = false;
                continue;
            }

            if (!inBrackets &&
                char.ToLowerInvariant(ch) is 'd' or 'm' or 'y' or 'h' or 's')
            {
                return true;
            }
        }

        return false;
    }
}
