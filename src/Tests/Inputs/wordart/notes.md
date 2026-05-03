### ImageSharp arc/circle warps render as flat text

ImageSharp.Drawing 2.1.7 has no text-on-path API. Skia uses `SKCanvas.DrawTextOnPath` to follow `prstTxWarp` arcs (`textArchUp` / `textArchDown` / `textCircle`) — see `Morph.Skia/SkiaPageRenderer.cs:TryRenderWordArtOnPath`. ImageSharp falls back to flat text at the correct font size; the warp shape is lost but text is at least readable and not blown up to fill the bbox.

If/when ImageSharp.Drawing gains text-on-path (or we move to a higher version), mirror the Skia implementation in `Morph.ImageSharp/ImageSharpPageRenderer.cs`.

### Other warps (Wave / Chevron / Slant / Triangle / Fade)

These are still approximated via canvas transforms in `ApplyWordArtTransform`. Visually crude but not actively broken — full path-based warps would need a per-warp glyph-positioning step (each preset has its own envelope).
