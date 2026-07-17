// Vendored from VerifyTests/Verify (src/Verify/Compare/Png/PngImage.cs, MIT) so the scenario
// suite computes the same SSIM as the Verify PNG comparer. Drop when Verify exposes it publicly.
﻿readonly struct PngImage(int width, int height, byte[] rgba)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Rgba { get; } = rgba;
}
