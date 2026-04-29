#if DEBUG
public class FontFileCacheTests
{
    // Use an in-memory extractor that maps known "file paths" to family names, so the
    // cache logic can be exercised without needing real font files on disk.

    static FontFileCache Build(params (string path, string? family)[] files)
    {
        var map = files.ToDictionary(f => f.path, f => f.family, StringComparer.Ordinal);
        return new(files.Select(f => f.path), path => map[path] is { } name ? [name] : []);
    }

    [Test]
    public async Task TryGet_ByExactFamilyName_ReturnsFile()
    {
        var cache = Build(("a.ttf", "Arial"), ("b.ttf", "Georgia"));
        var found = cache.TryGet("Arial", out var faces);
        await Assert.That(found).IsTrue();
        await Assert.That(faces!.Single().Path).IsEqualTo("a.ttf");
    }

    [Test]
    public async Task TryGet_ByFamilyName_IsCaseInsensitive()
    {
        var cache = Build(("a.ttf", "Arial"));
        var found = cache.TryGet("arial", out var faces);
        await Assert.That(found).IsTrue();
        await Assert.That(faces!.Single().Path).IsEqualTo("a.ttf");
    }

    [Test]
    public async Task TryGet_UnknownName_ReturnsFalse()
    {
        var cache = Build(("a.ttf", "Arial"));
        var found = cache.TryGet("Times New Roman", out var faces);
        await Assert.That(found).IsFalse();
        await Assert.That(faces).IsNull();
    }

    [Test]
    public async Task TryGet_WithStyleSuffix_FallsBackToBaseFamily()
    {
        var cache = Build(("a.ttf", "Arial"));
        // StripWeightSuffixes("Arial Bold") → "Arial"
        var found = cache.TryGet("Arial Bold", out var faces);
        await Assert.That(found).IsTrue();
        await Assert.That(faces!.Single().Path).IsEqualTo("a.ttf");
    }

    [Test]
    public async Task TryGet_WithMultipleSuffixes_FallsBackToBaseFamily()
    {
        var cache = Build(("a.ttf", "Helvetica"));
        var found = cache.TryGet("Helvetica Bold Italic", out var faces);
        await Assert.That(found).IsTrue();
        await Assert.That(faces!.Single().Path).IsEqualTo("a.ttf");
    }

    [Test]
    public async Task TryGet_MultipleFilesForSameFamily_ReturnsAll()
    {
        var cache = Build(
            ("arial-regular.ttf", "Arial"),
            ("arial-bold.ttf", "Arial"),
            ("arial-italic.ttf", "Arial"));
        var found = cache.TryGet("Arial", out var faces);
        await Assert.That(found).IsTrue();
        await Assert.That(faces!.Length).IsEqualTo(3);
    }

    [Test]
    public async Task Constructor_SkipsFilesWhoseExtractorThrows()
    {
        var cache = new FontFileCache(
            ["good.ttf", "corrupt.ttf", "another.ttf"],
            path => path == "corrupt.ttf"
                ? throw new InvalidDataException("simulated bad font")
                : path switch {"good.ttf" => ["Arial"], "another.ttf" => ["Arial"], _ => []});

        var found = cache.TryGet("Arial", out var faces);
        await Assert.That(found).IsTrue();
        await Assert.That(faces!.Length).IsEqualTo(2);
    }

    [Test]
    public async Task Constructor_SkipsFilesWithNullOrEmptyFamilyName()
    {
        var cache = Build(
            ("good.ttf", "Arial"),
            ("no-name.ttf", null),
            ("empty.ttf", ""));
        var found = cache.TryGet("Arial", out var faces);
        await Assert.That(found).IsTrue();
        await Assert.That(faces!.Single().Path).IsEqualTo("good.ttf");
    }

    [Test]
    public async Task Contains_MirrorsTryGet()
    {
        var cache = Build(("a.ttf", "Arial"));
        await Assert.That(cache.Contains("Arial")).IsTrue();
        await Assert.That(cache.Contains("Arial Bold")).IsTrue(); // stripped fallback
        await Assert.That(cache.Contains("Nothing")).IsFalse();
    }

    [Test]
    public async Task TryGet_FontNameCandidates_TriesEffectiveFirst()
    {
        var cache = Build(("a.ttf", "Arial"));
        var candidates = new FontNameCandidates("Arial", "Arial Semibold", "Arial");
        var found = cache.TryGet(candidates, out var faces);
        await Assert.That(found).IsTrue();
        await Assert.That(faces!.Single().Path).IsEqualTo("a.ttf");
    }

    [Test]
    public async Task TryGet_FontNameCandidates_FallsBackToOriginal()
    {
        // Effective miss, Original matches directly
        var cache = Build(("a.ttf", "Georgia"));
        var candidates = new FontNameCandidates("Missing", "Georgia", null);
        var found = cache.TryGet(candidates, out var faces);
        await Assert.That(found).IsTrue();
        await Assert.That(faces!.Single().Path).IsEqualTo("a.ttf");
    }

