/// <summary>A formula read into a tree (<see cref="FormulaParser"/>), which <see cref="FormulaEvaluator"/> evaluates.</summary>
abstract record FormulaNode;

sealed record NumberNode(double Value) : FormulaNode;

sealed record TextNode(string Value) : FormulaNode;

sealed record BooleanNode(bool Value) : FormulaNode;

sealed record ErrorNode(FormulaError Error) : FormulaNode;

sealed record ReferenceNode(FormulaReference Reference) : FormulaNode;

/// <summary>A defined name, which may be scoped to a sheet: <c>TaxRate</c>, <c>Sheet1!Total</c>.</summary>
sealed record NameNode(string? Sheet, string Name) : FormulaNode;

/// <summary>A table's structured reference. An empty table name is the table the formula is in.</summary>
sealed record StructuredNode(string Table, StructuredSpecifier Specifier) : FormulaNode;

/// <summary>Negation, a unary plus, or <c>@</c>, Excel's implicit intersection.</summary>
sealed record UnaryNode(string Operator, FormulaNode Operand) : FormulaNode;

sealed record PercentNode(FormulaNode Operand) : FormulaNode;

sealed record BinaryNode(string Operator, FormulaNode Left, FormulaNode Right) : FormulaNode;

/// <summary>A function, by its name in capitals with Excel's <c>_xlfn.</c> prefix taken off.</summary>
sealed record CallNode(string Name, IReadOnlyList<FormulaNode> Arguments) : FormulaNode;

sealed record ArrayNode(FormulaValue[,] Values) : FormulaNode;

/// <summary>An argument left out: the second of <c>IF(A1,,2)</c>.</summary>
sealed record MissingNode : FormulaNode
{
    public static MissingNode Instance { get; } = new();
}
