### Arc/Circle warps use chord-sagitta envelope geometry

The three single-curve warps (`textArchUp` / `textArchDown` / `textCircle`) render with the geometry Word actually uses, in both backends:

- **`textArchUp` / `textArchDown`**: bbox width is the arc **chord**, bbox height is the **sagitta** (perpendicular distance from chord midpoint to arc midpoint). Circle radius is `R = (W² + 4H²) / (8H)`. For a typical 4:1 wide-and-flat WordArt bbox (e.g. 432pt × 108pt), this gives R ≈ 270pt and a 106° arc — a gentle, mostly-horizontal curve that matches Word. Treating the bbox as the *full* arc bounding box (the obvious first read of "draw an arc inside the bbox") gives a 180° half-ellipse, far sharper and wrongly off-centre.
- **`textCircle`**: text wraps the right side of an inscribed circle. Short text covers a small arc centred on 3 o'clock and reads downward — matches Word's behaviour where the bbox diameter sizes the circle and text sits on the right hemisphere.

Path is sized to the rendered text width and centred on the arc peak/dip (or 3 o'clock for circle), so glyphs sit at the bbox-centre without depending on path-text alignment options. Neither ImageSharp's `RichTextOptions.HorizontalAlignment` nor Skia's `SKTextAlign.Center` centre text the way the obvious read suggests — both place text from the offset point in the direction of path travel — so sizing the path to fit is the more robust approach across both backends.

See `ImageSharpPageRenderer.TryRenderWordArtOnPath` / `BuildChordSagittaArc` and `SkiaPageRenderer.TryRenderWordArtOnPath` / `BuildChordSagittaArc`.

### Wave + Chevron also use path-based rendering

`textChevron` / `textChevronInverted` route through the same chord-sagitta arc as ArchUp/Down — Word's chevron renders as a single-peak smooth arch, not a sharp ^. (A literal polyline ^/v path causes per-glyph overlap at the discontinuous apex regardless of fillet size.)

`textWave1` uses a 64-segment polyline approximation of one full cosine period across the rendered text width: `y = midY - amplitude·cos(2π·t/textWidth)`. Amplitude is bbox H/4 (not H/2) so the wave excursion stays gentle relative to glyph height, matching Word.

### Remaining warps (Slant / Triangle / Fade / Inflate / Deflate / Can)

Aren't text-on-path — they're *envelope warps* where each glyph is non-uniformly scaled or sheared. Skia today approximates Slant with a `RotateDegrees`, Fade with a `Skew`, and Triangle with an x-scale; ImageSharp falls back to flat. Real envelope rendering would need per-glyph affine transforms (draw each character through `canvas.Save(transform)` / `Restore`), bigger than the single-curve cases above.
