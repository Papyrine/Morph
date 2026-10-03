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

`textCircle` wraps the right side of an inscribed circle. Short text covers a small arc centred on 3
o'clock and reads downward, as Word's does: the bbox diameter sizes the circle and the text sits on the
right hemisphere. The path is sized to the rendered text width and centred on 3 o'clock, so it does not
depend on path-text alignment options. Neither ImageSharp's `RichTextOptions.HorizontalAlignment` nor
Skia's `SKTextAlign.Center` centres text the way the obvious reading suggests. See each drawer's
`TryRenderWordArtOnPath`.

### Wave + Chevron also use path-based rendering

`textChevron` / `textChevronInverted` ride a chord-sagitta arc (bbox width as chord, height as sagitta, `R = (W² + 4H²) / (8H)`), because Word's chevron renders as a single-peak smooth arch, not a sharp ^. (A literal polyline ^/v path causes per-glyph overlap at the discontinuous apex regardless of fillet size.)

`textWave1` uses a 64-segment polyline approximation of one full cosine period across the rendered text width: `y = midY - amplitude·cos(2π·t/textWidth)`. Amplitude is bbox H/4 (not H/2) so the wave excursion stays gentle relative to glyph height, matching Word.

### Slant uses a straight diagonal path

`textSlantUp` / `textSlantDown` route through a straight diagonal `LineTo` path with slope ±H/W. Path-text rendering rotates each glyph to match the line angle, so letters lean at the slant angle (Word keeps glyphs upright on a slanted baseline — close approximation, not pixel-identical, but visually distinct from flat).

### Fade and Triangle use per-glyph rendering

`textFadeRight` / `textFadeLeft` / `textTriangle` are *envelope warps* — each glyph is vertically scaled relative to its position along the text. Implemented by drawing one `DrawText` per character with a `Scale(1, sy)` transform anchored at the baseline, so glyph bottoms align and tops shrink toward the baseline. Per-glyph rendering is slower than a single DrawText so the path-based warps are tried first.

Scale curves:
- FadeRight: `1.0 → 0.35` (full at left, reduced at right)
- FadeLeft: `0.35 → 1.0`
- Triangle: peaks at 1.0 mid-text, drops to 0.35 at edges (diamond envelope)

### Vertical positioning vs Word

The drift once recorded here was the layout's, not the warps': an inline WordArt dropped its own
paragraph's spacing, and the arch-up paragraph declares 10pt after, so every warp below it sat that much
high and two of them a page early. The WordArt now carries its paragraph's spacing. The line it takes is
exactly the box height (`_probe_archflow`: consecutive 72pt boxes 72pt apart at a 12pt and a 48pt
paragraph font), so every warp now starts on Word's page at Word's box top. What remains between the
warps and Word is each warp's own geometry.

### `textInflate` / `textDeflate` / `textCan*`

