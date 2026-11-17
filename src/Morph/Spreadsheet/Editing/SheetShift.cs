/// <summary>
/// Rows or columns inserted into one sheet, or deleted from it, and what that does to whatever names the
/// sheet's cells — a formula's references, the range a merge or a validation covers, a table, a picture's
/// anchor. The rules are Excel's, probed over COM (Excel 16, 2026-11-17):
///
/// <list type="bullet">
/// <item>Inserted, a row (or column) at or past the first one inserted moves on by as many. A range the
/// insert falls inside grows; one it falls at the start of moves whole, and one it falls just past is left
/// as it was. A range that reaches the sheet's last row (or column) still reaches it.</item>
/// <item>Deleted, a cell that went is <c>#REF!</c> (<c>Data!#REF!</c> when the sheet is named), and so is a
/// range that went whole; a range that lost part of itself keeps the rest, and one that reached the sheet's
/// last row still does. What was past the deleted rows moves back by as many.</item>
/// </list>
///
/// An A1 reference names its cell wherever its formula stands, so absolute and relative references move
/// alike. Only a shared formula's text depends on where its cell is, and the editor writes those out per
/// cell before anything moves.
/// </summary>
sealed class SheetShift(string sheet, bool rows, int at, int count, bool delete)
{
    /// <summary>The sheet's name, which a reference from another sheet names it by.</summary>
    public string Sheet { get; } = sheet;

    /// <summary>Rows, or else columns.</summary>
    public bool Rows { get; } = rows;

    /// <summary>The first row (or column) inserted or deleted, 1-based.</summary>
    public int At { get; } = at;

    public int Count { get; } = count;

    public bool Delete { get; } = delete;

    /// <summary>The last row (or column) deleted, or the last inserted.</summary>
    public int Last => At + Count - 1;

    public int Max => Rows ? CellReference.MaxRow : CellReference.MaxColumn;

    /// <summary>
    /// The tables that lose columns, by name: the names of the columns lost, or null for a table deleted
    /// whole. A structured reference to one is <c>#REF!</c>, as Excel makes it.
    /// </summary>
    public Dictionary<string, HashSet<string>?> Lost { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The data rows of the tables that rows are inserted into at their first data row. A range over just
    /// those rows, within the table's columns, grows with the table rather than moving down as a range the
    /// insert falls at the start of would: Excel's <c>SUM(E20:E28)</c> over a table's data reads
    /// <c>SUM(E20:E30)</c> with two rows inserted at row 20, where <c>SUM(E20:E22)</c> reads
    /// <c>SUM(E22:E24)</c>, and so do a name, a validation and a conditional format over them.
    /// </summary>
    public List<SheetRange> Bodies { get; } = [];

    /// <summary>Where one row (or column) goes; null when it was deleted or pushed off the sheet.</summary>
    public int? Index(int value)
    {
        if (value < At)
        {
            return value;
        }

        if (!Delete)
        {
            if (value + Count > Max)
            {
                return null;
            }

            return value + Count;
        }

        if (value > Last)
        {
            return value - Count;
        }

        return null;
    }

    /// <summary>Where a span of rows (or columns) goes; null when it went whole.</summary>
    public (int First, int Last)? Span(int first, int last)
    {
        if (first > last)
        {
            (first, last) = (last, first);
        }

        var reachesEnd = last == Max;
        if (!Delete)
        {
            if (first >= At)
            {
                first += Count;
            }

            if (last >= At)
            {
                last += Count;
            }

            if (first > Max)
            {
                return null;
            }

            return (first, Math.Min(last, Max));
        }

        if (first >= At &&
            last <= Last)
        {
            return null;
        }

        var newFirst = first;
        if (first > Last)
        {
            newFirst = first - Count;
        }
        else if (first >= At)
        {
            newFirst = At;
        }

        var newLast = last;
        if (reachesEnd)
        {
            newLast = Max;
        }
        else if (last > Last)
        {
            newLast = last - Count;
        }
        else if (last >= At)
        {
            newLast = At - 1;
        }

        return (newFirst, newLast);
    }

    /// <summary>Where a range goes; null when it went whole.</summary>
    public SheetRange? Range(SheetRange range)
    {
        if (Rows)
        {
            if (Span(range.FirstRow, range.LastRow) is not { } rowSpan)
            {
                return null;
            }

            return range with
            {
                FirstRow = rowSpan.First,
                LastRow = rowSpan.Last
            };
        }

        if (Span(range.FirstColumn, range.LastColumn) is not { } columnSpan)
        {
            return null;
        }

        return range with
        {
            FirstColumn = columnSpan.First,
            LastColumn = columnSpan.Last
        };
    }

    /// <summary>
    /// One reference with no sheet — <c>B2</c>, <c>A1:C9</c>, <c>A:A</c>, <c>3:5</c> — where it goes, or
    /// null when it went whole. Text that is no reference is left as it is.
    /// </summary>
    public string? Reference(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            FormulaReference.Parse(null, text) is not { } reference)
        {
            return text;
        }

        var moved = Adjust(reference);
        if (moved.Shape == ReferenceShape.Invalid)
        {
            return null;
        }

        if (moved == reference)
        {
            return text;
        }

        return moved.Write();
    }

    /// <summary>
    /// A list of references, as a validation's or a conditional format's <c>sqref</c> spells it, with each
    /// moved and those that went whole left out; null when none is left.
    /// </summary>
    public string? Ranges(string? sqref)
    {
        if (string.IsNullOrWhiteSpace(sqref))
        {
            return sqref;
        }

        var kept = new List<string>();
        foreach (var part in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Reference(part) is { } moved)
            {
                kept.Add(moved);
            }
        }

