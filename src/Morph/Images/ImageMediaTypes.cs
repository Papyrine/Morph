namespace Morph;

/// <summary>
/// The media types <see cref="ImageCompressor"/> is willing to rewrite, and the file extensions
/// they are stored under.
/// </summary>
/// <remarks>
/// The list is deliberately short. Vector formats (<c>image/svg+xml</c>) and metafiles
/// (<c>image/x-emf</c>, <c>image/x-wmf</c>) would have to be rasterized to be re-encoded, which
/// changes what Word draws rather than how much it weighs. JPEG XR (<c>image/vnd.ms-photo</c>,
/// the <c>.wdp</c> parts Word writes beside blur and 3D effects), GIF and TIFF are left alone
/// because neither backend can write them back and a silent format change is worse than a large
/// file.
/// </remarks>
static class ImageMediaTypes
{
    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";

    /// <summary>Whether a part with this media type is a candidate for rewriting.</summary>
    public static bool IsRewritable(string contentType) =>
        Matches(contentType, Png) ||
        Matches(contentType, Jpeg);

    /// <summary>Whether the package declares this media type as some kind of image at all.</summary>
    public static bool IsImage(string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public static bool Matches(string contentType, string mediaType)
    {
        // a declared content type may carry parameters, e.g. "image/jpeg; charset=binary"
        var separator = contentType.IndexOf(';');
        var media = separator < 0 ? contentType.AsSpan() : contentType.AsSpan(0, separator);
        return media.Trim().Equals(mediaType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The extension a part holding <paramref name="contentType"/> should be named with.</summary>
    public static string ExtensionFor(string contentType) =>
        Matches(contentType, Jpeg) ? "jpeg" : "png";
}
