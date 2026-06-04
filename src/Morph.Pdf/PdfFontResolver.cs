using PdfSharp.Fonts;

namespace Morph;

/// <summary>
/// Maps font family + style requests onto the bundled TrueType files so PdfSharp can embed real
/// glyphs. PdfSharp resolves fonts through a single process-global <see cref="GlobalFontSettings.FontResolver"/>,
/// so this is a registered-once singleton; directories are added additively (keyed by absolute file
/// path) which lets multiple <see cref="ExportOptions.FontDirectory"/> values coexist.
///
/// Font files follow the bundled naming convention <c>{Family}_{weight}[_Italic].ttf</c> (spaces in
/// the family become underscores), e.g. <c>Arial_Nova_700.ttf</c>, <c>Aptos_400_Italic.ttf</c>.
/// </summary>
sealed class PdfFontResolver : IFontResolver
{
    public static PdfFontResolver Instance { get; } = new();

    readonly Lock gate = new();
    readonly Dictionary<string, string> faceToPath = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string Family, bool Bold, bool Italic), string> index = [];
    readonly HashSet<string> scannedDirectories = new(StringComparer.OrdinalIgnoreCase);
    string? defaultFace;

    PdfFontResolver()
    {
    }

    /// <summary>Registers the resolver globally (idempotent) and indexes <paramref name="directory"/>.</summary>
    public static void Register(string? directory)
    {
        if (GlobalFontSettings.FontResolver is not PdfFontResolver)
        {
            GlobalFontSettings.FontResolver = Instance;
        }

        Instance.ScanDirectory(directory);
    }

    void ScanDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var full = Path.GetFullPath(directory);
        lock (gate)
        {
            if (!scannedDirectories.Add(full) || !Directory.Exists(full))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(full, "*.ttf", SearchOption.AllDirectories))
            {
                IndexFile(path);
            }
        }
    }

    void IndexFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        var italic = parts[^1].Equals("Italic", StringComparison.OrdinalIgnoreCase);
        var end = italic ? parts.Length - 1 : parts.Length;

        var weight = 400;
        if (end > 1 && int.TryParse(parts[end - 1], out var parsedWeight))
        {
            weight = parsedWeight;
            end--;
        }

        if (end <= 0)
        {
            return;
        }

        var family = string.Join(' ', parts[..end]);
        var bold = weight >= 600;

        faceToPath[path] = path;
        index[(family.ToLowerInvariant(), bold, italic)] = path;
        defaultFace ??= path;
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        lock (gate)
        {
            if (TryResolve(familyName, isBold, isItalic, out var face))
            {
                return new(face);
            }
        }

        // Fall back to the platform resolver (system fonts) when the family isn't bundled and no
        // FontDirectory restricted us. Keeps non-deterministic-but-available output on the host OS.
        var platform = PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);
        if (platform != null)
        {
            return platform;
        }

        lock (gate)
        {
            return defaultFace == null ? null : new FontResolverInfo(defaultFace);
        }
    }

    bool TryResolve(string familyName, bool bold, bool italic, out string face)
    {
        var family = familyName.ToLowerInvariant();
        Span<(bool Bold, bool Italic)> attempts =
        [
            (bold, italic),
            (bold, !italic),
            (!bold, italic),
            (false, false)
        ];

        foreach (var (attemptBold, attemptItalic) in attempts)
        {
            if (index.TryGetValue((family, attemptBold, attemptItalic), out var found))
            {
                face = found;
                return true;
            }
        }

        face = "";
        return false;
    }

    public byte[]? GetFont(string faceName)
    {
        lock (gate)
        {
            return faceToPath.TryGetValue(faceName, out var path) ? File.ReadAllBytes(path) : null;
        }
    }
}
