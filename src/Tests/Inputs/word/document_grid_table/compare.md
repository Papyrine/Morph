# document_grid_table

Pins where the document grid applies: in a table cell, and in a header.

A 36pt `lines` grid (`linePitch="720"`) with `w:compat/w:adjustLineHeightInTable` set, a body
paragraph, a one-cell table of three 12pt paragraphs, two more body paragraphs, and a two-line
header. The same document without `adjustLineHeightInTable` was also rendered.

| | with `adjustLineHeightInTable` | without |
| --- | --- | --- |
| body lines | 36 | 36 |
| cell lines | 36 | 13.8 (natural) |
| header lines | 13.8 | 13.8 |

* A table cell's paragraphs snap to the grid only under `adjustLineHeightInTable`.
* A header's paragraphs never snap (nor, by the same rule in the parser, a footer's or a note's).
* Without the flag, the body paragraph after the table starts where the table ended and is
  centred in its 36pt box from there: the grid sets line heights, not fixed positions on the
  page.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0023 · SSIM: 0.9990** | **Page 1. ErrorMetric: 0.0024 · SSIM: 0.9994** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
