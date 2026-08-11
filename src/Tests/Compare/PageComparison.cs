/// <summary>
/// The two per-page metrics the scenario suites record against the Word reference render. Both
/// come from a SINGLE decode of each image — they used to live in separate helpers that decoded
/// the same pair twice over.
///
/// * <c>AbsoluteError</c> — the fraction of pixels that differ at all (0 = identical). Pixels
///   outside the overlap of two differently-sized pages count as differing, and the result is
///   normalised by the EXPECTED page's pixel count, so an orientation mismatch scores near 1
///   rather than silently comparing a sub-window (business-plans/15 p12/p15 are the corpus's only
///   such pages).
/// * <c>Ssim</c> — the vendored Verify SSIM (8×8-window, luminance-only; 1 = identical). Null when
///   the two images differ in size: <see cref="Ssim.Compare"/> indexes the second image with the
///   first image's geometry, so a sub-window score would be silently wrong rather than merely
///   imprecise.
///
/// Both were originally Magick.NET calls. SSIM moved in-repo for speed (the managed implementation
/// is ~30× faster than Magick's SSIM metric, which added ~10 minutes to a full suite run); the
/// absolute error followed when Magick.NET 14.15 changed
/// <c>Compare(other, ErrorMetric.Absolute)</c> to return the RAW unnormalised error (a ~1e9 sum in
/// Q16 units) instead of the fraction, silently rewriting every recorded metric. Keeping both here
/// makes the recorded numbers stable across future upgrades — see docs/fidelity-audit.md; do not
/// route them back through Magick.
/// </summary>
static class PageComparison
{
    public static (double AbsoluteError, double? Ssim) Compare(string expectedPngPath, byte[] actualPng)
    {
        using var expectedStream = File.OpenRead(expectedPngPath);
        var expected = PngDecoder.Decode(expectedStream);
        var actual = PngDecoder.Decode(new MemoryStream(actualPng));

        return (AbsoluteError(expected, actual), Similarity(expected, actual));
    }

    /// <summary>
    /// A page size can disagree by a pixel without anything being wrong with the render: A4 is
    /// 793.71 x 1122.52 pixels at 96 DPI, and two independent rasterisers need not resolve that the
    /// same way — Excel rounds it into its XPS while Morph truncates. Dropping SSIM over that scored
    /// the whole spreadsheet corpus on error metric alone.
    ///
    /// So a difference of at most one pixel per axis is treated as the rounding artefact it is, and
    /// both images are cropped to their overlap first. Anything larger stays null: <see cref="Ssim"/>
    /// indexes the second image with the first's geometry, and a genuine size difference (an
    /// orientation flip, a different paper) would score a silently wrong sub-window rather than
    /// merely an imprecise one.
    /// </summary>
    static double? Similarity(PngImage expected, PngImage actual)
    {
        if (Math.Abs(expected.Width - actual.Width) > 1 ||
            Math.Abs(expected.Height - actual.Height) > 1)
        {
            return null;
        }

        if (expected.Width == actual.Width && expected.Height == actual.Height)
        {
            return Math.Round(Ssim.Compare(expected, actual), 4);
        }

        var width = Math.Min(expected.Width, actual.Width);
        var height = Math.Min(expected.Height, actual.Height);
        return Math.Round(Ssim.Compare(Crop(expected, width, height), Crop(actual, width, height)), 4);
    }

    static PngImage Crop(PngImage image, int width, int height)
    {
        if (image.Width == width && image.Height == height)
        {
            return image;
        }

        var cropped = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(image.Rgba, y * image.Width * 4, cropped, y * width * 4, width * 4);
        }

        return new(width, height, cropped);
    }

    static double AbsoluteError(PngImage expected, PngImage actual)
    {
        var overlapWidth = Math.Min(expected.Width, actual.Width);
        var overlapHeight = Math.Min(expected.Height, actual.Height);
        var expectedRgba = expected.Rgba;
        var actualRgba = actual.Rgba;

        var differing = 0;
        for (var y = 0; y < overlapHeight; y++)
        {
            var expectedRow = y * expected.Width * 4;
            var actualRow = y * actual.Width * 4;
            for (var x = 0; x < overlapWidth; x++)
            {
                var e = expectedRow + x * 4;
                var a = actualRow + x * 4;
                if (expectedRgba[e] != actualRgba[a] ||
                    expectedRgba[e + 1] != actualRgba[a + 1] ||
                    expectedRgba[e + 2] != actualRgba[a + 2])
                {
                    differing++;
                }
            }
        }

        var total = expected.Width * expected.Height;
        differing += total - overlapWidth * overlapHeight;

        return Math.Round((double) differing / total, 4);
    }
}
