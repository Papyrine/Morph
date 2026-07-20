# inline_group_rotation

# inline_group_rotation

Covers a group-level rotation (`wpg:grpSpPr/a:xfrm/@rot`) on an inline `wpg:wgp` that contains
a `pic:pic`. Word rotates the whole group — shapes *and* pictures — around the group centre.

Derived from `menus/07`: the three circle-cropped photo groups given distinct rotations so a
wrong pivot or a flipped sign shows up.

| group        | `@rot`      | degrees |
|--------------|-------------|---------|
| Appetizer    | `1800000`   | 30      |
| Main Course  | `5400000`   | 90      |
| Dessert      | `12000000`  | 200     |

The rotation composes with the rest of each group: the photo is still ellipse-cropped by its
`pic:spPr`, ringed by its `a:ln`, and casts its `a:outerShdw`. `expected_0001.png` is Word's own
render (via `RenderHelper`).

The corpus otherwise has no rotated group containing a picture: the eight rotated groups in
`business-plans/12` and `inline_shape_arrows` are all connector-line arrow glyphs.

Skia (`canvas.RotateDegrees` + `DrawBitmap`), the PDF backend (`XGraphics.RotateAtTransform`)
and HTML (an SVG `<g transform="rotate(...)">`) rotate the picture through the same canvas
transform that rotates the shapes. ImageSharp's `DrawingCanvas.Apply` — the ellipse-clip path —
ignores a pushed rotation, so a rotated photo is drawn from a pre-clipped standalone bitmap via
`DrawImage`, which honours it.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.1799 · SSIM: 0.7978** | **Page 1. ErrorMetric: 0.2019 · SSIM: 0.7856** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
