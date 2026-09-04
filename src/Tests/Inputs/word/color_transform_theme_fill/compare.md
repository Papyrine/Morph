# color_transform_theme_fill

Pins the WordprocessingML byte form — `w:themeFillShade` / `w:themeFillTint` on a `w:shd` — which
is **HSL luminance scaling**, a genuinely different transform from the DrawingML `a:shade` in
`color_transform_shade_tint` rather than a different encoding of it.

Fourteen full-width shaded paragraphs at an exact 12pt line, so each renders as a flat horizontal
band that can be sampled by scanning a column down the page. Two groups of seven, `w:themeFill`
of `accent1` (4472C4) then `accent2` (ED7D31): plain, then `w:themeFillShade` at `40`/`80`/`BF`,
then `w:themeFillTint` at `40`/`80`/`BF`.

The model is `L' = L·(S/255)` for shade and `L' = L·(T/255) + (255−T)/255` for tint, applied to
HSL luminance with hue and saturation preserved. **Word's values:**

| fill | plain | Shade 40 | Shade 80 | Shade BF | Tint 40 | Tint 80 | Tint BF |
| --- | --- | --- | --- | --- | --- | --- | --- |
| accent1 | 4472C4 | 0F1C32 | 1F3864 | 2F5496 | CFDBF0 | A0B7E1 | 7295D2 |
| accent2 | ED7D31 | 411E05 | 833C0B | C45911 | FADECB | F6BD97 | F19D64 |

Applying the linear-light model that IS correct for `a:shade` is out by up to 62 per channel here;
an sRGB blend by up to 103. Luminance scaling is exact to within one channel step on all twelve.
That last step does not clear under either `Math.Round` or truncation and is treated as a rounding
difference in the HSL round trip rather than a model error.

**Why the fixture exists.** These two attributes were parsed nowhere. Every band rendered as its
flat undarkened base — all twelve of them — and no corpus scenario noticed, because the shade of an
accent looks entirely reasonable until it is set beside the value Word produces. `w:color`'s
equivalent `w:themeShade`/`w:themeTint` had been read correctly all along, so a reading of the
parser that started from the run colour found nothing wrong.

The text in each band is incidental, present only to give the paragraph a line box; the sampling
column avoids the glyphs.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.1230 · SSIM: 0.9631** | **Page 1. ErrorMetric: 0.1231 · SSIM: 0.9627** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
