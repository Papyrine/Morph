Pins Word's page vertical alignment — `w:vAlign` in `w:sectPr`, Page Setup › Layout › Vertical
alignment. Hand-authored and amplified: 24pt text on a Letter page with a 1in top margin and a
**3in** bottom margin, so the content band is 72–576pt and an alignment against the paper rather
than the margins would be off by 72pt. Every section starts on its own page, so each page answers
one question. Numbers are Word's baselines, read off the XPS (`MORPH_KEEP_XPS=1`); the same text
top-aligned has its first baseline at 94.82pt and a 33.96pt line pitch.

| page | section | what it pins | Word |
| --- | --- | --- | --- |
| 1 | `center`, three lines | centred between the MARGINS: (504 − 101.8) / 2 = 201.1pt down | A1 at 295.97 |
| 2 | `center`, first `before` 36pt, last `after` 72pt | both spacings are inside the extent | B1 at 277.97 |
| 3 | `bottom`, as page 2 | the last paragraph's space-after ends on the margin | C3 at 492.91 |
| 4 | `both`, `after` 0/12/36pt and a five-line paragraph | slack shared EQUALLY between paragraphs (four gaps of ~37.6pt); lines inside a paragraph keep their pitch | D5 at 564.91 |
| 5–6 | `center`, twenty exact-36pt lines | each page aligned on its own — the full page has no slack, the six left over are centred | E15 at 244.82 |
| 7–8 | `both`, the same lines | a section's last page is justified too | F16–F20 at 93.6pt steps |
| 9 | `center`, paragraph + 3-row table + paragraph | a table is ordinary content | G0 at 256.01 |
| 10 | `center`, a page-anchored and a paragraph-anchored float | BOTH floats move with the text; the page float's box (100–172) counts in the extent | H1 at 296.81, page float at 302 |
| 11 | `center` with a footnote | the note area reaches the margin, so nothing moves | I1 at 94.82 |
| 12 | top section, continuous break, `center` section | a page carrying two sections is top-aligned | J1 at 94.82 |
| 13 | `center` section, continuous break, top section | … whichever section asked | K1 at 94.82 |
| 14–15 | `center` with a page break | a page ended by a break is centred like any other | L1, L2 at 329.93 |
| 16–17 | `both` with a page break | justified across the break, two paragraphs a page | N2 at 564.91 |
| 18 | `both` ending the document | the document's last page is justified too | M3 at 564.91 |

Page 10 is the surprising one: Word moves a PAGE-anchored float along with the text, and measures
the extent down to the lowest float as well as the lowest line. Two further probes pinned that down
(recorded in `Fragmenter.AlignPage`): a float wholly above the text does not widen the extent, one
below it does, and a behind-text full-page background leaves no slack at all.

Two things the probes found that this fixture deliberately avoids:

- Under `both`, Word welds a paragraph whose anchored float overlaps the next paragraph to that
  paragraph, and moves a page float with the last unit. The engine moves a float with the unit
  above it and does not weld on floats.
- Pages 5–8 use **exact** line spacing, so the page breaks rest on nothing but the band height. (With
  24pt `auto` lines the engine once fitted a 15th line Word moves on; the last-line fit rule has since
  been Word-probed and corrected — see Line Spacing in `docs/word-features.md`.)
