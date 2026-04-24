/// <summary>
/// Represents a floating/positioned text box (shape with text content).
/// </summary>
sealed class FloatingTextBoxElement : DocumentElement
{
    /// <summary>Text content of the text box.</summary>
    public required IReadOnlyList<DocumentElement> Content { get; init; }

    /// <summary>Width in points.</summary>
    public required double WidthPoints { get; init; }

    /// <summary>Height in points.</summary>
    public required double HeightPoints { get; init; }

    /// <summary>Horizontal position in points from the anchor reference.</summary>
    public double HorizontalPositionPoints { get; init; }

    /// <summary>Vertical position in points from the anchor reference.</summary>
    public double VerticalPositionPoints { get; init; }

    /// <summary>What the horizontal position is relative to.</summary>
    public HorizontalAnchor HorizontalAnchor { get; init; } = HorizontalAnchor.Column;

    /// <summary>What the vertical position is relative to.</summary>
    public VerticalAnchor VerticalAnchor { get; init; } = VerticalAnchor.Paragraph;

    /// <summary>How text wraps around this text box.</summary>
    public WrapType WrapType { get; init; } = WrapType.None;

    /// <summary>Whether this text box is behind text (vs in front).</summary>
    public bool BehindText { get; init; }

    /// <summary>Background color (hex). Null for transparent.</summary>
    public string? BackgroundColorHex { get; init; }

    /// <summary>Rotation in degrees (clockwise). 0 = no rotation.</summary>
    public double RotationDegrees { get; init; }
}