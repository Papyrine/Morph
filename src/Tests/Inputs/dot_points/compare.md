# dot_points

# dot_points

Multi-level bullet list exercising the three Word-default bullet glyphs across the
9 numbering levels (cycling every 3):

- ilvl 0/3/6 - Symbol font, U+F0B7 (filled round bullet)
- ilvl 1/4/7 - Courier New, literal lowercase 'o'
- ilvl 2/5/8 - Wingdings, U+F0A7 (small filled square)

Use this scenario to verify per-level bullet font + glyph fidelity.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0202** | **Page 1. ErrorMetric: 0.0216** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
