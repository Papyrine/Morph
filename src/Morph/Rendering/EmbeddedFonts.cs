/// <summary>
/// Provides byte-array access to the font files Morph ships as <c>EmbeddedResource</c>
/// inside <c>Morph.dll</c> (the four standard Aptos faces). Renderer backends consume the
/// bytes via their own in-memory APIs (<c>SKTypeface.FromStream</c>, ImageSharp's
/// <c>FontCollection.Add(Stream)</c>) — no temp files are written.
/// </summary>
static class EmbeddedFonts
{
    static readonly Lazy<IReadOnlyList<byte[]>> bytes = new(LoadAll);

    /// <summary>
    /// Returns the raw bytes of every embedded font resource, one entry per face.
    /// </summary>
    public static IReadOnlyList<byte[]> AllFaceBytes => bytes.Value;

    /// <summary>
    /// Opens a fresh <see cref="MemoryStream"/> over each face's bytes. Streams own a
    /// reference to the cached array but never copy it, so this is cheap to call.
    /// </summary>
    public static IEnumerable<Stream> OpenStreams()
    {
        foreach (var face in bytes.Value)
        {
            yield return new MemoryStream(face, writable: false);
        }
    }

    // Resource names produced by the default <EmbeddedResource> naming convention:
    // <RootNamespace>.<RelativePath with '/' replaced by '.'>.
    static readonly string[] resourceNames =
    [
        "Morph.EmbeddedFonts.Aptos_400.ttf",
        "Morph.EmbeddedFonts.Aptos_400_Italic.ttf",
        "Morph.EmbeddedFonts.Aptos_700.ttf",
        "Morph.EmbeddedFonts.Aptos_700_Italic.ttf"
    ];

    static IReadOnlyList<byte[]> LoadAll()
    {
        var assembly = typeof(EmbeddedFonts).Assembly;
        var result = new byte[resourceNames.Length][];

        for (var i = 0; i < resourceNames.Length; i++)
        {
            var name = resourceNames[i];
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded font resource '{name}' is missing from Morph.dll.");
            using var memory = new MemoryStream(capacity: (int) stream.Length);
            stream.CopyTo(memory);
            result[i] = memory.ToArray();
        }

        return result;
    }
}
