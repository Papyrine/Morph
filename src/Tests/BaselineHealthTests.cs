/// <summary>
/// Promotion-time guard against degenerate scenario baselines. The Verify comparison the
/// scenario suite runs (<see cref="SkiaScenarioTests"/> / <see cref="ImageSharpScenarioTests"/>
/// and the PDF export snapshots) compares each rendered page against its own committed
/// <c>*.verified.png</c> — so once a broken page is promoted, AE and SSIM compare the baseline
/// against itself and stay green forever. That blindness let a real regression through twice:
/// a metric-invisible thin-strip class, and — the case this guard is named for — four
/// <c>newsletters/06</c> Skia pages that collapsed to a solid navy fill with no content
/// (promoted broken at 8da1f624d, 2026-07-18) yet passed the suite because the baseline was
/// itself the broken image. That scenario is doubly instructive: it renders 6 pages against
/// Word's 4, and a page-count mismatch suppresses the per-page AE/SSIM diffs entirely, so it
/// had no metric coverage at all — this guard was the only thing watching it.
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
///
/// File-size collapse was considered as a second signal and rejected: PNG size is content- and
/// encoder-dependent (the solid-navy Skia pages were still ~18 KB), so it is far noisier than
/// the unique-colour count, which separates the two populations exactly.
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
        // basic-business-invoice spills a single banded row onto a second page, and Excel does the
        // same: its own expected_0002.png has 14 unique colours and one 5px ink band at rows
        // 116-120, against the render's 13-15 colours and a band at 116-119. A near-empty page is
        // the CORRECT output here, so the collapse is not a defect to chase.
        //
        // ImageSharp joined the list on 2026-08-14 and left it again on 2026-08-19, having crossed
        // the threshold in both directions without the page changing: it read 21 colours
        // (ImageSharp.Drawing 3.1 anti-aliasing the one band over more levels), then 14 when a
        // column-width change shifted where the band landed, then 21 again when the exact-row-fit
        // change (page_counts.md exp 22) shifted it back. Measured at the third flip: Skia 14
        // colours and ImageSharp 21, both inking the SAME 48 rows (72-119) against Excel's own 48
        // (73-120), and ImageSharp's page-2 SSIM moved 0.9851 -> 0.9951 — nearer Excel, not further.
        // It is not listed, for the reason to-do-list gives below: the threshold is what keeps
        // moving, not the render, and re-listing it only sets up the next spurious failure.
        "basic-business-invoice/skia_result#page_0002.verified.png",
        "basic-business-invoice/pdf_result#page_0002.verified.png",
        // -- Known regressions (temporary — remove when fixed) --
        // invoice-accessibility-guide's first sheet needs two landscape pages, and now gets them —
        // but the second comes out blank. That sheet's grid is twelve cells of narrow column-A text
        // Excel all but clips away; everything a reader sees is DRAWING (banner, contents list,
        // thumbnail), and SheetDrawingParser emits every drawing paragraph-anchored so it binds to
        // the sheet's FIRST page. So the split moves the invisible clipped text overleaf and leaves
        // the visible art behind. Excel's expected_0002.png is a full page (1573 colours, ink over
        // rows 72-761). Fixing it means paginating sheet drawings by their own anchor rows rather
        // than pinning them to the first page. Tracked in src/todo.md.
        // Note the metrics get BETTER when this page goes blank (AE 0.4842 to 0.1251, SSIM null to
        // 0.9365), because a white page differs from a sparse one less than a wrong page does —
        // exactly the blindness this guard exists to cover.
        "invoice-accessibility-guide/skia_result#page_0002.verified.png",
        "invoice-accessibility-guide/imagesharp_result#page_0002.verified.png",
        "invoice-accessibility-guide/pdf_result#page_0002.verified.png",
        // Horizontal pagination is not implemented. to-do-list spans columns A:Q and asks for no
        // fitToPage, so Excel prints it at 100% across TWO page strips, left and right. Morph prints
        // the left strip and clips the rest, then breaks vertically instead — which lands on the same
        // page count for the wrong reason and leaves the second page nearly empty. Page 1 is a fair
        // comparison; page 2 is the signature of the missing feature. Tracked in src/todo.md.
        //
        // ImageSharp is NOT listed, for the same reason as basic-business-invoice above: the sheet
        // asks for verticalCentered, and once that landed (2026-08-14) the strip moved into the
        // middle of the page, where ImageSharp anti-aliases its edges over enough extra levels to
        // read 17 colours. Skia sees 8 and PDF 13 on the same page, and the content is unchanged in
        // substance — still the same near-empty strip. Threshold moved, render did not.
        "to-do-list/skia_result#page_0002.verified.png",
        "to-do-list/pdf_result#page_0002.verified.png"
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
            if (seen.Add(color) && seen.Count > cap)
            {
                return cap + 1;
            }
        }

        return seen.Count;
    }
}