    [Test]
    public async Task TryGet_FontNameCandidates_FallsBackToStripped()
    {
        // Effective miss, Original miss, Stripped matches
        var cache = Build(("a.ttf", "Futura"));
        var candidates = new FontNameCandidates("Futura Light", "Futura Light", "Futura");
        var found = cache.TryGet(candidates, out var faces);
        await Assert.That(found).IsTrue();
        await Assert.That(faces!.Single().Path).IsEqualTo("a.ttf");
    }

    [Test]
    public async Task TryGet_FontNameCandidates_NoneMatch_ReturnsFalse()
    {
        var cache = Build(("a.ttf", "Arial"));
        var candidates = new FontNameCandidates("Zapfino", "Zapfino", null);
        var found = cache.TryGet(candidates, out var faces);
        await Assert.That(found).IsFalse();
        await Assert.That(faces).IsNull();
    }

    [Test]
    public async Task EnumerateCandidateNames_Effective_Only_WhenOthersAreSame()
    {
        var candidates = new FontNameCandidates("Arial", "Arial", null);
        var names = FontFileCache.EnumerateCandidateNames(candidates).ToList();
        await Assert.That(names).IsEquivalentTo(["Arial"]);
    }

    [Test]
    public async Task EnumerateCandidateNames_IncludesOriginalWhenDifferent()
    {
        var candidates = new FontNameCandidates("Arial", "Arial Medium", null);
        var names = FontFileCache.EnumerateCandidateNames(candidates).ToList();
        await Assert.That(names).IsEquivalentTo(["Arial", "Arial Medium"]);
    }

    [Test]
    public async Task EnumerateCandidateNames_IncludesStrippedWhenSet()
    {
        var candidates = new FontNameCandidates("Futura Light", "Futura Light", "Futura");
        var names = FontFileCache.EnumerateCandidateNames(candidates).ToList();
        await Assert.That(names).IsEquivalentTo(["Futura Light", "Futura"]);
    }

    [Test]
    public async Task EnumerateCandidateNames_FullySpecified_YieldsAllThree()
    {
        var candidates = new FontNameCandidates("Arial", "Arial Medium", "Arial");
        var names = FontFileCache.EnumerateCandidateNames(candidates).ToList();
        await Assert.That(names).IsEquivalentTo(["Arial", "Arial Medium", "Arial"]);
    }

    [Test]
    public async Task EmptyCache_ReturnsFalseForAnyLookup()
    {
        var cache = new FontFileCache([], _ => Array.Empty<string>());
        var found = cache.TryGet("Arial", out var faces);
        await Assert.That(found).IsFalse();
        await Assert.That(faces).IsNull();
    }

    [Test]
    public async Task FaceConstructor_IndexesEveryDeclaredName()
    {
        // One file declaring multiple names — every name resolves to the same face.
        var face = new FontFace { Path = "segoeuisl.ttf", Index = 0, Weight = 350, Width = 5, Italic = false };
        var cache = new FontFileCache(
            ["segoeuisl.ttf"],
            _ => [(face, new[] { "Segoe UI Semilight", "Segoe UI", "SegoeUI-Semilight" })]);

        await Assert.That(cache.TryGet("Segoe UI Semilight", out var byFull)).IsTrue();
        await Assert.That(byFull!.Single().Weight).IsEqualTo(350);

        await Assert.That(cache.TryGet("Segoe UI", out var byFamily)).IsTrue();
        await Assert.That(byFamily!.Single().Weight).IsEqualTo(350);

        await Assert.That(cache.TryGet("SegoeUI-Semilight", out var byPostScript)).IsTrue();
        await Assert.That(byPostScript!.Single().Weight).IsEqualTo(350);
    }

    [Test]
    public async Task FaceConstructor_FamilyAggregatesAllFacesOfThatFamily()
    {
        // Three faces of the same family — the family name resolves to all three;
        // the per-face Full Name resolves to just one.
        var regular = new FontFace { Path = "segoeui.ttf", Weight = 400 };
        var semilight = new FontFace { Path = "segoeuisl.ttf", Weight = 350 };
        var bold = new FontFace { Path = "segoeuib.ttf", Weight = 700 };

        var cache = new FontFileCache(
            [regular.Path, semilight.Path, bold.Path],
            file => file switch
            {
                "segoeui.ttf" => [(regular, new[] { "Segoe UI", "Segoe UI Regular" })],
                "segoeuisl.ttf" => [(semilight, new[] { "Segoe UI", "Segoe UI Semilight" })],
                "segoeuib.ttf" => [(bold, new[] { "Segoe UI", "Segoe UI Bold" })],
                _ => []
            });

        await Assert.That(cache.TryGet("Segoe UI", out var family)).IsTrue();
        await Assert.That(family!.Length).IsEqualTo(3);

        await Assert.That(cache.TryGet("Segoe UI Semilight", out var semi)).IsTrue();
        await Assert.That(semi!.Single().Weight).IsEqualTo(350);
    }
}
#endif
