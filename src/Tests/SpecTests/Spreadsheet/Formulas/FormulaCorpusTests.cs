using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// The formula engine against Excel itself: every workbook of the corpus is worked out again, and every
/// formula's value has to be the one Excel cached for it. The corpus's 757 formulas are mostly table
/// formulas — IFERROR, IF and ISBLANK over structured references, SUBTOTAL totals rows, VLOOKUP into
/// other tables, TEXT and WEEKDAY over dates — so this is the check that the engine reads real
/// workbooks as Excel does, not only the cases written for it.
///
/// What rests on TODAY or NOW is left out, since Excel cached it on the day the file was saved, and so
/// is what the engine cannot work out (which keeps the cached value, and so would pass regardless).
/// </summary>
public class FormulaCorpusTests
{
    public static IEnumerable<string> Workbooks() =>
        Directory.GetFiles(ScenarioInputs.Root(ScenarioFormat.Excel), "input.xlsx", SearchOption.AllDirectories)
            .Select(_ => Path.GetFileName(Path.GetDirectoryName(_)!))
            .Order(StringComparer.Ordinal);

    static string PathOf(string name) =>
        Path.Combine(ScenarioInputs.Root(ScenarioFormat.Excel), name, "input.xlsx");

    [Test]
    [MethodDataSource(nameof(Workbooks))]
    public async Task EveryFormula_WorksOutAsExcelCachedIt(string name)
    {
        using var document = SpreadsheetDocument.Open(PathOf(name), false);
        var book = FormulaWorkbook.Read(document.WorkbookPart!, new(2026, 11, 10, 9, 0, 0));
        book.Recalculate();

        var differences = new List<string>();
        foreach (var sheet in book.Sheets)
        {
            foreach (var cell in sheet.Cells
                         .Where(_ => _ is
                         {
                             HasFormula: true,
                             Fixed: false,
                             Unsupported: false,
                             Volatile: false
                         }))
            {
                if (!Same(cell.Value, cell.Cached))
                {
                    differences.Add($"{sheet.Name}!{CellReference.Format(cell.Column, cell.Row)} ={cell.Formula}: {cell.Value} for Excel's {cell.Cached}");
                }
            }
        }

        await Assert.That(differences).IsEmpty();
    }

    // Every function the corpus calls is one the engine has.
    [Test]
    public async Task EveryFunctionTheCorpusCalls_IsWorkedOut()
    {
        var unsupported = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in Workbooks())
        {
            using var document = SpreadsheetDocument.Open(PathOf(name), false);
            var book = FormulaWorkbook.Read(document.WorkbookPart!, new(2026, 11, 10));
            book.Recalculate();
            foreach (var sheet in book.Sheets)
            {
                foreach (var cell in sheet.Cells.Where(_ => _.Unsupported))
                {
                    unsupported.Add($"{name} {sheet.Name}!{CellReference.Format(cell.Column, cell.Row)} ={cell.Formula}");
                }
            }
        }

        await Assert.That(unsupported).IsEmpty();
    }

    // Excel caches a number to seventeen digits and the engine may differ in the last; a formula's
    // empty text is cached as an empty <v>, which reads as blank.
    static bool Same(FormulaValue mine, FormulaValue excel)
    {
        if (mine.IsNumber &&
            excel.IsNumber)
        {
            var scale = Math.Max(1, Math.Max(Math.Abs(mine.Number), Math.Abs(excel.Number)));
            return Math.Abs(mine.Number - excel.Number) <= scale * 1e-9;
        }

        if (excel.IsBlank &&
            mine.IsText &&
            mine.Text.Length == 0)
        {
            return true;
        }

        if (mine.Kind != excel.Kind)
        {
            return false;
        }

        return mine.Kind switch
        {
            FormulaValueKind.Text => mine.Text == excel.Text,
            FormulaValueKind.Boolean => mine.Boolean == excel.Boolean,
            FormulaValueKind.Error => mine.Error == excel.Error,
            _ => true
        };
    }
}
