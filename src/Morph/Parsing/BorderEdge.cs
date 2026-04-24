/// <summary>
/// Represents a single border edge (top, right, bottom, or left).
/// </summary>
sealed record BorderEdge
{
    /// <summary>Whether this border edge should be rendered.</summary>
    public bool IsVisible { get; init; }

    /// <summary>Border width in points.</summary>
    public double WidthPoints { get; init; } = 0.5;

    /// <summary>Border color as hex string (e.g., "000000").</summary>
    public string? ColorHex { get; init; } = "000000";

    public static BorderEdge None => new() { IsVisible = false };
    public static BorderEdge Default => new() { IsVisible = true, WidthPoints = 0.5, ColorHex = "000000" };
}