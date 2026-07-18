/// <summary>
/// Source-rectangle crop for an image (a:srcRect). Each value is the fraction of the source
/// image to trim from that edge before drawing; positive values crop, negative values pad —
/// the picture shrinks inside its frame with empty space on that edge (Word's arrow icons are
/// a landscape PNG in a tall frame via <c>t="-9453" b="-175911"</c>). Default zero = no crop.
/// </summary>
sealed record ImageCrop
{
    public double Left { get; init; }
    public double Top { get; init; }
    public double Right { get; init; }
    public double Bottom { get; init; }

    public bool IsCropped => Left != 0 || Top != 0 || Right != 0 || Bottom != 0;

    /// <summary>
    /// True when any edge is negative (padding). Backends whose crop fast path is a
    /// source-rectangle API route these through <see cref="Expand"/> + clip instead — a source
    /// rect can't extend beyond the bitmap.
    /// </summary>
    public bool HasPadding => Left < 0 || Top < 0 || Right < 0 || Bottom < 0;

    public static ImageCrop None { get; } = new();

    /// <summary>
    /// The rectangle the whole image must occupy for its cropped sub-rectangle to land exactly on
    /// the supplied box. Backends with no source-rectangle API (PDFsharp, SVG <c>&lt;image&gt;</c>)
    /// draw the image at this enlarged rectangle and clip back to the box; padding (negative)
    /// values produce a rectangle inside the box, the letterboxed placement Word renders. Returns
    /// the box unchanged when there is no crop, or when the crop leaves nothing visible.
    /// </summary>
    public (double X, double Y, double Width, double Height) Expand(double x, double y, double width, double height)
    {
        var visibleWidth = 1 - Left - Right;
        var visibleHeight = 1 - Top - Bottom;
        if (!IsCropped || visibleWidth <= 0 || visibleHeight <= 0)
        {
            return (x, y, width, height);
        }

        var fullWidth = width / visibleWidth;
        var fullHeight = height / visibleHeight;
        return (x - Left * fullWidth, y - Top * fullHeight, fullWidth, fullHeight);
    }
}
