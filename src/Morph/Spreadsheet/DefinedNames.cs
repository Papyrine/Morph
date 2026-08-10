using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// The workbook's built-in defined names, which is where a sheet's print settings live rather than
/// on the sheet itself.
///
/// <c>_xlnm.Print_Area</c> restricts what is printed, and <c>_xlnm.Print_Titles</c> names the rows
/// (and/or columns) repeated at the top of every page. Both are scoped to a sheet through
/// <c>localSheetId</c>, which indexes the workbook's sheet order.
/// </summary>
sealed class DefinedNames
{
    readonly Dictionary<string, string> printAreas = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> printTitles = new(StringComparer.OrdinalIgnoreCase);

    public static DefinedNames For(WorkbookPart workbookPart)
    {
        var result = new DefinedNames();
        var sheets = workbookPart.Workbook?.Sheets?.Elements<S.Sheet>().ToArray() ?? [];
        var names = workbookPart.Workbook?.DefinedNames?.Elements<S.DefinedName>();
        if (names == null)
        {
            return result;
        }

        foreach (var name in names)
        {
            var value = name.Text;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            // localSheetId indexes the sheet list; a name without one is workbook-scoped and does
            // not apply to any single sheet's printing.
            if (name.LocalSheetId?.Value is not { } scope || scope >= sheets.Length)
            {
                continue;
            }

            var sheetName = sheets[(int) scope].Name?.Value;
            if (sheetName == null)
            {
                continue;
            }

            switch (name.Name?.Value)
            {
                case "_xlnm.Print_Area":
                    result.printAreas[sheetName] = value;
                    break;
                case "_xlnm.Print_Titles":
                    result.printTitles[sheetName] = value;
                    break;
            }
        }

        return result;
    }

    public string? PrintArea(string sheetName) => printAreas.GetValueOrDefault(sheetName);

    /// <summary>
    /// The inclusive row band repeated on every page, or null when the sheet repeats none.
    ///
    /// The value is a range reference such as <c>Sheet1!$1:$4</c>, and may name columns as well as
    /// rows (<c>Sheet1!$A:$B,Sheet1!$1:$4</c>). Only the row part is honoured — the fragmenter can
    /// repeat header ROWS, having no notion of a repeated column.
    /// </summary>
    public (int First, int Last)? PrintTitleRows(string sheetName)
    {
        if (!printTitles.TryGetValue(sheetName, out var value))
        {
            return null;
        }

        foreach (var part in value.Split(','))
        {
            var bare = part;
            var bang = bare.LastIndexOf('!');
            if (bang >= 0)
            {
                bare = bare[(bang + 1)..];
            }

            bare = bare.Replace("$", string.Empty, StringComparison.Ordinal);
            var bounds = bare.Split(':');
            if (bounds.Length != 2)
            {
                continue;
            }

            // A row band is digits only ("1:4"); a column band would be letters.
            if (int.TryParse(bounds[0], out var first) && int.TryParse(bounds[1], out var last))
            {
                return (Math.Min(first, last), Math.Max(first, last));
            }
        }

        return null;
    }
}
