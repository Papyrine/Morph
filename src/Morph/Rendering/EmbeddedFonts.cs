/// <summary>
/// Raw bytes of the four standard Aptos faces Morph ships as <c>EmbeddedResource</c>
/// inside <c>Morph.dll</c>. Loaded eagerly on first class access. Consumed only by
/// <see cref="FontResolver{TFont}.BuildBundledSeed"/>; backends never reach in here.
/// </summary>
static class EmbeddedFonts
{
    public static byte[] Aptos400 { get; } = Load("Morph.EmbeddedFonts.Aptos_400.ttf");
    public static byte[] Aptos400Italic { get; } = Load("Morph.EmbeddedFonts.Aptos_400_Italic.ttf");
    public static byte[] Aptos700 { get; } = Load("Morph.EmbeddedFonts.Aptos_700.ttf");
    public static byte[] Aptos700Italic { get; } = Load("Morph.EmbeddedFonts.Aptos_700_Italic.ttf");

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
