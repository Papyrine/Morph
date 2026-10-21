using System.Diagnostics.CodeAnalysis;

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
                cached = Load(Type.GetType("SkiaWordArtRasterizer, Morph.Skia")) ?? Load(Type.GetType("ImageSharpWordArtRasterizer, Morph.ImageSharp"));
                probed = true;
            }
        }

        return cached;
    }

    // The backend's type is named literally, which is what lets a trimmed or native AOT app keep it: the
    // trimmer reads the name, keeps the type's constructors when its assembly is deployed (the
    // annotation below), and leaves null for one that is not. Scanning the assembly's types, as this did,
    // found nothing once trimming had removed them, which no build reported.
    static IWordArtRasterizer? Load([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.NonPublicConstructors)] Type? type)
    {
        if (type == null)
        {
            return null;
        }

        try
        {
            return (IWordArtRasterizer?) Activator.CreateInstance(type, true);
        }
        catch
        {
            // A load or construction failure: treat as "no backend available here".
            return null;
        }
    }
}
