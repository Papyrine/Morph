#if DEBUG
using SixLabors.Fonts;

public class FontDetectionTests
{
    /// <summary>
    /// Rendering scenarios that reference fonts only available in some styles (e.g.
    /// Univers Regular + Bold but no Italic/BoldItalic) previously threw
    /// `Font not found` when the document requested the missing style. Both backends
    /// must now degrade gracefully to the closest available variant.
    /// business-plans/15 uses "Univers" and wedding/08 uses "Baskerville Old Face";
    /// both fonts are bundled in src/Fonts and installed as system fonts on CI.
    /// </summary>
    [Test]
    public async Task ImageSharpResolvesUnivers()
    {
        var converter = new WordRender.ImageSharp.DocumentConverter();
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "business-plans", "15", "input.docx");
        var data = converter.ConvertToImageData(input);
        await Assert.That(data.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task ImageSharpResolvesBaskervilleOldFace()
    {
        var converter = new WordRender.ImageSharp.DocumentConverter();
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "wedding", "08", "input.docx");
        var data = converter.ConvertToImageData(input);
        await Assert.That(data.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task SkiaResolvesUnivers()
    {
        var converter = new WordRender.Skia.DocumentConverter();
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "business-plans", "15", "input.docx");
        var data = converter.ConvertToImageData(input);
        await Assert.That(data.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task SkiaResolvesBaskervilleOldFace()
    {
        var converter = new WordRender.Skia.DocumentConverter();
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "wedding", "08", "input.docx");
        var data = converter.ConvertToImageData(input);
        await Assert.That(data.Count).IsGreaterThan(0);
    }

    /// <summary>
    /// The shared FontFileCache must index files from either backend's family-name
    /// extractor and return those files when queried by candidate name. This exercises
    /// the Dict<string, string[]> build + stripped-name fallback used by both.
    /// </summary>
    [Test]
    public async Task FontFileCacheFindsFontsByFamilyName()
    {
        var fontsDir = Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts");
        var fontsDirFull = Path.GetFullPath(fontsDir);
        if (!Directory.Exists(fontsDirFull))
        {
            return;
        }

        var files = Directory.GetFiles(fontsDirFull, "*.ttf");
        // Use SixLabors.Fonts as the family-name extractor (same as ImageSharp backend)
        var cache = new FontFileCache(files, f => new FontCollection().Add(f).Name);

        var univers = FontHelpers.GetCandidateNames("Univers", bold: false);
        var baskerville = FontHelpers.GetCandidateNames("Baskerville Old Face", bold: false);

        await Assert.That(cache.TryGet(univers, out _)).IsTrue();
        await Assert.That(cache.TryGet(baskerville, out _)).IsTrue();
    }
}
#endif
