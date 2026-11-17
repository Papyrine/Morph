using S = DocumentFormat.OpenXml.Spreadsheet;
using XM = DocumentFormat.OpenXml.Office.Excel;

static partial class WorkbookEditor
{
    /// <summary>
    /// Adds an empty sheet at a place in the list of sheets, named Sheet1, Sheet2 and on — the first no
    /// sheet has (<see cref="SheetNames.Next"/>) — and makes it the active sheet, as Excel's New Sheet does.
    /// </summary>
    public static byte[] AddSheet(byte[] xlsx, int at) =>
        Edit(xlsx, null, _ =>
        {
            var workbookPart = _.WorkbookPart!;
            var sheets = SheetList(_);
            RefuseIfProtected(_);
            at = Math.Clamp(at, 0, sheets.Count);
            var part = workbookPart.AddNewPart<WorksheetPart>();
            part.Worksheet = new(
                new S.SheetViews(new S.SheetView
                {
                    WorkbookViewId = 0
                }),
                new S.SheetFormatProperties
                {
                    DefaultRowHeight = DefaultRowHeight(workbookPart)
                },
                new S.SheetData(),
                new S.PageMargins
                {
                    Left = 0.7,
                    Right = 0.7,
                    Top = 0.75,
                    Bottom = 0.75,
                    Header = 0.3,
                    Footer = 0.3
                });
            var made = new S.Sheet
            {
                Id = workbookPart.GetIdOfPart(part),
                SheetId = sheets.Count == 0 ? 1 : sheets.Max(_ => _.SheetId?.Value ?? 0) + 1,
                Name = SheetNames.Next(sheets.Select(_ => _.Name?.Value ?? string.Empty))
            };
            var list = workbookPart.Workbook!.Sheets!;
            if (at < sheets.Count)
            {
                list.InsertBefore(made, sheets[at]);
            }
            else
            {
                list.AppendChild(made);
            }

            // What names a sheet by its place moves on with the sheets after it.
            Renumber(_, place =>
            {
                if (place >= at)
                {
                    return place + 1;
                }

                return place;
            });
            Activate(_, at);
        });

    /// <summary>
    /// Renames a sheet, as Excel does: every formula, name and chart that names it spells the new name —
    /// in quotes where it has to be (<see cref="SheetNames.Spelled"/>) — and a link's target, which Excel
    /// leaves alone, is left alone. A name Excel would refuse is refused (<see cref="SheetNames.Problem"/>).
    /// </summary>
    public static byte[] RenameSheet(byte[] xlsx, int sheet, string name) =>
        Edit(xlsx, null, _ =>
        {
            var sheets = SheetList(_);
            RefuseIfProtected(_);
            var target = At(sheets, sheet);
            var old = target.Name?.Value ?? string.Empty;
            if (string.Equals(old, name, StringComparison.Ordinal))
            {
                return;
            }

            var others = sheets.Where(_ => !ReferenceEquals(_, target)).Select(_ => _.Name?.Value ?? string.Empty);
            if (SheetNames.Problem(name, others) is { } problem)
            {
                throw new InvalidOperationException(problem);
            }

            target.Name = name;
            EachFormula(_, (formula, _) => SheetNames.Renamed(formula, old, name));
            foreach (var cache in _.WorkbookPart!.PivotTableCacheDefinitionParts)
            {
                if (cache.PivotCacheDefinition?.CacheSource?.WorksheetSource is { } source &&
                    string.Equals(source.Sheet?.Value, old, StringComparison.OrdinalIgnoreCase))
                {
                    source.Sheet = name;
                }
            }
        });

