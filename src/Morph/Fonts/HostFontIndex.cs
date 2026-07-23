namespace Morph;

/// <summary>
/// Name index of every font file installed on the host (system, user, Office, cloud). Answers
/// "does this machine have this family?" without going through a rendering backend.
/// <para>
/// <see cref="FontResolver{TFont}"/> keeps its own eagerly-built cache because it needs the faces
/// themselves and is constructed on the render path anyway. This one exists for callers that only
/// need the availability answer — notably the PDF backend, whose PdfSharp resolver may not be
/// queried outside a font-resolution callback — and is deliberately lazy so callers that never ask
/// (the common case) never pay for the scan.
/// </para>
/// </summary>
static class HostFontIndex
{
    static readonly Lazy<FontFileCache> cache =
        new(() => new(FontCacheLoader.GetAllFontFiles(), OpenTypeReader.ReadFaces));

    /// <summary>
    /// True when some installed face declares <paramref name="family"/>, matched through the same
    /// priority-ordered candidate names the shared resolver uses (effective, original, stripped).
    /// </summary>
    public static bool Contains(string family, bool bold) =>
        cache.Value.TryGet(FontHelpers.GetCandidateNames(family, bold), out _);
}
