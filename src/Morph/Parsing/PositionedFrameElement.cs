/// <summary>
/// A run of consecutive <c>w:framePr</c>-positioned paragraphs pulled out of normal flow into a
/// single floating text frame (Word's text-frame feature). The renderers measure the
/// <see cref="Content"/>, resolve a position from the anchor + alignment (or explicit offset), then
/// draw the paragraphs there without advancing the body cursor.
/// </summary>
sealed class PositionedFrameElement : DocumentElement
{
    /// <summary>The paragraphs (and any other block content) hosted by the frame.</summary>
    public required IReadOnlyList<DocumentElement> Content { get; init; }

    /// <summary>What the horizontal position is relative to.</summary>
    public required HorizontalAnchor HorizontalAnchor { get; init; }

    /// <summary>What the vertical position is relative to.</summary>
    public required VerticalAnchor VerticalAnchor { get; init; }

    /// <summary>Horizontal alignment within the anchor reference.</summary>
    public FrameHorizontalAlignment HorizontalAlignment { get; init; }

    /// <summary>Vertical alignment within the anchor reference.</summary>
    public FrameVerticalAlignment VerticalAlignment { get; init; }

    /// <summary>Explicit horizontal offset in points (used when
    /// <see cref="HorizontalAlignment"/> is <see cref="FrameHorizontalAlignment.None"/>).</summary>
    public double XPoints { get; init; }

    /// <summary>Explicit vertical offset in points (used when
    /// <see cref="VerticalAlignment"/> is <see cref="FrameVerticalAlignment.None"/>).</summary>
    public double YPoints { get; init; }

    /// <summary>Explicit frame width in points. Null = size to the widest content line.</summary>
    public double? WidthPoints { get; init; }

    /// <summary>Explicit frame height in points. Null = size to the content's measured height.</summary>
    public double? HeightPoints { get; init; }
}
