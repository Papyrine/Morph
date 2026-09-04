# header_float_image

Regression scenario for **anchored pictures in a header or footer that sit IN FRONT of text**
(`behindDoc="0"`) — the case `Fragmenter.ResolveBandImages` used to drop on the floor.

A right-aligned logo anchored in the header is the ordinary shape of this: it is what Word
writes when you drop a picture into a header and give it `wrapNone`, and `behindDoc="0"` is
the default. `ResolveBandImages` matched `FloatingImageElement {BehindText: true}`, so that
logo never reached a `PlacedImage` and simply never appeared — silently, on every page and in
every output format. The band SHAPE branch beside it had already been widened to admit
front-of-text art (cards/05's fold-guide rules); the image branch had not.

`w:behindDoc` orders a float against the body TEXT and says nothing about the bands — see the
`_probe_footerz` finding in `docs/floating-art-pipeline.md`, where a `behindDoc="0"` and a
`behindDoc="1"` rectangle over footer text bury it equally. So the gate was never modelling a
real Word rule.

Three amplified 144x72pt blocks, each a flat colour with a white notch in its top-left corner
so position, scale, orientation and flip are all readable off the render:

| block | where | anchor |
| --- | --- | --- |
| blue `#1F4E79` | header, left at the margin | `behindDoc="1"`, H margin +0, V paragraph +0 |
| red `#C00000` | header, right at the margin | `behindDoc="0"`, H margin `<wp:align>right</wp:align>`, V paragraph +0 |
| green `#217346` | footer, left at the margin | `behindDoc="0"`, H margin +0, V page +720pt |

The red block is the reported shape exactly. The blue one beside it is the control: the model
is only confirmed by having both values of `behindDoc` in one render, since the behind-only
gate passed every fixture that carried nothing but `behindDoc="1"`.

Top and bottom margins are 144pt against a 36pt header/footer distance, so all three blocks
clear the body text. That is deliberate: `AssemblePages` emits the whole band story UNDER the
body, so a band float that overlapped body text would diverge from Word for a reason this
fixture is not about.

Measured against Word (150 DPI, A4 = 1240x1754):

| block | Word bbox | Morph bbox |
| --- | --- | --- |
| blue | 150,75 – 449,224 | identical |
| red | 791,75 – 1089,224 | 790,75 – 1089,224 (1px on the right-align fold) |
| green | 150,1500 – 449,1649 | identical |

**A hand-authored fixture image must carry an explicit DPI (`pHYs`) chunk.** Word assumes
**120 DPI** for a PNG that has none, which makes it render the source 1.25x oversize and clip
it to the declared `wp:extent` — the frame lands in the right place and at the right size, so
nothing looks wrong, but everything INSIDE the picture is 25% too big and the right and bottom
edges of the source are cropped away. Measured here before the chunk was added: the 25% notch
came back as 31.2% of the block (93x46px against the correct 75x38), i.e. Word drew only the
top-left 160x80 of the 200x100 source. Morph fills the declared extent from the whole source,
which is what `wp:extent` plus `a:stretch/a:fillRect` says. With `dpi=(96,96)` written into
the PNGs the two agree to 1px.

| Expected (Word) | Skia | ImageSharp |
| --- | --- | --- |
| **Page 1**&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; | **Page 1. ErrorMetric: 0.0053 · SSIM: 0.9972** | **Page 1. ErrorMetric: 0.0057 · SSIM: 0.9965** |
| <img src="expected_0001.png" width="500"> | <img src="skia_result%23page_0001.verified.png" width="500"> | <img src="imagesharp_result%23page_0001.verified.png" width="500"> |
