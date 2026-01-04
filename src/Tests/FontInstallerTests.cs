/// <summary>
/// Tests for font installation and verification.
/// Run these tests manually to install missing fonts.
/// </summary>
[Explicit]
public class FontInstallerTests
{
    /// <summary>
    /// Lists all fonts that are missing on this system.
    /// </summary>
    [Test]
    public void ListMissingFonts()
    {
        var missing = FontInstaller.GetMissingFonts();

        Console.WriteLine($"Missing fonts: {missing.Count}");
        foreach (var font in missing)
        {
            Console.WriteLine($"  - {font}");
        }

        // This test is informational - doesn't fail
    }

    /// <summary>
    /// Downloads and installs missing fonts from freefonts.co.
    /// Requires internet connection. Fonts are installed to user fonts folder.
    /// </summary>
    [Test]
    public async Task InstallMissingFonts()
    {
        var output = new ConsoleTestOutputHelper();
        await FontInstaller.InstallMissingFontsAsync(output);
    }

    /// <summary>
    /// Verifies font rendering after installation.
    /// </summary>
    [Test]
    public void VerifyFontAvailability()
    {
        var testFonts = new[]
        {
            "Arial",           // Should always be available
            "Times New Roman", // Should always be available
            "Aptos",           // Office 365 cloud font
            "Aptos Display",   // Office 365 cloud font
            "Source Sans 3",   // Custom font (downloaded)
            "Karla",           // Custom font (downloaded)
            "Bodoni MT Condensed",
            "Avenir Next LT Pro",
            "Futura",
            "Franklin Gothic Medium",
            "Calibri Light",
            "Univers"
        };

        Console.WriteLine("Font availability check:");
        Console.WriteLine(new string('-', 50));

        // Use a RenderContext to test font availability (includes cloud fonts)
        var pageSettings = new PageSettings { WidthPoints = 612, HeightPoints = 792 };
        using var context = new RenderContext(pageSettings, 96);

        foreach (var fontName in testFonts)
        {
            var typeface = context.GetTypeface(fontName, false, false);
            var available = typeface.FamilyName == fontName;
            var status = available ? "OK" : $"MISSING (using {typeface.FamilyName})";
            Console.WriteLine($"{fontName,-30} {status}");
        }
    }
}

/// <summary>
/// Simple console output helper for tests.
/// </summary>
public class ConsoleTestOutputHelper : ITestOutputHelper
{
    public void WriteLine(string message) => Console.WriteLine(message);
    public void WriteLine(string format, params object[] args) => Console.WriteLine(format, args);
}

/// <summary>
/// Interface for test output.
/// </summary>
public interface ITestOutputHelper
{
    void WriteLine(string message);
    void WriteLine(string format, params object[] args);
}
