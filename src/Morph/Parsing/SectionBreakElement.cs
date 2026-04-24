/// <summary>
/// Represents a section break with various types.
/// </summary>
sealed class SectionBreakElement : DocumentElement
{
    public required SectionBreakType BreakType { get; init; }

    /// <summary>
    /// Optional new section properties (page size, margins, columns, etc.)
    /// </summary>
    public PageSettings? NewSectionSettings { get; init; }
}