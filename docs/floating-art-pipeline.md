# Floating-art pipeline

How Morph parses and renders anchored/floating DrawingML art — shapes, pictures, text boxes and
WordArt inside `wp:anchor` drawings, including `wpg:wgp` groups. This domain produced the largest
share of fidelity findings in the 2026-07 full-corpus audit, and the rules below were each forced
by corpus evidence; the **decision log** at the end records approaches that were attempted,
measured and reverted, so they are not re-attempted from scratch.

Rendered output is compared against Word's own renders (see `fidelity-audit.md`). "SP" below is
`ShapeParser`; "the walk" is `DocumentParser.ParseAllShapesFromDrawing` and its helpers.

## Parse paths

A floating drawing reaches the model through up to three sweeps, all launched from
`ParseParagraph`'s group branch when a run contains an anchored drawing:

1. **ShapeParser (`ParseBackgroundShapes`)** — behind-text anchors only. Emits
   `FloatingShapeElement`s (solid, gradient or image fills) using FLAT scaling: each child's raw
   EMU coordinates are mapped through the root group's `chOff`/`chExt` → anchor-extent transform
   only. Nested `grpSp` transforms are ignored, which is correct precisely when nesting is
   identity (`chOff==off`, `chExt==ext`, no rotation) — Word's most common authoring pattern,
   where child coordinates already live in the root space.
2. **`ParseDrawingElements`** — pictures (`pic:pic`). Emits `FloatingImageElement`s (or inline
   `ImageElement`s when there is no anchor). Nested transforms compose through
   `GetAccumulatedTransform` (below).
3. **The walk (`ParseAllShapesFromDrawing`)** — per `wps:wsp`: a text box
   (`ParseTextBoxFromShapeWithTransform`), else — when the txbx is absent or text-free — a
   solid-fill shape (`ParseSolidFillShape`) or a line connector (`ParseLineShape`). Uses the same
   composed nested transform as the picture path. Deliberately does NOT parse blip-filled
   (image-textured) shapes — those belong to SP.

Cell-anchored floats additionally detach into `TableCell.Floats` and render from the shared
table renderer (see "Cell-attached floats").

## Authority: which sweep owns a group's children

Both SP and the walk traverse the same `wgp`, so a body-level group would double-emit. The rules,
each corpus-calibrated:

- **Identity-nested body groups** — dual-parse + dedup. SP's flat output is authoritative;
  the walk's `FloatingShapeElement` twins are dropped when position AND size match within 0.5pt.
  Blanket walk authority was tried and regressed agendas-minutes/02 (+0.92), newsletters/12
  (+0.97), newsletters/01 (+0.78), wedding/11 (+0.75): for those templates the flat math is the
  correct one.
- **Cell-anchored groups** (`cellGroup`: the paragraph has a `TableCell` ancestor) — the walk
  owns everything; SP output is skipped and the dedup is skipped (it would compare against
  absent SP output and eat the only copy — labels/14's navy card bodies vanished that way).
  SP's flat scaling emitted the same shapes 2.4× oversized for these (business/05's corner
  tiles over the body text).
- **Non-identity nested groups** (`HasNonIdentityNestedGroup`: any nested `grpSp` whose xfrm
  remaps offset, scale or rotation) — the walk owns the SHAPES; SP's flat math cannot represent
  the remapping, so its copies land at garbage positions and the size mismatch defeats the
  dedup, leaving both copies visible (labels/16's stray bear fragments; labels/08's oversized
  overlapping tickets). Carve-out: SP's IMAGE-filled shape emissions stay in the merge, because
  the walk never parses blip fills and a picture's own sub-group is often identity-nested even
  when a sibling's is not (newsletters/12's photo disappeared under full suppression, +1.00).

## Nested-transform composition