        if (kept.Count == 0)
        {
            return null;
        }

        return string.Join(' ', kept);
    }

    /// <summary>A reference to the sheet, moved: <see cref="ReferenceShape.Invalid"/> when it went whole.</summary>
    public FormulaReference Adjust(FormulaReference reference)
    {
        if (reference.Shape == ReferenceShape.Invalid ||
            (Rows && reference.Shape == ReferenceShape.Columns) ||
            (!Rows && reference.Shape == ReferenceShape.Rows))
        {
            return reference;
        }

        var cell = reference.Shape == ReferenceShape.Cell;
        if (Rows)
        {
            if (reference.FirstRow > reference.LastRow)
            {
                reference = reference with
                {
                    FirstRow = reference.LastRow,
                    LastRow = reference.FirstRow,
                    FirstRowAbsolute = reference.LastRowAbsolute,
                    LastRowAbsolute = reference.FirstRowAbsolute
                };
            }

            if (OverBody(reference))
            {
                return reference with
                {
                    LastRow = Math.Min(reference.LastRow + Count, Max)
                };
            }

            if (Moved(reference.FirstRow, reference.LastRow, cell) is not { } rowSpan)
            {
                return reference with
                {
                    Shape = ReferenceShape.Invalid
                };
            }

            return reference with
            {
                FirstRow = rowSpan.First,
                LastRow = rowSpan.Last
            };
        }

        if (reference.FirstColumn > reference.LastColumn)
        {
            reference = reference with
            {
                FirstColumn = reference.LastColumn,
                LastColumn = reference.FirstColumn,
                FirstColumnAbsolute = reference.LastColumnAbsolute,
                LastColumnAbsolute = reference.FirstColumnAbsolute
            };
        }

        if (Moved(reference.FirstColumn, reference.LastColumn, cell) is not { } columnSpan)
        {
            return reference with
            {
                Shape = ReferenceShape.Invalid
            };
        }

        return reference with
        {
            FirstColumn = columnSpan.First,
            LastColumn = columnSpan.Last
        };
    }

    bool OverBody(FormulaReference reference)
    {
        if (Delete ||
            reference.Shape != ReferenceShape.Area)
        {
            return false;
        }

        foreach (var body in Bodies)
        {
            if (body.FirstRow == reference.FirstRow &&
                body.LastRow == reference.LastRow &&
                reference.FirstColumn >= body.FirstColumn &&
                reference.LastColumn <= body.LastColumn)
            {
                return true;
            }
        }

        return false;
    }

    // A single cell moves as a cell does — off the sheet when pushed past its edge — and a range as a span.
    (int First, int Last)? Moved(int first, int last, bool cell)
    {
        if (!cell)
        {
            return Span(first, last);
        }

        if (Index(first) is not { } index)
        {
            return null;
        }

        return (index, index);
    }

    /// <summary>
    /// A formula's text with its references to the sheet moved, and its structured references to a table
    /// or a column that was deleted made <c>#REF!</c>. Only those are rewritten; the rest of the text stays
    /// as it was written. A formula that cannot be read is left as it is.
    /// </summary>
    /// <param name="formula">The formula, without its <c>=</c>.</param>
    /// <param name="ownSheet">The sheet the formula is on, which its references without a sheet's name
    /// name; null for a defined name's, which name their sheets.</param>
    /// <param name="ownTable">The table a structured reference without a table's name, <c>[@Price]</c>,
    /// stands in.</param>
    public string Formula(string formula, string? ownSheet, string? ownTable = null)
    {
        if (string.IsNullOrEmpty(formula))
        {
            return formula;
        }

        List<FormulaToken> tokens;
        try
        {
            tokens = FormulaLexer.Tokenize(formula);
        }
        catch (FormulaSyntaxException)
        {
            return formula;
        }

        StringBuilder? builder = null;
        var copied = 0;
        foreach (var token in tokens)
        {
            string replacement;
            int start;
            if (token is {Kind: FormulaTokenKind.Reference, Reference: { } reference})
            {
                var named = token.Sheet ?? ownSheet;
                if (named == null ||
                    !string.Equals(named, Sheet, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var moved = Adjust(reference);
                if (moved == reference)
                {
                    continue;
                }

                replacement = moved.Write();
                start = token.ReferenceStart;
            }
            else if (token.Kind == FormulaTokenKind.Structured &&
                     Lost.Count > 0 &&
                     Gone(token, ownTable))
            {
                replacement = "#REF!";
                start = token.Start;
            }
            else
            {
                continue;
            }

            builder ??= new(formula.Length + 8);
            builder.Append(formula, copied, start - copied);
            builder.Append(replacement);
            copied = token.Start + token.Length;
        }

        if (builder == null)
        {
            return formula;
        }

        builder.Append(formula, copied, formula.Length - copied);
        return builder.ToString();
    }

    // Whether a structured reference names a table deleted, or one of the columns deleted from it.
    bool Gone(FormulaToken token, string? ownTable)
    {
        var table = token.Sheet ?? ownTable;
        if (table == null ||
            !Lost.TryGetValue(table, out var columns))
        {
            return false;
        }

        if (columns == null)
        {
            return true;
        }

        StructuredSpecifier specifier;
        try
        {
            specifier = StructuredSpecifier.Parse(token.Text[(token.Sheet?.Length ?? 0)..]);
        }
        catch (FormulaSyntaxException)
        {
            return false;
        }

        return (specifier.FirstColumn is { } first && columns.Contains(first)) ||
               (specifier.LastColumn is { } last && columns.Contains(last));
    }
}
