/// <summary>
/// Locates an <see cref="IImageEffects"/> from an optional raster backend so the PDF engine can
/// apply Word's blip effects — the Recolor transforms and <c>a:alphaModFix</c> transparency —
/// without a compile-time dependency on either engine. Prefers <c>Morph.Skia</c>, falling back to
/// <c>Morph.ImageSharp</c>. The probe runs at most once per process and its result (present or
/// absent) is cached; when neither backend is deployed the picture embeds its original bytes,
/// which is what the PDF backend did for every picture before this existed.
/// </summary>
static class ImageEffectsFactory
{
    static readonly object gate = new();
    static IImageEffects? cached;
    static bool probed;

    public static IImageEffects? TryGet()
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

    static IImageEffects? Load(string assemblyName)
    {
        try
        {
            var assembly = Assembly.Load(assemblyName);
            foreach (var type in assembly.GetTypes())
            {
                if (type is {IsAbstract: false, IsInterface: false} &&
                    typeof(IImageEffects).IsAssignableFrom(type))
                {
                    return (IImageEffects?)Activator.CreateInstance(type, true);
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
