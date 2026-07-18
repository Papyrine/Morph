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

    /// <summary>
    /// <c>wp:anchor@relativeHeight</c> — the drawing's position in Word's single z-space. Every
    /// floating drawing draws in ascending order of this value (a shape can sit entirely beneath
    /// a sibling anchored later in the document); behind-text vs in-front is a separate flag.
    /// </summary>
    public uint RelativeHeight { get; init; }
    public double? WidthPercent { get; init; }
    public SizeRelativeFrom WidthRelativeFrom { get; init; }
    public double? HeightPercent { get; init; }
    public SizeRelativeFrom HeightRelativeFrom { get; init; }

    /// <summary>
    /// Horizontal position as a fraction (0..1) of the anchor's reference frame, parsed from
    /// <c>wp14:pctPosHOffset</c>. Null when no percentage positioning is present.
    /// When set, the renderer overrides <see cref="HorizontalPositionPoints"/>.
    /// </summary>
    public double? HorizontalPositionPercent { get; init; }

    /// <summary>
    /// Vertical position as a fraction (0..1) of the anchor's reference frame, parsed from
    /// <c>wp14:pctPosVOffset</c>. Null when no percentage positioning is present.
    /// </summary>
    public double? VerticalPositionPercent { get; init; }
}
