/// <summary>
/// Represents a single ink stroke (trace).
/// </summary>
sealed class InkStroke
{
    /// <summary>Points that make up this stroke.</summary>
    public required IReadOnlyList<InkPoint> Points { get; init; }

    /// <summary>Stroke color in hex format.</summary>
    public string ColorHex { get; init; } = "000000";

    /// <summary>Stroke width in points.</summary>
    public double WidthPoints { get; init; } = 1.5;

    /// <summary>Stroke transparency (0 = opaque, 255 = fully transparent).</summary>
    public byte Transparency { get; init; }

    /// <summary>Pen tip shape.</summary>
    public InkPenTip PenTip { get; init; } = InkPenTip.Ellipse;

    /// <summary>Whether the stroke represents a highlighter (semi-transparent).</summary>
    public bool IsHighlighter { get; init; }
}