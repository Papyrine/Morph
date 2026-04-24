/// <summary>
/// Represents a paragraph in the document.
/// </summary>
sealed class ParagraphElement : DocumentElement
{
    public required IReadOnlyList<Run> Runs { get; init; }
    public ParagraphProperties Properties { get; init; } = new();
}