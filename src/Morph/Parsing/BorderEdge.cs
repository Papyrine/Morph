/// <summary>
/// Represents a single border edge (top, right, bottom, or left).
/// </summary>
sealed record BorderEdge
{
    /// <summary>Whether this border edge should be rendered.</summary>
    public bool IsVisible { get; init; }

    /// <summary>Border width in points. For <c>Double</c>, this is the total width
    /// (both lines + gap) per OOXML; renderers split into two thinner lines.</summary>
    public double WidthPoints { get; init; } = 0.5;

    /// <summary>Border color as hex string (e.g., "000000").</summary>
    public string? ColorHex { get; init; } = "000000";

    /// <summary>Line style — single (default), double, dotted, dashed.</summary>
    public BorderLineStyle Style { get; init; } = BorderLineStyle.Single;

    public static BorderEdge None => new()
    {
        IsVisible = false
    };

    public static BorderEdge Default => new()
    {
        IsVisible = true,
        WidthPoints = 0.5,
        ColorHex = "000000"
    };
}

enum BorderLineStyle
{
    Single,
    Double,
    Dotted,
    Dashed
}
