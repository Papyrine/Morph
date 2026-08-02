# header_banner_table

Regression scenario for shaded banner tables in a page header — the pattern many Word templates
use to paint a coloured marking/title bar above the body.

Covers two former bugs:

1. Tables inside a header/footer were parsed but never rendered — `RenderHeaderFooterElements`
   had no `TableElement` branch, so the banner silently disappeared.
2. Rows without an explicit `w:trHeight` were forced to a 20pt floor, so the thin spacer rows
   (whose height comes only from an empty paragraph mark, `sz=6`/`sz=10`) ballooned and doubled
   the banner's height.

The header holds only the banner, sized to sit within the header band, so the body clears it
without header-space reservation (a separate concern).

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0364 · SSIM: 0.9712** | **Page 1. ErrorMetric: 0.0413 · SSIM: 0.9566** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
