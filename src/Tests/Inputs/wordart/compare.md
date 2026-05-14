# wordart

### Arc/Circle warps use chord-sagitta envelope geometry

The three single-curve warps (`textArchUp` / `textArchDown` / `textCircle`) render with the geometry Word actually uses, in both backends:

- **`textArchUp` / `textArchDown`**: bbox width is the arc **chord**, bbox height is the **sagitta** (perpendicular distance from chord midpoint to arc midpoint). Circle radius is `R = (W² + 4H²) / (8H)`. For a typical 4:1 wide-and-flat WordArt bbox (e.g. 432pt × 108pt), this gives R ≈ 270pt and a 106° arc — a gentle, mostly-horizontal curve that matches Word. Treating the bbox as the *full* arc bounding box (the obvious first read of "draw an arc inside the bbox") gives a 180° half-ellipse, far sharper and wrongly off-centre.
- **`textCircle`**: text wraps the right side of an inscribed circle. Short text covers a small arc centred on 3 o'clock and reads downward — matches Word's behaviour where the bbox diameter sizes the circle and text sits on the right hemisphere.

Path is sized to the rendered text width and centred on the arc peak/dip (or 3 o'clock for circle), so glyphs sit at the bbox-centre without depending on path-text alignment options. Neither ImageSharp's `RichTextOptions.HorizontalAlignment` nor Skia's `SKTextAlign.Center` centre text the way the obvious read suggests — both place text from the offset point in the direction of path travel — so sizing the path to fit is the more robust approach across both backends.

See `ImageSharpPageRenderer.TryRenderWordArtOnPath` / `BuildChordSagittaArc` and `SkiaPageRenderer.TryRenderWordArtOnPath` / `BuildChordSagittaArc`.

### Other warps (Wave / Chevron / Slant / Triangle / Fade)

Still approximated via canvas transforms in `Morph.Skia.ApplyWordArtTransform`; ImageSharp falls back to flat shrink-to-fit text. Visually crude but not actively broken — full path-based warps would need a per-warp glyph-positioning step (each preset has its own envelope), bigger than the single-curve cases above.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1** | **Page 1. ErrorMetric: 0.0071** | **Page 1. ErrorMetric: 0.0094** |
| <img src="expected_0001.png" width="500"> | <img src="results_skia%23page_0001.verified.png" width="500"> | <img src="results_imagesharp%23page_0001.verified.png" width="500"> |
| **Page 2** | **Page 2. ErrorMetric: 0.0492** | **Page 2. ErrorMetric: 0.0541** |
| <img src="expected_0002.png" width="500"> | <img src="results_skia%23page_0002.verified.png" width="500"> | <img src="results_imagesharp%23page_0002.verified.png" width="500"> |
| **Page 3** | **Page 3. ErrorMetric: 0.2198** | **Page 3. ErrorMetric: 0.2252** |
| <img src="expected_0003.png" width="500"> | <img src="results_skia%23page_0003.verified.png" width="500"> | <img src="results_imagesharp%23page_0003.verified.png" width="500"> |
| **Page 4** | **Page 4. ErrorMetric: 0.2014** | **Page 4. ErrorMetric: 0.2047** |
| <img src="expected_0004.png" width="500"> | <img src="results_skia%23page_0004.verified.png" width="500"> | <img src="results_imagesharp%23page_0004.verified.png" width="500"> |
| **Page 5** | **Page 5. ErrorMetric: 0.0341** | **Page 5. ErrorMetric: 0.0309** |
| <img src="expected_0005.png" width="500"> | <img src="results_skia%23page_0005.verified.png" width="500"> | <img src="results_imagesharp%23page_0005.verified.png" width="500"> |
| **Page 6** | **Page 6. ErrorMetric: 0.0498** | **Page 6. ErrorMetric: 0.0435** |
| <img src="expected_0006.png" width="500"> | <img src="results_skia%23page_0006.verified.png" width="500"> | <img src="results_imagesharp%23page_0006.verified.png" width="500"> |
| **Page 7** | **Page 7. ErrorMetric: 0.0467** | **Page 7. ErrorMetric: 0.0462** |
| <img src="expected_0007.png" width="500"> | <img src="results_skia%23page_0007.verified.png" width="500"> | <img src="results_imagesharp%23page_0007.verified.png" width="500"> |
| **Page 8** | **Page 8. ErrorMetric: 0.0751** | **Page 8. ErrorMetric: 0.0681** |
| <img src="expected_0008.png" width="500"> | <img src="results_skia%23page_0008.verified.png" width="500"> | <img src="results_imagesharp%23page_0008.verified.png" width="500"> |
| **Page 9** | **Page 9. ErrorMetric: 0.0470** | **Page 9. ErrorMetric: 0.0483** |
| <img src="expected_0009.png" width="500"> | <img src="results_skia%23page_0009.verified.png" width="500"> | <img src="results_imagesharp%23page_0009.verified.png" width="500"> |
| **Page 10** | **Page 10. ErrorMetric: 0.0716** | **Page 10. ErrorMetric: 0.0723** |
| <img src="expected_0010.png" width="500"> | <img src="results_skia%23page_0010.verified.png" width="500"> | <img src="results_imagesharp%23page_0010.verified.png" width="500"> |
| **Page 11** | **Page 11. ErrorMetric: 0.0612** | **Page 11. ErrorMetric: 0.0584** |
| <img src="expected_0011.png" width="500"> | <img src="results_skia%23page_0011.verified.png" width="500"> | <img src="results_imagesharp%23page_0011.verified.png" width="500"> |
| **Page 12** | **Page 12. ErrorMetric: 0.0313** | **Page 12. ErrorMetric: 0.0267** |
| <img src="expected_0012.png" width="500"> | <img src="results_skia%23page_0012.verified.png" width="500"> | <img src="results_imagesharp%23page_0012.verified.png" width="500"> |
| **Page 13** | **Page 13. ErrorMetric: 0.0187** | **Page 13. ErrorMetric: 0.0169** |
| <img src="expected_0013.png" width="500"> | <img src="results_skia%23page_0013.verified.png" width="500"> | <img src="results_imagesharp%23page_0013.verified.png" width="500"> |
| **Page 14** | **Page 14. ErrorMetric: 0.0716** | **Page 14. ErrorMetric: 0.0646** |
| <img src="expected_0014.png" width="500"> | <img src="results_skia%23page_0014.verified.png" width="500"> | <img src="results_imagesharp%23page_0014.verified.png" width="500"> |
| **Page 15** | **Page 15. ErrorMetric: 0.0339** | **Page 15. ErrorMetric: 0.0331** |
| <img src="expected_0015.png" width="500"> | <img src="results_skia%23page_0015.verified.png" width="500"> | <img src="results_imagesharp%23page_0015.verified.png" width="500"> |
