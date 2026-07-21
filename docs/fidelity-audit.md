# Fidelity audit method

How Morph's output is compared against Word's own renders, and how a rendering change is judged.
The 2026-07 full-corpus audit (`src/todo.md`, a temporary working document) was produced and
worked down with this method; it applies unchanged to a future re-audit against a new Word
version or an expanded corpus.

## Reference material

- Every scenario in `src/Tests/Inputs/` carries `expected_*.png` — Word's own render at 150 DPI,
  produced per scenario by the `src/RenderHelper` project (Word COM automation, Windows-only;
  see CLAUDE.md for the single-scenario invocation).
- Morph's side: `skia_result#page_*.verified.png`, `imagesharp_result#page_*.verified.png`,
  `pdf_result#page_*.verified.png` (PDFium render of the produced PDF), and
  `html_result.verified.png` (headless-browser screenshot of the HTML export).

## Producing findings

Compare each expected page against each backend page. Record findings as
`severity | backends | pages | description` (`all` = skia+imagesharp+pdf). HTML findings ignore
pagination and viewport-width reflow by design and flag only content/styling errors. Not
reported: anti-aliasing texture, 1-2px subpixel shifts, ImageSharp's softer glyph rasterization.

## The two recorded metrics

Each scenario's `*_result.verified.json` records, per page, an **AE** (`ErrorMetric`) and an
**SSIM**, both against the Word reference page. `PageComparison` (`src/Tests/Compare/`) computes
the pair from a single decode of each image — not via ImageMagick:

