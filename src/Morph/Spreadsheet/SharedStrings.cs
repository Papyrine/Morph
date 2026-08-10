using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// The workbook's shared string table, which is where nearly all cell text actually lives: a text
/// cell stores an index into this table rather than the string itself, so the same label repeated
/// down a column is stored once.
///
/// Materialised eagerly into an array because the corpus reads far more cells than the table has
/// entries, and walking the part's element list per lookup would be quadratic.
/// </summary>
sealed class SharedStrings
{
    readonly string[] entries;

    public SharedStrings(WorkbookPart workbookPart) =>
        entries = workbookPart.SharedStringTablePart?.SharedStringTable?
            .Elements<S.SharedStringItem>()
            .Select(Flatten)
            .ToArray() ?? [];

    public string Get(int index) =>
        index >= 0 && index < entries.Length ? entries[index] : string.Empty;

    /// <summary>
    /// An entry is either one text node or a sequence of formatted runs. The runs' own formatting is
    /// dropped: the model applies one style per cell, and a cell is the unit a spreadsheet formats.
    /// </summary>
    static string Flatten(S.SharedStringItem item)
    {
        if (item.Text?.Text is { } single)
        {
            return single;
        }

        return string.Concat(item.Elements<S.Run>().Select(_ => _.Text?.Text ?? string.Empty));
    }
}
