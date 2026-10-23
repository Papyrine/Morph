Pins `w:trPr/w:gridBefore` and `w:gridAfter` — grid columns a row leaves empty at either end.

The same six rows twice, once `tblLayout="fixed"` and once autofit, over a 4-column grid of
unequal widths (1440 / 2160 / 1440 / 2880 twips) with 3pt borders:

| row | gridBefore | gridAfter | cells |
| --- | --- | --- | --- |
| 1 | | | A B C D |
| 2 | 1 | | B C D |
| 3 | | 2 | A B |
| 4 | 1 | 1 | B C |
| 5 | 2 | | C–D (`gridSpan=2`) |
| 6 | 1 | | B C D (no `wBefore`) |

What Word does, in both layouts:

* A row's first cell starts at grid column `gridBefore`; the cells after it keep their grid
  widths.
* The skipped columns draw nothing — no border, no shading. Every cell draws its own edges, so a
  row that stops short leaves the edges of the cells above and below it open.
* `wBefore` / `wAfter` change nothing when the grid gives the widths.

Before this fixture Morph read neither element and packed every row's cells from grid column 0.
