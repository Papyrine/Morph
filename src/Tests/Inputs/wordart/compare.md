# wordart

### ImageSharp arc/circle warps render as flat text

ImageSharp.Drawing 2.1.7 has no text-on-path API. Skia uses `SKCanvas.DrawTextOnPath` to follow `prstTxWarp` arcs (`textArchUp` / `textArchDown` / `textCircle`) — see `Morph.Skia/SkiaPageRenderer.cs:TryRenderWordArtOnPath`. ImageSharp falls back to flat text at the correct font size; the warp shape is lost but text is at least readable and not blown up to fill the bbox.

If/when ImageSharp.Drawing gains text-on-path (or we move to a higher version), mirror the Skia implementation in `Morph.ImageSharp/ImageSharpPageRenderer.cs`.

### Other warps (Wave / Chevron / Slant / Triangle / Fade)

These are still approximated via canvas transforms in `ApplyWordArtTransform`. Visually crude but not actively broken — full path-based warps would need a per-warp glyph-positioning step (each preset has its own envelope).

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1** | **Page 1. ErrorMetric: 0.0076** | **Page 1. ErrorMetric: 0.0094** |
| <img src="expected_0001.png" width="500"> | <img src="results_skia%23page_0001.verified.png" width="500"> | <img src="results_imagesharp%23page_0001.verified.png" width="500"> |
| **Page 2** | **Page 2. ErrorMetric: 0.0476** | **Page 2. ErrorMetric: 0.0399** |
| <img src="expected_0002.png" width="500"> | <img src="results_skia%23page_0002.verified.png" width="500"> | <img src="results_imagesharp%23page_0002.verified.png" width="500"> |
| **Page 3** | **Page 3. ErrorMetric: 0.2091** | **Page 3. ErrorMetric: 0.2094** |
| <img src="expected_0003.png" width="500"> | <img src="results_skia%23page_0003.verified.png" width="500"> | <img src="results_imagesharp%23page_0003.verified.png" width="500"> |
| **Page 4** | **Page 4. ErrorMetric: 0.2014** | **Page 4. ErrorMetric: 0.2046** |
| <img src="expected_0004.png" width="500"> | <img src="results_skia%23page_0004.verified.png" width="500"> | <img src="results_imagesharp%23page_0004.verified.png" width="500"> |
| **Page 5** | **Page 5. ErrorMetric: 0.0320** | **Page 5. ErrorMetric: 0.0301** |
| <img src="expected_0005.png" width="500"> | <img src="results_skia%23page_0005.verified.png" width="500"> | <img src="results_imagesharp%23page_0005.verified.png" width="500"> |
| **Page 6** | **Page 6. ErrorMetric: 0.0475** | **Page 6. ErrorMetric: 0.0432** |
| <img src="expected_0006.png" width="500"> | <img src="results_skia%23page_0006.verified.png" width="500"> | <img src="results_imagesharp%23page_0006.verified.png" width="500"> |
| **Page 7** | **Page 7. ErrorMetric: 0.0456** | **Page 7. ErrorMetric: 0.0455** |
| <img src="expected_0007.png" width="500"> | <img src="results_skia%23page_0007.verified.png" width="500"> | <img src="results_imagesharp%23page_0007.verified.png" width="500"> |
| **Page 8** | **Page 8. ErrorMetric: 0.0703** | **Page 8. ErrorMetric: 0.0678** |
| <img src="expected_0008.png" width="500"> | <img src="results_skia%23page_0008.verified.png" width="500"> | <img src="results_imagesharp%23page_0008.verified.png" width="500"> |
| **Page 9** | **Page 9. ErrorMetric: 0.0466** | **Page 9. ErrorMetric: 0.0470** |
| <img src="expected_0009.png" width="500"> | <img src="results_skia%23page_0009.verified.png" width="500"> | <img src="results_imagesharp%23page_0009.verified.png" width="500"> |
| **Page 10** | **Page 10. ErrorMetric: 0.0614** | **Page 10. ErrorMetric: 0.0617** |
| <img src="expected_0010.png" width="500"> | <img src="results_skia%23page_0010.verified.png" width="500"> | <img src="results_imagesharp%23page_0010.verified.png" width="500"> |
| **Page 11** | **Page 11. ErrorMetric: 0.0550** | **Page 11. ErrorMetric: 0.0539** |
| <img src="expected_0011.png" width="500"> | <img src="results_skia%23page_0011.verified.png" width="500"> | <img src="results_imagesharp%23page_0011.verified.png" width="500"> |
| **Page 12** | **Page 12. ErrorMetric: 0.0298** | **Page 12. ErrorMetric: 0.0268** |
| <img src="expected_0012.png" width="500"> | <img src="results_skia%23page_0012.verified.png" width="500"> | <img src="results_imagesharp%23page_0012.verified.png" width="500"> |
| **Page 13** | **Page 13. ErrorMetric: 0.0172** | **Page 13. ErrorMetric: 0.0161** |
| <img src="expected_0013.png" width="500"> | <img src="results_skia%23page_0013.verified.png" width="500"> | <img src="results_imagesharp%23page_0013.verified.png" width="500"> |
| **Page 14** | **Page 14. ErrorMetric: 0.0675** | **Page 14. ErrorMetric: 0.0637** |
| <img src="expected_0014.png" width="500"> | <img src="results_skia%23page_0014.verified.png" width="500"> | <img src="results_imagesharp%23page_0014.verified.png" width="500"> |
| **Page 15** | **Page 15. ErrorMetric: 0.0325** | **Page 15. ErrorMetric: 0.0331** |
| <img src="expected_0015.png" width="500"> | <img src="results_skia%23page_0015.verified.png" width="500"> | <img src="results_imagesharp%23page_0015.verified.png" width="500"> |
