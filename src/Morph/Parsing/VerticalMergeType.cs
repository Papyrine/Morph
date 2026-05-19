/// <summary>
/// Vertical merge state for table cells.
/// </summary>
enum VerticalMergeType
{
    /// <summary>Cell is not part of a vertical merge.</summary>
    None,
    /// <summary>Cell starts a vertical merge (spans downward).</summary>
    Restart,
    /// <summary>Cell continues a vertical merge from above (should not be rendered separately).</summary>
    Continue
}