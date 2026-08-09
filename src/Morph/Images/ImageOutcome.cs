namespace Morph;

/// <summary>What <see cref="ImageCompressor"/> did to one image part.</summary>
public enum ImageOutcome
{
    /// <summary>Re-encoded, and the result was no smaller — the original bytes were kept.</summary>
    NoGain,

    /// <summary>Re-encoded at the same pixel dimensions and in the same format.</summary>
    Recompressed,

    /// <summary>Resampled down to the target resolution, and re-encoded in the same format.</summary>
    Resampled,

    /// <summary>Written out in a different format, which renames the part and retargets the
    /// relationships that reach it.</summary>
    Converted,

    /// <summary>A format the compressor deliberately leaves alone — vector art, metafiles, and
    /// anything else that cannot survive a raster round trip.</summary>
    UnsupportedFormat,

    /// <summary>A format the compressor handles, but this particular image could not be decoded.</summary>
    Unreadable
}