`GetAccumulatedTransform` builds a full 2×2 affine + translation from the child's ancestor
`grpSp` chain, composed innermost-out. Each group contributes `R·diag(scale)` about its own
centre: a child point maps as `c + R·(off + (p − chOff)·scale − c)`. `MapRectangle` maps a child
rect's centre exactly, sizes by the basis-vector lengths (a 90°-family rotation therefore swaps
which outer scale applies to which child axis), and returns the composed rotation for the
renderers, which rotate about the placed box's centre; the child's own `@rot` adds on top. An
anisotropic outer scale over a rotated inner group would be a skew; that residual is dropped.

History note: the pre-affine implementation composed offset+scale only, and in the wrong order
for non-identity chains (outermost-first with an innermost-first update rule) — harmless for
identity nesting, which is why it survived until labels/14's 270°-rotated wave sub-groups and
cards/09's ±45° cancelling pairs exposed it.

## Z-order and paint order

- `wp:anchor@relativeHeight` is Word's z-value across ALL floating drawings.
  `SortFloatingBatchesByZ` stable-sorts each maximal run of consecutive floating elements in the
  parse stream; behind-text vs in-front routing is untouched. Group children share their
  anchor's value, so intra-group order is decided by stream position.
- WITHIN a group, Word paints children back-to-front in DOCUMENT order. The three sweeps destroy
  that order by construction (all shapes, then all pictures, then all walk output), and no
  concatenation is ever right: labels/11 needs its white base boxes UNDER its brush art while
  labels/06 needs its captions OVER its ticket pictures. Every sweep therefore records each
  emitted element's source node (`childSources`), and the group branch stable-orders the union
  by the node's position in `GroupDrawables(wgp)`. Elements without a mapped source
  (txbx-nested pictures) keep their relative order at the end.
- All walk emissions carry the anchor's `RelativeHeight` so the batch sort keeps same-anchor
  children together. `LayoutInCell` rides along the same way.

## Fills

- `a:grpFill` defers a child's fill to its group: `ResolveGroupFill` walks the ancestor
  `grpSp`/`wgp` chain to the nearest group whose `grpSpPr` carries a solid fill. Fill-less
  wrapper groups are looked through (labels/07 nests clusters two wrappers deep); an ancestor
  with a concrete non-solid fill (gradient/blip/noFill) stops the walk rather than inheriting a
  wrong colour.
- A shape with an explicit `<a:noFill/>` and no stroke is dropped; stroke-only shapes with real
  path geometry render their contours (solid dash only).
- Text-free `wps:txbx` placeholders do not become text boxes — Word's templated artwork stores
  an empty txbx in every decorative shape, and emitting a box would mask same-anchor overlays.

## Clipping

- **Group frame, shapes only:** Word cuts a group's children at the group's extent box. The
  child box and the frame share the anchor's coordinate space, so the cut is parse-time
  geometry: `GroupFrameClipper` (Sutherland–Hodgman) clips each unit-square contour against the
  frame transformed into the shape's unit space — synthesizing the unit square for plain rect
  fills, returning the original list untouched for fully-inside shapes, dropping fully-outside
  shapes. Applied in BOTH shape parsers. Exempt: rotated and percent-positioned/sized children
  (their boxes resolve differently at render). This restored labels/07's white page margin and
  removed cards/04's out-of-frame stray art.
- **Pictures are deliberately NOT frame-clipped** — see the decision log.
- **`pic:spPr` geometry crops:** a floating picture whose spPr declares `prstGeom
  prst="ellipse"` or a custGeom is Word's shaped "picture style" (brochures/03's round photos).
  `FloatingImageElement.ClipToEllipse`/`ClipSubpaths` carry it; Skia clips via `ClipPath`, PDF
  via `IntersectClip`, ImageSharp composites a pre-clipped bitmap for the ellipse case
  (`GetEllipseClippedImage`) and draws custGeom crops unclipped (no canvas clip stack). Rotated
  pictures are exempt: the clip sits in page space while the bitmap turns inside the draw.

## Cell-attached floats

