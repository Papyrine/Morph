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
