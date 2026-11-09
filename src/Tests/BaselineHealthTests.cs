/// <summary>
/// Promotion-time guard against degenerate scenario baselines. The Verify comparison the
/// scenario suite runs (<see cref="SkiaScenarioTests"/> / <see cref="ImageSharpScenarioTests"/>
/// and the PDF export snapshots) compares each rendered page against its own committed
/// <c>*.verified.png</c> — so once a broken page is promoted, AE and SSIM compare the baseline
/// against itself and stay green forever. A page-count mismatch against Word also suppresses
/// the per-page AE/SSIM diffs entirely, so such a scenario has no metric coverage but this guard.
/// A rendered document page essentially always carries anti-aliased text, so it has hundreds
/// of unique colours. A page that has collapsed to a single solid fill has a handful. Across
/// the whole corpus the two populations are cleanly separated: the degenerate pages sit at
/// 1–3 unique colours, the lowest healthy raster page at 159, with nothing in between. This
/// guard flags any paginated raster baseline (<c>{skia,imagesharp,pdf}_result#page_*.verified.png</c>)
/// whose unique-colour count has collapsed below <see cref="degenerateColorThreshold"/>.
/// Scope notes:
/// * Only paginated page renders are checked. The single-image HTML/Markdown export snapshots
///   (<c>html_result.verified.png</c> / <c>md_result.verified.png</c>) are excluded: a text
///   export can be legitimately empty (e.g. a labels sheet whose content is all in shapes the
///   text exporter doesn't traverse), so a low colour count there is not a reliable defect
///   signal.
/// * The threshold is deliberately low. It catches the "whole page collapsed to a solid fill"
///   class — the failure mode the Verify comparison is blind to. It is NOT a general
///   completeness check: a page that dropped most of its content but kept a chart still has
///   hundreds of colours and is out of scope here (that is what the AE/SSIM comparison against
///   the Word reference is for).
/// * PNG file size is not used: it is content- and encoder-dependent, far noisier than the
///   unique-colour count.
/// </summary>
public class BaselineHealthTests
{
    /// <summary>
    /// A raster page baseline with at most this many unique colours has collapsed to a
    /// near-solid fill and carries no rendered content. Observed corpus separation: the
    /// degenerate pages have 1–3 unique colours; the lowest healthy raster page has 159. 16
    /// sits in that empty band with generous headroom against a future legitimately sparse
    /// page while still catching a solid fill plus a little hard-edged chrome.
    /// </summary>
    const int degenerateColorThreshold = 16;

    /// <summary>
    /// Baselines that are allowed to be degenerate, keyed by their path relative to the format root
    /// (<c>Inputs/word/</c> and friends) with forward slashes. Two categories, kept distinct on purpose:
    /// 1. Intentionally blank — Word itself renders the page empty, so the collapse is
    ///    correct and permanent.
    /// 2. Known regressions — a real defect that is tracked elsewhere and not yet fixed.
    ///    These entries are temporary; delete them when the underlying render is fixed and
    ///    the baseline regenerated.
    ///
    /// The test also asserts that every entry here is STILL degenerate, so a fixed-and-
    /// regenerated page forces its own removal from this list instead of rotting here.
    /// </summary>
    static readonly HashSet<string> knownDegenerate =
    [
        with(StringComparer.OrdinalIgnoreCase),
        // -- Intentionally blank (permanent) --
        // explicit_break_blank_page's second page is empty by design; Word renders it blank
        // too (the scenario ships expected_0002.png as a blank reference).
        "explicit_break_blank_page/skia_result#page_0002.verified.png",
        "explicit_break_blank_page/imagesharp_result#page_0002.verified.png",
        "explicit_break_blank_page/pdf_result#page_0002.verified.png",
        // section_numbering_even_odd's fourth page is Word's parity filler: a section restarting its
        // numbering at 1 on an even page gets a bare page first under w:evenAndOddHeaders, with no
        // header or footer (its expected_0004.png is blank too).
        "section_numbering_even_odd/skia_result#page_0004.verified.png",
        "section_numbering_even_odd/imagesharp_result#page_0004.verified.png",
        "section_numbering_even_odd/pdf_result#page_0004.verified.png",
        // basic-business-invoice spills a single banded row onto a second page, and Excel does the
        // same: its own expected_0002.png has 14 unique colours and one 5px ink band at rows
        // 116-120, against the render's 13-15 colours and a band at 116-119. A near-empty page is
        // the CORRECT output here, so the collapse is not a defect to chase. Its colour count
        // straddles the threshold as anti-aliasing shifts, without the content changing, so all
        // three backends are listed.
        "basic-business-invoice/skia_result#page_0002.verified.png",
        "basic-business-invoice/imagesharp_result#page_0002.verified.png",
        "basic-business-invoice/pdf_result#page_0002.verified.png",
        // weekly-lesson-planner carries its trailing empty rows onto a second page (docs/word-features.md
        // Multi-page Tables rule 8). Excel prints that page too: its expected_0002.png is near-empty.
        // Correct output.
        "weekly-lesson-planner/skia_result#page_0002.verified.png",
        "weekly-lesson-planner/imagesharp_result#page_0002.verified.png",
        "weekly-lesson-planner/pdf_result#page_0002.verified.png"
    ];

