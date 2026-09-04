/// <summary>
/// Pins the baseline-ascent contract: the canonical measurer positions every baseline at the line box
/// minus the font's descent, so the line gap / external leading stacks ABOVE the text and the descent is
/// what remains below it. The descent comes from the same metric family as the box —
/// <c>−sTypoDescender</c> when the font sets USE_TYPO_METRICS, <c>usWinDescent</c> otherwise.
///
/// <para>This replaced two earlier rules, both measured and both wrong: the hhea ascender (0.2 em high on
/// Calibri, whose hhea box is not its GDI cell) and then <c>usWinAscent</c> for every font regardless of
/// the flag — which coincided with Word on faces that carry no leading (Calibri, Segoe UI, Baskerville Old
/// Face) and was 0.071 em low on Aptos, 0.033 em on Arial, 0.19 em on Gabriola. Word-measured 2026-09-04
/// (<c>_probe_baseline</c> / <c>_probe_baseline2</c>: 23 faces at 24/48/96pt, one face and size per page
/// with spacing zeroed, first baseline read from the XPS <c>Glyphs</c> origin against the top margin) —
/// the table in <see cref="Word_measured_baselines_are_reproduced_on_the_120dpi_grid"/> is that data for
/// the bundled faces.</para>
///
/// <para>Table-driven with no backend font library, so it means the same thing on every platform:
/// SkiaSharp's reported ascent is platform-dependent (usWinAscent on Windows, the hhea ascender under
/// FreeType), which is why the engine's rule anchors to Word rather than to a backend.</para>
/// </summary>
public class BaselineAscentContractTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    [Test]
    public async Task Every_baseline_sits_at_the_line_box_minus_the_descent()
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

            int descent;
            if (metrics.WinAscent == 0)
            {
                // No OS/2 table: the hhea box and descender are all there is.
                descent = -metrics.Descender;
            }
            else
            {
                withOs2++;
                if (metrics.UseTypoMetrics)
                {
                    flagged++;
                    descent = -metrics.TypoDescender;
                }
                else
                {
                    descent = metrics.WinDescent;
                }
            }

            if (metrics.DescentUnits != descent)
            {
                wrong.Add($"{Path.GetFileName(path)}: descent {metrics.DescentUnits} != {descent} (flagged={metrics.UseTypoMetrics})");
            }

            if (metrics.BaselineAscentUnits != metrics.LineBoxUnits - descent)
            {
                wrong.Add($"{Path.GetFileName(path)}: baseline {metrics.BaselineAscentUnits} != box {metrics.LineBoxUnits} − descent {descent}");
            }
        }

        await Assert.That(withOs2).IsGreaterThan(150);
        // The rule only means something if the corpus actually contains flagged fonts to take the typo side on.
        await Assert.That(flagged).IsGreaterThan(3);
        await Assert.That(wrong).IsEmpty();
    }

    /// <summary>
    /// Word's first-line baselines, in pixels on its 120-dpi layout grid below the top margin, for the
    /// bundled faces the probe covered (the bundled files carry the same vertical metrics as the installed
    /// ones Word rendered with). The model is <c>round(box px) − round(descent px)</c>; three rows are
    /// one pixel under it (Verdana 96pt, Book Antiqua 48pt, Century Schoolbook 96pt — Word rounds a
    /// .53–.59 descent fraction down there where it rounds Aptos's .54 up), which is the residual the
    /// contract tolerates and counts.
    /// </summary>
    public static IEnumerable<(string File, int SizePoints, int WordPixels)> WordMeasuredBaselines() =>
    [
        ("Aptos_400.ttf", 24, 38), ("Aptos_400.ttf", 48, 75), ("Aptos_400.ttf", 96, 150),
        ("Calibri_400.ttf", 24, 38), ("Calibri_400.ttf", 48, 77), ("Calibri_400.ttf", 96, 152),
        ("Arial_400.ttf", 24, 38), ("Arial_400.ttf", 48, 75), ("Arial_400.ttf", 96, 150),
        ("Times_New_Roman_400.ttf", 24, 37), ("Times_New_Roman_400.ttf", 48, 75), ("Times_New_Roman_400.ttf", 96, 149),
        ("Segoe_UI_400.ttf", 24, 43), ("Segoe_UI_400.ttf", 48, 86), ("Segoe_UI_400.ttf", 96, 173),
        ("Bahnschrift_400.ttf", 24, 40), ("Bahnschrift_400.ttf", 48, 80), ("Bahnschrift_400.ttf", 96, 159),
        ("Georgia_400.ttf", 24, 36), ("Georgia_400.ttf", 48, 73), ("Georgia_400.ttf", 96, 147),
        ("Verdana_400.ttf", 24, 41), ("Verdana_400.ttf", 48, 80), ("Verdana_400.ttf", 96, 161),
        ("Tahoma_400.ttf", 24, 40), ("Tahoma_400.ttf", 48, 80), ("Tahoma_400.ttf", 96, 160),
        ("Constantia_400.ttf", 24, 38), ("Constantia_400.ttf", 48, 77), ("Constantia_400.ttf", 96, 152),
        ("Century_Gothic_400.ttf", 24, 40), ("Century_Gothic_400.ttf", 48, 80), ("Century_Gothic_400.ttf", 96, 161),
        ("Trebuchet_MS_400.ttf", 24, 37), ("Trebuchet_MS_400.ttf", 48, 75), ("Trebuchet_MS_400.ttf", 96, 150),
        ("Garamond_400.ttf", 24, 34), ("Garamond_400.ttf", 48, 69), ("Garamond_400.ttf", 96, 138),
        ("Candara_400.ttf", 24, 38), ("Candara_400.ttf", 48, 77), ("Candara_400.ttf", 96, 152),
        ("Consolas_400.ttf", 24, 37), ("Consolas_400.ttf", 48, 74), ("Consolas_400.ttf", 96, 147),
        ("Book_Antiqua_400.ttf", 24, 39), ("Book_Antiqua_400.ttf", 48, 77), ("Book_Antiqua_400.ttf", 96, 154),
        ("Century_Schoolbook_400.ttf", 24, 39), ("Century_Schoolbook_400.ttf", 48, 79), ("Century_Schoolbook_400.ttf", 96, 158),
        ("Baskerville_Old_Face_400.ttf", 24, 36), ("Baskerville_Old_Face_400.ttf", 48, 70), ("Baskerville_Old_Face_400.ttf", 96, 142)
    ];

    [Test]
    public async Task Word_measured_baselines_are_reproduced_on_the_120dpi_grid()
    {
        var offByOne = new List<string>();
        var worse = new List<string>();
        var rows = 0;
        foreach (var (file, sizePoints, wordPixels) in WordMeasuredBaselines())
        {
            var metrics = FontMetricsReader.Read(Path.Combine(fontsDirectory, file));
            await Assert.That(metrics).IsNotNull();
            rows++;

            // The box snaps to the grid per line in Word; the engine keeps it fractional (its pitch is
            // accumulate-then-snap on Word's side), so compare the engine's fractional answer rounded once.
            var enginePixels = (int) Math.Round(metrics!.BaselineAscentPoints(sizePoints) * CanonicalTextMeasurer.ReferenceDpi / 72.0, MidpointRounding.AwayFromZero);
            var delta = enginePixels - wordPixels;
            if (delta == 0)
            {
                continue;
            }

            (Math.Abs(delta) == 1 ? offByOne : worse).Add($"{file} {sizePoints}pt: engine {enginePixels}px, Word {wordPixels}px");
        }

        await Assert.That(rows).IsEqualTo(54);
        await Assert.That(worse).IsEmpty();
        // The three known rounding residues, and nothing else — a fourth means the model moved.
        await Assert.That(offByOne.Count).IsEqualTo(3);
        await Assert.That(offByOne.Any(_ => _.StartsWith("Verdana_400.ttf 96pt"))).IsTrue();
        await Assert.That(offByOne.Any(_ => _.StartsWith("Book_Antiqua_400.ttf 48pt"))).IsTrue();
        await Assert.That(offByOne.Any(_ => _.StartsWith("Century_Schoolbook_400.ttf 96pt"))).IsTrue();
    }
}
