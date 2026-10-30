# wordart

### The arches are Word-probed

`textArchUp` / `textArchDown` follow `WordArtArch`, whose rules were read off Word's XPS glyph
transforms over 32 probe boxes (see `docs/word-features.md`, WordArt Transforms):

- The text is drawn smaller than declared, by `W' / (W' + 2·ink)` (text rect width over that width plus
  twice the ink height). This page's 36pt Impact draws at 31.25pt.
- The path is the half ellipse of the text rect, scaled by that factor about the rect's centre.
- The text is laid along the path by arc length, by its in-box alignment.
- Each glyph turns to the tangent, its baseline `usWinAscent − sTypoAscender` below the path.

The glyph tops on the arch up therefore stand about 7.5pt above the box, close under the subtitle, as in
Word. The chord-sagitta circle this page used before (bbox width as chord, height as sagitta, path sized to
the text) drew the text at full size on a far tighter curve, over the subtitle.

### Circle

`textCircle` is the arch construction on the whole ellipse (`WordArtArch.Place`), run clockwise from its
left end: centred text sits about the right end and reads downward. Its scale takes the longer side of
the text rect. Read off Word's XPS on `_probe_circle_*`; `docs/word-features.md`, WordArt Transforms,
has the numbers and the one case that does not fit (Arial in a square box).

### Wave, chevron, slant, fade and triangle are envelope warps

Each is a top and a bottom guide drawn in the text rect by the preset's own definition, and the line's
INK box is stretched between them (`WordArtEnvelope`, `TryRenderEnvelope` in both drawers). So every
one of them fills its box edge to edge whatever size the text declares, as Word's does; the text-on-path
and per-glyph scaling this page used before drew the text at its declared size, less than half the width
Word fills. `../wordart-envelope/notes.md` has the guides of the four warps that fixture covers.

### Vertical positioning vs Word

The drift once recorded here was the layout's, not the warps': an inline WordArt dropped its own
paragraph's spacing, and the arch-up paragraph declares 10pt after, so every warp below it sat that much
high and two of them a page early. The WordArt now carries its paragraph's spacing. The line it takes is
exactly the box height (`_probe_archflow`: consecutive 72pt boxes 72pt apart at a 12pt and a 48pt
paragraph font), so every warp now starts on Word's page at Word's box top. A box carried to a fresh
page keeps its paragraph's space-after as well, which the wave at the head of page 3 had lost.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0037 · SSIM: 0.9952** | **Page 1. ErrorMetric: 0.0039 · SSIM: 0.9959** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
| **Page 2**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 2. ErrorMetric: 0.0224 · SSIM: 0.9700** | **Page 2. ErrorMetric: 0.0225 · SSIM: 0.9713** |
| <img src="expected_0002.png" width="500"> | <img src="skia_result%23page_0002.verified.png" width="500"> | <img src="imagesharp_result%23page_0002.verified.png" width="500"> |
| **Page 3**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 3. ErrorMetric: 0.0730 · SSIM: 0.9232** | **Page 3. ErrorMetric: 0.0694 · SSIM: 0.9319** |
| <img src="expected_0003.png" width="500"> | <img src="skia_result%23page_0003.verified.png" width="500"> | <img src="imagesharp_result%23page_0003.verified.png" width="500"> |
| **Page 4**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 4. ErrorMetric: 0.0952 · SSIM: 0.9376** | **Page 4. ErrorMetric: 0.0940 · SSIM: 0.9427** |
| <img src="expected_0004.png" width="500"> | <img src="skia_result%23page_0004.verified.png" width="500"> | <img src="imagesharp_result%23page_0004.verified.png" width="500"> |
| **Page 5**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 5. ErrorMetric: 0.0077 · SSIM: 0.9903** | **Page 5. ErrorMetric: 0.0079 · SSIM: 0.9903** |
| <img src="expected_0005.png" width="500"> | <img src="skia_result%23page_0005.verified.png" width="500"> | <img src="imagesharp_result%23page_0005.verified.png" width="500"> |
| **Page 6**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 6. ErrorMetric: 0.0075 · SSIM: 0.9874** | **Page 6. ErrorMetric: 0.0079 · SSIM: 0.9884** |
| <img src="expected_0006.png" width="500"> | <img src="skia_result%23page_0006.verified.png" width="500"> | <img src="imagesharp_result%23page_0006.verified.png" width="500"> |
| **Page 7**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 7. ErrorMetric: 0.0128 · SSIM: 0.9837** | **Page 7. ErrorMetric: 0.0130 · SSIM: 0.9834** |
| <img src="expected_0007.png" width="500"> | <img src="skia_result%23page_0007.verified.png" width="500"> | <img src="imagesharp_result%23page_0007.verified.png" width="500"> |
| **Page 8**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 8. ErrorMetric: 0.0106 · SSIM: 0.9874** | **Page 8. ErrorMetric: 0.0110 · SSIM: 0.9891** |
| <img src="expected_0008.png" width="500"> | <img src="skia_result%23page_0008.verified.png" width="500"> | <img src="imagesharp_result%23page_0008.verified.png" width="500"> |
| **Page 9**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 9. ErrorMetric: 0.0174 · SSIM: 0.9753** | **Page 9. ErrorMetric: 0.0175 · SSIM: 0.9766** |
| <img src="expected_0009.png" width="500"> | <img src="skia_result%23page_0009.verified.png" width="500"> | <img src="imagesharp_result%23page_0009.verified.png" width="500"> |
| **Page 10**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 10. ErrorMetric: 0.0131 · SSIM: 0.9859** | **Page 10. ErrorMetric: 0.0134 · SSIM: 0.9867** |
| <img src="expected_0010.png" width="500"> | <img src="skia_result%23page_0010.verified.png" width="500"> | <img src="imagesharp_result%23page_0010.verified.png" width="500"> |
| **Page 11**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 11. ErrorMetric: 0.0107 · SSIM: 0.9873** | **Page 11. ErrorMetric: 0.0110 · SSIM: 0.9878** |
| <img src="expected_0011.png" width="500"> | <img src="skia_result%23page_0011.verified.png" width="500"> | <img src="imagesharp_result%23page_0011.verified.png" width="500"> |
| **Page 12**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 12. ErrorMetric: 0.0065 · SSIM: 0.9898** | **Page 12. ErrorMetric: 0.0066 · SSIM: 0.9909** |
| <img src="expected_0012.png" width="500"> | <img src="skia_result%23page_0012.verified.png" width="500"> | <img src="imagesharp_result%23page_0012.verified.png" width="500"> |
| **Page 13**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 13. ErrorMetric: 0.0042 · SSIM: 0.9957** | **Page 13. ErrorMetric: 0.0043 · SSIM: 0.9959** |
| <img src="expected_0013.png" width="500"> | <img src="skia_result%23page_0013.verified.png" width="500"> | <img src="imagesharp_result%23page_0013.verified.png" width="500"> |
| **Page 14**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 14. ErrorMetric: 0.0143 · SSIM: 0.9795** | **Page 14. ErrorMetric: 0.0147 · SSIM: 0.9791** |
| <img src="expected_0014.png" width="500"> | <img src="skia_result%23page_0014.verified.png" width="500"> | <img src="imagesharp_result%23page_0014.verified.png" width="500"> |
| **Page 15**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 15. ErrorMetric: 0.0129 · SSIM: 0.9853** | **Page 15. ErrorMetric: 0.0129 · SSIM: 0.9857** |
| <img src="expected_0015.png" width="500"> | <img src="skia_result%23page_0015.verified.png" width="500"> | <img src="imagesharp_result%23page_0015.verified.png" width="500"> |
