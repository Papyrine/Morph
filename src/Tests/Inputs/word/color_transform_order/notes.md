Pins the composition rule: Word applies a colour's transform children **in document order**, so
the same two transforms in opposite order give different colours.

A 10x2 grid of page-anchored 0.7in squares, first swatch at 0.3in / 1.0in, 0.78in pitch. Rows are
`a:schemeClr val="accent1"` (4472C4) and `accent2` (ED7D31). Columns:

| # | children, in document order |
| --- | --- |
| 1 | none |
| 2 | `lumMod 50` |
| 3 | `shade 50` |
| 4 | `lumMod 50`, `shade 50` |
| 5 | `shade 50`, `lumMod 50` |
| 6 | `lumMod 75`, `tint 50` |
| 7 | `tint 50`, `lumMod 75` |
| 8 | `satMod 200`, `shade 50` |
| 9 | `shade 50`, `satMod 200` |
| 10 | `lumMod 60`, `lumOff 20`, `shade 75` |

**Columns 4/5, 6/7 and 8/9 are the same pair reversed and must NOT render alike.** On accent1,
column 4 is 142748 and column 5 is 182948; on accent2, column 6 is E5C4BC against column 7's
E9805E — a 94-per-channel gap. Any implementation that sorts transforms into a fixed order, or
that folds them into a single pass, fits one column of each pair and misses the other. Columns 2
and 3 are the single-transform controls that both orderings must agree with.

This rule is why the transform list is an ordered `IReadOnlyList<ColorTransform>` rather than the
flat property bag it replaced: the bag could not represent column 4 and column 5 as different
inputs at all. Repeats matter for the same reason and are kept rather than collapsed — the corpus
carries colours with nine `lumMod`/`satMod`/`tint` triples in a row, and reading only the first of
each kind silently dropped the rest.

Column 10 is the three-child case that mixes both spaces in one chain, and column 8/9 the case
where the interleaved transform pushes saturation past the gamut edge; those two are where an
implementation that batches HSL operations has to be careful to break the batch at a `shade`.
