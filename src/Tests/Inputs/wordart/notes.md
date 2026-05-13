### ImageSharp arc/circle warps now render along a path

ImageSharp.Drawing 3.0 added `DrawingCanvas.DrawText(options, text, IPath, brush, pen)` so the three single-curve warps (`textArchUp` / `textArchDown` / `textCircle`) now render warped instead of flat. Mirrors `SkiaPageRenderer.TryRenderWordArtOnPath` — same arc geometry, same alignment choices. See `ImageSharpPageRenderer.TryRenderWordArtOnPath`.

The text-on-path positioning isn't a perfect pixel match for Skia. ImageSharp's interpretation of `RichTextOptions.HorizontalAlignment` along a path baseline differs subtly from Skia's `SKTextAlign`, so the visible text sits at a different offset within the same bounding box. Acceptable today since both backends are still well off Word's reference positions for floating WordArt — a follow-up fix would be alignment tuning rather than re-architecture.

### Other warps (Wave / Chevron / Slant / Triangle / Fade)

Still approximated via canvas transforms in `Morph.Skia.ApplyWordArtTransform`; ImageSharp falls back to flat shrink-to-fit text. Visually crude but not actively broken — full path-based warps would need a per-warp glyph-positioning step (each preset has its own envelope), bigger than the single-curve cases above.
