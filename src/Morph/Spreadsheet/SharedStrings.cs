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
    readonly S.SharedStringItem[] items;

    public SharedStrings(WorkbookPart workbookPart)
    {
        items = workbookPart.SharedStringTablePart?.SharedStringTable?
            .Elements<S.SharedStringItem>()
            .ToArray() ?? [];
        entries = items.Select(Flatten).ToArray();
    }

    /// <summary>The entry's formatted runs, or null when it is plain text.</summary>
    public IReadOnlyList<S.Run>? RunsOf(int index) =>
        index >= 0 && index < items.Length ? Runs(items[index]) : null;

    /// <summary>
    /// The formatted runs of a rich string (<c>CT_Rst</c>), or null when it is one text node or no run
    /// formats itself — the cell's own font then holds for the whole text.
    /// </summary>
    public static IReadOnlyList<S.Run>? Runs(OpenXmlElement item)
    {
        if (item.GetFirstChild<S.Text>() != null)
        {
            return null;
        }

        var runs = item.Elements<S.Run>().ToArray();
        return runs.Any(_ => _.RunProperties != null) ? runs : null;
    }

    public string Get(int index)
    {
        if (index >= 0 &&
            index < entries.Length)
        {
            return entries[index];
        }

        return string.Empty;
    }

    /// <summary>
    /// The text of a rich string: either one text node or a sequence of formatted runs, whose own
    /// formatting <see cref="Runs"/> keeps.
    ///
    /// Typed as <see cref="OpenXmlElement"/> because the same content model — <c>CT_Rst</c> — backs
    /// both a shared string entry and a cell's own <c>is</c>, and an inline rich string is otherwise
    /// easy to read as its absent single <c>t</c> and so render blank.
    /// </summary>
    public static string Flatten(OpenXmlElement item)
    {
        if (item.GetFirstChild<S.Text>()?.Text is { } single)
        {
            return single;
        }

        return string.Concat(item.Elements<S.Run>().Select(_ => _.Text?.Text ?? string.Empty));
    }
}
