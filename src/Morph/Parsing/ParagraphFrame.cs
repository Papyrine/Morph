/// <summary>
/// Text-frame positioning parsed from <c>w:framePr</c> (the positioning subset — anchors,
/// alignment, explicit offsets, and size). Drop-cap-only frames (which carry just
/// <c>w:dropCap</c>/<c>w:lines</c>) do not produce a <see cref="ParagraphFrame"/>; those stay on
/// <see cref="ParagraphProperties.DropCap"/>.
///
/// A record so consecutive framed paragraphs can be grouped by value equality: paragraphs whose
/// frames compare equal belong to the same floating text frame and are pulled out of flow together.
/// </summary>
sealed record ParagraphFrame
{
    /// <summary>What the horizontal position is relative to (<c>w:hAnchor</c>).</summary>
    public HorizontalAnchor HorizontalAnchor { get; init; } = HorizontalAnchor.Column;

    /// <summary>What the vertical position is relative to (<c>w:vAnchor</c>).</summary>
    public VerticalAnchor VerticalAnchor { get; init; } = VerticalAnchor.Margin;

    /// <summary>Horizontal alignment within the anchor (<c>w:xAlign</c>).</summary>
    public FrameHorizontalAlignment HorizontalAlignment { get; init; } = FrameHorizontalAlignment.None;

    /// <summary>Vertical alignment within the anchor (<c>w:yAlign</c>).</summary>
    public FrameVerticalAlignment VerticalAlignment { get; init; } = FrameVerticalAlignment.None;

    /// <summary>Explicit horizontal offset in points (<c>w:x</c>). Used when
    /// <see cref="HorizontalAlignment"/> is <see cref="FrameHorizontalAlignment.None"/>.</summary>
    public double XPoints { get; init; }

    /// <summary>Explicit vertical offset in points (<c>w:y</c>). Used when
    /// <see cref="VerticalAlignment"/> is <see cref="FrameVerticalAlignment.None"/>.</summary>
    public double YPoints { get; init; }

    /// <summary>Explicit frame width in points (<c>w:w</c>). Null = size to content.</summary>
    public double? WidthPoints { get; init; }

    /// <summary>Explicit frame height in points (<c>w:h</c>). Null = size to content.</summary>
    public double? HeightPoints { get; init; }
}
