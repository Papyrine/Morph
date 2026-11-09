/// <summary>
/// Internal class to store numbering level definitions.
/// </summary>
sealed class NumberingLevelDefinition
{
    public string LevelText { get; init; } = "";
    public string? FontFamily { get; init; }
    public double? FontSizePoints { get; init; }
    public string? ColorHex { get; init; }
    public double LeftIndentPoints { get; init; }
    public double HangingIndentPoints { get; init; }
    public bool IsBullet { get; init; }
    public int StartNumber { get; init; } = 1;
    public NumberFormatValues NumberFormat { get; init; } = NumberFormatValues.Decimal;

    /// <summary><c>w:lvlJc="right"</c> — the marker right-aligns at the number position
    /// (see <see cref="NumberingInfo.MarkerRightAligned"/> for the probe law).</summary>
    public bool RightAligned { get; init; }

    public ListMarkerSuffix Suffix { get; init; }

    /// <summary>
    /// OOXML <c>w:lvlRestart</c>. <c>null</c> = default behaviour (restart whenever any
    /// shallower level increments). <c>0</c> = never restart. A positive value <c>K</c>
    /// means restart only when an item at level <c>K-1</c> or shallower increments.
    /// </summary>
    public int? LevelRestart { get; init; }
}
