# wordart-envelope

### Inflate / Deflate / CanUp / CanDown — top+bottom envelope warps

Per-glyph **path-level** distortion: each glyph's outline is extracted, every point on the outline is remapped so the glyph's top edge follows the envelope's top curve and the bottom edge follows the bottom curve. Each glyph ends up as a true non-affine trapezoid (or pillow / pinch shape), matching what Word does. The earlier affine-per-glyph version only varied glyph height while keeping each glyph rectangular — close enough to read but visibly wrong.

**Algorithm** (`TryRenderWordArtPathWarp` in both backends):

1. Render the text outline once at natural size — `TextBuilder.GeneratePaths` (ImageSharp) / `SKPaint.GetTextPath` (Skia). Path is in text-local coords with X spanning `[0, totalWidth]`.
2. Take the path's actual bounds (`pathsBounds.Top` / `pathsBounds.Height`). This is the *visible* glyph extent, tighter than the font's metric box, and is what the envelope curve should map onto.
3. Walk every point. For each `(x, y)`:
   - `t = x / totalWidth` — normalised position along the word
   - `(top_y, bot_y) = EnvelopeAt(t)` — envelope curves at this column
   - `normY = (y - bounds.Top) / bounds.Height` — relative position within the glyph row
   - New point: `(x_box + t·width_box, top_y + normY·(bot_y - top_y))`
4. Build the warped path and fill it.

ImageSharp uses `IPath.Flatten()` to get linear segments; Skia uses `SKPathMeasure` to walk each subpath as a polyline (~2-pixel sample interval).

**Envelope curves** (`EnvelopeAt`): edge text height is `minRatio` of the bbox (0.55) so glyphs at the ends of the word stay readable — without the floor, `sin(πt)·height` collapses to 0 at the edges.

| Warp     | Top curve                | Bottom curve              | Centred on   |
|----------|--------------------------|---------------------------|--------------|
| Inflate  | bows up (peaks above)    | bows down (peaks below)   | bbox centre  |
| Deflate  | dips down (peaks below)  | rises up (peaks above)    | bbox centre  |
| CanUp    | arches up                | flat at bbox bottom       | bbox bottom  |
| CanDown  | flat at bbox top         | arches down               | bbox top     |

The legacy affine `TryRenderWordArtEnvelope` is still the fallback for Fade / Triangle (single-axis warps that don't need path distortion), and tries last in case the path-warp short-circuits.

See `ImageSharpPageRenderer.TryRenderWordArtPathWarp` and `SkiaPageRenderer.TryRenderWordArtPathWarp`.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 9441420757.7500** | **Page 1. ErrorMetric: 8291757343.2500** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
