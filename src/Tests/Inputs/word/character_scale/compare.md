# character_scale

Pins `w:w` — a run's characters stretched or condensed horizontally.

Six one-line paragraphs of `HHHH` at 24pt Arial under `w:w` 100, 50, 66, 150, 200 and 300,
each followed by an unscaled `|` that shows where the scaled run's advance ends; then a sentence
repeated to three lines at 12pt under 50% and 200%, to show the scale moving the line breaks.

Read from Word's XPS:

* A scaled run is drawn under a pure horizontal scale matrix about its own origin — `0.5,0,0,1`
  at 50%, `2,0,0,1` at 200%, `3,0,0,1` at 300% — so glyph outlines and advances scale together
  and heights do not. 66% draws at 0.65909, the em rounded onto Word's pixel grid; the layout
  takes the declared percent.
* Line heights are unchanged by the scale.
* The sentence breaks where its scaled widths say: three long lines at 50%, five short ones at
  200%.

Morph measures each run's advances at the scale (`RunProperties.CharacterScalePercent`, through
the measurer's width scale) and every painter draws it under the same x-scale about the run's
start. Before this fixture `w:w` was not read.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0222 · SSIM: 0.9758** | **Page 1. ErrorMetric: 0.0228 · SSIM: 0.9758** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
