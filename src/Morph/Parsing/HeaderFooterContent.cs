/// <summary>
/// Content for a header or footer.
/// </summary>
sealed class HeaderFooterContent
{
    public required IReadOnlyList<DocumentElement> Elements { get; init; }
}