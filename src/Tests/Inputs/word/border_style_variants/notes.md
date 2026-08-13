A border torture fixture: every `ST_Border` line style (ECMA-376 17.18.2) on both `w:pBdr`
edges and run `w:bdr`, plus the `w:sz` width sweep, edge combinations, `w:space`, `w:between`
grouping, colours, the `w:shadow`/`w:frame` attributes and per-style table cell borders. The
~160 art borders are page-border-only and stay with `page_borders`. Section 1 is the fixture
this scenario grew from, kept verbatim.

Word renders 4 pages and so does every backend.

**What this scenario found and fixed (2026-08-13).** Everything below was broken when the
fixture landed; the root cause was one collapse. `BorderLineStyle` held four values and
`MapBorderStyle` folded OOXML's 27 line styles into them, which lost the stroke AND — because
`ParagraphProperties.SharesBorderGroupWith` compares border records for equality — merged
adjacent paragraphs whose declared styles differed into a single box. Section 2's 27 paragraphs
drew as 6 boxes against Word's 25, and section 1's four separate boxes drew as one rectangle.
Un-collapsing the enum fixed the grouping and the stroke together; `BorderStroke` now owns the
line/gap layout and dash patterns for all three backends, and run borders (`w:bdr`) paint for
the first time.

Two rules in `BorderStroke` are measured off this fixture and `_probe_bordersp`, and they
DISAGREE BY SCOPE — a paragraph border's `w:sz` is the width of each line for the symmetric
families (a 3pt `double` stacks to 9pt) while a cell border's is the total. Both readings are
direct measurements of Word's ink; see `docs/word-features.md` (Cell Borders) for the numbers.
Deriving one from a single width was wrong twice during this work, so do not re-derive either
from one sample.

**Still open**, and visible here:

1. `wave` and `doubleWave` stroke straight (one and two lines) — the sine path needs geometry
   in three painters.
2. The bevel SHADING of `threeDEmboss`/`threeDEngrave`/`outset`/`inset` is not reproduced. Their
   line structure is (two lines and one respectively), which is what makes them distinguishable.
3. At `sz=96` (12pt) the band paints across the paragraph text rather than outside it, so the
   label reads "ingle, sz=96". Word keeps the text clear.
4. The thin/thick family's stack is fitted to the measurement rather than understood — see the
   `perLine` comment in `BorderStroke.Bands`.

**On the AE metric.** This scenario's recorded error ROSE when the rendering was fixed
(0.1231 → 0.1225 after the whole sequence, having peaked at 0.1465 mid-way) even though the
render moved much closer to Word. That is the new-ink offset penalty in its clearest form: going
from 6 boxes to 25 means Morph now draws ink almost everywhere Word does, and residual
misalignment counts twice where absent ink counted once. The measure that tracked the
improvement was positional — mean distance from each drawn rule to the nearest Word rule on p1
went 29.1px → 10.2px. Read this scenario's AE with that in mind before treating a change here
as a regression.
