# agendas-minutes/16

### Decorative shapes are SVG with raster fallback

The pink leaf in the top-right (and the dot patterns in the corners) ship as both an SVG (`a:svgBlip` extension) and a PNG raster fallback. Skia renders the SVG via `Svg.Skia`; ImageSharp can't render SVG (`PageRendererBase.CanRenderContentType` returns false for `image/svg+xml`) and falls back to the PNG. Both paths now honour `<a:srcRect>` cropping — see `RenderSvgImage` in `Morph.Skia/SkiaPageRenderer.cs` for the SVG-specific implementation.

Pixel-level differences between Skia's SVG render and Word's renderer remain (gradient interpolation, anti-aliasing) — the verified PNGs lock in our renderer's output, not perfect Word fidelity.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.1156 · SSIM: 0.8420** | **Page 1. ErrorMetric: 0.1149 · SSIM: 0.8362** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
