### Inflate / Deflate / CanUp / CanDown — top+bottom envelope warps

These four warps are *envelope* warps where the top edge and bottom edge of the rendered text follow independent curves. Each glyph is scaled non-uniformly to fit between them. Implemented in both backends as per-glyph `Save → Scale → DrawText → Restore` with:

- **Box-fill scaling** — unlike Fade / Triangle (which keep the natural font size and just modulate Y), the envelope warps stretch each glyph horizontally to fill the bbox width and vertically so the *peak* glyph fills the bbox height. `baseScaleY = bboxH / (peakScale × glyphH)` keeps the most-stretched glyph inside the bbox.
- **Per-glyph scale curve** — `scaleY(t) = 1 + amplitude·sin(πt)` for Inflate / Can (peak in the middle), `1 - amplitude·sin(πt)` for Deflate (pinch in the middle). Amplitudes 0.5 / 0.45 chosen to give a visible bulge without exceeding the bbox at the centre.
- **Anchor varies by warp** — Inflate / Deflate scale around the bbox vertical centre (both edges move symmetrically). CanUp scales around the baseline (bottom stays flat, top arches up). CanDown scales around the bbox top (top stays flat, bottom arches down).

This is an affine per-glyph approximation — Word actually distorts each glyph's path between two non-parallel curves, so individual glyphs in Word have slightly tapered sides where mine stay rectangular. The shape envelope and overall layout match; per-glyph trapezoid distortion would need glyph-path tessellation, much bigger than the linear-scale approach.

Single-character labels use `t = 0.5` for the box-filling warps so the centre amplitude still applies (a one-letter Inflate would otherwise compute `t=0` → no warp).

See `ImageSharpPageRenderer.TryRenderWordArtEnvelope` and `SkiaPageRenderer.TryRenderWordArtEnvelope`.
