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

## Practical notes

- Bit-identical output requires the pinned container; host renders are for layout inspection
  only (positions and wraps are faithful, anti-aliasing differs).
- A host-side scratch harness that converts one scenario to PDF and rasterizes it via
  Morph.PDFium is the fastest way to inspect a single page mid-investigation without a
  container round trip.
- Word's expected renders are themselves evidence, not gospel: a handful of reference pages
  contain Word quirks (see the audit's "anomalies worth re-checking" section) — verify against
  the DOCX markup before treating Word as correct.