    public static IEnumerable<string> GetScenarioDirectories() => ScenarioInputs.AllDirectories();

    [Test]
    [MethodDataSource(nameof(GetScenarioDirectories))]
    public async Task RasterPageBaselinesAreNotDegenerate(string directory)
    {
        // "*_result#page_*.verified.png" matches exactly the paginated raster backends
        // (skia_/imagesharp_/pdf_); the html_result / md_result export snapshots carry no
        // "#page_" segment and are correctly excluded.
        var baselines = Directory.GetFiles(directory, "*_result#page_*.verified.png").Order();

        var problems = new List<string>();
        foreach (var file in baselines)
        {
            var relative = $"{ScenarioInputs.ScenarioName(directory)}/{Path.GetFileName(file)}";
            var colors = CountColorsUpTo(file, degenerateColorThreshold);
            var suppressed = knownDegenerate.Contains(relative);

            if (suppressed)
            {
                if (colors > degenerateColorThreshold)
                {
                    problems.Add(
                        $"{relative}: on the known-degenerate allow-list but now has more than " +
                        $"{degenerateColorThreshold} unique colours — it is no longer degenerate. If it " +
                        $"was fixed, remove it from {nameof(knownDegenerate)}.");
                }

                continue;
            }

            if (colors <= degenerateColorThreshold)
            {
                problems.Add(
                    $"{relative}: only {colors} unique colours — the page has collapsed to a near-solid " +
                    "fill with no rendered content. The Verify suite cannot catch this because AE and " +
                    "SSIM compare the baseline against itself. If the page is intentionally blank, add " +
                    $"it to {nameof(knownDegenerate)}; otherwise this is a rendering regression that " +
                    "must be fixed before the baseline is promoted.");
            }
        }

        await Assert.That(problems).IsEmpty()
            .Because(Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// Distinct pixel colours in the page, counted from the decoded pixels (the same vendored
    /// decoder <see cref="PageComparison"/> uses, so the whole suite stays off Magick — see
    /// docs/fidelity-audit.md).
    /// Counting stops once more than <paramref name="cap"/> distinct colours have been seen: the
    /// guard only needs to tell "collapsed to a solid fill" from "has content", and a healthy page
    /// hits the cap within its first few hundred pixels, which keeps a full-corpus scan quick. The
    /// result is therefore exact up to <paramref name="cap"/>, and <c>cap + 1</c> means "more".
    /// </summary>
    static int CountColorsUpTo(string file, int cap)
    {
        using var stream = File.OpenRead(file);
        var image = PngDecoder.Decode(stream);
        var rgba = image.Rgba;
        var seen = new HashSet<uint>();
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var color = (uint) ((rgba[i] << 24) | (rgba[i + 1] << 16) | (rgba[i + 2] << 8) | rgba[i + 3]);
            if (seen.Add(color) &&
                seen.Count > cap)
            {
                return cap + 1;
            }
        }

        return seen.Count;
    }
}