    /// <summary>
    /// Deletes a sheet, as Excel does: a reference to it anywhere else, a name qualified by it and a
    /// structured reference to one of its tables is <c>#REF!</c>; the names scoped to it go with it. The
    /// last visible sheet can't be deleted, nor any sheet of a workbook whose structure is protected.
    /// </summary>
    public static byte[] DeleteSheet(byte[] xlsx, int sheet, DateTime now) =>
        Edit(xlsx, now, _ =>
        {
            var workbookPart = _.WorkbookPart!;
            var sheets = SheetList(_);
            RefuseIfProtected(_);
            var target = At(sheets, sheet);
            if (!sheets.Any(_ => !ReferenceEquals(_, target) && Visible(_)))
            {
                throw new InvalidOperationException("A workbook must keep at least one visible sheet.");
            }

            var name = target.Name?.Value ?? string.Empty;
            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var part = target.Id?.Value is { } id && workbookPart.TryGetPartById(id, out var found) ? found : null;
            if (part is WorksheetPart worksheetPart)
            {
                foreach (var table in worksheetPart.TableDefinitionParts)
                {
                    if (table.Table is { } definition)
                    {
                        tables.Add(definition.DisplayName?.Value ?? definition.Name?.Value ?? string.Empty);
                    }
                }
            }

            // The names scoped to it go; the rest of what names sheets by place moves back.
            foreach (var defined in workbookPart.Workbook?.DefinedNames?.Elements<S.DefinedName>().ToList() ?? [])
            {
                if (defined.LocalSheetId?.Value == (uint) sheet)
                {
                    defined.Remove();
                }
            }

            target.Remove();
            if (part != null)
            {
                workbookPart.DeletePart(part);
            }

            if (workbookPart.Workbook?.DefinedNames is {HasChildren: false} empty)
            {
                empty.Remove();
            }

            Renumber(_, place =>
            {
                if (place > sheet)
                {
                    return place - 1;
                }

                return place;
            });
            EachFormula(_, (formula, _) => SheetNames.Deleted(formula, name, tables));

            // The sheet shown is the one that took its place, or the last, as Excel shows it.
            var remaining = SheetList(_);
            var shown = Math.Min(sheet, remaining.Count - 1);
            while (shown > 0 &&
                   !Visible(remaining[shown]))
            {
                shown--;
            }

            Activate(_, shown);
        });

    /// <summary>
    /// Moves a sheet to another place in the list. Formulas name sheets by name and need nothing; what
    /// names a sheet by its place — a name scoped to it, the active tab — follows it.
    /// </summary>
    public static byte[] MoveSheet(byte[] xlsx, int from, int to) =>
        Edit(xlsx, null, _ =>
        {
            var sheets = SheetList(_);
            RefuseIfProtected(_);
            var moving = At(sheets, from);
            to = Math.Clamp(to, 0, sheets.Count - 1);
            if (to == from)
            {
                return;
            }

            var list = _.WorkbookPart!.Workbook!.Sheets!;
            moving.Remove();
            var rest = list.Elements<S.Sheet>().ToList();
            if (to < rest.Count)
            {
                list.InsertBefore(moving, rest[to]);
            }
            else
            {
                list.AppendChild(moving);
            }

            Renumber(_, place =>
            {
                if (place == from)
                {
                    return to;
                }

                if (from < to && place > from && place <= to)
                {
                    return place - 1;
                }

                if (to < from && place >= to && place < from)
                {
                    return place + 1;
                }

                return place;
            });
            Activate(_, to);
        });

    static List<S.Sheet> SheetList(SpreadsheetDocument document) =>
        document.WorkbookPart?.Workbook?.Sheets?.Elements<S.Sheet>().ToList() ?? [];

    static S.Sheet At(List<S.Sheet> sheets, int sheet)
    {
        if (sheet < 0 ||
            sheet >= sheets.Count)
        {
            throw new InvalidOperationException("The workbook has no such sheet.");
        }

        return sheets[sheet];
    }

    static bool Visible(S.Sheet sheet) =>
        sheet.State == null ||
        sheet.State.Value == S.SheetStateValues.Visible;

    static void RefuseIfProtected(SpreadsheetDocument document)
    {
        if (document.WorkbookPart?.Workbook?.WorkbookProtection?.LockStructure?.Value == true)
        {
            throw new InvalidOperationException("The workbook's structure is protected: its sheets can't be added, renamed, deleted or moved.");
        }
    }

    // A new sheet's rows stand as high as the workbook's first sheet says its rows do, which follows from
    // the Normal style's font as Excel works it out.
    static double DefaultRowHeight(WorkbookPart workbookPart)
    {
        foreach (var part in workbookPart.WorksheetParts)
        {
            if (part.Worksheet?.GetFirstChild<S.SheetFormatProperties>()?.DefaultRowHeight?.Value is { } height and > 0)
            {
                return height;
            }
        }

        return 15;
    }

