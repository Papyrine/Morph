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

        var sameSize = expected.Width == actual.Width &&
                       expected.Height == actual.Height;

        return (
            AbsoluteError(expected, actual),
            sameSize ? Math.Round(Ssim.Compare(expected, actual), 4) : null);
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
                var e = expectedRow + (x * 4);
                var a = actualRow + (x * 4);
                if (expectedRgba[e] != actualRgba[a] ||
                    expectedRgba[e + 1] != actualRgba[a + 1] ||
                    expectedRgba[e + 2] != actualRgba[a + 2])
                {
                    differing++;
                }
            }
        }

        var total = expected.Width * expected.Height;
        differing += total - (overlapWidth * overlapHeight);

        return Math.Round((double) differing / total, 4);
    }
}
