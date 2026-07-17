# pct_pos_offset

# pct_pos_offset

Exercises percentage-based positioning of anchored shapes via `wp14:pctPosHOffset` /
`wp14:pctPosVOffset` (the Word 2010 wordprocessing-drawing extension where the offset
value is `1000 × percent`, e.g. `50000` → 50%).

The page contains two `behindDoc=1` rectangles whose only positioning information is
percentage-based:

| Shape | Position             | Encoding                                                       |
|-------|----------------------|----------------------------------------------------------------|
| Red   | 10% of page / 10%    | bare `<wp14:pctPosHOffset>` / `<wp14:pctPosVOffset>` children  |
| Blue  | 50% of page / 50%    | wrapped in `mc:AlternateContent` with a wp14 `mc:Choice` and an EMU `mc:Fallback` |

The two encodings together cover both code paths in
`OpenXmlExtensions.ParsePositioning` — the direct lookup of `wp:positionH`/`wp:positionV`
and the `mc:AlternateContent` unwrapping that picks the wp14 `mc:Choice`. Without the
unwrapping, the blue square would render at the EMU fallback (slightly off — ~10% wide
shift on letter-size paper) and the diff against Word's render would jump by an order of
magnitude.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0040** | **Page 1. ErrorMetric: 0.0046** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
