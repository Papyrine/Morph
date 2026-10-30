# spacing_line_units

Pins paragraph spacing counted in lines (`w:spacing/@w:beforeLines`, `@w:afterLines`) off a
document grid, beside the line rules for reference.

The paragraphs of `document_grid_lines` under a bare `<w:docGrid w:linePitch="360"/>` with no
`@w:type`, which Word writes into most documents as a stored default and lays nothing out on.

* A line unit is 12pt off a grid: `beforeLines="100"` adds 12pt, `afterLines="50"` 6pt.
* A positive line unit outranks the twips value beside it: `before="480" beforeLines="50"` adds
  6pt, not 24.
* `0` is ignored rather than zeroing the spacing: `afterLines="0" after="480"` adds 24pt.
  agendas-minutes/17's style declares `afterLines="0"` beside `after="40"`, and keeps its 2pt.
* The untyped `linePitch="360"` changes no line: a 12pt Arial line stays 13.8pt.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0135 · SSIM: 0.9879** | **Page 1. ErrorMetric: 0.0139 · SSIM: 0.9894** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
| **Page 2**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 2. ErrorMetric: 0.0110 · SSIM: 0.9939** | **Page 2. ErrorMetric: 0.0111 · SSIM: 0.9941** |
| <img src="expected_0002.png" width="500"> | <img src="skia_result%23page_0002.verified.png" width="500"> | <img src="imagesharp_result%23page_0002.verified.png" width="500"> |
