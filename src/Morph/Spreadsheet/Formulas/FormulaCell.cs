/// <summary>
/// A cell as the calculation sees it: its value, and the formula it is worked out from if it has one.
/// <see cref="Cached"/> is what the file held, which a formula the engine cannot work out keeps.
/// </summary>
sealed class FormulaCell(int row, int column)
{
    public int Row { get; } = row;

    public int Column { get; } = column;

    public FormulaValue Value { get; set; }

    /// <summary>The value the file held for the cell when it was read.</summary>
    public FormulaValue Cached { get; set; }

    /// <summary>The formula's text without its <c>=</c>, a shared formula's moved to this cell.</summary>
    public string? Formula { get; set; }

    /// <summary>An array formula's cells (<c>t="array"</c>), this cell their first.</summary>
    public SheetRange? ArrayRange { get; set; }

    /// <summary>A formula the engine leaves alone: a data table's, or one in a block an array formula fills.</summary>
    public bool Fixed { get; set; }

    internal FormulaNode? Node { get; set; }

    internal CellState State { get; set; }

    /// <summary>Whether the formula used a function the engine has not got, so its value is the file's.</summary>
    public bool Unsupported { get; set; }

    /// <summary>Whether the value rests on TODAY, NOW, RAND and the like, here or in a cell it reads.</summary>
    public bool Volatile { get; set; }

    /// <summary>Whether the formula is a SUBTOTAL or AGGREGATE, which SUBTOTAL leaves out of its ranges.</summary>
    internal bool Subtotal { get; set; }

    internal List<FormulaCell>? Precedents { get; set; }

    /// <summary>The cell's element in the sheet it was read from, which the results are written back to.</summary>
    public DocumentFormat.OpenXml.Spreadsheet.Cell? Element { get; set; }

    public bool HasFormula => Formula != null;
}

enum CellState
{
    Pending,
    Computing,
    Computed
}
