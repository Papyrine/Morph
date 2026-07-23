/// <summary>
/// Every font family the corpus uses in a BOLD run should resolve to a face of weight 700 or above,
/// so the backends draw a real bold rather than approximating one.
///
/// Where no bold face is bundled, Skia falls back to synthetic bold (dilating the regular outline)
/// and ImageSharp, which has no equivalent, renders at normal weight — so the two backends disagree
/// and neither matches Word. Measured over ten such families at six sizes, Word's designed bold adds
/// ~40% ink where dilation adds ~78%, with the error varying per typeface rather than per size; no
/// stroke width reconciles them, because a designed bold redraws letterforms instead of fattening
/// them. See <c>docs/word-features.md</c> (Bold) and <c>src/Fonts/README.md</c>.
///
/// The gap is therefore pinned rather than asserted away. Adding a real bold face to
/// <c>src/Fonts</c> closes an entry with no code change — both backends gate synthesis on the
/// resolved weight — and this test then fails until the entry is deleted.
/// </summary>
public class BundledBoldCoverageTests
{
    /// <summary>
    /// Families used bold by the corpus that resolve to a face lighter than 700, mapped to the
    /// weight that currently resolves. Two distinct causes share this list:
    ///
    /// <list type="bullet">
    /// <item>No face at 700+ is bundled for the family. A licensing gap rather than a code one:
    /// most are proprietary (Microsoft, Monotype/ITC, Linotype) and cannot be redistributed here,
    /// though Playfair Display, Work Sans, Lato and Source Sans are under open licences.</item>
    /// <item>The requested name carries its own weight suffix, which
    /// <see cref="FontHelpers.ResolveTargetWeight"/> deliberately lets outrank the bold flag so a
    /// bold run in "Segoe UI Semilight" keeps the Semilight face. The nearest bundled face to that
    /// pinned target is then lighter than bold even when the family does own a 700 —
    /// "AvenirNext LT Pro Medium" pins 500 and lands on the 400.</item>
    /// </list>
    ///
    /// Either way the backends see a sub-bold face and synthesise, so the entry belongs here; the
    /// comment records which cause applies.
    /// </summary>
    static readonly Dictionary<string, int> knownMissingBold = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Arial Rounded MT Bold"] = 400,         // 1 scenario, resumes/03 — sole face, and its OS/2
                                                 // usWeightClass is 400 despite the name saying Bold
        ["Avenir Next LT Pro Light"] = 400,      // 2 scenarios, e.g. business-plans/13
        ["AvenirNext LT Pro Medium"] = 400,      // 1 scenario, newsletters/07 — suffix pins 500, and
                                                 // the 400 face is nearer that than the bundled 700
        ["Bahnschrift"] = 400,                   // 4 scenarios, e.g. cover-letters/07
        ["Baskerville Old Face"] = 400,          // 2 scenarios, e.g. business-plans/05
        ["Batang"] = 400,                        // 1 scenario, newsletters/10
        ["Book Antiqua"] = 400,                  // 2 scenarios, e.g. agendas-minutes/16
        ["Bookman Old Style"] = 600,             // 2 scenarios, e.g. cards/02
        ["Calibri Light"] = 300,                 // 6 scenarios, e.g. business/03
        ["Cochocib Script Latin Pro"] = 400,     // 1 scenario, labels/15
        ["Euphemia"] = 400,                      // 1 scenario, newsletters/04
        ["Franklin Gothic Book"] = 400,          // 3 scenarios, e.g. resumes/07
        ["Franklin Gothic Demi"] = 600,          // 3 scenarios, e.g. brochures/02
        ["Franklin Gothic Medium"] = 400,        // 2 scenarios, e.g. cover-letters/05
        ["Franklin Gothic Medium Cond"] = 400,   // 1 scenario, wedding/05
        ["Impact"] = 400,                        // 2 scenarios, e.g. wordart
        ["Lato Light"] = 300,                    // 1 scenario, business-plans/04
        ["Lucida Sans Typewriter"] = 600,        // 1 scenario, menus/06
        ["Neue Haas Grotesk Text Pro"] = 400,    // 1 scenario, business-plans/06
        ["Playfair Display"] = 400,              // 1 scenario, business-plans/04
        ["Source Sans Pro Light"] = 400,         // 1 scenario, cards/19
        ["Sylfaen"] = 400,                       // 1 scenario, newsletters/04
        ["The Hand"] = 400,                      // 1 scenario, cards/08
        ["Trade Gothic Next"] = 400,             // 1 scenario, business-plans/03
        ["Trade Gothic Next Cond"] = 400,        // 1 scenario, business-plans/02
        ["Tw Cen MT"] = 400,                     // 1 scenario, agendas-minutes/02
        ["Work Sans"] = 400                      // 1 scenario, business-plans/06
    };

    [Test]
    public async Task EveryBoldFamilyInTheCorpusResolvesToARealBoldFace()
    {
        var offenders = ResolveBoldFamilies()
            .Where(_ => _.Weight < FontHelpers.BoldWeight)
            .Where(_ => !knownMissingBold.ContainsKey(_.Family))
            .Select(_ => $"{_.Family} (resolves to weight {_.Weight}, used by {_.Scenario})")
            .Order()
            .ToList();

        await Assert.That(offenders)
            .IsEmpty()
            .Because("a bold run resolved to a face lighter than bold. Bundle a bold face for the " +
                     "family, or add it to knownMissingBold with the scenarios that use it.");
    }

    [Test]
    public async Task NoStaleEntriesInTheKnownMissingList()
    {
        // Mirrors BaselineHealthTests' allow-list discipline: an entry that is no longer missing
        // must be deleted, so bundling a bold face cannot silently leave the record wrong.
        var resolved = ResolveBoldFamilies().ToDictionary(_ => _.Family, _ => _.Weight, StringComparer.OrdinalIgnoreCase);

        var stale = knownMissingBold.Keys
            .Where(family => !resolved.TryGetValue(family, out var weight) || weight >= FontHelpers.BoldWeight)
            .Order()
            .ToList();

        await Assert.That(stale)
            .IsEmpty()
            .Because("these families no longer resolve to a light face, or are no longer used bold " +
                     "by any scenario. Remove them from knownMissingBold.");
    }

    /// <summary>
    /// Every distinct family used by a non-empty bold run anywhere in the corpus, with the weight
    /// resolution picks for a bold request and one scenario that uses it.
    /// </summary>
    /// <remarks>
    /// The weight is read from the face's OS/2 <c>usWeightClass</c> via the same
    /// <see cref="FontFileCache"/> the resolver uses, NOT from a loaded
    /// <c>SKTypeface.FontStyle.Weight</c>. Skia's answer is platform-dependent for any family whose
    /// name embeds a style word: given the one bundled Arial Rounded file — family
    /// "Arial Rounded MT Bold", <c>usWeightClass</c> 400, REGULAR bit set — DirectWrite on Windows
    /// splits the name and reports family "Arial Rounded MT" at weight 700, while FreeType in the
    /// container reports 400. Same bytes, two answers, so a list pinned against one platform was
    /// guaranteed to fail on the other.
    /// </remarks>
    static IEnumerable<(string Family, int Weight, string Scenario)> ResolveBoldFamilies()
    {
        var inputs = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
        var fontCache = new FontFileCache(
            FontCacheLoader.EnumerateFontFilesInDirectory(ProjectFonts.Directory, recursive: true),
            OpenTypeReader.ReadFaces);

        var seen = new Dictionary<string, (int Weight, string Scenario)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(inputs, "input.docx", SearchOption.AllDirectories).Order())
        {
            var scenario = Path.GetRelativePath(inputs, Path.GetDirectoryName(file)!).Replace('\\', '/');
            ParsedDocument document;
            try
            {
                using var stream = File.OpenRead(file);
                document = new DocumentParser().Parse(stream);
            }
            catch
            {
                // A fixture the parser rejects is not this test's business.
                continue;
            }

            foreach (var run in Paragraphs(document.Elements).SelectMany(_ => _.Runs))
            {
                if (!run.Properties.Bold || run.Text.Trim().Length == 0)
                {
                    continue;
                }

                var family = run.Properties.FontFamily;
                if (seen.ContainsKey(family))
                {
                    continue;
                }

                var face = ResolveBoldFace(fontCache, family, run.Properties.Italic);
                if (face != null)
                {
                    seen[family] = (face.Weight, scenario);
                }

                // A family that resolves to nothing at all is the font-fallback tests' concern.
            }
        }

        return seen.Select(_ => (_.Key, _.Value.Weight, _.Value.Scenario)).ToList();
    }

    /// <summary>
    /// Mirrors <c>FontResolver.TryResolveDirectoryMode</c> for a bold request: best face by score,
    /// then the configured fallback when the direct match's weight is too far off. The fallback has
    /// to be included — a family that renders through Century Gothic and lands on a real 700 draws a
    /// real bold, so it is not a gap.
    /// </summary>
    static FontFace? ResolveBoldFace(FontFileCache cache, string family, bool italic)
    {
        var candidates = FontHelpers.GetCandidateNames(family, true);
        var face = BestFace(cache, candidates, FontHelpers.ResolveTargetWeight(family, true), italic, out var delta);

        var fallbackName = FontHelpers.FindFallback(candidates);
        if (fallbackName == null ||
            (face != null && delta < weightFallbackThreshold))
        {
            return face;
        }

        var fallbackCandidates = FontHelpers.GetCandidateNames(fallbackName, true);
        var fallback = BestFace(
            cache,
            fallbackCandidates,
            FontHelpers.ResolveTargetWeight(fallbackName, true),
            italic,
            out var fallbackDelta);

        if (fallback != null &&
            (face == null || fallbackDelta < delta))
        {
            return fallback;
        }

        return face;
    }

    static FontFace? BestFace(
        FontFileCache cache,
        FontNameCandidates candidates,
        int targetWeight,
        bool italic,
        out int weightDelta)
    {
        weightDelta = int.MaxValue;
        if (!cache.TryGet(candidates, out var faces))
        {
            return null;
        }

        var face = FontHelpers.PickBestFace(faces, targetWeight, italic);
        if (face != null)
        {
            weightDelta = Math.Abs(face.Weight - targetWeight);
        }

        return face;
    }

    // Mirrors FontResolver's private const of the same name.
    const int weightFallbackThreshold = 300;

    static IEnumerable<ParagraphElement> Paragraphs(IEnumerable<DocumentElement> elements)
    {
        foreach (var element in elements)
        {
            if (element is ParagraphElement paragraph)
            {
                yield return paragraph;
            }
            else if (element is TableElement table)
            {
                var nested = table.Rows.SelectMany(_ => _.Cells).SelectMany(_ => _.Content);
                foreach (var inner in Paragraphs(nested))
                {
                    yield return inner;
                }
            }
        }
    }
}
