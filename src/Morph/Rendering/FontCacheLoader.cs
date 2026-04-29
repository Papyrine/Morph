/// <summary>
/// Discovers font file paths from various system caches.
/// Backends use these paths with their own font loading APIs.
/// </summary>
static class FontCacheLoader
{
    /// <summary>
    /// Gets font file paths from the Microsoft 365 cloud fonts cache.
    /// </summary>
    internal static IEnumerable<string> GetCloudFontFiles()
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

    internal static string GetCloudFontPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Microsoft", "FontCache", "4", "CloudFonts");
    }

    internal static string GetUserFontPath()
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
        foreach (var path in GetOfficeFontPaths())
        {
            yield return path;
        }
        yield return GetCloudFontPath();
        yield return "(Morph embedded fonts)";
    }

    /// <summary>
    /// Gets font file paths from Microsoft Office private fonts.
    /// </summary>
    internal static IEnumerable<string> GetOfficeFontFiles()
    {
        foreach (var officeFontsPath in GetOfficeFontPaths())
        {
            foreach (var fontFile in EnumerateFontFilesInDirectory(officeFontsPath))
            {
                yield return fontFile;
            }
        }
    }

    /// <summary>
    /// Gets font file paths from user-installed fonts (installed without admin rights).
    /// </summary>
    internal static IEnumerable<string> GetUserFontFiles() =>
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

    static IEnumerable<string> GetOfficeFontPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Office", "root", "vfs", "Fonts", "private");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Microsoft Word.app/Contents/Resources/DFonts";
            yield return "/Applications/Microsoft Excel.app/Contents/Resources/DFonts";
            yield return "/Applications/Microsoft PowerPoint.app/Contents/Resources/DFonts";
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
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*", option);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

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
