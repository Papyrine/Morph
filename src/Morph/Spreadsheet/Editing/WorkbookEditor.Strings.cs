using S = DocumentFormat.OpenXml.Spreadsheet;

static partial class WorkbookEditor
{
    /// <summary>
    /// The workbook's shared strings, as an edit adds to them: text already in the table is pointed at
    /// rather than added again, as Excel stores a label repeated down a column once. Only plain entries
    /// are matched — a rich string, with runs of its own formatting, is not the same text typed plain.
    /// </summary>
    sealed class StringTable(WorkbookPart workbookPart)
    {
        Dictionary<string, int>? index;
        S.SharedStringTable? table;
        int count;
        bool changed;

        public int Index(string text)
        {
            if (index == null)
            {
                Load();
            }

            if (index!.TryGetValue(text, out var existing))
            {
                return existing;
            }

            var item = new S.SharedStringItem(Text(text));
            table!.AppendChild(item);
            var added = count++;
            index[text] = added;
            changed = true;
            return added;
        }

        void Load()
        {
            var part = workbookPart.SharedStringTablePart ?? workbookPart.AddNewPart<SharedStringTablePart>();
            table = part.SharedStringTable ??= new();
            index = new(StringComparer.Ordinal);
            foreach (var item in table.Elements<S.SharedStringItem>())
            {
                if (item.GetFirstChild<S.Text>() is { } plain &&
                    !item.Elements<S.Run>().Any())
                {
                    index.TryAdd(plain.Text, count);
                }

                count++;
            }
        }

        /// <summary>The counts the table states, kept true; the total of uses is left unstated.</summary>
        public void Save()
        {
            if (!changed ||
                table == null)
            {
                return;
            }

            table.UniqueCount = (uint) count;
            table.Count = null;
        }
    }

    // Spaces at either end are kept only if the text says so.
    static S.Text Text(string text)
    {
        var element = new S.Text(text);
        if (text.Length > 0 &&
            (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]) || text.Contains('\n')))
        {
            element.Space = SpaceProcessingModeValues.Preserve;
        }

        return element;
    }
}
