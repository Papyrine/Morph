namespace Morph.Web.Services;

/// <summary>
/// Materialises the fonts every render needs into the browser's in-memory filesystem.
///
/// Rendering has to be pinned to a known font set. A WASM client has no OS fonts, and Morph's embedded
/// Aptos covers only weights 400/700 — so a document that asks for, say, "Aptos Light" (300) makes the
/// renderer walk its OS-font fallback chain and throw <c>Font not found</c> (this doesn't surface on a dev
/// box that happens to have the font installed, but it does in the browser and on a clean CI runner).
///
/// Passing a <see cref="ExportOptions.FontDirectory"/> instead restricts resolution to that directory and
/// substitutes unknown families/weights within it — no OS lookup, no throw, deterministic output. So the
/// four Aptos faces are shipped as static assets under <c>wwwroot/fonts/</c>, fetched once, written here,
/// and this directory is handed to both the PNG and PDF converters.
/// </summary>
public static class FontStore
{
    // A directory in the WASM in-memory (Emscripten) filesystem. Not persisted across reloads, so the
    // fonts are re-materialised each session — cheap, and keeps the resolver's directory scan happy.
    public const string Directory = "/morph-fonts";

    // File names follow the resolver's "{Family}_{weight}[_Italic].ttf" convention, so "Aptos"
    // (400/700, upright/italic) resolves and every other family maps onto it.
    static readonly string[] files =
    [
        "Aptos_400.ttf",
        "Aptos_400_Italic.ttf",
        "Aptos_700.ttf",
        "Aptos_700_Italic.ttf",
    ];

    static bool loaded;

    /// <summary>
    /// Fetches the bundled Aptos faces from <c>wwwroot/fonts/</c> and writes them into the in-memory
    /// filesystem under <see cref="Directory"/>. Idempotent — the download happens only once per session.
    /// Returns the directory to pass as the render font directory.
    /// </summary>
    public static async Task<string> EnsureAsync(HttpClient http)
    {
        if (loaded)
        {
            return Directory;
        }

        System.IO.Directory.CreateDirectory(Directory);
        foreach (var file in files)
        {
            var path = $"{Directory}/{file}";
            if (!File.Exists(path))
            {
                var bytes = await http.GetByteArrayAsync($"fonts/{file}");
                await File.WriteAllBytesAsync(path, bytes);
            }
        }

        loaded = true;
        return Directory;
    }
}
