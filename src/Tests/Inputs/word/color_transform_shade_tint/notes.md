Pins the model for DrawingML `a:shade` / `a:tint`: **linear light**, at the full `0-100000`
precision, independent of whether the base colour is a literal or a theme entry.

A 7x4 grid of page-anchored 0.8in squares. Columns are the transform — plain, `a:shade` at
25/50/75%, `a:tint` at 25/50/75%. Rows are the base — `a:srgbClr val="748DF3"`,
`a:srgbClr val="ED7D31"`, `a:schemeClr val="accent1"` (4472C4), `a:schemeClr val="accent2"`
(ED7D31). Geometry: first swatch at 0.35in / 0.8in from the page origin, 1.1in pitch, so at
150 DPI a swatch centre sits at `150 * (0.75 + 1.1*column)` by `150 * (1.2 + 1.1*row)`.

Rows 2 and 4 are the same base colour reached two different ways, and must render identically.
That pairing is the point of the fixture: the transform means the same thing whichever kind of
colour declared it, and the codebase previously disagreed with itself about that.

**Word's values** (150 DPI, measured at the swatch centres):

| base | plain | shade 25% | shade 50% | shade 75% | tint 25% | tint 50% | tint 75% |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 748DF3 | 748DF3 | 3B4982 | 5366B3 | 657BD6 | E6E9FC | C9D0F9 | A6B3F6 |
| ED7D31 | ED7D31 | 7F4015 | AE5A21 | D16D2A | FBE7E2 | F6CCBE | F2AA8F |
| accent1 | 4472C4 | 203A68 | 2F528F | 3B64AC | E3E6F2 | C0C9E4 | 93A5D5 |
| accent2 | ED7D31 | 7F4015 | AE5A21 | D16D2A | FBE7E2 | F6CCBE | F2AA8F |

**Why the fixture exists.** Two wrong models shipped against these values, and both looked
plausible at a realistic magnitude. An sRGB-space blend — multiply the encoded byte for shade,
blend toward 255 for tint — is out by up to 127 per channel; HSL luminance scaling, which IS
correct for the WordprocessingML byte form in `color_transform_theme_fill`, is out by up to 69.
Linear light reproduces all 24 transformed swatches exactly. The three models are 30 to 60 apart
per channel here precisely because the magnitudes are amplified; at a realistic `shade 90%` they
agree to within a few counts and the measurement settles nothing.

Six shape-fill call sites also dropped the transform children entirely, rendering every swatch at
its base colour. Nothing in the corpus caught that, which is what this scenario is for.
