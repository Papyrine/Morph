/// <summary>
/// Pins the baseline-ascent contract: the canonical measurer positions every baseline at OS/2
/// <c>usWinAscent</c>, deliberately ignoring the USE_TYPO_METRICS flag. Both halves of that are measured
/// decisions, twice reversed before landing here, so the test states them explicitly:
///
/// <para>1. Word lays text out with GDI metrics, which use usWinAscent regardless of the flag — a
/// flag-honouring rule was implemented and measured corpus-wide, and it moved the corpus AWAY from Word
/// (−0.0017, 90 documents regressing), so it was reverted.</para>
///
/// <para>2. "Match the production backends" is not even a well-defined alternative: SkiaSharp's reported
/// ascent is PLATFORM-DEPENDENT — usWinAscent on Windows (GDI-compatible), the hhea ascender on linux
/// (FreeType), which is what the container baselines were rendered with. An earlier version of this test
/// asserted canonical == SkiaSharp; it passed on the host and failed in the container with 46 mismatches.
/// Word is the one stable oracle, and usWinAscent is Word's behaviour.</para>
///
/// <para>This is deliberately table-driven (no backend font library), so it means the same thing on every
/// platform: a future change that reintroduces flag-awareness, or "fixes" the divergence from a backend
/// library, fails here with the rationale attached.</para>
/// </summary>
public class BaselineAscentContractTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    [Test]
    public async Task Every_baseline_sits_at_usWinAscent_ignoring_the_typo_flag()
    {
        var wrong = new List<string>();
        int withOs2 = 0, flagged = 0;
        foreach (var path in Directory.GetFiles(fontsDirectory, "*.ttf").Order())
        {
            var metrics = FontMetricsReader.Read(path);
            if (metrics == null)
            {
                continue;
            }

            if (metrics.WinAscent == 0)
            {
                // No OS/2 table: the fallback is the hhea ascender.
                if (metrics.BaselineAscentUnits != metrics.Ascender)
                {
                    wrong.Add($"{Path.GetFileName(path)}: no OS/2 but baseline != hhea ascender");
                }

                continue;
            }

            withOs2++;
            if (metrics.UseTypoMetrics)
            {
                flagged++;
            }

            if (metrics.BaselineAscentUnits != metrics.WinAscent)
            {
                wrong.Add($"{Path.GetFileName(path)}: baseline {metrics.BaselineAscentUnits} != usWinAscent {metrics.WinAscent} (flagged={metrics.UseTypoMetrics})");
            }
        }

        await Assert.That(withOs2).IsGreaterThan(150);
        // The rule only means something if the corpus actually contains flagged fonts to ignore the flag on.
        await Assert.That(flagged).IsGreaterThan(3);
        await Assert.That(wrong).IsEmpty();
    }
}
