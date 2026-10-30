# underline_styles

Pins every `w:u/@w:val` style, plus `w:strike`, `w:dstrike` and `w:dstrike` over `w:u="single"`.

One paragraph per style, Arial at 48pt — amplified so a dash, a gap and a band thickness are each
several pixels at 150 DPI. Each line is `HH HH ` plain then `HHH  HH` underlined, so `words` has
two spaces to leave bare.

Read from Word's XPS (`MORPH_KEEP_XPS=1`), and again from the same document at 24pt to separate
laws that scale with the font from ones that do not. `p` is the single underline's offset below
the baseline and `t` its thickness, both from the font (Arial: p = 5.04, t = 3.48 at 48pt;
2.52 / 1.80 at 24pt). XPS values are quantised to 0.12pt.

| style | Word draws | 48pt | 24pt |
| --- | --- | --- | --- |
| single, light dashes | band `[p, p+t]` | +5.04, 3.48 | +2.52, 1.80 |
| words | the same, over non-space runs only | gap 26.64 = two spaces | |
| double | two `t/2` rules filling `[p/2, p+t]` | +2.52 and +6.72, 1.80 each | +1.32 and +3.36, 0.96 each |
| thick, every heavy | band `[p/2, p+t/2]` | +2.52, 4.20 | +1.32, 2.04 |
| wave | 2.4pt period, top at `p/2`, stroke 0.24pt | crest-trough 2.88 | 1.32 |
| wavyHeavy | as wave, stroke 0.96pt | | |
| wavyDouble | two waves, the second 1.56 lower at 48pt | | |
| strike | the font's strikeout band | -12.48, 2.40 | -6.24, 1.20 |
| dstrike | two strike-thick rules, one thickness either side of the single's centre | -14.88 and -10.08 | -7.44 and -5.04 |

Dash patterns are **fixed lengths in points** — identical at 24pt and 48pt — patterned from the
run's start, and a heavy style keeps its light style's pattern:

| style | on / off (pt) |
| --- | --- |
| dotted | 1.44 / 1.44 |
| dash | 3.96 / 1.92 |
| dashLong | 7.92 / 3.96 |
| dotDash | 3.0 / .96 / .96 / .96 |
| dotDotDash | 2.16 / .72 / .72 / .72 / .72 / .72 |

Morph's single underline and strike keep their own placement (`size * 0.12` and `ascent * 0.3`);
every other style is placed against those exactly as Word places it against the font's
(`TextDecorations`).

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0374 · SSIM: 0.9471** | **Page 1. ErrorMetric: 0.0378 · SSIM: 0.9507** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
| **Page 2**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 2. ErrorMetric: 0.0321 · SSIM: 0.9522** | **Page 2. ErrorMetric: 0.0325 · SSIM: 0.9544** |
| <img src="expected_0002.png" width="500"> | <img src="skia_result%23page_0002.verified.png" width="500"> | <img src="imagesharp_result%23page_0002.verified.png" width="500"> |
