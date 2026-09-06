/// <summary>
/// Raw bytes of the fonts Morph ships as <c>EmbeddedResource</c> inside <c>Morph.dll</c>:
/// the four standard Aptos faces (the modern Word default) and a tiny custom
/// <c>Bullets.ttf</c> used to render list bullet glyphs cross-platform. Loaded eagerly on
/// first class access. Consumed only by <see cref="FontResolver{TFont}.BuildBundledSeed(Func{string, byte[], TFont})"/>;
/// backends never reach in here.
/// </summary>
static class EmbeddedFonts
{
    public static byte[] Aptos400 { get; } = Load("Morph.EmbeddedFonts.Aptos_400.ttf");
    public static byte[] Aptos400Italic { get; } = Load("Morph.EmbeddedFonts.Aptos_400_Italic.ttf");
    public static byte[] Aptos700 { get; } = Load("Morph.EmbeddedFonts.Aptos_700.ttf");
    public static byte[] Aptos700Italic { get; } = Load("Morph.EmbeddedFonts.Aptos_700_Italic.ttf");
    public static byte[] Bullets { get; } = Load("Morph.EmbeddedFonts.Bullets.ttf");

    /// <summary>
    /// The Word advance sidecar embedded beside an Aptos face — <c>Aptos_400.wordadvances</c> /
    /// <c>.wordadvances15</c>, the same files <c>src/Fonts</c> carries (see
    /// <see cref="FontMetrics.WordAdvances"/>). Null when no sidecar is embedded for the face. The
    /// seeded metrics face has no path to find a sidecar beside, so the resolver's seed reads it here;
    /// without this the embedded Aptos measured on the linear fallback in every document while the
    /// directory copy of the same face measured on Word's advances — and the seed wins the lookup.
    /// </summary>
    public static string? WordAdvances(string faceName, string extension)
    {
        var assembly = typeof(EmbeddedFonts).Assembly;
        using var stream = assembly.GetManifestResourceStream($"Morph.EmbeddedFonts.{faceName}{extension}");
        if (stream == null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static byte[] Load(string resourceName)
    {
        var assembly = typeof(EmbeddedFonts).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' is missing from Morph.dll.");
        using var memory = new MemoryStream(capacity: (int) stream.Length);
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
