# nonstandard_main_part_name

Regression scenario for an OPC package whose main document part is **not** `word/document.xml`.

This is the only fixture in the corpus with that shape: `_rels/.rels` points its
`officeDocument` relationship at `word/document2.xml`, so any code that reaches for the
conventional part name instead of resolving the relationship finds nothing and renders a blank
page. Word writes this layout after certain edit/round-trip histories, and the SDK's
`MainDocumentPart` resolves it correctly — the scenario exists so that stays true.

Also exercised, all incidental to the source template but worth having covered:

- `<w:b w:val="false"/>` — the string form of `ST_OnOff` rather than `0`. The Heading 3 line
  ("SMITH, John") splits a bold run from a bold-off run, so a parser that only recognises
  `0`/`off` renders the whole line bold.
- `w:titlePg` with distinct first/even/default headers **and** footers, where the first-page
  header carries a shaded banner table (compare `header_banner_table`, which isolates that) and
  the first-page footer is a plain `PAGE` field.
- A4 page size (`w:pgSz` 11907×16840, `w:code="9"`) — most of the corpus is US Letter, so this
  is the main A4 pagination reference.
- A fixed-layout single-cell table whose only content is seven `<w:br/>` runs: an empty
  "Notes:" box whose height comes entirely from break-only line boxes.

Provenance: derived from a public-sector template. The classification marking it carried in its
header/footer was replaced with the equal-length placeholder `SENSITIVE//EXAMPLE` (identical
glyph metrics, so the layout is unchanged), and the originating tenant's SharePoint
document-management metadata — taxonomy term GUIDs, record ID, site-column schema — was dropped
from `customXml/item2.xml` and `item3.xml`. None of it is read by the renderer.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0156 · SSIM: 0.9716** | **Page 1. ErrorMetric: 0.0191 · SSIM: 0.9592** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