- **AE** — the fraction of pixels that differ at all (0 = identical). Pixels outside the overlap of
  two differently-sized pages count as differing and the result is normalised by the EXPECTED
  page's pixel count, so an orientation mismatch scores near 1 instead of silently comparing a
  sub-window (business-plans/15 p12/p15 are the corpus's only such pages).
- **SSIM** — the vendored Verify SSIM (1 = identical), null when page sizes differ.

Both were originally Magick.NET calls. SSIM moved in-repo for speed (~30× faster). AE followed
after **Magick.NET 14.15 changed `Compare(…, ErrorMetric.Absolute)` to return the raw unnormalised
error** (a ~1e9 sum in Q16 units) instead of the fraction — silently rewriting every recorded
metric and breaking comparability with all historical audit numbers. Keeping both in-repo makes
the recorded metrics stable across future upgrades; do not route them back through Magick. With
Magick gone the suite no longer takes a native image-decode dependency at all, which cut a full
run from ~7m to ~4m20s.

## Judging a change

The scenario tests compare PNGs via SSIM with tolerance, so a PASSING suite does not mean
pixel-identical — small real shifts pass against stale baselines, and thin-strip changes (page
margins, single digits) are metric-invisible. The judging loop that proved reliable:

1. Run the full suite in the container (`./scripts/test.sh`, the canonical environment) and let
   failing scenarios write `*.received.*` files.
2. For every received PNG, compute an error metric against the WORD expected page for both the
   received file and the git-HEAD verified file; the delta (received minus verified) is the
   change's effect for that page. Sum per scenario. Negative = closer to Word.
3. **Vet outliers visually** with three-up crops (Word | old | new) before believing the number,
   in both directions:
   - *New-ink offset penalty:* correct-but-slightly-shifted content scores WORSE than absent
     content. A small positive delta on a scenario whose missing art now renders is usually an
     improvement — the crops decide.
   - *Metric-invisible changes:* single digits, thin strips and small glyphs move the metric by
     ±0.000x; correctness there is judged by direct measurement (digit positions, margin pixel
     counts), not the sum.
4. Decide per the evidence: net improvement → promote (`*.received.*` → `*.verified.*`), rerun
   the suite to full green, then commit. Net regression → revert the code and record the
   evidence (what was tried, the per-scenario numbers, the blocking mechanism) so the attempt is
   not repeated blind.
5. Promotion caveat: a change that SHRINKS a scenario's page count leaves the old
   highest-numbered per-page verified file orphaned; Verify then fails that scenario with a
   `Delete:` instruction and NO received files. Remove the orphan by hand.

## Promotion-time guard against degenerate baselines

The judging loop above is a manual discipline; the suite itself has one blind spot it cannot
close on its own. Because each scenario page is compared against its OWN committed
`*.verified.png`, once a broken page is promoted, AE and SSIM compare the baseline against itself
and stay green forever. That let two real regressions through: a metric-invisible thin-strip
class, and — the extreme case — four `newsletters/06` Skia pages that collapsed to a solid navy
fill with no content yet passed the suite because the baseline WAS the broken image.

A second, compounding gap sits under the same scenario: **a page-count mismatch suppresses the
per-page diffs entirely.** `PageDiffs` returns null unless the rendered page count equals the
reference count, so a scenario like `newsletters/06` (6 pages against Word's 4) records nothing
but the two counts — no AE, no SSIM, on any page. As of 2026-07-21 that is 4 of 325 scenarios:
`business-plans/02`, `image_wrap_square`, `newsletters/06`, `resumes/13`. Coverage is regained the
moment the page count matches — the fixed-layout table fix brought `newsletters/09` from 5 pages
back to Word's 4 and its per-page metrics came with it, which is the cheapest way to grow the
audit's reach. For
those the guard below and manual crop-vetting are the only signals, and step 2 of the judging
loop cannot run at all — a metric-delta report has nothing to compare and will not list them
among the changed scenarios. Check that set by hand whenever a change touches them.

`BaselineHealthTests` (`src/Tests/BaselineHealthTests.cs`) is the automated backstop for the
extreme end of that class: a whole page collapsing to a near-solid fill. A rendered document page
essentially always carries anti-aliased text, so it has hundreds of unique colours; a collapsed
page has a handful. Across the corpus the two populations are cleanly separated — degenerate
pages sit at 1–3 unique colours, the lowest healthy raster page at 159, nothing in between — so
the test flags any paginated raster baseline (`{skia,imagesharp,pdf}_result#page_*.verified.png`)
whose unique-colour count is at or below **16**.

- **How it gates promotion.** It is an ordinary suite `[Test]`, so it runs on every
  `./scripts/test.sh` and, critically, in the confirming re-run at the end of
  `regenerate-baselines.sh`. A degenerate page produced by a baseline regen fails there before the
  commit. It reads only committed PNGs (platform-independent), so unlike the scenario tests it
  also runs on a host `dotnet test` without the container.
- **Scope is deliberately narrow.** Only paginated page renders are checked — the single-image
  HTML/Markdown export snapshots are excluded, because a text export can be legitimately empty (a
  labels sheet whose content is all in shapes the text exporter doesn't traverse). And the check
  is only for the *collapse-to-solid* failure mode; a page that dropped most of its content but
  kept a chart still has hundreds of colours and is caught, if at all, by the AE/SSIM comparison
  against the Word reference, not here.
- **Why unique-colour count and not file size.** File size was considered and rejected: PNG size
  is content- and encoder-dependent (the solid-navy Skia pages were still ~18 KB), so it is far
  noisier than the colour count, which separates the two populations exactly.
- **Allow-list.** `KnownDegenerate` holds the exceptions in two labelled categories: pages that
  are *intentionally blank* (Word renders them empty too, e.g. `explicit_break_blank_page` page 2)
  and *known regressions* not yet fixed (empty since 2026-07-21 — the four `newsletters/06` Skia
  pages the guard was written for are fixed; see `floating-art-pipeline.md`). The test also
  asserts every allow-listed page is STILL degenerate, so fixing and regenerating one forces its
  own removal from the list instead of letting the entry rot.

## Practical notes

- Bit-identical output requires the pinned container; host renders are for layout inspection
  only (positions and wraps are faithful, anti-aliasing differs).
- A host-side scratch harness that converts one scenario to PDF and rasterizes it via
  Morph.PDFium is the fastest way to inspect a single page mid-investigation without a
  container round trip.
- Word's expected renders are themselves evidence, not gospel: a handful of reference pages
  contain Word quirks (see the audit's "anomalies worth re-checking" section) — verify against
  the DOCX markup before treating Word as correct.
