/// <summary>
/// Two edits that cannot both be kept: both changed one paragraph, or one deleted what the other
/// changed. The merge refuses the edit whole, since an edit that changed two paragraphs (a join, a
/// split) is only right with both.
/// </summary>
sealed class MergeConflict(string reason, string? paragraph = null) : Exception(reason)
{
    /// <summary>The id of the paragraph both changed, where there is one.</summary>
    public string? Paragraph { get; } = paragraph;
}
