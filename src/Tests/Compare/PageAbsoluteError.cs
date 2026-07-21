/// <summary>
/// Word-reference vs Morph-render absolute error for the scenario suites: the fraction of pixels
/// that differ at all (0 = identical, 1 = every pixel differs).
///
/// This was <c>MagickImage.Compare(actual, ErrorMetric.Absolute)</c>, which returned exactly that
/// fraction. Magick.NET 14.15 changed the same call to return the RAW unnormalized error instead
/// (a ~1e9 sum in Q16 units), which is resolution-dependent and not comparable with any recorded
/// baseline — so the fraction is computed here from the decoded pixels, for the same reason
/// <see cref="PageSsim"/> is vendored. Being independent of Magick also keeps the recorded metric
/// stable across future upgrades, and drops an image decode from the per-page hot path.
///
/// Pixels outside the overlap of two differently-sized pages count as differing, and the result is
/// normalised by the EXPECTED page's pixel count, so a size mismatch scores near 1 rather than
/// silently comparing a sub-window.
/// </summary>
static class PageAbsoluteError
{
    public static double Compare(string expectedPngPath, byte[] actualPng)
    {
        using var expectedStream = File.OpenRead(expectedPngPath);
        var expected = PngDecoder.Decode(expectedStream);
        var actual = PngDecoder.Decode(new MemoryStream(actualPng));

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
