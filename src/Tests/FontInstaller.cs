using System.Runtime.InteropServices;

/// <summary>
/// Downloads and installs missing fonts for test accuracy.
/// </summary>
public static class FontInstaller
{
    static HttpClient httpClient = new();

    // Fonts available from freefonts.co
    static Dictionary<string, string[]> availableFonts = new()
    {
        ["Bodoni MT Condensed"] =
        [
            "bodoni-mt-condensed",
            "bodoni-mt-condensed-bold",
            "bodoni-mt-condensed-italic",
            "bodoni-mt-condensed-bold-italic"
        ],
        ["Avenir Next LT Pro"] =
        [
            "avenir-next-lt-pro-regular",
            "avenir-next-lt-pro-bold",
            "avenir-next-lt-pro-demi",
            "avenir-next-lt-pro-italic"
        ],
        ["Franklin Gothic"] =
        [
            "franklin-gothic-medium-regular",
            "franklin-gothic-demi",
            "franklin-gothic-medium-italic",
            "franklin-gothic-condensed"
        ],
        ["Futura"] =
        [
            "futura-medium",
            "futura-bold",
            "futura-light-light",
            "futura-book-book"
        ],
        ["Source Sans Pro"] =
        [
            "source-sans-pro-regular",
            "source-sans-pro-bold",
            "source-sans-pro-light",
            "source-sans-pro-black"
        ],
        ["Calibri Light"] = ["calibri-light"],
        ["Arial Black"] = ["arial-black"],
        ["Univers"] =
        [
            "univers-regular",
            "univers-bold",
            "univers-medium"
        ],
        ["Arial Rounded MT Bold"] = ["arial-rounded-mt-bold"],
        ["Georgia Pro"] = ["georgia-bold", "georgia-regular", "georgia-italic"]
    };

    // Office 365 cloud fonts - require Microsoft 365 installation
    static string[] office365Fonts =
    [
        "Aptos",
        "Aptos Display",
        "Aptos Light",
        "Tenorite",
        "Grandview",
        "Daytona",
        "Seaford"
    ];

    /// <summary>
    /// Installs missing fonts from freefonts.co
    /// </summary>
    public static async Task InstallMissingFontsAsync(ITestOutputHelper? output = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            output?.WriteLine("Font installation only supported on Windows");
            return;
        }

        var fontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var localFontsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Fonts");

        Directory.CreateDirectory(localFontsFolder);

        var tempDir = Path.Combine(Path.GetTempPath(), "WordRenderFonts");
        Directory.CreateDirectory(tempDir);

        var installed = 0;
        var failed = 0;

        foreach (var (fontFamily, fontSlugs) in availableFonts)
        {
            foreach (var slug in fontSlugs)
            {
                try
                {
                    var result = await DownloadAndInstallFontAsync(slug, localFontsFolder, tempDir, output);
                    if (result)
                    {
                        installed++;
                    }
                }
                catch (Exception ex)
                {
                    output?.WriteLine($"Failed to install {slug}: {ex.Message}");
                    failed++;
                }
            }
        }

        // Cleanup temp directory
        try { Directory.Delete(tempDir, true); } catch { }

        output?.WriteLine($"\nFont installation complete: {installed} installed, {failed} failed");

        // Warn about Office 365 fonts
        output?.WriteLine($"\nNote: {office365Fonts.Length} fonts require Microsoft 365 installation:");
        foreach (var font in office365Fonts)
        {
            output?.WriteLine($"  - {font}");
        }
    }

    static async Task<bool> DownloadAndInstallFontAsync(
        string fontSlug,
        string installDir,
        string tempDir,
        ITestOutputHelper? output)
    {
        // First, get the download page to find the actual download link
        var pageUrl = $"https://freefonts.co/fonts/{fontSlug}";

        output?.WriteLine($"Fetching {fontSlug}...");

        var pageContent = await httpClient.GetStringAsync(pageUrl);

        // Extract download link - typically points to a zip file
        // The download link is usually in format: /download/font-name
        var downloadUrl = $"https://freefonts.co/download/{fontSlug}";

        var zipPath = Path.Combine(tempDir, $"{fontSlug}.zip");
        var extractDir = Path.Combine(tempDir, fontSlug);

        // Download the font zip
        using (var response = await httpClient.GetAsync(downloadUrl))
        {
            if (!response.IsSuccessStatusCode)
            {
                output?.WriteLine($"  Download failed: {response.StatusCode}");
                return false;
            }

            await using var fs = File.Create(zipPath);
            await response.Content.CopyToAsync(fs);
        }

        // Extract the zip
        Directory.CreateDirectory(extractDir);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
        }
        catch (InvalidDataException)
        {
            // Not a zip file - might be direct font file
            var directFontPath = Path.Combine(extractDir, $"{fontSlug}.ttf");
            File.Move(zipPath, directFontPath, overwrite: true);
        }

        // Find and install font files
        var fontFiles = Directory.GetFiles(extractDir, "*.ttf", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(extractDir, "*.otf", SearchOption.AllDirectories))
            .ToArray();

        if (fontFiles.Length == 0)
        {
            output?.WriteLine($"  No font files found in download");
            return false;
        }

        foreach (var fontFile in fontFiles)
        {
            var destPath = Path.Combine(installDir, Path.GetFileName(fontFile));

            if (File.Exists(destPath))
            {
                output?.WriteLine($"  {Path.GetFileName(fontFile)} already installed");
                continue;
            }

            File.Copy(fontFile, destPath, overwrite: true);

            // Register font with Windows (user fonts don't require admin)
            RegisterFont(destPath);

            output?.WriteLine($"  Installed {Path.GetFileName(fontFile)}");
        }

        return true;
    }

    [DllImport("gdi32.dll", EntryPoint = "AddFontResourceW", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int AddFontResource(string lpFileName);

    [DllImport("user32.dll", SetLastError = true)]
    static extern int SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    const uint wmFontchange = 0x001D;
    static readonly IntPtr hwndBroadcast = new(0xffff);

    static void RegisterFont(string fontPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            AddFontResource(fontPath);
            SendMessage(hwndBroadcast, wmFontchange, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Font registration may fail without admin rights, but font will still work after restart
        }
    }

    /// <summary>
    /// Checks which fonts are missing on the system.
    /// </summary>
    public static List<string> GetMissingFonts()
    {
        var missing = new List<string>();

        foreach (var fontFamily in availableFonts.Keys.Concat(office365Fonts))
        {
            using var typeface = SkiaSharp.SKTypeface.FromFamilyName(fontFamily);
            if (typeface.FamilyName != fontFamily)
            {
                missing.Add(fontFamily);
            }
        }

        return missing;
    }
}
