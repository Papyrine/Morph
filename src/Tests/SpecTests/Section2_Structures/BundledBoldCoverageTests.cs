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
    /// Families used bold by the corpus for which no face at weight 700+ is bundled, mapped to the
    /// weight that currently resolves. Entries are a licensing gap, not a code one: most are
    /// proprietary (Microsoft, Monotype/ITC, Linotype) and cannot be redistributed here, though
    /// Playfair Display, Work Sans, Lato and Source Sans are under open licences.
    /// </summary>
    static readonly Dictionary<string, int> knownMissingBold = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Avenir Next LT Pro Light"] = 400,      // 2 scenarios, e.g. business-plans/13
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
    /// the shared resolver picks for a bold request and one scenario that uses it.
    /// </summary>
    static IEnumerable<(string Family, int Weight, string Scenario)> ResolveBoldFamilies()
    {
        var inputs = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
        var pageSettings = new PageSettings {WidthPoints = 612, HeightPoints = 792};
        using var context = new SkiaRenderContext(pageSettings, 150, fontDirectory: ProjectFonts.Directory);

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

                try
                {
                    seen[family] = (context.GetTypeface(family, true, run.Properties.Italic).FontStyle.Weight, scenario);
                }
                catch
                {
                    // Unresolvable families are the font-fallback tests' concern.
                }
            }
        }

        return seen.Select(_ => (_.Key, _.Value.Weight, _.Value.Scenario)).ToList();
    }

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
