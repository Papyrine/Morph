# inline_group_crop

# inline_group_crop

Covers `a:srcRect` crops on the `pic:pic` members of an inline `wpg:wgp` group.

Derived from `menus/07` — the same three circle-cropped photos, each given a different
crop so a swapped axis or a flipped sign shows up:

| picture          | `a:srcRect`                          | effect                    |
|------------------|--------------------------------------|---------------------------|
| Bowl of soup     | `l="30000"`                          | trims 30% off the left    |
| Warm potato dish | `t="30000"`                          | trims 30% off the top     |
| Cupcakes         | `l/t="10000"`, `r/b="25000"`         | asymmetric on all four    |

The crop composes with everything else the group carries: each picture is still cropped to an
ellipse by its `pic:spPr/a:prstGeom`, ringed by its `a:ln`, and casts the `a:outerShdw` drop
shadow. `expected_0001.png` is Word's own rendering (via `RenderHelper`).

The corpus otherwise has no usable `a:srcRect` inside a group: `menus/07`'s only value is a
negative `t="-168"`, which `ReadCrop` clamps away.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.1813 · SSIM: 0.7999** | **Page 1. ErrorMetric: 0.2070 · SSIM: 0.7842** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
