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

        if (!Directory.Exists(userFontsPath))
        {
            yield break;
        }

        foreach (var fontFile in Directory.GetFiles(userFontsPath, "*.ttf")
                     .Concat(Directory.GetFiles(userFontsPath, "*.otf")))
        {
            yield return fontFile;
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
}
