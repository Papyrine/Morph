# wordart

### Arc/Circle warps use chord-sagitta envelope geometry

The three single-curve warps (`textArchUp` / `textArchDown` / `textCircle`) render with the geometry Word actually uses, in both backends:

- **`textArchUp` / `textArchDown`**: bbox width is the arc **chord**, bbox height is the **sagitta** (perpendicular distance from chord midpoint to arc midpoint). Circle radius is `R = (W² + 4H²) / (8H)`. For a typical 4:1 wide-and-flat WordArt bbox (e.g. 432pt × 108pt), this gives R ≈ 270pt and a 106° arc — a gentle, mostly-horizontal curve that matches Word. Treating the bbox as the *full* arc bounding box (the obvious first read of "draw an arc inside the bbox") gives a 180° half-ellipse, far sharper and wrongly off-centre.
- **`textCircle`**: text wraps the right side of an inscribed circle. Short text covers a small arc centred on 3 o'clock and reads downward — matches Word's behaviour where the bbox diameter sizes the circle and text sits on the right hemisphere.

Path is sized to the rendered text width and centred on the arc peak/dip (or 3 o'clock for circle), so glyphs sit at the bbox-centre without depending on path-text alignment options. Neither ImageSharp's `RichTextOptions.HorizontalAlignment` nor Skia's `SKTextAlign.Center` centre text the way the obvious read suggests — both place text from the offset point in the direction of path travel — so sizing the path to fit is the more robust approach across both backends.

See `ImageSharpPageRenderer.TryRenderWordArtOnPath` / `BuildChordSagittaArc` and `SkiaPageRenderer.TryRenderWordArtOnPath` / `BuildChordSagittaArc`.

### Wave + Chevron also use path-based rendering

`textChevron` / `textChevronInverted` route through the same chord-sagitta arc as ArchUp/Down — Word's chevron renders as a single-peak smooth arch, not a sharp ^. (A literal polyline ^/v path causes per-glyph overlap at the discontinuous apex regardless of fillet size.)

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

Path Y is the bbox top — glyph baselines sit on the path peak, so glyph tops extend slightly above the bbox. Word's expected sits ~100px higher again (about one ascent), and the gap is the same shape across all warps. The remaining drift isn't in the warp geometry itself but in the inline-drawing layout cursor (paragraph spacing-after, line metrics) accumulating differently than Word's. A `bodyPr anchor="ctr"` shift (centring the path on the bbox midline) makes it worse — it pushes glyphs further down. Leave the path at the bbox top; closing the residual gap is a layout-flow concern, not a WordArt one.

### `textInflate` / `textDeflate` / `textCan*`

Now handled — see [`../wordart-envelope/notes.md`](../wordart-envelope/notes.md). Top+bottom envelope warps render as per-glyph affine scale (anchor varies by warp), with the text stretched to fill the bbox and the peak glyph sized to fit the bbox height.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1** | **Page 1. ErrorMetric: 0.0073** | **Page 1. ErrorMetric: 0.0094** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
| **Page 2** | **Page 2. ErrorMetric: 0.0378** | **Page 2. ErrorMetric: 0.0541** |
| <img src="expected_0002.png" width="500"> | <img src="skia_result%23page_0002.verified.png" width="500"> | <img src="imagesharp_result%23page_0002.verified.png" width="500"> |
| **Page 3** | **Page 3. ErrorMetric: 0.2057** | **Page 3. ErrorMetric: 0.2269** |
| <img src="expected_0003.png" width="500"> | <img src="skia_result%23page_0003.verified.png" width="500"> | <img src="imagesharp_result%23page_0003.verified.png" width="500"> |
| **Page 4** | **Page 4. ErrorMetric: 0.2062** | **Page 4. ErrorMetric: 0.2404** |
| <img src="expected_0004.png" width="500"> | <img src="skia_result%23page_0004.verified.png" width="500"> | <img src="imagesharp_result%23page_0004.verified.png" width="500"> |
| **Page 5** | **Page 5. ErrorMetric: 0.0366** | **Page 5. ErrorMetric: 0.0323** |
| <img src="expected_0005.png" width="500"> | <img src="skia_result%23page_0005.verified.png" width="500"> | <img src="imagesharp_result%23page_0005.verified.png" width="500"> |
| **Page 6** | **Page 6. ErrorMetric: 0.0500** | **Page 6. ErrorMetric: 0.0435** |
| <img src="expected_0006.png" width="500"> | <img src="skia_result%23page_0006.verified.png" width="500"> | <img src="imagesharp_result%23page_0006.verified.png" width="500"> |
| **Page 7** | **Page 7. ErrorMetric: 0.0494** | **Page 7. ErrorMetric: 0.0477** |
| <img src="expected_0007.png" width="500"> | <img src="skia_result%23page_0007.verified.png" width="500"> | <img src="imagesharp_result%23page_0007.verified.png" width="500"> |
| **Page 8** | **Page 8. ErrorMetric: 0.0754** | **Page 8. ErrorMetric: 0.0681** |
| <img src="expected_0008.png" width="500"> | <img src="skia_result%23page_0008.verified.png" width="500"> | <img src="imagesharp_result%23page_0008.verified.png" width="500"> |
| **Page 9** | **Page 9. ErrorMetric: 0.0478** | **Page 9. ErrorMetric: 0.0483** |
| <img src="expected_0009.png" width="500"> | <img src="skia_result%23page_0009.verified.png" width="500"> | <img src="imagesharp_result%23page_0009.verified.png" width="500"> |
| **Page 10** | **Page 10. ErrorMetric: 0.0717** | **Page 10. ErrorMetric: 0.0723** |
| <img src="expected_0010.png" width="500"> | <img src="skia_result%23page_0010.verified.png" width="500"> | <img src="imagesharp_result%23page_0010.verified.png" width="500"> |
| **Page 11** | **Page 11. ErrorMetric: 0.0644** | **Page 11. ErrorMetric: 0.0608** |
| <img src="expected_0011.png" width="500"> | <img src="skia_result%23page_0011.verified.png" width="500"> | <img src="imagesharp_result%23page_0011.verified.png" width="500"> |
| **Page 12** | **Page 12. ErrorMetric: 0.0317** | **Page 12. ErrorMetric: 0.0267** |
| <img src="expected_0012.png" width="500"> | <img src="skia_result%23page_0012.verified.png" width="500"> | <img src="imagesharp_result%23page_0012.verified.png" width="500"> |
| **Page 13** | **Page 13. ErrorMetric: 0.0201** | **Page 13. ErrorMetric: 0.0179** |
| <img src="expected_0013.png" width="500"> | <img src="skia_result%23page_0013.verified.png" width="500"> | <img src="imagesharp_result%23page_0013.verified.png" width="500"> |
| **Page 14** | **Page 14. ErrorMetric: 0.0723** | **Page 14. ErrorMetric: 0.0646** |
| <img src="expected_0014.png" width="500"> | <img src="skia_result%23page_0014.verified.png" width="500"> | <img src="imagesharp_result%23page_0014.verified.png" width="500"> |
| **Page 15** | **Page 15. ErrorMetric: 0.0347** | **Page 15. ErrorMetric: 0.0331** |
| <img src="expected_0015.png" width="500"> | <img src="skia_result%23page_0015.verified.png" width="500"> | <img src="imagesharp_result%23page_0015.verified.png" width="500"> |