Now handled — see [`../wordart-envelope/notes.md`](../wordart-envelope/notes.md). Top+bottom envelope warps render as per-glyph affine scale (anchor varies by warp), with the text stretched to fill the bbox and the peak glyph sized to fit the bbox height.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0061 · SSIM: 0.9889** | **Page 1. ErrorMetric: 0.0068 · SSIM: 0.9882** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
| **Page 2**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 2. ErrorMetric: 0.0318 · SSIM: 0.9548** | **Page 2. ErrorMetric: 0.0295 · SSIM: 0.9591** |
| <img src="expected_0002.png" width="500"> | <img src="skia_result%23page_0002.verified.png" width="500"> | <img src="imagesharp_result%23page_0002.verified.png" width="500"> |
| **Page 3**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 3. ErrorMetric: 0.2170 · SSIM: 0.8343** | **Page 3. ErrorMetric: 0.2226 · SSIM: 0.8275** |
| <img src="expected_0003.png" width="500"> | <img src="skia_result%23page_0003.verified.png" width="500"> | <img src="imagesharp_result%23page_0003.verified.png" width="500"> |
| **Page 4**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 4. ErrorMetric: 0.1535 · SSIM: 0.8738** | **Page 4. ErrorMetric: 0.1779 · SSIM: 0.8608** |
| <img src="expected_0004.png" width="500"> | <img src="skia_result%23page_0004.verified.png" width="500"> | <img src="imagesharp_result%23page_0004.verified.png" width="500"> |
| **Page 5**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 5. ErrorMetric: 0.0110 · SSIM: 0.9829** | **Page 5. ErrorMetric: 0.0118 · SSIM: 0.9831** |
| <img src="expected_0005.png" width="500"> | <img src="skia_result%23page_0005.verified.png" width="500"> | <img src="imagesharp_result%23page_0005.verified.png" width="500"> |
| **Page 6**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 6. ErrorMetric: 0.0089 · SSIM: 0.9852** | **Page 6. ErrorMetric: 0.0094 · SSIM: 0.9844** |
| <img src="expected_0006.png" width="500"> | <img src="skia_result%23page_0006.verified.png" width="500"> | <img src="imagesharp_result%23page_0006.verified.png" width="500"> |
| **Page 7**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 7. ErrorMetric: 0.0141 · SSIM: 0.9799** | **Page 7. ErrorMetric: 0.0144 · SSIM: 0.9794** |
| <img src="expected_0007.png" width="500"> | <img src="skia_result%23page_0007.verified.png" width="500"> | <img src="imagesharp_result%23page_0007.verified.png" width="500"> |
| **Page 8**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 8. ErrorMetric: 0.0186 · SSIM: 0.9635** | **Page 8. ErrorMetric: 0.0207 · SSIM: 0.9621** |
| <img src="expected_0008.png" width="500"> | <img src="skia_result%23page_0008.verified.png" width="500"> | <img src="imagesharp_result%23page_0008.verified.png" width="500"> |
| **Page 9**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 9. ErrorMetric: 0.0179 · SSIM: 0.9753** | **Page 9. ErrorMetric: 0.0182 · SSIM: 0.9762** |
| <img src="expected_0009.png" width="500"> | <img src="skia_result%23page_0009.verified.png" width="500"> | <img src="imagesharp_result%23page_0009.verified.png" width="500"> |
| **Page 10**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 10. ErrorMetric: 0.0130 · SSIM: 0.9861** | **Page 10. ErrorMetric: 0.0139 · SSIM: 0.9862** |
| <img src="expected_0010.png" width="500"> | <img src="skia_result%23page_0010.verified.png" width="500"> | <img src="imagesharp_result%23page_0010.verified.png" width="500"> |
| **Page 11**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 11. ErrorMetric: 0.0136 · SSIM: 0.9793** | **Page 11. ErrorMetric: 0.0143 · SSIM: 0.9789** |
| <img src="expected_0011.png" width="500"> | <img src="skia_result%23page_0011.verified.png" width="500"> | <img src="imagesharp_result%23page_0011.verified.png" width="500"> |
| **Page 12**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 12. ErrorMetric: 0.0081 · SSIM: 0.9869** | **Page 12. ErrorMetric: 0.0083 · SSIM: 0.9887** |
| <img src="expected_0012.png" width="500"> | <img src="skia_result%23page_0012.verified.png" width="500"> | <img src="imagesharp_result%23page_0012.verified.png" width="500"> |
| **Page 13**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 13. ErrorMetric: 0.0046 · SSIM: 0.9944** | **Page 13. ErrorMetric: 0.0048 · SSIM: 0.9949** |
| <img src="expected_0013.png" width="500"> | <img src="skia_result%23page_0013.verified.png" width="500"> | <img src="imagesharp_result%23page_0013.verified.png" width="500"> |
| **Page 14**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 14. ErrorMetric: 0.0187 · SSIM: 0.9672** | **Page 14. ErrorMetric: 0.0203 · SSIM: 0.9654** |
| <img src="expected_0014.png" width="500"> | <img src="skia_result%23page_0014.verified.png" width="500"> | <img src="imagesharp_result%23page_0014.verified.png" width="500"> |
| **Page 15**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 15. ErrorMetric: 0.0155 · SSIM: 0.9802** | **Page 15. ErrorMetric: 0.0152 · SSIM: 0.9818** |
| <img src="expected_0015.png" width="500"> | <img src="skia_result%23page_0015.verified.png" width="500"> | <img src="imagesharp_result%23page_0015.verified.png" width="500"> |
