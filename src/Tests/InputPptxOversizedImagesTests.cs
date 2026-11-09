/// <summary>
/// No deck under <c>Tests\Inputs\powerpoint\</c> may carry artwork that costs more stored bits than
/// the slide can ever show pixels — see <see cref="DeckImages"/> for how the drawn size is resolved.
///
/// These decks are Microsoft gallery templates, which ship artwork at a consistent 1.5625x the size
/// it is drawn at, on top of print-grade JPEG quality. Slides render at 96 DPI
/// (<see cref="ScenarioInputs.Dpi"/>), so none of that resolution ever reaches a rendered pixel, yet
/// the HTML/Markdown and PDF snapshots inline it all again.
///
/// The budget is <see cref="maxBitsPerDrawnPixel"/> bits per drawn pixel rather than a cap on
/// oversampling, because the gallery templates cluster tightly at 1.56x, so a ratio threshold
/// separates nothing; what varies is encoding quality. Bits-per-drawn-pixel folds both failure modes into one
/// number — too many pixels for the box, and too many bits for each of those pixels — and is cheap
/// and deterministic to compute.
///
/// Oversampling still gates the budget, though, via <see cref="minOversampleToJudge"/>: an image
/// already stored at the size it is drawn has nothing left to give except lossier encoding, and some
/// of this corpus is legitimately dense artwork at 1:1 — detailed RGBA illustrations drawn small,
/// where quantising further would band. Exempting those structurally beats listing them by name.
///
/// It is a ratchet against the worst of what a gallery template brings, not a proof that a deck is
/// optimal — a deck can sit under the budget and still have room.
///
/// This guard exists because that weight arrives silently. A deck downloaded from the gallery brings
/// whatever its author saved, and nothing about how it renders would say so — the same reasoning as
/// <see cref="InputDocxUnusedPartsTests"/>, which strips the DOCX corpus of parts nothing reads.
///
/// Re-cutting a deck is not a matter of running an encoder over it: shrinking artwork changes what
/// PowerPoint itself renders, so <c>expected_*.png</c> has to be regenerated through RenderHelper and
/// every Verify baseline with it. Judge the result on the rendered page, not the source image: a
/// duotone recolouring can turn invisible JPEG noise into visible banding.
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
    /// Empty, and worth keeping that way.
    ///
    /// Keys carry byte counts, so any re-encoding invalidates them, and the test asserts every entry
    /// still matches an over-budget image — a deck that gets fixed forces its own entries out of this
    /// list rather than leaving them to rot.
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
