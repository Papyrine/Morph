# agendas-minutes/19

### Empty table rows render shorter than Word

The 17 empty rows in the contact table use `<w:trHeight w:val="432"/>` (21.6pt) with no `hRule`. Word renders them at ~30pt; we render at ~25pt. The document has `<w:docGrid w:linePitch="360"/>` (18pt), and Word treats `linePitch` as a per-line minimum even inside table cells. We honour this in `LayoutParagraphForMeasurement` (Skia + ImageSharp), but only for empty paragraphs — applying it to non-empty paragraphs broke other scenarios (e.g. `agendas-minutes/02`'s short data rows).

The remaining ~5pt gap (linePitch 18 + cell padding 7.2 = 25.2pt vs Word's 30pt) isn't fully decoded. Word likely adds extra leading or interprets `trHeight` differently when `linePitch` is present, but a clean formula would need empirical sweeps across more docs.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1** | **Page 1. ErrorMetric: 0.1473** | **Page 1. ErrorMetric: 0.1496** |
| <img src="expected_0001.png" width="500"> | <img src="results_skia%23page_0001.verified.png" width="500"> | <img src="results_imagesharp%23page_0001.verified.png" width="500"> |
