#if DEBUG
public class FontCacheLoaderTests
{
    [Test]
    public async Task EnumerateFontFilesInDirectory_NonExistentDirectory_ReturnsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var files = FontCacheLoader.EnumerateFontFilesInDirectory(missing).ToList();
        await Assert.That(files).IsEmpty();
    }

    [Test]
    public async Task EnumerateFontFilesInDirectory_ReturnsTtfOtfAndTtcOnly()
    {
        var dir = CreateTempDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "a.ttf"), "");
            await File.WriteAllTextAsync(Path.Combine(dir, "b.otf"), "");
            await File.WriteAllTextAsync(Path.Combine(dir, "c.ttc"), "");
            await File.WriteAllTextAsync(Path.Combine(dir, "ignore.txt"), "");
            await File.WriteAllTextAsync(Path.Combine(dir, "ignore.fon"), "");

            var files = FontCacheLoader.EnumerateFontFilesInDirectory(dir)
                .Select(_ => Path.GetFileName(_))
                .OrderBy(_ => _)
                .ToList();

            await Assert.That(files).IsEquivalentTo(["a.ttf", "b.otf", "c.ttc"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task EnumerateFontFilesInDirectory_IsCaseInsensitiveOnExtension()
    {
        var dir = CreateTempDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "upper.TTF"), "");
            await File.WriteAllTextAsync(Path.Combine(dir, "mixed.Otf"), "");

            var files = FontCacheLoader.EnumerateFontFilesInDirectory(dir)
                .Select(_ => Path.GetFileName(_))
                .OrderBy(_ => _)
                .ToList();

            await Assert.That(files).IsEquivalentTo(["mixed.Otf", "upper.TTF"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task EnumerateFontFilesInDirectory_NonRecursive_IgnoresSubdirectories()
    {
        var dir = CreateTempDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "top.ttf"), "");
            var sub = Path.Combine(dir, "sub");
            Directory.CreateDirectory(sub);
            await File.WriteAllTextAsync(Path.Combine(sub, "nested.ttf"), "");

            var files = FontCacheLoader.EnumerateFontFilesInDirectory(dir)
                .Select(_ => Path.GetFileName(_))
                .ToList();

            await Assert.That(files).IsEquivalentTo(["top.ttf"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task EnumerateFontFilesInDirectory_Recursive_IncludesSubdirectories()
    {
        var dir = CreateTempDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "top.ttf"), "");
            var sub = Path.Combine(dir, "sub");
            Directory.CreateDirectory(sub);
            await File.WriteAllTextAsync(Path.Combine(sub, "nested.ttf"), "");
            var deeper = Path.Combine(sub, "deeper");
            Directory.CreateDirectory(deeper);
            await File.WriteAllTextAsync(Path.Combine(deeper, "deep.otf"), "");

            var files = FontCacheLoader.EnumerateFontFilesInDirectory(dir, recursive: true)
                .Select(_ => Path.GetFileName(_))
                .OrderBy(_ => _)
                .ToList();

            await Assert.That(files).IsEquivalentTo(["deep.otf", "nested.ttf", "top.ttf"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task GetSystemFontFiles_OnWindows_ReturnsFontsFromWindowsFontsDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var files = FontCacheLoader.GetSystemFontFiles().ToList();

        // %WINDIR%\Fonts is always populated on Windows.
        await Assert.That(files).IsNotEmpty();

        var systemFontsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Fonts");

        foreach (var file in files)
        {
            await Assert.That(file).StartsWith(systemFontsDir);
            var ext = Path.GetExtension(file);
            await Assert.That(
                ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
    }

    static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "morph-fontloader-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
#endif
