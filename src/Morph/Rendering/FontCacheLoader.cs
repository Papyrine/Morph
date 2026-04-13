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
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cloudFontsPath = Path.Combine(localAppData, "Microsoft", "FontCache", "4", "CloudFonts");

        if (!Directory.Exists(cloudFontsPath))
        {
            yield break;
        }

        foreach (var fontDir in Directory.GetDirectories(cloudFontsPath))
        {
            foreach (var fontFile in Directory.GetFiles(fontDir, "*.ttf"))
            {
                yield return fontFile;
            }
        }
    }

    /// <summary>
    /// Gets font file paths from Microsoft Office private fonts.
    /// </summary>
    internal static IEnumerable<string> GetOfficeFontFiles()
    {
        foreach (var officeFontsPath in GetOfficeFontPaths())
        {
            if (!Directory.Exists(officeFontsPath))
            {
                continue;
            }

            foreach (var fontFile in Directory.GetFiles(officeFontsPath, "*.ttf"))
            {
                yield return fontFile;
            }
        }
    }

    /// <summary>
    /// Gets font file paths from user-installed fonts (installed without admin rights).
    /// </summary>
    internal static IEnumerable<string> GetUserFontFiles()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userFontsPath = Path.Combine(localAppData, "Microsoft", "Windows", "Fonts");
        return EnumerateFontFilesInDirectory(userFontsPath);
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
    /// Enumerates <c>.ttf</c> and <c>.otf</c> files in <paramref name="directory"/>.
    /// Returns nothing if the directory does not exist.
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
                file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }
}
