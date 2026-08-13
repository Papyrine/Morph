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
   fill shape (`ParseSolidFillShape`) or a line connector (`ParseLineShape`). Uses the same
   composed nested transform as the picture path. `ParseSolidFillShape` also parses blip-filled
   (image-textured) shapes into `FloatingShapeElement.ImageData`; the renderers clip the picture
   to the shape's geometry (ellipse preset / custom contours), and the group-frame clip exempts
   image fills the same way it exempts pictures.

The group branch is entered when SP emitted something, the drawing holds a `wpg:wgp`, or the
anchored drawing contains a blip-filled, solid text-free, or LINE-CONNECTOR text-free wsp
(`hasAnchoredFillShape`) — the last route exists because a FRONT-of-text anchored standalone
shape reaches neither SP (behindDoc-only) nor the pic-based image path (newsletters/08's cover
photo is a front-anchored blip-filled freeform; resumes/10's accent circle a front-anchored
solid custGeom; cards/15's fold/cut guides are front-anchored zero-extent line connectors in
the HEADERS, invisible until `IsLineShape` wsps qualified for the route). Text-carrying
solid shapes stay on the ParseTextBox path. At render, front-of-text `FloatingShapeElement`s
draw over the content painted so far, at flow order, whatever their fill — the front-SOLID
corpus experiment ran 2026-07-19 at net −0.46 (letters/11's logo + tile strip, brochures/06's
olive quote box, newsletters/12's purple dash overlay, menus/02's dark burst, resumes/10's
circles), with two co-requisites: front shapes take the same `AdvanceToBackgroundsTargetPage`
page-advance as behind ones (a front shape anchored to a continuous-section paragraph that
overflows must follow its content to the next page), and the empty-anchor-paragraph rule counts
ANY `FloatingShapeElement`, not only behind-text ones — otherwise routing a front shape through
the group branch swallowed its anchor paragraph's line and shifted the whole flow.

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
  overlapping tickets). History: while the walk could not parse blip fills, SP's IMAGE-filled
  emissions were carved back into the merge (newsletters/12's photo disappeared under full
  suppression, +1.00); once `ParseSolidFillShape` gained blip-fill support the carve-out became
  a double-draw and walk-owned groups now suppress SP output entirely.

