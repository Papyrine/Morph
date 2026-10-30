# ole_chart_preview

A LibreOffice-saved letter-size document whose chart is an embedded OLE object
(`LibreOffice.ChartDocument.1`): a `w:object` holding a floating VML `v:shape`
(`position:absolute`, 322.8pt x 162.2pt, 92.5pt right of the column and 8.5pt below its paragraph,
`w10:wrap type="square" side="largest"`) whose `v:imagedata` is the chart's EMF preview, beside the
`o:OLEObject` itself. Word draws the object from that preview, and so does Morph
(`OlePreviewParser`, `MetafileComposer`). The shape is `filled="t"` with no `fillcolor`, so the chart
stands on VML's default white, which shows against the document's grey page colour.

The body is set in DejaVu Sans, which the fontTable gives `w:altName` Verdana — what Word substitutes,
and what `FontHelpers.FontFallbacks` maps it to.

**The references are printed, not exported.** Word cannot export this file: `SaveAs2` to XPS (what
RenderHelper does) and `ExportAsFixedFormat` to PDF both fail with "The export failed due to an
unexpected error", under every OptimizeFor / DocStructureTags / BitmapMissingFonts / PDF/A
combination. The EMF is the trigger: the same document exports with the `w:object` removed, and with
`letters/10`'s EMF swapped in for its own. Word does still lay it out and print it, so the
`expected_*.png` here were printed from Word 16 to Microsoft Print to PDF (with Word's
`Options.MapPaperSize` off, else it scales the letter page onto the printer's A4 paper) and rasterised
by PDFium at 150 DPI. A print leaves out the page colour, so the references are on white where
Morph's pages are grey.

Measured against them, the chart sits 23px (11pt) higher on Morph's page than on Word's, relative to
the last bullet above it — the 11.25pt `w:after` of that bullet's paragraph. See `src/todo.md`.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.9089 · SSIM: 0.7658** | **Page 1. ErrorMetric: 0.9095 · SSIM: 0.7663** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
| **Page 2**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 2. ErrorMetric: 0.9999 · SSIM: 0.6877** | **Page 2. ErrorMetric: 0.9999 · SSIM: 0.6829** |
| <img src="expected_0002.png" width="500"> | <img src="skia_result%23page_0002.verified.png" width="500"> | <img src="imagesharp_result%23page_0002.verified.png" width="500"> |
