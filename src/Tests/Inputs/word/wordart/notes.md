### The arches are Word-probed

`textArchUp` / `textArchDown` follow `WordArtArch`, whose rules were read off Word's XPS glyph
transforms over 32 probe boxes (see `docs/word-features.md`, WordArt Transforms):

- The text is drawn smaller than declared, by `W' / (W' + 2·ink)` (text rect width over that width plus
  twice the ink height). This page's 36pt Impact draws at 31.25pt.
- The path is the half ellipse of the text rect, scaled by that factor about the rect's centre.
- The text is laid along the path by arc length, by its in-box alignment.
- Each glyph turns to the tangent, its baseline `usWinAscent − sTypoAscender` below the path.

The glyph tops on the arch up therefore stand about 7.5pt above the box, close under the subtitle, as in
Word.

[known] "Arc Text Down" runs about 10px wider than Word's. Word spaces an arch's glyphs either as modelled
or 1.0217 times tighter, and which one a box gets flips with changes of a few thousandths of a point in
size, with no order to it (`docs/word-features.md`, WordArt arches). This box gets the tight one; Morph
keeps the modelled spacing, which is Word's at the sizes either side of it. The chord-sagitta circle this page used before (bbox width as chord, height as sagitta, path sized to
the text) drew the text at full size on a far tighter curve, over the subtitle.

### Circle

`textCircle` is the arch construction on the whole ellipse (`WordArtArch.Place`), run clockwise from its
left end: centred text sits about the right end and reads downward. Its scale takes the longer side of
the text rect. Read off Word's XPS on `_probe_circle_*`; `docs/word-features.md`, WordArt Transforms,
has the numbers and the one case that does not fit (Arial in a square box).

### Wave, chevron, slant, fade and triangle are envelope warps

Each is a top and a bottom guide drawn in the text rect by the preset's own definition, and the line's
INK box is stretched between them (`WordArtEnvelope`, `TryRenderEnvelope` in both drawers). So every
one of them fills its box edge to edge whatever size the text declares, as Word's does; the text-on-path
and per-glyph scaling this page used before drew the text at its declared size, less than half the width
Word fills. `../wordart-envelope/notes.md` has the guides of the four warps that fixture covers.

### Vertical positioning vs Word

The drift once recorded here was the layout's, not the warps': an inline WordArt dropped its own
paragraph's spacing, and the arch-up paragraph declares 10pt after, so every warp below it sat that much
high and two of them a page early. The WordArt now carries its paragraph's spacing. The line it takes is
exactly the box height (`_probe_archflow`: consecutive 72pt boxes 72pt apart at a 12pt and a 48pt
paragraph font), so every warp now starts on Word's page at Word's box top. A box carried to a fresh
page keeps its paragraph's space-after as well, which the wave at the head of page 3 had lost.
