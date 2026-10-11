# wordart-envelope

### Inflate / Deflate / CanUp / CanDown are envelope warps

Each is a top and a bottom guide path drawn in the shape's text rect by the preset's own definition, and
the text is stretched between them: a point of the line's INK box lands its own share of the way across
the rect and its own share of the way from the top guide to the bottom one. `WordArtEnvelope` holds the
guides and the mapping, and both raster drawers hand it the line's outline as polylines
(`TryRenderEnvelope`); the PDF embeds the raster.

The guides, as fractions of the rect's height at the fraction `u` of its width (Word-probed on
`_probe_warp_b`, a row of capitals read at every eighth of the width; see `docs/word-features.md`,
WordArt Transforms):

| Warp | Top | Bottom |
|------|-----|--------|
| Inflate | `0.1875 · (1 − 4u(1 − u))` | one minus the top |
| Deflate | `0.1875 · s · 4u(1 − u)`, `s` the rect's shorter side over its height | one minus the top |
| CanUp | `0.1429 · (1 − √(1 − (2u − 1)²))` | `1 − 0.1429 · √(1 − (2u − 1)²)` |
| CanDown | `0.1429 · √(1 − (2u − 1)²)` | `0.8571 + 0.1429 · √(1 − (2u − 1)²)` |

A can keeps its letters one height from end to end, 0.857 of the rect, on a gentle arc; inflate and
deflate change the height across the word. The sine envelope that stood here before held the ends of
every warp to 0.55 of the height and flattened one edge of each can, which drew the Can lines at half
Word's height with a much deeper bow.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.1168 · SSIM: 0.9391** | **Page 1. ErrorMetric: 0.1161 · SSIM: 0.9436** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
