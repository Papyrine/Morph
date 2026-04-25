/// <summary>
/// Source-rectangle crop for an image (a:srcRect). Each value is the fraction (0..1)
/// of the source image to trim from that edge before drawing. Default zero = no crop.
/// </summary>
sealed record ImageCrop
{
    public double Left { get; init; }
    public double Top { get; init; }
    public double Right { get; init; }
    public double Bottom { get; init; }

    public bool IsCropped => Left > 0 || Top > 0 || Right > 0 || Bottom > 0;

    public static ImageCrop None { get; } = new();
}
