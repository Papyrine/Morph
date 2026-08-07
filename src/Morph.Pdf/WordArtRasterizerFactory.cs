/// <summary>
/// Locates an <see cref="IWordArtRasterizer"/> from an optional raster backend so the PDF engine
/// can embed high-fidelity WordArt without a compile-time dependency on either engine. Prefers
/// <c>Morph.Skia</c>, falling back to <c>Morph.ImageSharp</c>. The probe runs at most once per
/// process and its result (present or absent) is cached; when neither backend is deployed the PDF
/// backend renders WordArt as plain text.
/// </summary>
static class WordArtRasterizerFactory
{
    static readonly object gate = new();
    static IWordArtRasterizer? cached;
    static bool probed;

    public static IWordArtRasterizer? TryGet()
    {
        if (probed)
        {
            return cached;
        }

        lock (gate)
        {
            if (!probed)
            {
                cached = Load("Morph.Skia") ?? Load("Morph.ImageSharp");
                probed = true;
            }
        }

        return cached;
    }

    static IWordArtRasterizer? Load(string assemblyName)
    {
        try
        {
            var assembly = Assembly.Load(assemblyName);
            foreach (var type in assembly.GetTypes())
            {
                if (type is {IsAbstract: false, IsInterface: false} &&
                    typeof(IWordArtRasterizer).IsAssignableFrom(type))
                {
                    return (IWordArtRasterizer?)Activator.CreateInstance(type, true);
                }
            }
        }
        catch
        {
            // Assembly not deployed (FileNotFoundException), a load/reflection failure, or a
            // missing parameterless constructor — treat as "no raster backend available".
        }

        return null;
    }
}
