using System.Diagnostics.CodeAnalysis;

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
                cached = Load(Type.GetType("Morph.ImageSharpImageCodec, Morph.ImageSharp")) ?? Load(Type.GetType("Morph.SkiaImageCodec, Morph.Skia"));
                probed = true;
            }
        }

        return cached;
    }

    // The backend's type is named literally, which is what lets a trimmed or native AOT app keep it: the
    // trimmer reads the name, keeps the type's constructors when its assembly is deployed (the
    // annotation below), and leaves null for one that is not. Scanning the assembly's types, as this did,
    // found nothing once trimming had removed them, which no build reported.
    static ImageCodec? Load([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.NonPublicConstructors)] Type? type)
    {
        if (type == null)
        {
            return null;
        }

        try
        {
            return (ImageCodec?) Activator.CreateInstance(type, true);
        }
        catch
        {
            // A load or construction failure: treat as "no backend available here".
            return null;
        }
    }
}
