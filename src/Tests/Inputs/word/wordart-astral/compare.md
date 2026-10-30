# wordart-astral

### Supplementary-plane glyphs in the envelope warps (Fade / Triangle)

Pins the rule that a character above the BMP is **one glyph** under a warp: one outline, taking one
place in the line that is stretched onto the envelope.

Six WordArt boxes, Arial Bold 48pt, all six identical apart from the text. `U+10780`
(MODIFIER LETTER SMALL CAPITAL AA — Arial 7.06 glyph 4557, UTF-16 `D801 DF80`) is placed at each
position in an otherwise all-`A` label, against an all-BMP control of the same glyph count:

| box | warp | text |
| --- | --- | --- |
| 1 | `textFadeRight` | `AAAAA` (control) |
| 2 | `textFadeRight` | `𐞀AAAA` |
| 3 | `textFadeRight` | `AA𐞀AA` |
| 4 | `textFadeRight` | `AAAA𐞀` |
| 5 | `textTriangle` | `AAAAA` (control) |
| 6 | `textTriangle` | `AA𐞀AA` |

**What draws it.** Fade and Triangle are envelope warps like the rest (`WordArtEnvelope`): the whole
line is outlined in one call (`SKFont.GetTextPath` / `TextBuilder.GeneratePaths`) and its ink box bent
between the preset's two guides. That never indexes into the string, so a surrogate pair is safe by
construction. Until 2026-10-11 these two presets ran a per-glyph loop that scaled each character on its
own; split per UTF-16 unit, it handed the backend two lone surrogates, lost the glyph and counted six
positions for five. The arches and the circle still place glyph by glyph, by text element
(`WordArtArch.Glyphs`).

**Word's own reference is the reason this is a fidelity fixture.** The bundled `src/Fonts/Arial_700.ttf`
and the machine's `arialbd.ttf` are both Version 7.06 and map `U+10780` to the same glyph 4557, so Word
draws the real glyph, not a fallback, and `expected_0001.png` shows it taking exactly one position in
each warp, the line stretched to its box as every envelope warp's is.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.1014 · SSIM: 0.9719** | **Page 1. ErrorMetric: 0.1015 · SSIM: 0.9760** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
