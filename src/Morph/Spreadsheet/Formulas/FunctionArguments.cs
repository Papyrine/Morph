/// <summary>
/// A function's arguments, evaluated only when the function asks for one — IF and IFERROR need only
/// some of theirs, and an argument they never read can neither fail nor make the result volatile —
/// and evaluated once however often it is asked for.
/// </summary>
sealed class FunctionArguments(FormulaEvaluator evaluator, IReadOnlyList<FormulaNode> nodes)
{
    readonly FormulaValue?[] values = new FormulaValue?[nodes.Count];

    public FormulaEvaluator Evaluator { get; } = evaluator;

    public int Count => nodes.Count;

    public FormulaNode Node(int index) => nodes[index];

    /// <summary>Whether the argument was left out, or not given at all.</summary>
    public bool IsMissing(int index) =>
        index >= nodes.Count ||
        nodes[index] is MissingNode;

    /// <summary>The argument as it evaluated: a reference stays a reference.</summary>
    public FormulaValue Raw(int index)
    {
        if (index >= nodes.Count)
        {
            return FormulaValue.Blank;
        }

        return values[index] ??= Evaluator.Evaluate(nodes[index]);
    }

    /// <summary>The argument as one value.</summary>
    public FormulaValue Scalar(int index) =>
        Evaluator.Scalar(Raw(index));

    /// <summary>The argument as a number, or the error that stops it being one.</summary>
    public FormulaValue Number(int index) =>
        FormulaCoercion.ToNumber(Scalar(index));

    /// <summary>The argument as a number; a left-out argument is <paramref name="fallback"/>.</summary>
    public FormulaValue Number(int index, double fallback)
    {
        if (IsMissing(index))
        {
            return FormulaValue.FromNumber(fallback);
        }

        return Number(index);
    }

    public FormulaValue Text(int index) =>
        FormulaCoercion.ToText(Scalar(index));

    public FormulaValue Boolean(int index) =>
        FormulaCoercion.ToBoolean(Scalar(index));

    public FormulaValue Boolean(int index, bool fallback)
    {
        if (IsMissing(index))
        {
            return FormulaValue.FromBoolean(fallback);
        }

        return Boolean(index);
    }

    /// <summary>The argument as an array: a range read whole.</summary>
    public FormulaValue[,] Array(int index) =>
        Evaluator.ToArray(Raw(index));

    /// <summary>The argument's area, when it is a reference.</summary>
    public FormulaArea? Area(int index)
    {
        var raw = Raw(index);
        if (raw.IsReference)
        {
            return raw.Area;
        }

        return null;
    }
}
