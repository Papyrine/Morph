/// <summary>
/// An SVG picture as a flat list of paths to fill and stroke, back to front — what
/// <see cref="SvgReader"/> makes of a document, and what a painter draws with its own vector calls
/// instead of handing the markup to an SVG library. Office stores an icon or an illustration twice,
/// as the SVG and as a PNG it rendered from it; drawing the SVG is drawing the original, sharp at
/// any size, where the PNG is a copy at one size (and, in a file another tool wrote, not always a
/// copy of the same thing).
///
/// The frame — <see cref="X"/>, <see cref="Y"/>, <see cref="Width"/>, <see cref="Height"/> — is the
/// document's <c>viewBox</c>, and every path is already in its coordinates: no transform, style or
/// reference is left to resolve. The frame is stretched over the picture's box, as the PNG would
/// be, and nothing outside it is drawn.
/// </summary>
sealed class SvgDrawing(double x, double y, double width, double height, IReadOnlyList<SvgPath> paths)
{
    /// <summary>The media type a picture part carries when it is one of these.</summary>
    public const string ContentType = "image/svg+xml";

    // One reading per part: a picture part's bytes are one array for the whole parse, however many
    // pictures point at it and however many pages draw them. Weakly keyed, so the reading goes
    // when the parsed document does, and a document read concurrently shares it.
    static readonly ConditionalWeakTable<byte[], SvgReadResult> readings = [];

    public double X { get; } = x;

    public double Y { get; } = y;

    public double Width { get; } = width;

    public double Height { get; } = height;

    public IReadOnlyList<SvgPath> Paths { get; } = paths;

    /// <summary>
    /// The drawing for a picture's bytes, or null when they are not SVG, or are an SVG that uses
    /// something <see cref="SvgReader"/> does not draw — a caller then falls back to the picture's
    /// raster twin, exactly as it did before there was a reader.
    /// </summary>
    public static SvgDrawing? For(byte[]? data, string? contentType)
    {
        if (contentType != ContentType || data is not {Length: > 0})
        {
            return null;
        }

        return readings.GetValue(data, SvgReader.Read).Drawing;
    }

    /// <summary>
    /// Where the drawing lands in a picture's box: its frame stretched over the box, or — through an
    /// <c>a:srcRect</c> crop — over the larger rectangle that puts the cropped part on the box
    /// (<see cref="ImageCrop.Expand"/>; a negative crop gives a smaller one inside it).
    /// </summary>
    public SvgPlacement Place(double boxX, double boxY, double boxWidth, double boxHeight, ImageCrop? crop)
    {
        var (frameX, frameY, frameWidth, frameHeight) = crop?.Expand(boxX, boxY, boxWidth, boxHeight) ?? (boxX, boxY, boxWidth, boxHeight);
        var scaleX = frameWidth / Width;
        var scaleY = frameHeight / Height;
        return new(
            scaleX,
            scaleY,
            frameX - X * scaleX,
            frameY - Y * scaleY,
            Math.Max(boxX, frameX),
            Math.Max(boxY, frameY),
            Math.Min(boxX + boxWidth, frameX + frameWidth),
            Math.Min(boxY + boxHeight, frameY + frameHeight));
    }

    /// <summary>
    /// The drawing with Word's Recolor transform (a:duotone, a:grayscl, a:lum) applied to every
    /// colour in it. A recolour is the same affine map of every pixel's channels, and a drawing is
    /// flat colours, so mapping the colours is the same picture a pixel filter would give — and it
    /// keeps the recoloured picture a vector one. Null leaves the drawing as it is.
    /// </summary>
    public SvgDrawing Recolored(ImageRecolor? recolor)
    {
        if (recolor == null)
        {
            return this;
        }

        var (red, green, blue) = recolor.Rows();
        var recolored = new SvgPath[Paths.Count];
        for (var index = 0; index < recolored.Length; index++)
        {
            var path = Paths[index];
            recolored[index] = path with
            {
                FillColorHex = path.FillColorHex == null ? null : Map(path.FillColorHex),
                Stroke = path.Stroke == null ? null : path.Stroke with {ColorHex = Map(path.Stroke.ColorHex)}
            };
        }

        return new(X, Y, Width, Height, recolored);

        string Map(string hex)
        {
            if (!hex.TryParse(out var sourceRed, out var sourceGreen, out var sourceBlue))
            {
                return hex;
            }

            return HexColor.ToHex(Channel(red), Channel(green), Channel(blue));

            byte Channel(ImageRecolor.Row row) =>
                (byte) Math.Round(Math.Clamp(row.Red * sourceRed / 255f + row.Green * sourceGreen / 255f + row.Blue * sourceBlue / 255f + row.Offset, 0, 1) * 255);
        }
    }
}