`TableCell.Floats` carries a cell's anchored drawings (z-sorted at parse) plus
`FloatAnchorParagraphOrdinals`. The shared `PageRendererBase.RenderTableCell` draws them when
the cell rectangle is known: behind-text ones after the cell background/borders and under the
content, the rest after the content. `RenderCellFloats` re-bases layoutInCell anchors to
absolute page coordinates at the cell's outer box via full-member `WithAbsolutePosition` copies;
`layoutInCell="0"` anchors keep their real page resolution.

Vertical resolution and clamping, each rule evidence-backed:

- Paragraph-relative anchors resolve against their ANCHOR PARAGRAPH's measured top inside the
  cell (`CellFloatY` walks `Measurer` heights), not the cell top.
- ECMA's layoutInCell rule ("the object shall be positioned within the existing table cell")
  clamps ONLY paragraph-relative vertical targets, only at the near edge, only for unrotated
  elements. Margin/page targets render where they say (letters/13's letterhead band at margin
  −735pt regressed under any wider clamp); horizontal is never clamped (brochures/04 bleeds its
  photo −16pt off the cell edge); far edges are never clamped (drawings overflow cell bottoms,
  and sheet-spanning groups anchored in the first cell keep their children in place — an early
  both-edge clamp crushed labels/14's whole sheet into cell (0,0)). The clamp is what lands
  brochures/07's balloons (anchored −260.6pt above an empty cell's paragraph) at the cell's top
  edge, exactly where Word draws them.

## Decision log — attempted, measured, reverted

Do not re-attempt these as-is; each needs the recorded blocker resolved first.

1. **Frame-clipping group PICTURES** (+0.99 net, 16 scenarios regressed, e.g.
   agendas-minutes/02 +0.46, cards/18 +0.14): intersecting picture dest rects with the group
   frame and folding the cut into `ImageCrop`. The corpus's group pictures legitimately
   overflow their group extents — overflow art is an authored idiom — and SP-authority body
   groups position pictures under flat scaling where anchor-frame math does not correspond.
   Word's cut is evidently conditional; re-attempting needs per-scenario forensics of what Word
   actually clips.
2. **`RelativeHeight` on walk solid fills, without document-order interleave** (labels/08 and
   labels/11 white-boxed, +1.4/+1.6 each): for body groups the walk's fills duplicate SP's, the
   dedup misses mis-scaled pairs, and the rh=0 default is what kept those strays buried at the
   bottom of the z-sorted batch. Superseded by the interleave + authority rules above, which
   made the wiring safe.
3. **Blanket walk authority over ALL body groups** (agendas-minutes/02 +0.92, newsletters/12
   +0.97, newsletters/01 +0.78, wedding/11 +0.75): for identity-nested templates SP's flat
   scaling is the correct interpretation. Authority is scoped by nesting identity instead.
4. **Parse-time layoutInCell rebase** (+0.035): horizontal could be approximated
   (column-relative + grid offset) but the VERTICAL half is unknowable at parse — a mid-page
   cell's floats pinned to the table top. Superseded by the render-time cell-attached design.
5. **Suppressing text past an overlong right tab stop** (tab-stop domain, recorded here for
   adjacency): the "drop the following text" rule was calibrated against a fixture whose
   entries have no text after the tab at all. Word CLAMPS a Right/Center/Decimal stop past the
   wrap width to the wrap width; the clamp is what renders TOC page numbers at the cell edge.
6. **Outline-only shape emission** (issue #6, two attempts): `a:noFill` + `a:ln` shapes are
   dropped by SP for lacking a fill; emitting them regressed 10 scenarios against 4 improved.
   Real co-fix kept from the second attempt: `a:ln` solid-fill ALPHA. Re-landing needs
   per-shape forensics on menus/04 (452 shapes) and a skip-when-txbx guard (labels/04
   double-draws — its outline shapes carry a txbx whose chrome already strokes them).

## See also

- `word-features.md` — per-feature status, test scenarios and per-row limitations.
- `fidelity-audit.md` — how renders are compared against Word and how a change is judged.
