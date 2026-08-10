### Decorative shapes are SVG with raster fallback

The pink leaf in the top-right (and the dot patterns in the corners) ship as both an SVG (`a:svgBlip` extension) and a PNG raster fallback. Skia renders the SVG via `Svg.Skia`; ImageSharp can't render SVG (`PageRendererBase.CanRenderContentType` returns false for `image/svg+xml`) and falls back to the PNG. Both paths now honour `<a:srcRect>` cropping — see `RenderSvgImage` in `Morph.Skia/SkiaPageRenderer.cs` for the SVG-specific implementation.

Pixel-level differences between Skia's SVG render and Word's renderer remain (gradient interpolation, anti-aliasing) — the verified PNGs lock in our renderer's output, not perfect Word fidelity.
