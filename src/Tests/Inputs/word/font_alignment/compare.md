# font_alignment

Pins `w:textAlignment` — where a line's smaller runs sit against its largest.

Each paragraph sets a small `Hx` beside a large one under `baseline`, `top`, `center` and
`bottom`, at two size pairs (6pt/24pt and 8pt/40pt) in two faces (Arial, Times New Roman).

Read from Word's XPS as the small run's baseline less the large run's, as a fraction of how much
smaller the small run is:

| alignment | Arial 6/24 | Arial 8/40 | Times 6/24 | Times 8/40 |
| --- | --- | --- | --- | --- |
| baseline | 0 | 0 | 0 | 0 |
| top | up 0.887 | up 0.904 | up 0.900 | up 0.889 |
| center | up 0.333 | up 0.345 | up 0.340 | up 0.341 |
| bottom | down 0.113 | down 0.105 | down 0.107 | down 0.105 |

The fractions agree within 0.015 across two faces whose ascents differ widely (Arial 0.905,
Times New Roman 0.891 of the em with very different internal leading), so Word places the runs on
their em boxes rather than by the faces' metrics; top and bottom sum to one em. Morph moves each
run by 0.895, 0.34 and -0.105 of its difference from the line's largest
(`CanonicalParagraphMeasurer.AlignToLargest`). The line keeps its height. Before this fixture
`w:textAlignment` was not read.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0051 · SSIM: 0.9940** | **Page 1. ErrorMetric: 0.0054 · SSIM: 0.9943** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
| **Page 2**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 2. ErrorMetric: 0.0004 · SSIM: 0.9997** | **Page 2. ErrorMetric: 0.0005 · SSIM: 0.9997** |
| <img src="expected_0002.png" width="500"> | <img src="skia_result%23page_0002.verified.png" width="500"> | <img src="imagesharp_result%23page_0002.verified.png" width="500"> |
