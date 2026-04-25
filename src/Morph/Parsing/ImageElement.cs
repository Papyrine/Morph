/// <summary>
/// Represents an inline image.
/// </summary>
sealed class ImageElement : DocumentElement
{
    public required byte[] ImageData { get; init; }
    public required double WidthPoints { get; init; }
    public required double HeightPoints { get; init; }
    public string? ContentType { get; init; }

    /// <summary>Rotation in degrees (clockwise). 0 means no rotation.</summary>
    public double RotationDegrees { get; init; }
}