/// <summary>
/// Cell-level properties.
/// </summary>
sealed record TableCellProperties
{
    public double? WidthPoints { get; init; }
    public string? BackgroundColorHex { get; init; }

    /// <summary>Cell padding (inset from border to content). Null means use table default.</summary>
    public CellSpacing? Padding { get; init; }

    /// <summary>Cell margin (space outside the border). Null means use table default.</summary>
    public CellSpacing? Margin { get; init; }

    /// <summary>Per-edge border specifications. Null means use table default borders.</summary>
    public CellBorders? Borders { get; init; }

    /// <summary>Number of grid columns this cell spans. Default is 1.</summary>
    public int GridSpan { get; init; } = 1;

    /// <summary>Vertical alignment of content within the cell. Default is Top.</summary>
    public CellVerticalAlignment VerticalAlignment { get; init; } = CellVerticalAlignment.Top;

    /// <summary>Vertical merge state for this cell. Default is None.</summary>
    public VerticalMergeType VerticalMerge { get; init; } = VerticalMergeType.None;
}