    // What names a sheet by its place in the list: names scoped to a sheet, and the workbook's views'
    // active and first tabs.
    static void Renumber(SpreadsheetDocument document, Func<int, int> place)
    {
        var workbook = document.WorkbookPart!.Workbook!;
        foreach (var defined in workbook.DefinedNames?.Elements<S.DefinedName>() ?? [])
        {
            if (defined.LocalSheetId?.Value is { } local)
            {
                defined.LocalSheetId = (uint) place((int) local);
            }
        }

        foreach (var view in workbook.BookViews?.Elements<S.WorkbookView>() ?? [])
        {
            if (view.FirstSheet?.Value is { } first)
            {
                view.FirstSheet = (uint) Math.Max(0, place((int) first));
            }
        }
    }

    // The sheet shown and selected when the workbook opens.
    static void Activate(SpreadsheetDocument document, int sheet)
    {
        var workbookPart = document.WorkbookPart!;
        var workbook = workbookPart.Workbook!;
        var views = workbook.BookViews ??= new();
        if (!views.Elements<S.WorkbookView>().Any())
        {
            views.AppendChild(new S.WorkbookView());
        }

        foreach (var view in views.Elements<S.WorkbookView>())
        {
            view.ActiveTab = (uint) sheet;
            if (view.FirstSheet?.Value > (uint) sheet)
            {
                view.FirstSheet = (uint) sheet;
            }
        }

        var sheets = SheetList(document);
        for (var index = 0; index < sheets.Count; index++)
        {
            if (sheets[index].Id?.Value is not { } id ||
                !workbookPart.TryGetPartById(id, out var part))
            {
                continue;
            }

            var sheetViews = part switch
            {
                WorksheetPart worksheet => worksheet.Worksheet?.GetFirstChild<S.SheetViews>()?.Elements<S.SheetView>(),
                ChartsheetPart chartsheet => chartsheet.Chartsheet?.GetFirstChild<S.ChartSheetViews>()?.Elements<S.ChartSheetView>().Cast<OpenXmlElement>(),
                _ => null
            };
            foreach (var view in sheetViews ?? [])
            {
                var selected = index == sheet;
                switch (view)
                {
                    case S.SheetView cells:
                        cells.TabSelected = selected ? true : null;
                        break;
                    case S.ChartSheetView chart:
                        chart.TabSelected = selected ? true : null;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Every formula in the workbook rewritten: in cells, conditional formats and validations on every
    /// sheet, in Excel 2010's extensions to those, in tables' calculated columns and totals, in defined
    /// names, and in charts' series. The second argument is the formula's own sheet, null for a defined
    /// name's or a chart's.
    /// </summary>
    static void EachFormula(SpreadsheetDocument document, Func<string, string?, string> rewrite)
    {
        var workbookPart = document.WorkbookPart!;

        void Apply(OpenXmlLeafTextElement? element, string? sheet)
        {
            if (element?.Text is not {Length: > 0} text)
            {
                return;
            }

            var changed = rewrite(text, sheet);
            if (changed != text)
            {
                element.Text = changed;
            }
        }

        foreach (var (worksheet, name) in Sheets(document))
        {
            foreach (var formula in worksheet.Descendants<S.CellFormula>())
            {
                Apply(formula, name);
            }

            foreach (var format in worksheet.Elements<S.ConditionalFormatting>())
            {
                foreach (var formula in format.Descendants<S.Formula>())
                {
                    Apply(formula, name);
                }
            }

            foreach (var validation in worksheet.Descendants<S.DataValidation>())
            {
                Apply(validation.Formula1, name);
                Apply(validation.Formula2, name);
            }

            foreach (var formula in worksheet.Descendants<XM.Formula>())
            {
                Apply(formula, name);
            }

            foreach (var tablePart in worksheet.WorksheetPart?.TableDefinitionParts ?? [])
            {
                foreach (var column in tablePart.Table?.TableColumns?.Elements<S.TableColumn>() ?? [])
                {
                    Apply(column.CalculatedColumnFormula, name);
                    Apply(column.TotalsRowFormula, name);
                }
            }
        }

        foreach (var defined in workbookPart.Workbook?.DefinedNames?.Elements<S.DefinedName>() ?? [])
        {
            Apply(defined, null);
        }

        foreach (var chart in Charts(workbookPart))
        {
            foreach (var leaf in chart.Descendants<OpenXmlLeafTextElement>().ToList())
            {
                if (leaf.LocalName == "f")
                {
                    Apply(leaf, null);
                }
            }
        }
    }
}
