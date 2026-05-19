/// <summary>
/// Discovers font file paths from various system caches.
/// Backends use these paths with their own font loading APIs.
/// </summary>
static class FontCacheLoader
{
    static string? officeFontPath;

    static FontCacheLoader()
    {
        if (OperatingSystem.IsWindows())
        {
            officeFontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Office", "root", "vfs", "Fonts", "private");
        }
        else if (OperatingSystem.IsMacOS())
        {
            officeFontPath = "/Applications/Microsoft Word.app/Contents/Resources/DFonts";
        }
    }

    /// <summary>
    /// Gets font file paths from the Microsoft 365 cloud fonts cache.
    /// </summary>
    static IEnumerable<string> GetCloudFontFiles()
    {
        var cloudFontsPath = GetCloudFontPath();
        if (!Directory.Exists(cloudFontsPath))
        {
            yield break;
        }

        foreach (var fontDir in Directory.GetDirectories(cloudFontsPath))
        {
            foreach (var fontFile in EnumerateFontFilesInDirectory(fontDir))
            {
                yield return fontFile;
            }
        }
    }

    static string GetCloudFontPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Microsoft", "FontCache", "4", "CloudFonts");
    }

    static string GetUserFontPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Microsoft", "Windows", "Fonts");
    }

    internal static IEnumerable<string> GetSearchedPaths()
    {
        foreach (var path in GetSystemFontPaths())
        {
            yield return path;
        }
        yield return GetUserFontPath();

        if (officeFontPath != null)
        {
            yield return officeFontPath;
        }

        yield return GetCloudFontPath();
        yield return "(Morph embedded fonts)";
    }

    /// <summary>
    /// Gets font file paths from Microsoft Office private fonts.
    /// </summary>
    static IEnumerable<string> GetOfficeFontFiles()
    {
        if (officeFontPath != null)
        {
            foreach (var fontFile in EnumerateFontFilesInDirectory(officeFontPath))
            {
                yield return fontFile;
            }
        }
    }

    /// <summary>
    /// Gets font file paths from user-installed fonts (installed without admin rights).
    /// </summary>
    static IEnumerable<string> GetUserFontFiles() =>
        EnumerateFontFilesInDirectory(GetUserFontPath());

    /// <summary>
    /// Yields every font file Morph can find on the host, in priority order
    /// (user → Office → cloud → system). Identical paths emitted by more than one
    /// source are dropped so the resulting <see cref="FontFileCache"/> indexes each
    /// file once. The ordering preserves the original "user installs win on ties"
    /// rule — when the same family name is registered by two different files,
    /// the higher-priority source's face appears first in the cache's bucket.
    /// </summary>
    internal static IEnumerable<string> GetAllFontFiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in GetUserFontFiles()
                     .Concat(GetOfficeFontFiles())
                     .Concat(GetCloudFontFiles())
                     .Concat(GetSystemFontFiles()))
        {
            if (seen.Add(path))
            {
                yield return path;
            }
        }
    }

    /// <summary>
    /// Gets font file paths from machine-wide font directories.
    /// On Windows that is <c>%WINDIR%\Fonts</c>; on Linux the standard
    /// <c>/usr/share/fonts</c> tree; on macOS <c>/Library/Fonts</c> and
    /// <c>/System/Library/Fonts</c>. Reading these directories directly avoids
    /// depending on the OS font manager having indexed newly-installed fonts,
    /// which is unreliable on CI agents.
    /// </summary>
    internal static IEnumerable<string> GetSystemFontFiles()
    {
        foreach (var path in GetSystemFontPaths())
        {
            foreach (var fontFile in EnumerateFontFilesInDirectory(path, recursive: true))
            {
                yield return fontFile;
            }
        }
    }

    static IEnumerable<string> GetSystemFontPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Library/Fonts";
            yield return "/System/Library/Fonts";
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return "/usr/share/fonts";
            yield return "/usr/local/share/fonts";
        }
    }

    /// <summary>
    /// Enumerates <c>.ttf</c>, <c>.otf</c>, <c>.ttc</c>, and <c>.woff2</c> files in
    /// <paramref name="directory"/>. Returns nothing if the directory does not exist.
    /// </summary>
    internal static IEnumerable<string> EnumerateFontFilesInDirectory(string directory, bool recursive = false)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(directory, "*", option);

        foreach (var file in files)
        {
            if (file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }
}
