/// <summary>
/// One of the engine's functions: how many arguments it takes, and whether it is volatile (worked out
/// afresh whenever anything is, as TODAY is) or takes arrays (its arguments' operations over ranges
/// worked cell by cell, as SUMPRODUCT's are).
/// </summary>
sealed record FormulaFunction(string Name, int Least, int Most, Func<FunctionArguments, FormulaValue> Invoke)
{
    public bool Volatile { get; init; }

    public bool TakesArrays { get; init; }
}
