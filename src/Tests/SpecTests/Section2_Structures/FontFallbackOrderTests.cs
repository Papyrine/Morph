extern alias Skia;
extern alias ImageSharp;

/// <summary>
/// Pins the order the shared <c>FontResolver</c> walks its fallbacks in: the curated
/// <see cref="FontHelpers.FontFallbacks"/> alias first, then the caller's <c>FontFallback</c>. The
/// alias used to win outright, so where its target was absent the family threw rather than reach
/// the caller's fallback. business-plans/13 is set in Avenir Next LT Pro, which the alias maps to
/// Century Gothic; the Blazor app's font directory holds only Aptos and maps every other family to
/// it, and every page of that document failed to render there.
/// </summary>
public class FontFallbackOrderTests
{
    [Test]
    public async Task AliasTargetMissing_Skia_ResolvesToCallersFallback()
    {
        using var fonts = new FontSubset("Arial_400.ttf", "Arial_700.ttf");
        using var context = new SkiaRenderContext(new(), 96, fontFallback: _ => "Arial", fontDirectory: fonts.Directory);
        using var typeface = context.GetTypeface("Avenir Next LT Pro Demi", false, false);
        await Assert.That(typeface.FamilyName).IsEqualTo("Arial");
    }

    [Test]
    public async Task AliasTargetMissing_ImageSharp_ResolvesToCallersFallback()
    {
        using var fonts = new FontSubset("Arial_400.ttf", "Arial_700.ttf");
        using var context = new ImageSharpRenderContext(new(), 96, fontFallback: _ => "Arial", fontDirectory: fonts.Directory);
        var family = context.GetFontFamily("Avenir Next LT Pro Demi", false, false);
        await Assert.That(family.Name).IsEqualTo("Arial");
    }

    [Test]
    public async Task AliasTargetPresent_WinsWithoutAskingTheCaller()
    {
        using var fonts = new FontSubset("Arial_400.ttf", "Century_Gothic_400.ttf", "Century_Gothic_700.ttf");
        var asked = new List<string>();
        using var context = new SkiaRenderContext(
            new(),
            96,
            fontFallback: _ =>
            {
                asked.Add(_);
                return "Arial";
            },
            fontDirectory: fonts.Directory);
        using var typeface = context.GetTypeface("Avenir Next LT Pro", false, false);
        await Assert.That(typeface.FamilyName).IsEqualTo("Century Gothic");
        await Assert.That(asked).IsEmpty();
    }

    [Test]
    public async Task AliasTargetMissing_NoCallersFallback_ThrowsNamingTheDirectory()
    {
        using var fonts = new FontSubset("Arial_400.ttf");
        using var context = new SkiaRenderContext(new(), 96, fontDirectory: fonts.Directory);
        var exception = Assert.Throws<InvalidOperationException>(
            // ReSharper disable once AccessToDisposedClosure
            () => context.GetTypeface("Avenir Next LT Pro Demi", false, false));
        await Assert.That(exception.Message).Contains(fonts.Directory);
    }

    /// <summary>
    /// The Blazor app's configuration — a directory of the four Aptos faces and every other family
    /// sent to Aptos — painting both of business-plans/13's Avenir faces.
    /// </summary>
    [Test]
    public async Task AptosOnlyDirectory_DocumentInAvenirRenders()
    {
        using var fonts = new FontSubset("Aptos_400.ttf", "Aptos_400_Italic.ttf", "Aptos_700.ttf", "Aptos_700_Italic.ttf");
        var document = Doc(
            Para(AvenirRun("Avenir Next LT Pro")),
            Para(AvenirRun("Avenir Next LT Pro Demi")));
        var options = new ImageExportOptions
        {
            Dpi = 30,
            FontDirectory = fonts.Directory,
            FontFallback = _ => "Aptos"
        };
        var pages = ImageSharpDocumentConverter.RenderPagesCounted(document, options, _ => _(Stream.Null));
        await Assert.That(pages).IsEqualTo(1);
    }

    static Run AvenirRun(string fontFamily) =>
        new()
        {
            Text = "alpha",
            Properties = new()
            {
                FontFamily = fontFamily,
                FontSizePoints = 11
            }
        };

    /// <summary>A temporary font directory holding only the named files from <c>src/Fonts</c>.</summary>
    sealed class FontSubset : IDisposable
    {
        public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("morph-font-subset-").FullName;

        public FontSubset(params string[] files)
        {
            foreach (var file in files)
            {
                File.Copy(Path.Combine(ProjectFonts.Directory, file), Path.Combine(Directory, file));
            }
        }

        public void Dispose() =>
            System.IO.Directory.Delete(Directory, true);
    }
}
