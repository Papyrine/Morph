namespace Morph;

/// <summary>One image part's contribution to an <see cref="ImageCompressionResult"/>.</summary>
/// <param name="PartName">Package-root-relative name of the part as it was read, e.g. <c>word/media/image1.png</c>.</param>
/// <param name="ContentType">The media type the package declares for it.</param>
/// <param name="Bytes">Compressed size in the source package.</param>
/// <param name="NewBytes">Compressed size in the output package. Equal to <paramref name="Bytes"/> when nothing was rewritten.</param>
/// <param name="Width">Intrinsic width in pixels, or null when the image could not be read.</param>
/// <param name="Height">Intrinsic height in pixels, or null when the image could not be read.</param>
/// <param name="RenderedDpi">
/// Effective resolution: pixel width divided by the width the document draws it at. Null when the
/// image is not reached by any drawing that states a size, in which case it is never resampled —
/// there is nothing to say how many pixels it actually needs.
/// </param>
/// <param name="Outcome">What was done.</param>
/// <param name="NewPartName">The part's name in the output package, when <paramref name="Outcome"/>
/// is <see cref="ImageOutcome.Converted"/> and the extension therefore changed. Null otherwise.</param>
public sealed record ImageReport(
    string PartName,
    string ContentType,
    long Bytes,
    long NewBytes,
    int? Width,
    int? Height,
    double? RenderedDpi,
    ImageOutcome Outcome,
    string? NewPartName = null)
{
    /// <summary>Bytes recovered from this part. Zero when it was left alone.</summary>
    public long Saved => Bytes - NewBytes;
}
