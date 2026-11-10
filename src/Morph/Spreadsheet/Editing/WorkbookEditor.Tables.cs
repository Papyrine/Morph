using S = DocumentFormat.OpenXml.Spreadsheet;

static partial class WorkbookEditor
{
    /// <summary>
    /// A table's header row, which Excel keeps in step with the table's own list of columns: a header
    /// cell's text IS its column's name, and a file where the two differ is one Excel repairs. So text
    /// typed into a header renames the column — to <c>Column1</c> and the like when it is cleared, with a
    /// number added where another column has the name already — and the structured references that name
    /// it are renamed with it, as Excel renames them.
    /// </summary>
    static class Tables
    {
        public sealed record Header(S.Table Table, int Index);

        public static Header? HeaderAt(S.Worksheet worksheet, int row, int column)
        {
            if (worksheet.WorksheetPart is not { } part)
            {
                return null;
            }

            foreach (var tablePart in part.TableDefinitionParts)
            {
                if (tablePart.Table is not { } table ||
                    (table.HeaderRowCount?.Value ?? 1) == 0 ||
                    CellReference.ParseRange(table.Reference?.Value) is not { } range ||
                    range.FirstRow != row ||
                    column < range.FirstColumn ||
                    column > range.LastColumn)
                {
                    continue;
                }

                return new(table, column - range.FirstColumn);
            }

            return null;
        }

        public static void Rename(SpreadsheetDocument document, Header header, CellEntry entry, StringTable strings, S.Cell cell)
        {
            var columns = header.Table.TableColumns?.Elements<S.TableColumn>().ToList() ?? [];
            if (header.Index >= columns.Count)
            {
                return;
            }

            var named = columns[header.Index];
            var old = Decode(named.Name?.Value ?? string.Empty);
            var wanted = entry.Kind switch
            {
                CellEntryKind.Clear => $"Column{header.Index + 1}",
                CellEntryKind.Number => FormulaCoercion.NumberText(entry.Number),
                CellEntryKind.Boolean when entry.Number != 0 => "TRUE",
                CellEntryKind.Boolean => "FALSE",
                CellEntryKind.Error => FormulaErrors.Text(entry.Error),
                CellEntryKind.Formula => "=" + entry.Text,
                _ => entry.Text
            };

            var others = columns.Where((_, index) => index != header.Index)
                .Select(_ => Decode(_.Name?.Value ?? string.Empty))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = wanted;
            for (var suffix = 2; others.Contains(name); suffix++)
            {
                name = wanted + suffix.ToString(CultureInfo.InvariantCulture);
            }

            cell.CellFormula = null;
            cell.InlineString = null;
            cell.DataType = S.CellValues.SharedString;
            cell.CellValue = new(strings.Index(name).ToString(CultureInfo.InvariantCulture));
            Tidy(cell);

            if (string.Equals(old, name, StringComparison.Ordinal))
            {
                return;
            }

            named.Name = Encode(name);
            var table = header.Table.DisplayName?.Value ?? header.Table.Name?.Value ?? string.Empty;
            RenameReferences(document, table, old, name);
        }

        // The formulas that name the column by its old name, in cells and in the tables' own columns.
        static void RenameReferences(SpreadsheetDocument document, string table, string old, string name)
        {
            var workbookPart = document.WorkbookPart!;
            foreach (var part in workbookPart.WorksheetParts)
            {
                foreach (var formula in part.Worksheet?.Descendants<S.CellFormula>() ?? [])
                {
                    if (formula.Text is { Length: > 0 } text &&
                        Renamed(text, table, old, name) is { } renamed)
                    {
                        formula.Text = renamed;
                    }
                }

                foreach (var tablePart in part.TableDefinitionParts)
                {
                    foreach (var element in tablePart.Table?.Descendants<OpenXmlLeafTextElement>() ?? [])
                    {
                        if (element is S.CalculatedColumnFormula or S.TotalsRowFormula &&
                            element.Text is { Length: > 0 } text &&
                            Renamed(text, table, old, name) is { } renamed)
                        {
                            element.Text = renamed;
                        }
                    }
                }
            }

            foreach (var defined in workbookPart.Workbook?.DefinedNames?.Elements<S.DefinedName>() ?? [])
            {
                if (Renamed(defined.Text, table, old, name) is { } renamed)
                {
                    defined.Text = renamed;
                }
            }
        }

        // A formula with the table's structured references to the old column renamed; null when it has none.
        static string? Renamed(string formula, string table, string old, string name)
        {
            List<FormulaToken> tokens;
            try
            {
                tokens = FormulaLexer.Tokenize(formula);
            }
            catch (FormulaSyntaxException)
            {
                return null;
            }

            var builder = new StringBuilder(formula.Length);
            var copied = 0;
            var changed = false;
            var from = "[" + Escape(old) + "]";
            var to = "[" + Escape(name) + "]";
            foreach (var token in tokens)
            {
                if (token.Kind != FormulaTokenKind.Structured ||
                    (token.Sheet is { Length: > 0 } owner && !string.Equals(owner, table, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var text = token.Text;
                var replaced = text.Replace(from, to, StringComparison.OrdinalIgnoreCase);
                // [@Price], the shorthand for this row.
                replaced = replaced.Replace("[@" + Escape(old) + "]", "[@" + Escape(name) + "]", StringComparison.OrdinalIgnoreCase);
                // Table1[Price] has its column's name in the outer brackets alone.
                var brackets = (token.Sheet ?? string.Empty).Length;
                if (replaced.Length > brackets &&
                    replaced.AsSpan(brackets).Equals(from, StringComparison.OrdinalIgnoreCase))
                {
                    replaced = replaced[..brackets] + to;
                }

                if (replaced == text)
                {
                    continue;
                }

                builder.Append(formula, copied, token.Start - copied);
                builder.Append(replaced);
                copied = token.Start + token.Length;
                changed = true;
            }

            if (!changed)
            {
                return null;
            }

            builder.Append(formula, copied, formula.Length - copied);
            return builder.ToString();
        }

        // A column's name as a structured reference spells it: ' before [ ] # and '.
        static string Escape(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (ch is '[' or ']' or '#' or '\'')
                {
                    builder.Append('\'');
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        static readonly Regex escaped = new("_x([0-9A-Fa-f]{4})_", RegexOptions.CultureInvariant);

        static string Decode(string name) =>
            escaped.Replace(name, _ => ((char) int.Parse(_.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());

        // A line break in a column's name is written _x000a_, which an attribute keeps where a raw one
        // would be read back as a space.
        static string Encode(string name) =>
            name.Replace("\r", "_x000d_", StringComparison.Ordinal)
                .Replace("\n", "_x000a_", StringComparison.Ordinal);
    }
}
