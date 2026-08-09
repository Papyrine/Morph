namespace Morph;

/// <summary>What <see cref="ImageCompressor.Compress(string, ImageCompressionOptions?)"/> achieved.</summary>
/// <param name="OriginalBytes">Compressed size of every image part in the source package.</param>
/// <param name="CompressedBytes">Compressed size of the same parts in the output package.</param>
/// <param name="Images">One entry per image part, in package order.</param>
public sealed record ImageCompressionResult(
    long OriginalBytes,
    long CompressedBytes,
    IReadOnlyList<ImageReport> Images)
{
    /// <summary>Bytes recovered across every image part. Never negative — a part is only rewritten when it shrinks.</summary>
    public long Saved => OriginalBytes - CompressedBytes;

    /// <summary>True when at least one part was rewritten.</summary>
    public bool Changed => Saved > 0;

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Images.Count} images, {OriginalBytes:N0} -> {CompressedBytes:N0} bytes (saved {Saved:N0})";
}
