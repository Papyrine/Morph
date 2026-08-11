/// <summary>
/// Every raster image a deck actually draws, paired with the size it is drawn at.
///
/// The drawn size comes from Morph's own parse rather than a second reading of the OOXML, because
/// getting it right means resolving placeholder inheritance, composing nested
/// <c>p:grpSp</c> transforms (a group scales its children by <c>ext/chExt</c>) and undoing
/// <c>a:srcRect</c> — a fill showing 40% of an image across 500px needs 1250px of source, not 500.
/// <see cref="SlideShapeParser"/> already does all of that, and a private copy here would be a
/// second implementation to keep in step with the first.
///
/// That parse flattens groups: <c>SlideShapeParser.Walk</c> recurses into each <c>p:grpSp</c> and
/// appends its children to the same list with the composed transform already applied, so a deck's
/// <see cref="ParsedDocument.Elements"/> is flat and no recursive descent is needed here.
/// </summary>
static class DeckImages
{
    /// <summary>
    /// One drawn image. Sizes are in pixels at the DPI the deck corpus renders and compares at
    /// (<see cref="ScenarioInputs.Dpi"/>), which is the resolution its stored pixels are ultimately
    /// judged against.
    /// </summary>
    internal readonly record struct DrawnImage(
        string Description,
        int PixelWidth,
        int PixelHeight,
        double DrawnWidth,
        double DrawnHeight,
        int Bytes)
    {
        /// <summary>
        /// How many times more pixels the image stores than the box it is drawn in can resolve.
        /// 1.0 means it is stored at exactly its drawn size; 2.0 means it carries four times the
        /// pixels that will ever be seen.
        ///
        /// The binding axis is the SMALLER of the two ratios, not the larger: an image must still
        /// cover its box on both axes, so the axis with the least slack is the one that decides how
        /// far it could be shrunk. Taking the larger would report a 3000x100 banner drawn 1000x100
        /// as 3x oversampled when its height has no slack at all.
        /// </summary>
        public double Oversample => Math.Min(PixelWidth / DrawnWidth, PixelHeight / DrawnHeight);

        /// <summary>
        /// Stored bits per pixel the image can ever draw. This is the measure that matters for
        /// corpus weight, because it folds both ways an image wastes space into one number: too
        /// many pixels for its box, and too many bits for each of those pixels.
        /// </summary>
        public double BitsPerDrawnPixel => Bytes * 8 / (DrawnWidth * DrawnHeight);

        /// <summary>
        /// Identity for the over-budget allow-list, unique within a deck and deliberately including
        /// the byte count: re-encoding an image changes its key, so an entry cannot silently go on
        /// covering an image that has since been dealt with.
        /// </summary>
        public string Key => $"{PixelWidth}x{PixelHeight}/{Bytes}";
    }

    /// <summary>Every raster image drawn by the deck at <paramref name="pptxPath"/>.</summary>
    public static IEnumerable<DrawnImage> Drawn(string pptxPath)
    {
        var deck = new PowerPointDocument(pptxPath);

        foreach (var element in deck.Document.Elements)
        {
            var drawn = element switch
            {
                FloatingImageElement image => Measure(
                    image.ImageData, image.ContentType, image.Description,
                    image.WidthPoints, image.HeightPoints, image.Crop),
                FloatingShapeElement { ImageData.Length: > 0 } shape => Measure(
                    shape.ImageData, shape.ImageContentType, null,
                    shape.WidthPoints, shape.HeightPoints, null),
                _ => null
            };

            if (drawn is { } found)
            {
                yield return found;
            }
        }
    }

    static DrawnImage? Measure(
        byte[] data,
        string? contentType,
        string? description,
        double widthPoints,
        double heightPoints,
        ImageCrop? crop)
    {
        // SVG is vector — it has no stored resolution to be wasteful with. Anything else that
        // ImageSharp cannot identify (a JPEG-XR .wdp effect backup, say) is equally out of scope.
        if (contentType == "image/svg+xml" ||
            Identify(data) is not { } pixels)
        {
            return null;
        }

        // A crop means the box shows only part of the image, so the WHOLE image is effectively
        // drawn at a larger rectangle — the one Expand computes. Negative (padding) values shrink
        // it instead, which is equally the right number to measure against.
        var (_, _, drawnWidth, drawnHeight) = crop?.Expand(0, 0, widthPoints, heightPoints) ??
                                              (0d, 0d, widthPoints, heightPoints);

        var dpi = ScenarioInputs.Dpi(ScenarioFormat.PowerPoint);
        drawnWidth = drawnWidth * dpi / 72;
        drawnHeight = drawnHeight * dpi / 72;

        if (drawnWidth <= 0 ||
            drawnHeight <= 0)
        {
            return null;
        }

        return new(
            description ?? $"{pixels.Width}x{pixels.Height} {contentType}",
            pixels.Width,
            pixels.Height,
            drawnWidth,
            drawnHeight,
            data.Length);
    }

    static Size? Identify(byte[] data)
    {
        try
        {
            return Image.Identify(data).Size;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
