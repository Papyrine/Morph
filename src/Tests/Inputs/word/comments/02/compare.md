# comments/02

Pins how Word prints comments with All Markup when there are several, with
`ExportOptions.ShowComments` on (the scenario suite turns it on).

Built from `comments/01`'s package (no styles, Calibri 12pt body, Letter with 1in margins) with its
body and comments swapped. Four comments over two pages:

- **R1** on "First paragraph.", 2 lines × 116 characters long, so its balloon wraps over five lines.
  Word's fourth line ends "…in the balloon" (224.02pt of Calibri 9pt) and its first refuses
  "should" (225.53pt), which brackets the balloon text's measure.
- **R2** on "Second commented paragraph.", two paragraphs, each on a line of its own.
- **R3** on the very next line, whose balloon cannot stand level with it: Word pushes it to 2.4pt
  under R2's bottom.
- **R4** on page 2, so the pane is drawn on both pages.

Word's XPS, in the page's own points (shrink 0.7590, page top 95.47pt down the paper):

| balloon | top | bottom | first baseline |
| --- | --- | --- | --- |
| R1 | 72.0 (its line's top) | 131.4 | 84.0 |
| R2 | 246.6 (its line's top) | 272.9 | 258.6 |
| R3 | 275.4 (R2's bottom + 2.4) | 291.0 | 287.4 |

The geometry these come from, and the other six probes that varied the page and its margins, is in
`docs/word-features.md`, Comments.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0243 · SSIM: 0.9580** | **Page 1. ErrorMetric: 0.0330 · SSIM: 0.9584** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
| **Page 2**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 2. ErrorMetric: 0.0050 · SSIM: 0.9913** | **Page 2. ErrorMetric: 0.0074 · SSIM: 0.9878** |
| <img src="expected_0002.png" width="500"> | <img src="skia_result%23page_0002.verified.png" width="500"> | <img src="imagesharp_result%23page_0002.verified.png" width="500"> |
