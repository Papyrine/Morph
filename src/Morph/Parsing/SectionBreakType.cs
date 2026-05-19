/// <summary>
/// Types of section breaks.
/// </summary>
enum SectionBreakType
{
    /// <summary>Starts new section on the next page.</summary>
    NextPage,

    /// <summary>Starts new section on the same page (continuous).</summary>
    Continuous,

    /// <summary>Starts new section on the next even-numbered page.</summary>
    EvenPage,

    /// <summary>Starts new section on the next odd-numbered page.</summary>
    OddPage,

    /// <summary>Starts new section in the next column (for multi-column layouts).</summary>
    NextColumn
}