/// <summary>
/// Locates an <see cref="ImageCodec"/> from whichever raster backend is deployed, so core
/// <c>Morph</c> can recompress images without a compile-time dependency on an imaging library.
/// The probe runs at most once per process and its result — present or absent — is cached.
/// </summary>
/// <remarks>
/// Prefers <c>Morph.ImageSharp</c>, which is the opposite of
/// <c>WordArtRasterizerFactory</c>'s Skia-first order and deliberately so: SkiaSharp's PNG encoder
/// exposes no compression level or filter strategy, so a Skia-first default would quietly produce
/// larger PNGs than the same call on a machine that happened to reference ImageSharp. For JPEG the
/// two are equivalent.
/// </remarks>
static class ImageCodecFactory
{
    static readonly object gate = new();
    static ImageCodec? cached;
    static bool probed;

    public static ImageCodec? TryGet()
    {
        if (probed)
        {
            return cached;
        }

        lock (gate)
        {
            if (!probed)
            {
                cached = Load("Morph.ImageSharp") ?? Load("Morph.Skia");
                probed = true;
            }
        }

        return cached;
    }

    static ImageCodec? Load(string assemblyName)
    {
        try
        {
            var assembly = Assembly.Load(assemblyName);
            foreach (var type in assembly.GetTypes())
            {
                if (type is {IsAbstract: false} &&
                    typeof(ImageCodec).IsAssignableFrom(type))
                {
                    return (ImageCodec?)Activator.CreateInstance(type, true);
                }
            }
        }
        catch
        {
            // Assembly not deployed (FileNotFoundException), a load/reflection failure, or a
            // missing parameterless constructor — treat as "no codec available here".
        }

        return null;
    }
}
