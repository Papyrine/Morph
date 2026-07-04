/// <summary>A node in a list-nesting forest produced by <see cref="DocumentExportHelpers.BuildListForest"/>.</summary>
sealed class ListNode
{
    public required ParagraphElement Paragraph { get; init; }
    public int? Level { get; init; }
    public double Indent { get; init; }
    public bool Ordered { get; init; }
    public List<ListNode> Children { get; } = [];
}