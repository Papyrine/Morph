/// <summary>
/// Positioning information extracted from an anchor element.
/// </summary>
internal readonly struct AnchorPositioning
{
    public double HorizontalPositionPoints { get; init; }
    public double VerticalPositionPoints { get; init; }
    public HorizontalAnchor HorizontalAnchor { get; init; }
    public VerticalAnchor VerticalAnchor { get; init; }
    public bool BehindText { get; init; }
    public double? WidthPercent { get; init; }
    public SizeRelativeFrom WidthRelativeFrom { get; init; }
    public double? HeightPercent { get; init; }
    public SizeRelativeFrom HeightRelativeFrom { get; init; }
}
