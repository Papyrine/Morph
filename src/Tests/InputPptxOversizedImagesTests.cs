/// <summary>
/// No deck under <c>Tests\Inputs\powerpoint\</c> may carry artwork that costs more stored bits than
/// the slide can ever show pixels — see <see cref="DeckImages"/> for how the drawn size is resolved.
///
/// The corpus was cut from 31.7 MB to 20.1 MB this way: these decks are Microsoft gallery templates,
/// and they ship artwork at a consistent 1.5625x the size it is drawn at, on top of print-grade JPEG
/// quality. Slides render at 96 DPI (<see cref="ScenarioInputs.Dpi"/>), so none of that resolution
/// ever reaches a rendered pixel. The saving was much larger than the decks themselves: the
/// HTML/Markdown export snapshots inline every slide's artwork, so they fell by a further 37.4 MB,
/// and the embedded PDF baselines by 3.4 MB — 55.1 MB of repository weight in total.
///
/// The budget is <see cref="maxBitsPerDrawnPixel"/> bits per drawn pixel rather than a cap on
/// oversampling, because oversampling alone does not find this weight. The gallery templates cluster
/// tightly at 1.56x both before and after the cut, so a ratio threshold separates nothing; what
/// actually varied was encoding quality. Bits-per-drawn-pixel folds both failure modes into one
/// number — too many pixels for the box, and too many bits for each of those pixels — and is cheap
/// and deterministic to compute.
///
/// Oversampling still gates the budget, though, via <see cref="minOversampleToJudge"/>: an image
/// already stored at the size it is drawn has nothing left to give except lossier encoding, and some
/// of this corpus is legitimately dense artwork at 1:1 — detailed RGBA illustrations drawn small,
/// where quantising further would band. Exempting those structurally beats listing them by name.
///
/// What the guard is worth, measured by running it against the corpus as it was: it fails 8 of the
/// 26 decks and flags 32 images holding 6.4 MB. It is a ratchet against the worst of what a gallery
/// template brings, not a proof that a deck is optimal — a deck can sit under the budget and still
/// have room, as 18 of these did.
///
/// This guard exists because that weight arrives silently. A deck downloaded from the gallery brings
/// whatever its author saved, and nothing about how it renders would say so — the same reasoning as
/// <see cref="InputDocxUnusedPartsTests"/>, which strips the DOCX corpus of parts nothing reads.
///
/// Re-cutting a deck is not a matter of running an encoder over it: shrinking artwork changes what
/// PowerPoint itself renders, so <c>expected_*.png</c> has to be regenerated through RenderHelper and
/// every Verify baseline with it. Judge the result on the rendered page, not the source image — one
/// deck cleared a 36 dB per-image bar on every picture and still fell to 24 dB on the page, because
/// it recolours a photo through a duotone that turns invisible JPEG noise into visible banding.
/// </summary>
public class InputPptxOversizedImagesTests
{
    /// <summary>
    /// The per-image budget, in stored bits per pixel the image can ever draw.
    ///
    /// Measured: once <see cref="minOversampleToJudge"/> has excluded the dense 1:1 artwork, 40 of
    /// the corpus's 312 images are in scope and the heaviest costs 10.2, so 12 clears the corpus
    /// outright with headroom. For scale, an image stored at exactly its drawn size costs 24 bits per
    /// drawn pixel uncompressed, so the budget asks only that oversampled artwork be compressed at
    /// all — the corpus median is far below it.
    ///
    /// Tightening to 10 would fail one image, in <c>sketchlines</c>, that cannot be cut further
    /// without visible damage: it is the deck that needed the strictest fidelity gate of the corpus.
    /// </summary>
    const double maxBitsPerDrawnPixel = 12;

    /// <summary>
    /// How much more resolution than it can draw an image must carry before the budget judges it.
    /// The margin over 1.0 is there to keep floating-point noise in the drawn-size arithmetic from
    /// pulling exactly-1:1 images into scope, not to grant real slack.
    /// </summary>
    const double minOversampleToJudge = 1.05;

    /// <summary>
    /// Images allowed to exceed the budget, keyed by <c>{scenario}/{pixels}/{bytes}</c>.
    ///
    /// Empty, and worth keeping that way. It briefly held five images that the first cut had sized
    /// with a standalone OOXML reader — one that cannot resolve geometry a picture inherits from its
    /// layout placeholder, so it handed those the whole slide box, read them as already small enough
    /// and left them uncut. Re-cutting the corpus against <see cref="DeckImages"/>'s drawn sizes,
    /// which come from Morph's own parse, cleared all five and took a further 1.3 MB out of the decks
    /// with them.
    ///
    /// Keys carry byte counts, so any re-encoding invalidates them, and the test asserts every entry
    /// still matches an over-budget image — a deck that gets fixed forces its own entries out of this
    /// list rather than leaving them to rot. That is what emptied it.
    /// </summary>
    static readonly HashSet<string> overBudget = [];

    public static IEnumerable<string> GetDeckDirectories() =>
        ScenarioInputs.Directories(ScenarioFormat.PowerPoint);

    [Test]
    [MethodDataSource(nameof(GetDeckDirectories))]
    public async Task NoDeckShipsArtworkAboveTheDrawnPixelBudget(string directory)
    {
        var scenario = ScenarioInputs.ScenarioName(directory);
        var problems = new List<string>();
        var matched = new HashSet<string>();

        foreach (var image in DeckImages.Drawn(ScenarioInputs.InputFile(directory)))
        {
            if (image.Oversample <= minOversampleToJudge)
            {
                continue;
            }

            var key = $"{scenario}/{image.Key}";
            var bits = image.BitsPerDrawnPixel;

            if (overBudget.Contains(key))
            {
                matched.Add(key);

                if (bits <= maxBitsPerDrawnPixel)
                {
                    problems.Add(
                        $"{key}: on the {nameof(overBudget)} allow-list but now costs only " +
                        $"{bits:F1} bits per drawn pixel — it is within budget. Remove the entry.");
                }

                continue;
            }

            if (bits > maxBitsPerDrawnPixel)
            {
                problems.Add(
                    $"{key}: {bits:F1} bits per drawn pixel against a budget of " +
                    $"{maxBitsPerDrawnPixel}. It stores {image.PixelWidth}x{image.PixelHeight} to " +
                    $"fill a {image.DrawnWidth:F0}x{image.DrawnHeight:F0} box ({image.Oversample:F2}x " +
                    $"oversampled) at {image.Bytes:N0} bytes — \"{image.Description}\". Downscale it " +
                    "to its drawn size and re-encode, then regenerate this deck's references and " +
                    "baselines.");
            }
        }

        problems.AddRange(overBudget
            .Where(_ => _.StartsWith($"{scenario}/", StringComparison.Ordinal))
            .Except(matched)
            .Select(_ =>
                $"{_}: on the {nameof(overBudget)} allow-list but no such image is in the deck any " +
                "more — the entry is stale and must be removed."));

        await Assert.That(problems).IsEmpty()
            .Because(
                $"{problems.Count} image budget problem(s) in {scenario}:{Environment.NewLine}  " +
                string.Join($"{Environment.NewLine}  ", problems));
    }
}