Inline (`wp:inline`) drawings are a separate subsystem (`InlineShapeGroup`/`GroupShape`, flowed
with text), but share the blip-fill rule: a `wps:wsp` child with `a:blipFill` carries
`GroupShape.ImageData`, and a STANDALONE inline blip-filled wsp (no `wpg:wgp` wrapper — Word's
"fill a shape with a picture", cover-letters/09's circular profile photo) wraps into a
one-element group via `ParseInlineShapeImageRun` so the ellipse clip applies. A standalone
inline SOLID-filled wsp (rect or custGeom) routes the same way via `ParseInlineSingleShapeRun`
— business-plans/01's accent bar, cover-letters/10's triple-crescent logo, labels/12's 30
flourish ornaments, cover-letters/07's underline rule.

## Nested-transform composition

`GetAccumulatedTransform` builds a full 2×2 affine + translation from the child's ancestor
`grpSp` chain, composed innermost-out. Each group contributes `R·F·diag(scale)` about its own
centre — `F` its `@flipH`/`@flipV` mirror: a child point maps as
`c + R·F·(off + (p − chOff)·scale − c)`. `MapRectangle` maps a child rect's centre exactly,
sizes by the basis-vector lengths (a 90°-family rotation therefore swaps which outer scale
applies to which child axis), and returns the composed rotation plus a canonical flip: a
negative determinant (odd flip count) decomposes as rotation + flipV, since any mirrored
similarity is `R(θ)·diag(s, −s)` with θ read off the first basis column. Consumers combine
with the child's own transform via `F·R(θ) = R(−θ)·F`: total rotation = group θ ± child θ
(sign flips under a mirroring ancestor), child flipV XORs with the group's. Flips compose
only for SHAPE and PICTURE geometry (`composeFlips: true`); text boxes keep the flip-free
transform — Word mirrors box geometry, never text (cards/03's streamer sits in a flipH'd
nested group and rendered mirrored until this). The child's own `@rot` adds on top. An
anisotropic outer scale over a rotated inner group would be a skew; that residual is dropped.

History note: the pre-affine implementation composed offset+scale only, and in the wrong order
for non-identity chains (outermost-first with an innermost-first update rule) — harmless for
identity nesting, which is why it survived until labels/14's 270°-rotated wave sub-groups and
cards/09's ±45° cancelling pairs exposed it.

Degenerate extents: a group of zero-width vertical connectors legitimately has `cx=0` in the
anchor extent AND its child space (cards/06's page-2 fold lines) — the root scale went 0/0 and
NaN'd every child position. Both root-scale computations (SP and the walk) clamp each
degenerate length to 1, matching `GetAccumulatedTransform`'s long-standing nested-group
clamps, and a zero-area group frame skips the child clip entirely.

## Stroke widths under a group transform

A group member's `a:ln/@w` is **absolute EMU and does not ride the group transform**. All four
renderers convert it straight to points (`w / 12700`) and scale only by the page's device
factor; the child→display scale (`sx`/`sy`, or the HTML export's `viewBox`, which is neutralised
by `vector-effect="non-scaling-stroke"`) applies to geometry alone.

The trap is that `a:chExt` is NOT reliably EMU. The schema types it as a coordinate, but a group
converted from legacy VML carries the VML `coordsize` grid instead — newsletters/06's icons are
`a:ext=908050 EMU` over `a:chExt=1430`, i.e. **1 child unit = 635 EMU = 1 twip**. So
`pixelWidth / ChildExtentX` is "pixels per child unit", and multiplying `LineWidthEmu` by it
silently folds a *unit conversion* into the stroke: those icons' 2pt frame drew at 2646px and
flooded four whole pages with solid navy. Because `a:ext / a:chExt` is 635 in that unit-change
case and 0.59 in business-plans/12's honest resize case, the ratio alone cannot tell the two
apart — which is the real reason there is no correct way to scale the stroke by it.

Measured against Word both ways, at opposite extremes of the ratio:

| scenario | `a:ext / a:chExt` | authored `a:ln/@w` | Word renders |
| --- | --- | --- | --- |
| newsletters/06 balloons frame | 635 (twip child grid) | 25400 EMU = 2pt | 4–5px @150dpi = **2pt** |
| business-plans/12 header arrow | 0.592 (genuine resize) | 165100 EMU = 13pt | 28px @150dpi = **13pt** |

Both are the authored width untouched. Note the second row is the same drawing as the
document-body arrow (`a:ext == a:chExt == wp:extent`) shrunk to fit the header: the geometry
scales, the stroke does not.

## Anchor alignment (`wp:align`)

`wp:positionH`/`wp:positionV` carry EITHER a `wp:posOffset` or a `wp:align`
(center/right/bottom/…) that aligns the object within its `relativeFrom` box.
`ParsePositioning` folds the alignment into the position points AT PARSE, using the anchor's
`wp:extent` and the first section's page metrics (passed by `DocumentParser`, threaded into
`ShapeParser.ParseBackgroundShapes` as `alignmentPage` — BOTH dual-parse sweeps must fold
identically or the position/size dedup breaks). Parse-time folding is what lets group
children inherit the group's centring delta through the ordinary transform math — an
over-wide centred group overhangs both margins symmetrically (inline_group_crop's 568pt
board on a 468pt margin area starts 50pt LEFT of the margin; unfolded it sat AT the margin,
~100px right of Word, and menus/07 shared the defect). Deliberate limits: cell- and
txbx-nested anchors skip the fold (cell floats re-base against the CELL box at render);
margin-strip references (`leftMargin` etc.) and paragraph/line-relative vertical alignment
have no parse-time box and don't fold; `inside`/`outside` approximate as odd-page
left/right; later sections reuse section 1's page box. A fold of zero is common — full-page
centred backgrounds have extent == reference box, which is why only 4 of the corpus's 9
align-carrying scenarios moved when this landed (−0.10..−0.11/backend on the three
board-menu scenarios, zero regressions).

## Anchor resolution (vertical, shapes)

`FloatingPosition.ResolveShapeY`: page → 0, margin → top margin, paragraph/line → the top
margin as an approximation (background shapes typically anchor to a page-top paragraph, and
the approximation is deliberately immune to flow drift — agendas-minutes/05's corner
triangles piled up when they followed the cursor). Two exceptions resolve against the
cursor: header/footer rendering (`RenderContextBase.InHeaderFooter` — the cursor is the
header paragraph's position at HeaderDistance; cover-letters/10's full-page band bleeds off
the page top, letters/10's white card, labels/16's sheet) and a shape the margin
approximation would land entirely above the page (menus/06-class deep-negative offsets —
under the approximation those were invisible, so the cursor can only improve them).
`AdvanceToBackgroundsTargetPage`'s look-through for the next break-driving element stops at
section breaks: a background's anchor paragraph is in its own section, so it never lifts to
the page the next section's table opens. Preset rect/ellipse fills and strokes rotate about
their box centre like any other xfrm (resumes/06's corner strips are 90°-rotated rects;
business-plans/08's accent rule).

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
- HEADER/FOOTER content gets the same `SortFloatingBatchesByZ` pass after
  `AppendHeaderFooterElements` — letters/02's header stacks an opaque white JPEG
  (z 251658240) UNDER its frame PNG (251658241), and painting in document order whited out
  the whole letterhead.
- The header/footer STORY as a whole sits below every body item, floats included.
  `Fragmenter.AssemblePages` emits
  `backgroundImages, footerImages, headerBand, footerBand, behindFloats, body, frontFloats`,
  and `w:behindDoc` orders a float only against the body TEXT — it says nothing about the
  bands, which are under it either way. Word-probed (`_probe_footerz`, 2026-08-13): a footer
  of three tab-separated words with two opaque page-anchored rectangles over the first two,
  one `behindDoc="1"` and one `behindDoc="0"`, has BOTH words buried outright while the
  uncovered third proves the footer rendered; the same rectangle pair over a line of body text
  shows the ordinary behind/in-front split, so the fixture cannot be blamed for the result.
  The footer band used to paint LAST, over everything, which drew business-plans/13's cover
  footer on top of the grey title rectangle — a behind-text body float running from 575.4pt to
  the page bottom — where Word has it buried (measured: 1896 ink pixels in that strip against
  Word's 0, now 0). The header band was already ordered correctly. Worth −0.0013 on
  business-plans/13 and −0.0001 on business-plans/15 across all three backends, with thirteen
  further PDF scenarios re-snapshotting for content-stream order alone at identical pixels.

## Fills

- `a:grpFill` defers a child's fill to its group: `ResolveGroupFill` walks the ancestor
  `grpSp`/`wgp` chain to the nearest group whose `grpSpPr` carries a solid fill. Fill-less
  wrapper groups are looked through (labels/07 nests clusters two wrappers deep); an ancestor
  with a concrete non-solid fill (gradient/blip/noFill) stops the walk rather than inheriting a
  wrong colour. Both the floating walk (`ParseSolidFillShape`) and the INLINE group parser
  (`ParseInlineShapeGroupRun`) resolve it — brochures/06's accent stripes and balloon line-art are
  inline `wpg:wgp` clusters whose every rect defers via `grpFill` (outline `a:noFill` too), so
  before the inline path resolved it they drew as nothing on p1 in every backend.
- Linear `a:gradFill` parses in BOTH group paths (SP's grouped branch and the walk) — every
  corpus gradient sits on a group child (labels/04's 90° accent bars, cover-letters/06's
  banner wash + page-bottom band), and until 2026-07-20 only SP's unused STANDALONE branch
  read it; the walk flattened gradients to the `wps:style` fillRef colour. A direct gradFill
  suppresses the fillRef fallback (the direct property wins; unmodelled radial/path gradients
  still fall through). GUARDED to faithful geometry — parsed contours or a plain rect/ellipse
  preset — because `FillShape` paints an unbuilt preset's BOUNDING BOX: the unguarded first
  attempt drew labels/04's soft hexagon accents as saturated gradient boxes (+0.004, visibly
  worse than absent). Word-side softness on such accents also involves gradient-stop alpha,
  which `GradientFill` doesn't model.
- A shape with an explicit `<a:noFill/>` and no stroke is dropped; stroke-only shapes with real
  path geometry render their contours, honouring `a:prstDash` through `LineDashPattern`
  (letters/05's teal dashed rules; labels/03's tear lines are 90°-rotated sysDot connectors).
  Open custGeom contours still close before stroking — letters/05's teal arc draws as a filled
  crescent instead of a dashed open arc.
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
6. **Outline-only shape emission** (issue #6) — LANDED on the third attempt (2026-07-19,
   net −0.02; letters/05's orange square, menus/07/09 + inline_group_* board frames,
   labels/09's ticket rules). The first two attempts regressed 10 scenarios against 4; what
   made the third land: (a) skip when the wsp carries a `wps:txbx` — the text-box path
   strokes its own chrome (labels/04's double-draw); (b) stroke only faithfully-strokeable
   geometry — parsed contours or a plain rect/ellipse preset; a custGeom whose contours
   failed to parse (menus/04's arc-path doodles) or a preset outside `PresetShapeGeometry`'s
   coverage (letters/05's triangle) would stroke its bounding box, a frame Word doesn't
   draw; and (c) the document-order interleave, `a:ln` alpha and walk-authority passes had
   already fixed the z-order and rendering defects that sank the earlier attempts
   (cards/02's outline drew under its ticket body; menus/09's frame was pixel-right but the
   scenario net-regressed from OTHER emitted shapes). Emission lives at both `ShapeParser`
   tails (`TryBuildOutlineOnlyShape`) and in the walk's `ParseSolidFillShape` skip rule.
   FOURTH PASS (2026-07-20): `ParseSolidFillShape` returned null on an explicit `<a:noFill/>`
   before its stroke logic ran, so outline-only children of WALK-OWNED groups never rendered
   anywhere — SP's copies are suppressed for those groups and the walk was the only surviving
   parser. It now falls through to stroke-only emission under the same guards (skip txbx
   carriers; explicit noFill also suppresses the `fillRef` fallback — the direct property
   wins), and plumbs `ExtractLineStyle`'s alpha into `LineAlpha` (labels/04's hexagon frames
   are 10%-alpha tx1 — opaque they read as a dark mesh). Landed cards/13's white card
   outlines + labels/02's grey label borders (both noFill rects w/ theme-`lnRef` widths in
   translation-remapped nested groups), menus/04's doodle pattern (its custGeom contours
   flatten fine — the noFill bail, not contour parsing, was the blocker), cards/02's notched
   ticket/frame outlines, letters/05's p1 orange square, cover-letters/06's segment bars.
   Note the per-shape metric often ticks POSITIVE (new-ink offset penalty) — every scenario
   was crop-vetted against Word before promotion.
   FIFTH PASS (2026-07-20): FILLED walk shapes now stroke their `a:ln` exactly like
   ShapeParser's solid branch always has. The walk had deliberately nulled
   `LineColorHex` on solid fills, which left walk-owned filled shapes borderless —
   letters/05's teal "triangle" is a WHITE-filled custGeom whose 4.9pt teal stroke is the
   only visible ink (its p1 anchor carries a non-identity nested sibling group, so the whole
   drawing is walk-owned; p2/p3's identity anchors rendered via SP all along — a useful
   diagnostic pattern: the same art correct on some pages and missing on others usually
   means the pages' anchors differ in nesting identity, not in the art). Also landed:
   resumes/10's page-edge circle now cuts flat at the page edge like Word. Cost accepted:
   a thin filled rect with a same-colour `a:ln` draws one line-width fatter (wedding/04's
   centre divider, +0.001/backend).
7. **Scaling group member stroke widths by the group transform** (landed 2026-07-18 at
   `8da1f624d`, reverted 2026-07-21): `StrokeWidth = LineWidthEmu · sx` in the raster/PDF
   backends, with a geometric mean for boxes and an along-axis factor for lines to dodge the
   degenerate cross-axis of single-line wrapper groups. It is wrong at the premise — see
   "Stroke widths under a group transform" above: `a:ln/@w` is absolute, and `sx` is pixels per
   CHILD unit, which is only pixels-per-EMU when `a:chExt` happens to be EMU. Two things hid it
   for three days. First, its motivating scenario (business-plans/12) is EMU-child-space, so the
   change merely thinned strokes 13pt→7.7pt and the arrow still *assembled* correctly — the
   glyph shape was judged, its stroke weight was not, and the accompanying square-cap fix
   (correct, kept) carried the visible improvement. Second, its worst victim
   (newsletters/06) renders 6 pages against Word's 4, and a page-count mismatch suppresses the
   per-page AE/SSIM diffs entirely, so four pages could go solid navy with no metric moving.
   `BaselineHealthTests` exists because of exactly that gap. The revert is a net improvement on
   every affected scenario: business-plans/12 −0.0006/page across 16 pages in both raster
   backends, resumes/03 −0.0005, all six newsletters/06 pages restored in all three backends
   (which also cleared its "icons render as solid navy squares" finding — the icon art was
   always parsed correctly, the flooded stroke was painting over it), and newsletters/03's
   hexagon outlines back to Word's measured 3px from 1–2px. newsletters/03 p2 posts +0.0009 AE
   for that — a textbook new-ink offset penalty, since the now-correct outline sits ~3px off
   Word's position.

## See also

- `word-features.md` — per-feature status, test scenarios and per-row limitations.
- `fidelity-audit.md` — how renders are compared against Word and how a change is judged.
