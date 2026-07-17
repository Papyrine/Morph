/// <summary>
/// Word-reference vs Morph-render structural similarity for the scenario suites, using the
/// vendored Verify SSIM (8×8-window, luminance-only; 1 = identical). Null when the two images
/// differ in size — <see cref="Ssim.Compare"/> indexes the second image with the first image's
/// geometry, so a sub-window score would be silently wrong rather than merely imprecise.
/// (The managed implementation is ~30× faster than Magick's SSIM metric, which added ~10
/// minutes to a full suite run.)
/// </summary>
static class PageSsim
{
    public static double? Compare(string expectedPngPath, byte[] actualPng)
    {
        using var expectedStream = File.OpenRead(expectedPngPath);
        var expected = PngDecoder.Decode(expectedStream);
        var actual = PngDecoder.Decode(new MemoryStream(actualPng));
        if (expected.Width != actual.Width ||
            expected.Height != actual.Height)
        {
            return null;
        }

        return Math.Round(Ssim.Compare(expected, actual), 4);
    }
}
