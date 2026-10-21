using System.Diagnostics.CodeAnalysis;

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
                cached = Load(Type.GetType("SkiaImageEffects, Morph.Skia")) ?? Load(Type.GetType("ImageSharpImageEffects, Morph.ImageSharp"));
                probed = true;
            }
        }

        return cached;
    }

    // The backend's type is named literally, which is what lets a trimmed or native AOT app keep it: the
    // trimmer reads the name, keeps the type's constructors when its assembly is deployed (the
    // annotation below), and leaves null for one that is not. Scanning the assembly's types, as this did,
    // found nothing once trimming had removed them, which no build reported.
    static IImageEffects? Load([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.NonPublicConstructors)] Type? type)
    {
        if (type == null)
        {
            return null;
        }

        try
        {
            return (IImageEffects?) Activator.CreateInstance(type, true);
        }
        catch
        {
            // A load or construction failure: treat as "no backend available here".
            return null;
        }
    }
}
