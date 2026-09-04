# color_transform_hsl

Pins the HSL transform family — `a:satMod`, `a:lumMod`, `a:lumOff` — and in particular the rule
that **saturation is not clamped**.

A 6x7 grid of page-anchored 0.8in squares, first swatch at 0.35in / 0.8in, 1.1in pitch.

Rows 1-4 sweep `a:satMod` at 50/100/150/200/300/400% over bases of rising saturation:
4472C4 (s≈0.50), ED7D31 (s≈0.83), A5A5A5 (s=0, a control that must not move at any value) and
FF0000 (s=1, already at the gamut edge).

Rows 5-7 are the luminance cases over 4472C4, ED7D31 and C8C8C8: `lumMod 150`, `lumMod 200`,
`lumOff 50`, `lumOff 80`, `lumMod 75 + lumOff 50`, `lumMod 200 + lumOff 50`.

**Saturation is unclamped.** Word lets `a:satMod` drive HSL saturation past 1 and clips at the
RGB byte instead. 4472C4 therefore keeps moving after saturation nominally maxes out:

| base | 50% | 100% | 150% | 200% | 300% | 400% |
| --- | --- | --- | --- | --- | --- | --- |
| 4472C4 | 647BA4 | 4472C4 | 2469E4 | 0460FF | 004EFF | 003CFF |
| ED7D31 | BE8660 | ED7D31 | FF7402 | FF6B00 | FF5900 | FF4700 |
| A5A5A5 | A5A5A5 | A5A5A5 | A5A5A5 | A5A5A5 | A5A5A5 | A5A5A5 |
| FF0000 | BF4040 | FF0000 | FF0000 | FF0000 | FF0000 | FF0000 |

Clamping saturation to 1 parks 4472C4 at 0961FF from 200% onward and is out by up to 51 per
channel by 400%. The unclamped model is exact on all 24. This is not an edge case: over 97% of
the `a:satMod` values in the corpus exceed 100%.

**Luminance, by contrast, may be clamped** — an out-of-range luminance saturates to black or
white either way, and rows 5-7 measured identical under both. The implementation clamps it, and
this fixture records that the choice is free rather than load-bearing.

The `A5A5A5` row is the one to read first if this scenario ever fails: a neutral grey has no
saturation to modulate, so any movement there means the HSL round trip itself has broken rather
than the transform.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0065 · SSIM: 0.9877** | **Page 1. ErrorMetric: 0.0088 · SSIM: 0.9874** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
