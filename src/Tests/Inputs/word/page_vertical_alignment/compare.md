# page_vertical_alignment

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
- Pages 5–8 use **exact** line spacing. With 24pt `auto` lines the engine fits a 15th line whose
  descent overruns the margin by 5.4pt, where Word moves it to the next page — the last-line fit
  rule, independent of alignment.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0006 · SSIM: 0.9996** | **Page 1. ErrorMetric: 0.0006 · SSIM: 0.9997** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
| **Page 2**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 2. ErrorMetric: 0.0006 · SSIM: 0.9997** | **Page 2. ErrorMetric: 0.0006 · SSIM: 0.9998** |
| <img src="expected_0002.png" width="500"> | <img src="skia_result%23page_0002.verified.png" width="500"> | <img src="imagesharp_result%23page_0002.verified.png" width="500"> |
| **Page 3**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 3. ErrorMetric: 0.0005 · SSIM: 0.9998** | **Page 3. ErrorMetric: 0.0005 · SSIM: 0.9998** |
| <img src="expected_0003.png" width="500"> | <img src="skia_result%23page_0003.verified.png" width="500"> | <img src="imagesharp_result%23page_0003.verified.png" width="500"> |
| **Page 4**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 4. ErrorMetric: 0.0164 · SSIM: 0.9853** | **Page 4. ErrorMetric: 0.0135 · SSIM: 0.9957** |
| <img src="expected_0004.png" width="500"> | <img src="skia_result%23page_0004.verified.png" width="500"> | <img src="imagesharp_result%23page_0004.verified.png" width="500"> |
| **Page 5**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 5. ErrorMetric: 0.0025 · SSIM: 0.9990** | **Page 5. ErrorMetric: 0.0024 · SSIM: 0.9995** |
| <img src="expected_0005.png" width="500"> | <img src="skia_result%23page_0005.verified.png" width="500"> | <img src="imagesharp_result%23page_0005.verified.png" width="500"> |
| **Page 6**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 6. ErrorMetric: 0.0014 · SSIM: 0.9995** | **Page 6. ErrorMetric: 0.0013 · SSIM: 0.9997** |
| <img src="expected_0006.png" width="500"> | <img src="skia_result%23page_0006.verified.png" width="500"> | <img src="imagesharp_result%23page_0006.verified.png" width="500"> |
| **Page 7**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 7. ErrorMetric: 0.0023 · SSIM: 0.9992** | **Page 7. ErrorMetric: 0.0023 · SSIM: 0.9996** |
| <img src="expected_0007.png" width="500"> | <img src="skia_result%23page_0007.verified.png" width="500"> | <img src="imagesharp_result%23page_0007.verified.png" width="500"> |
| **Page 8**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 8. ErrorMetric: 0.0014 · SSIM: 0.9996** | **Page 8. ErrorMetric: 0.0013 · SSIM: 0.9998** |
| <img src="expected_0008.png" width="500"> | <img src="skia_result%23page_0008.verified.png" width="500"> | <img src="imagesharp_result%23page_0008.verified.png" width="500"> |
| **Page 9**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 9. ErrorMetric: 0.0052 · SSIM: 0.9988** | **Page 9. ErrorMetric: 0.0047 · SSIM: 0.9990** |
| <img src="expected_0009.png" width="500"> | <img src="skia_result%23page_0009.verified.png" width="500"> | <img src="imagesharp_result%23page_0009.verified.png" width="500"> |
| **Page 10**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 10. ErrorMetric: 0.0007 · SSIM: 0.9998** | **Page 10. ErrorMetric: 0.0007 · SSIM: 0.9999** |
| <img src="expected_0010.png" width="500"> | <img src="skia_result%23page_0010.verified.png" width="500"> | <img src="imagesharp_result%23page_0010.verified.png" width="500"> |
| **Page 11**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 11. ErrorMetric: 0.0013 · SSIM: 0.9991** | **Page 11. ErrorMetric: 0.0014 · SSIM: 0.9994** |
| <img src="expected_0011.png" width="500"> | <img src="skia_result%23page_0011.verified.png" width="500"> | <img src="imagesharp_result%23page_0011.verified.png" width="500"> |
| **Page 12**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 12. ErrorMetric: 0.0004 · SSIM: 0.9999** | **Page 12. ErrorMetric: 0.0004 · SSIM: 0.9999** |
| <img src="expected_0012.png" width="500"> | <img src="skia_result%23page_0012.verified.png" width="500"> | <img src="imagesharp_result%23page_0012.verified.png" width="500"> |
| **Page 13**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 13. ErrorMetric: 0.0005 · SSIM: 0.9998** | **Page 13. ErrorMetric: 0.0005 · SSIM: 0.9999** |
| <img src="expected_0013.png" width="500"> | <img src="skia_result%23page_0013.verified.png" width="500"> | <img src="imagesharp_result%23page_0013.verified.png" width="500"> |
| **Page 14**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 14. ErrorMetric: 0.0001 · SSIM: 1.0000** | **Page 14. ErrorMetric: 0.0001 · SSIM: 1.0000** |
| <img src="expected_0014.png" width="500"> | <img src="skia_result%23page_0014.verified.png" width="500"> | <img src="imagesharp_result%23page_0014.verified.png" width="500"> |
| **Page 15**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 15. ErrorMetric: 0.0001 · SSIM: 1.0000** | **Page 15. ErrorMetric: 0.0001 · SSIM: 1.0000** |
| <img src="expected_0015.png" width="500"> | <img src="skia_result%23page_0015.verified.png" width="500"> | <img src="imagesharp_result%23page_0015.verified.png" width="500"> |
| **Page 16**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 16. ErrorMetric: 0.0004 · SSIM: 0.9999** | **Page 16. ErrorMetric: 0.0004 · SSIM: 0.9999** |
| <img src="expected_0016.png" width="500"> | <img src="skia_result%23page_0016.verified.png" width="500"> | <img src="imagesharp_result%23page_0016.verified.png" width="500"> |
| **Page 17**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 17. ErrorMetric: 0.0004 · SSIM: 0.9998** | **Page 17. ErrorMetric: 0.0004 · SSIM: 0.9999** |
| <img src="expected_0017.png" width="500"> | <img src="skia_result%23page_0017.verified.png" width="500"> | <img src="imagesharp_result%23page_0017.verified.png" width="500"> |
| **Page 18**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 18. ErrorMetric: 0.0007 · SSIM: 0.9998** | **Page 18. ErrorMetric: 0.0006 · SSIM: 0.9999** |
| <img src="expected_0018.png" width="500"> | <img src="skia_result%23page_0018.verified.png" width="500"> | <img src="imagesharp_result%23page_0018.verified.png" width="500"> |
