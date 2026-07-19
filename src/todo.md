# Rendering fidelity todo

Deep comparison of every scenario in `src/Tests/Inputs/` (324 scenarios, 547 Word reference pages): `expected_*.png` (Word, 150 DPI, via RenderHelper) versus `skia_result#page_*.verified.png`, `imagesharp_result#page_*.verified.png`, `pdf_result#page_*.verified.png` (PDFium render), and `html_result.verified.png` (headless-browser screenshot of the HTML export).

Each finding: `severity | backends | pages | description`. `all` = skia+imagesharp+pdf. HTML findings ignore pagination/viewport-width reflow by design and only flag content/styling errors. Not reported: anti-aliasing texture, 1-2px subpixel shifts, ImageSharp's softer glyph rasterization. `[known]` = already documented as accepted in that scenario's notes.md.

Findings: **394 major**, 535 medium, 383 minor across 303 scenarios; 21 scenarios fully faithful on all four outputs. (Systemic issue #1, per-page page-number fields, is now fixed — see the ✅ entry below; the counts reflect the resolved/downgraded findings. Systemic issue #2, default metrics/table row heights, is now mostly fixed — see its ✅ entry — but the per-scenario findings and these counts have NOT been re-audited against the regenerated baselines; expect many "drifts up/compresses" entries below to be stale or resolved.)

## Systemic issues (cross-scenario root causes)

These patterns repeat across many scenarios; fixing one of these clears whole families of the per-scenario findings below.

### All raster + PDF backends

1. **Page-number fields (PAGE/NUMPAGES/SECTIONPAGES) evaluated per page** — ✅ FIXED (merged, commit 78055694a). PAGE/NUMPAGES/SECTIONPAGES now resolve to the live value per page (`Run.PageField` + a gated counting pass establishes the total). Fully cleared: `page_numbers`, `field_codes_simple/01` (raster/PDF), `business/03`, `business-plans/10`. **Residuals still open:** (a) section-restarted numbering (`w:pgNumType` @fmt/@start) is unwired, so `PAGE` is the *physical* page number — `business-plans/09`/`15` increment now but don't restart per section to match Word; (b) `business-plans/13`/`resumes/13` increment correctly, but their sequence still differs from Word because of the page-count divergence (issue #2); (c) ⚠ PARTIALLY FIXED: `business-plans/02`'s footer page numbers now render — Word's "Page Numbers (Bottom of Page)" gallery wraps the whole footer in a root-level `w:sdt`, which `ExtractHeaderFooter`'s Paragraph/Table-only walk skipped, so the footer parsed empty (`AppendHeaderFooterElements` now unwraps `SdtBlock` content recursively; also restored `wedding/10`'s bottom-right page numbers at Word's exact position — of the 11 corpus docs with SDT-wrapped footers only these two changed, because the other nine's SDT footers belong to sections whose footer the single-footer model doesn't select). `business-plans/12`'s remain missing — that one IS the multi-section footer selection (Morph wires one footer set document-wide; per-section headers/footers are unmodelled); (d) HTML/Markdown keep the cached value by design (no pagination).
2. **Vertical metrics run ~10% tight and glyph advances ~9-10% narrow vs Word** — ✅ MOSTLY FIXED. This was never one defect; three coupled causes, each measured against Word's own renders:
   (a) the built-in Normal defaults (documents with no styles.xml / no docDefaults — 138 of 324 scenarios) were Calibri-era: 11pt with `w:line=259` (1.08). Modern Word supplies **12pt with `w:line=278`** (`long_paragraph`: Word's glyph advances solve to 12pt Aptos and its 35.35px pitch to 12pt × 1.2207em × 278/240 — both now matched exactly, 27 lines vs 27). The "advances ~9-10% narrow" half was the same bug: 11/12 = 8.3%. Runs with no `w:rPr` at all also bypassed the document default (the `RunProperties` record's static 11pt won) — now anchored to docDefaults' `w:sz` via `effectiveDefaultFontSizePoints`.
   (b) unstyled tables carried an invented 2pt top/bottom cell padding ("Word breathing room" — false: Word's unstyled row is line + spacing-after exactly, `simple_table` 52.02px measured).
   (c) the last cell paragraph's spacing-after was overlapped with the bottom cell margin (max, not sum); Word stacks them (`table_default_style` with 3pt margins: row = 3 + 16.97 + 8 + 3 = 31pt). (b) and (c) were calibrated against the too-short (a) lines and surfaced when (a) was fixed. A fourth rode along: `w:contextualSpacing` never collapsed between paragraphs with **no** `pStyle` (the same-style guard required a non-null styleId, but unstyled = implicit Normal = same style) — fixed in all backends; `letters/01`'s letterhead now collapses to Word's 30px gaps.
   Cleared: `long_paragraph`, `line_numbers_*` pitch, `multiple_pages`/`header_footer`/`page_numbers` redistribution, `two_columns`/`three_columns`, `header_banner_table`, `complex_spacing` (7 pages = Word), `business-plans/10` (5 pages = Word), `agendas-minutes/03`/`09`/`18`, `letters/01`. Aggregate error vs Word across the ~140 changed scenarios: Skia −5.0%, PDF −3.9%, ImageSharp −1.9%; zero page-count regressions.
   **Residuals still open:** (a) **docDefaults/pPrDefault's own `w:line` is deliberately not parsed** — Word's cascade for it is conditional around table cells in undecoded ways. Counterexamples: `postcards/04` declares `w:line=360` yet Word renders its (atLeast-height) card cells at single spacing — applying 1.5× grows it 3→6 pages; `agendas-minutes/07` declares 264 and applying it matches Word's inter-heading gaps exactly while shifting the whole content block ~45px below Word. Body-only evidence says it SHOULD cascade there (`letters/09`: docDefaults without `w:line` renders single, not 1.08). Decode the table-cell rule first — see the comment on `defaultLineSpacingMultiplier` in DocumentParser.cs. Until then, templates carrying docDefaults `w:line` keep a ~2-6% vertical drift, and the pagination divergences on `business-plans/02`/`13`/`15`, `newsletters/06`/`09`, `resumes/13`, `image_wrap_square`, `section_break_odd_page` stay open. (b) **Framed-paragraph spacing**: Word charges no inter-paragraph spacing through a frame-positioned paragraph (`resumes/07`/`brochures/06` letterhead rows: unstyled "Company, location" + framed "Month Year" + Heading3 render tight in Word); Morph charges it, which is what tips those scenarios onto an extra page once lines are Word-height. (c) **Autofit column distribution** (issue #10) is now the dominant residual on `table_default_style`/`complex_tables`: at 12pt the mis-distributed columns wrap header text Word fits on one line.
3. **Expanded character spacing (`w:spacing`) mishandled** — ✅ FIXED (all backends). One defect produced both symptoms: fragment widths were allocated as natural + spacing×length but DRAWN untracked, so positive tracking piled its surplus into the trailing word gap ("PTA  MEETING") and negative tracking pulled the next fragment back over the space (issue #4's collapses). `DrawFragmentText` (Skia/ImageSharp) now draws tracked fragments per-glyph — each character advances by its own width plus the tracking, so drawn extent equals allocated width; the untracked fast path stays byte-identical. The PDF backend previously ignored tracking entirely (headings narrower than Word): its layout now adds spacing to word AND space advances, and `DrawTrackedString` draws per-glyph. Net −0.389 summed pixel-diff across 89 re-baselined scenarios — biggest: menus/06 −0.082, newsletters/03 −0.031, labels/09 −0.019, business-plans/05, agendas-minutes/04/05/08/11, cover-letters/03/07, resumes/11/12/14/16, newsletters/13/14, letters/06, business-plans/10. Only visible residual class: pages whose tracked headings now wrap/flow at slightly different points against other pre-existing offsets (brochures/02 +0.009, business-plans/02 PDF p1 +0.004).
4. **Word spaces collapse to zero in some display/heading text (Skia/ImageSharp)** — ✅ FIXED, same root cause as #3: those headings carry NEGATIVE `w:spacing` (condensed tracking, e.g. cover-letters/03's Title at `w:spacing -30`), and the accumulated per-word deficit was landing in the word gap, pulling the following word back over the space — "Sheetal Parmar" → "SheetalParmar". With per-glyph tracked drawing the space survives: verified restored on cover-letters/03 ("Sheetal Parmar") and cover-letters/16 ("4 APRIL 20XX" / "HIRING MANAGER" / "ESG FINANCIAL SERVICES" / "5678 MOUNTAIN DRIVE" all match Word), with the same fix applying to the whole #4 list.
5. **Floating/anchored decorative art missing or misplaced** — the largest source of MAJOR findings. ⚠ FIRST STRUCTURAL FIX LANDED (2026-07-19): **floating drawings anchored inside table cells never rendered at all** — Word anchors them to the page (the cell is only where the anchor lives), but the renderers' table path lays out flow content only, so cell-anchored floats parsed into cell content and vanished (labels/14 rendered a completely BLANK page: its entire label sheet — navy cards, wave art, captions — is one behind-text group anchored in a layout table's first cell). Three coupled changes: (a) `ParseTable` hoists floating elements out of cell content into `pendingCellFloats`, drained by every call site in front of the emitted table, where the behind-text pre-pass and floating dispatch see them; (b) for CELL-anchored groups the DocumentParser walk owns the children (its nested-group transform math resolves page positions/sizes correctly; ShapeParser's flat anchor-extent scaling emitted the same shapes 2.4× oversized at group-local positions — business/05's corner tiles splattered over the body). Body-level groups keep the long-standing dual-parser + dedup behaviour: blanket doc-parser authority regressed agendas-minutes/02 (+0.92), newsletters/12 (+0.97), newsletters/01 (+0.78) and wedding/11 (+0.75) — for those templates ShapeParser's flat scaling is the correct one, so the authority flip is scoped to `cellGroup` (group + `TableCell` ancestry); (c) the position/size dedup is skipped for cell groups (it compared against ShapeParser's no-longer-emitted output and silently ate the only copy — labels/14's navy card bodies). The empty-txbx solid-fill filter was also relaxed to text-free txbx (Word's templated artwork stores an empty placeholder txbx in every decorative shape; real Subpaths geometry removed the old filled-bounding-box overdraw risk). Net −0.585 summed pixel error: labels/14 −1.29 (blank → full sheet), business/04, letters/13; business/05's tile clusters now match Word exactly (+0.09 is the new-ink offset penalty — 3-up verified). **Known residuals from this pass:** brochures/04 (+0.55) and brochures/07 (+0.11) — their cell-anchored PICTURES now render (the left-panel construction photo was entirely missing) but mis-sized (~half height) and over the quote text: the doc-parser picture path's group-child size resolution has the same class of scale defect ShapeParser has for shapes, and hoisted art draws over cell text (cross-type z-order, the #6 dossier's known gap). **Second pass landed — relativeHeight z-ordering:** Word draws every floating drawing in one z-space ordered by `wp:anchor@relativeHeight` (a shape can sit entirely beneath a sibling anchored later in the document). `AnchorPositioning` now carries the value onto all four floating element types, and `SortFloatingBatchesByZ` stable-sorts each maximal run of consecutive floating elements at parse — behind/in-front routing untouched, group children share their anchor's value so intra-group order survives. Net −4.90 summed pixel error across 30 scenarios: labels/11 −1.63, labels/08 −1.43, brochures/07 −1.07 (photos no longer wrongly stacked), newsletters/07 −0.64, newsletters/11 −0.54, menus/06, cover-letters/07, brochures/04. brochures/03 (+0.17 metric) is visually far CLOSER to Word — its circle-clipped photos and starbursts now render correctly layered; the residual is their positions. cards/13 (+0.34) is layer-shuffle noise on an already-divergent card layout. **Third pass attempted and reverted — layoutInCell evidence:** `wp:anchor@layoutInCell` (default true) makes a cell-anchored drawing position against its CELL's frame, and the model now parses the flag (`AnchorPositioning.LayoutInCell` → all four floating elements) for the eventual fix. A parse-time rebase (H → column-relative + the cell's grid offset; V → paragraph-relative at the pre-table cursor) was net-negative (+0.035: brochures/04 +0.022, brochures/07 +0.013) because the VERTICAL half is unknowable at parse — brochures/04's photo cell sits mid-page, not in row 0, so the paragraph-relative approximation pins it to the table top while Word wants it at the cell's own Y. The correct design is render-time: attach cell floats to their `TableCell` (instead of the parse-side hoist) and render them from the shared table renderer when the cell rectangle is known, translating page/margin anchors against the cell rect — that also fixes behind-text ordering against the cell's own text. **Fourth pass landed — cell-attached render-time floats:** the render-time design is in. `TableCell.Floats` carries the cell's anchored drawings (z-sorted at parse; the body-level hoist and its `pendingCellFloats` machinery are gone), and the SHARED `PageRendererBase.RenderTableCell` draws them when the cell rectangle is known — behind-text ones after the cell background/borders and under its content, the rest after the content — via `RenderCellFloats`: layoutInCell anchors are re-based to absolute page coordinates at the cell's outer box (`WithAbsolutePosition` copies, member-audited this time; `RenderFloatingTextBox`/`RenderFloatingWordArt` promoted to base abstracts), layoutInCell="0" anchors dispatch unchanged and keep their real page resolution. brochures/04's construction photo now sits at the panel bottom exactly like Word (−0.56) and business/05 improved further (−0.21); net −0.048 with two documented position residuals: labels/14 (+0.20) — the cell path resolves against the TABLE's x (including its indent) where the old hoist resolved against the content margin, a ~14pt whole-sheet shift whose correct reference (Word appears to measure from the page margin there) needs pinning against more samples; brochures/07 ✅ RESOLVED (−1.46): the balloons hang at `positionV paragraph −260.6pt` from a paragraph in an otherwise-EMPTY cell, so the target escapes above the cell — and ECMA's layoutInCell rule ("the object shall be positioned within the existing table cell") is what lands them at the cell's top edge, exactly where Word draws them. The clamp is deliberately narrow, each exclusion evidence-backed: paragraph-relative vertical targets only (margin/page targets render where they say — letters/13's letterhead band at margin −735pt regressed +0.17 under any wider clamp), never horizontal (brochures/04 bleeds its photo −16pt off the cell edge), never the far edges (drawings overflow cell bottoms; sheet-spanning groups anchored in the first cell keep their children in place — an early both-edge clamp crushed labels/14's whole sheet into cell (0,0)), and never rotated elements (their box is the unrotated frame). Paragraph-relative anchors also resolve against their ANCHOR PARAGRAPH's measured top inside the cell (`TableCell.FloatAnchorParagraphOrdinals` + `CellFloatY` walking `Measurer` heights), not the cell top. **Fifth pass landed — nested-group rotation composition (−2.42 summed):** the labels/14 "reference-frame question" measured out to a different defect entirely — the sheet's left/top origin matched Word within 0.5pt all along; what differed was every wave INSIDE the cards, because each card holds a nested wave sub-group with `a:xfrm rot="16200000"` (270°) that `GetAccumulatedTransform` dropped (it composed offset+scale only — and composed them in the WRONG ORDER for non-identity nesting: outermost-first with an innermost-first update rule, harmless only because Word's usual `chOff==off/chExt==ext` nesting is order-independent). The walk now builds a full 2×2 affine: each grpSp contributes `R·diag(scale)` rotating about its own centre, composed innermost-out; `MapRectangle` maps a child rect's centre exactly, sizes by basis lengths (so a 90°-family rotation correctly swaps which outer scale hits which child axis), and returns the composed rotation for the model's centre-rotation renderers — child `@rot` adds on top. Threaded through all `AccumulatedTransform` consumers (solid-fill shapes now carry `RotationDegrees`; text boxes and group-child pictures compose it; group lines bail on any composed rotation, same policy as child rot). labels/14 −0.458/backend (0.51→0.053 — waves flow vertically down each card exactly like Word), cards/09 −0.174/backend (its green cards carry child rot=−45° that a dropped group rot=+45° was meant to cancel — they rendered as overlapping DIAMONDS, now sit square), cards/02 −0.097/backend (scroll+stars sub-groups at 348° now tilt together), business/05 −0.071×2 (the 180° staircase motifs were mirrored), labels/04, cards/06/18 small improvements; worst delta +0.0006 noise. Corpus has 10 scenarios with rotated nested groups; labels/03 (90°×11), labels/06 (270°×22), menus/05, newsletters/03, wedding/04/09 showed no pixel change — their rotated groups sit in paths that still don't reach this walk (e.g. ShapeParser-owned body anchors, blip-filled children) — candidates for a follow-up. Remaining: (b) the earlier brochures/07 picture-size class where sizes still differ; (c) stray-art clipping (cards/13's white placeholder boxes). Missing freeform/vector shapes: brochures/04/06 (chevrons, balloon art, quote box), business/04/05 (banners, watercolor blobs), business-plans/01 (accent bar), cover-letters/06/07/10 (banner bands, logos), letters/02/03/11 (frames, gradient banners, logo strip), cards/18/12/05/06 (fold guides), labels/02/03/07 (cell borders, tear lines, collage shapes), menus/01/04/06 (floral art, doodle pattern, red bars), newsletters/12/13/14, resumes/10/12. Missing/mangled pictures: brochures/07 (photos swapped/missing/misplaced), newsletters/03 (all photos), newsletters/04 (all inset photos), newsletters/08/11 (photos clipped to slivers), cards/02 (blossom behind card), labels/11/14 (background art gone → white-on-white blank page), business-plans/12 (SWOT graphic), cover-letters/09 (profile photo), newsletters/13/14.
6. **Shape geometry defects** — ⚠ PARTIALLY FIXED. **Preset polygons now render** (were rect fallbacks): `PresetShapeGeometry` evaluates the ECMA formulas for the seven presets the corpus uses — hexagon, roundRect, plaque, octagon, star5, frame, round2SameRect — into the same unit-square contours as `a:custGeom`, wired into standalone shapes, floating-group children (ShapeParser) and inline-group children (`GroupShape.Subpaths` + polygon branches in all four renderers, even-odd fill so `frame` stays a ring). Cleared: `newsletters/03` hexagons (`labels/04`'s hexagon group never reaches the shape parse — its baselines didn't change; still open), `labels/10` stadiums, `cards/02` plaque notches + star5, `letters/10`'s frame (was a filled rect covering the whole letter — PDF error 0.865→0.176, the corpus's largest single improvement), improvements on brochures/06, letters/05, labels/11, cover-letters/06, menus/09, newsletters/06. **Still open:** (a) ✅ shapes that carry text now draw their chrome: `FloatingTextBoxElement` carries the `a:ln` outline (previously text boxes had no outline concept at all) and the shape's geometry (`ExtractSubpaths` ?? `PresetShapeGeometry`), drawn behind the text in Skia/ImageSharp/PDF — cleared labels/10's bottom stadium rows and improved letters/05, labels/09, agendas-minutes/05/06 (HTML: text boxes still emit no border). **cards/02's orange ticket outlines turned out NOT to be text-box-carried — they're outline-only shapes (`a:noFill` + `a:ln`, no `txbx`), which ShapeParser drops for lacking a fill.** An outline-only emission has now been attempted twice and reverted twice; the second round's evidence: (1) a real co-defect was found and **kept** — `a:ln` solid-fill **alpha** was ignored (menus/09's 6.75pt octagon frame is `bg1` lumMod65 at **16% alpha**; opaque it rendered as a silver band) — with `LineAlpha` wired through `ExtractLineStyle` → `FloatingShapeElement`/`FloatingTextBoxElement` → all four renderers' strokes; (2) with alpha correct, menus/09's frame is **pixel-verified right** (scanline: Word band x=96 w=14 lum 80–82 vs Morph x=95 w=15 lum 70–82) yet the scenario still nets **worse** — the residual regressions come from the *other* emitted outline-only shapes on those pages (unidentified per-shape), the board trio stays +0.016/backend, labels/04 double-draws (its 30 outline-only shapes carry `txbx`, so the text-box chrome already strokes them — any re-attempt needs a skip-when-txbx guard), and cards/02's outline draws under the ticket body (text boxes render in a later phase than background shapes — cross-type z-order, `relativeHeight` unmodelled). Net across the corpus: 4 scenarios better, 10 worse. Re-landing needs per-shape forensics on menus/04 (452 shapes)/labels/02/the boards + the z-order model; (b) ✅ picture flips now apply — `a:xfrm/@flipH`/`@flipV` parse onto inline runs, block `ImageElement`s and anchored `FloatingImageElement`s, and mirror around the image centre inside the rotated frame in Skia (canvas scale), ImageSharp (`GetProcessedImage` flip steps; `FlipMode.Horizontal` = left-right mirror, confirmed empirically) and PDF (transform). Cleared `agendas-minutes/02`'s mirrored wave and `labels/13`'s flipped arrows, and un-mirrored the wedding/02/04/06/09 floral clusters (−0.015..−0.047 per backend) plus newsletters/01 — every backend improved in lockstep. HTML residual: the exporter applies no picture transforms at all (rotation included); (c) ✅ `business-plans/12`'s section-marker arrows now assemble cleanly — two causes fixed: group-transform-scaled stroke widths (a group displayed below its child size drew full-width strokes over shrunken geometry; lines scale by their along-axis factor since a single-line wrapper group's cross axis is degenerate — a divider rule's child box is 2103120×1 EMU) and square line caps on the PDF's group-line pen (the raster already had them; flat caps left notches where the arrow pieces meet). `business-plans/02`'s arrow finding remains open (different construction — its baselines were unchanged by this fix); (d) duplicated art (`cards/06`, `business/06`), group-child offset (`menus/07`/`inline_group_crop`), stray art Word hides (`cards/04`, `labels/16`, `newsletters/12`).
7. **Text inside dark shapes renders black instead of its white/light run color** — ✅ FIXED. Two coupled cascade defects, untangled via a corpus differential (8 docs carry a white `w:docDefaults` colour; Word paints the fall-through runs white on menus/07/08's boards yet black on cards/01/05/15 + wedding/03 — the cards' `Normal` declares `w:color w:val="auto"`, an explicit reset): (a) `DocumentParser` deliberately discarded a white docDefaults colour ("Word renders such runs black" — wrong; the blackness came from the auto override it wasn't modelling); the white default now flows like any other. (b) `w:color w:val="auto"` in a *style* couldn't reset an inherited colour — `ResolveRunColor` maps auto to null, and null means "keep inheriting" — so an `automaticColorSentinel` now survives the style chain (incl. `basedOn` inheritance) and converts to the automatic colour at run resolution; the run-level reset already existed. (c) **Automatic is contrast-aware**: `ComputeAutomaticRunColor` resolves automatic/colourless runs to white when `w:background` is dark (BT.601 brightness < 128) — brochures/03's "Technology For all"/"Relecloud"/body now render white on navy in all backends (cover-letters/03/16, the other dark-page docs, carry explicit colours and were untouched). Cleared: menus/07, menus/08 (title, headings and item lines), inline_group_crop, inline_group_rotation (board text white in all four backends), brochures/03's two MAJOR white-text findings, menus/09 (−0.003); incidental corrections where explicit-auto styles previously inherited a colour and now reset black per Word (brochures/04 contact block, business-plans/12, wedding/05, agendas-minutes/08, resumes/06 — brochures/04/menus/08 tick the pixel metric up slightly because the now-correct glyph colours pay a position-offset penalty their formerly-invisible text didn't; visually verified toward Word, 3-up crops). cards/01/05/15 and wedding/03 changed by zero bytes. menus/03 was misclassified under this issue: its board text was already white — its real defect is the missing EVENT TITLE header block (text-box placement). Residual: the HTML export now emits the white text faithfully but still doesn't paint dark shape/page backgrounds behind it (separate export gap).
8. **Picture effects ignored** — ⚠ PARTIALLY FIXED. **Duotone colour ✅ (raster):** `a:duotone` rendered plain greyscale AND never applied to anchored pictures at all (`FloatingImageElement` carried no effect — brochures/02's three duotones are all anchored). The parser now resolves the duotone ramp's dark end (first colour child, theme transforms via the ShapeParser helpers; the trailing `prstClr white` is the ramp top) onto `ImageElement`/`FloatingImageElement.DuotoneColorHex`, and Skia/ImageSharp map luminance onto the dark→white ramp (colour matrix: `out_c = dark_c + L·(1−dark_c)`; Skia filter, ImageSharp `Grayscale()+Filter`). brochures/02 −0.020..−0.040/page and brochures/03 p1 −0.053..−0.059 (its tinted hand-writing photo), letters/01 a small bonus — every changed page closer to Word. **Still open:** the PDF backend applies NO picture effects (Morph.Pdf has no pixel pipeline — PdfSharp only); the HTML export likewise ships the original bytes; group-shape pictures don't carry effects; soft-focus/blur (business-plans/02) and warm-tone (newsletters/07) unmodelled; brochures/03's circle crop is separate (its grayscale side is now duotone-corrected).
9. **Centered paragraphs inside text boxes/shapes render left-aligned** — ⚠ MOSTLY FIXED. The dominant cause was the document-wide default: card/label/menu templates centre every paragraph via `w:docDefaults/w:pPrDefault/w:jc` and `ExtractDefaultParagraphProperties` never read it — `defaultAlignment` now seeds the alignment cascade (style/paragraph `w:jc`, including an explicit "left", still overrides). Cleared the alignment on cards/01/05/07/11/12/15, labels/05, menus/02/08, wedding/03/06/08/09, inline_group_crop/rotation, menus/07 (with menus/09 the biggest single improvement, −0.032/backend) — 20 scenarios, every backend. **Follow-up landed:** (b) resolved — document_capture's "x" is `m:oMathPara` display math, now centred per OMML's `m:jc` default (centerGroup; explicit `m:jc`/`w:jc` overrides honoured), and the cards/16 / labels/03-class HTML findings were the exporter: cell paragraphs render inline so their alignment vanished — the cells' common non-left alignment now rides on the `<td>` (`CommonCellAlignment`; ~70 HTML snapshots re-centred their cell text, e.g. labels/03's tickets and cards/16's messages — the html_result-vs-print-PNG metric ticks up on a few because centred text in reflowed (wider) HTML cells sits at a different absolute x than Word's print layout, a comparison artifact, not a semantic miss). **Still open:** (a) the co-listed "narrower wrap box" (6 lines vs Word's 5) is unchanged — centred text can still wrap in a narrower measure; (c) cards/02's ticket-back placeholder barely moved (±0.0003).
10. **Table-style conformance gaps** — table-style bold/caps lost: business-plans/10 (header + first-column bold), business-plans/12 p17 (bolding inverted), business-plans/15 (ALL-CAPS headers lost), complex_tables (heading style colors wrong). **Attempted 2026-07-19 and reverted — evidence for the next attempt:** cascading `tblStylePr` rPr bold/caps onto cell runs (tri-state `ConditionalFormat.RunBold`/`RunAllCaps`, applied ON-only alongside the existing colour cascade) was NET-NEGATIVE: business-plans/15 saw ±0.02-0.03 swings both ways (p15 −0.018 better, p12 +0.033 worse — ALL-CAPS headers change text widths so wraps/pagination shift against other pre-existing offsets), business-plans/13 +0.0003..+0.0037 all-positive, business-plans/10 itself slightly worse everywhere (+0.0001..+0.0005 — correct bold at Morph's wider advances re-wraps header text Word fits), and business-plans/12's "inverted" pages worsened (+0.001..+0.0025 — that scenario needs the explicit `w:b w:val="0"` OFF distinction, which `RunProperties.Bold` being a plain bool can't represent; ON-only application is the wrong half for it). Re-landing needs (a) tri-state bold in the run model so region bold can be explicitly cleared, and (b) the table autofit/width work first so bolded headers don't re-wrap. The attempt's one keeper: `Run.WithProperties` — the conditional-colour cascade's manual run copy silently dropped hyperlink targets, footnote/endnote ids and page-field markers from recoloured runs; it now preserves every member; borders: business-plans/10 (grid incomplete on header/first rows), newsletters/04 (spurious cell borders on borderless tables), cards/13 (card outlines missing); widths: business-plans/12 p14 (phantom extra column), business-plans/13 (JUL column clips "$22,5("), menus/04 (colored cells end short), business/01 (memo columns too narrow), table_autofit_no_widths / table_default_style / complex_tables (autofit distribution differs from Word — since the issue-#2 fix moved text to 12pt this is those scenarios' dominant residual: mis-distributed columns wrap header text Word fits on one line).
11. **Numbering defects** — ⚠ PARTIALLY FIXED. **Bullet marker glyph coverage ✅ FIXED (all backends):** level-3/5 bullets rendered tofu because the plain-Unicode geometric glyphs (■ U+25A0, ▸ U+25B8) aren't in the bundled text faces and markers only routed to the embedded `Bullets.ttf` when the level *declared* Symbol/Wingdings. The ▸/► triangles were drawn into `Bullets.ttf` (fontTools, sized against its ▪/■), and the marker-font decision is now the shared `FontHelpers.UseBulletFont`: Symbol/Wingdings-declared OR one of ■ ◆ ▸ ► (Word glyph-falls-back to Segoe UI Symbol for those; other markers keep the paragraph font as Word does). The PDF backend previously ignored the marker font entirely — it now routes like the raster backends, with the embedded subset registered in `PdfFontResolver` (`::MorphBullets` face key; PDFs with proprietary-declared bullets grow ~3.8KB, visuals within ±0.0003). Cleared `deep_nested_list`/`nested_list` tofu and **the PDF tiny-middle-dot bullets** (business-plans/10, resumes/14, newsletters/01 — Word-size round bullets now, verified in 3-up crops). **Cross-table restart ✅ FIXED:** OOXML's counter belongs to the ABSTRACT numbering definition, shared by every numId that references it, and `w:startOverride` resets it at that numId's first use — business-plans/09's tables each start with an override numId while their remaining rows share the style's base numId, so the per-numId counters produced 1,6-9/1,10-13. `numberingCounters` is now keyed `(abstractNumId, ilvl)` with first-use override tracking; every table numbers 1-5 like Word. **Also:** the PDF backend drew NO marker for run-less numbered paragraphs (the "No."-column cells, whose number IS the content — the stale finding's values came from an era when they rendered) — the zero-lines branch now draws the marker with the paragraph mark's formatting. Only business-plans/09 re-baselined (the re-key is behaviourally identical when numIds map 1:1 to abstracts). **Still open:** SWOT bullets lose colors (business-plans/12). Also verified stale (already render correctly): the "roman/letter formats render as decimal" claims in #27 — agendas-minutes/04/05/06/14 all draw I./II./III. + a./b. matching Word.
12. **TOC rendering broken** — page numbers absent with dot leaders running to the margin, entries underlined in hyperlink style with inflated spacing (`business-plans/12`, `business-plans/13`; `business-plans/15` overflows the TOC onto an extra page).
13. **Footnotes/endnotes** — PDF drops them entirely; Skia/ImageSharp invent "Footnotes"/"Endnotes" heading sections, don't pin to page bottom, and the superscript reference marks are missing in all backends (`document_capture/01`).
14. **Comment markup not rendered** — no balloon, no highlight, no markup-area page shrink (`comments/01`).
15. **Legacy form-field glyphs missing** — checkbox boxes not drawn (`form_checkboxes`); inline content controls render as block-level widgets with chrome (`content_control_inline`).
16. **Automatic hyphenation not implemented** — Word's hyphenated breaks don't happen (`hyphenation_auto`, `hyphenation_suppressed` para 3); a word broken mid-word without hyphen in `letters/03` ("Customer S/ervice").
17. **Line-number values** — ✅ FIXED (all backends). The shared counter now pre-increments so line numbers match Word's `Start+1..Start+N` (2..N+1 for `w:start=1`); the count-by interval anchors to multiples of `w:countBy` (5,10,15,20, not 1,6,11,16); the gutter font is 10pt (was 9pt — measured against Word's reference output); and the PDF backend restarts numbering per section via `ResetLineNumbersForSection` (matching the raster's `SectionBreakHandler` + Word's per-section 2..16). Raster + PDF `line_numbers_*` baselines regenerated. **Fully resolved:** the stray line number on the empty section-break paragraph is gone — a paragraph whose only content is the sectPr is not a numbered line in Word, so the parser marks it `SuppressLineNumbers` (keeps its height, draws and consumes no number).

### PDF-only

18. **Line numbers** — ✅ FIXED (rendering). `w:lnNumType` line numbers now render in the PDF gutter via `PdfTextEngine.RenderLineNumber` — right-aligned at `ContentLeft − w:distance`, 9pt, one per line, only every `w:countBy` line, skipping (and not counting) `w:suppressLineNumbers` paragraphs; per-page restart wired through `StartNewPage`. A faithful port of the raster backends, so it inherits their **#17** residuals vs Word (off-by-one count, interval anchored to `w:start` rather than to multiples of `w:countBy`, and 9pt digits smaller than Word's) — fixing #17 in the shared logic would clear all three backends at once. Cleared the six `line_numbers_*` PDF scenarios (all render now).
19. **Paragraph borders** — ✅ FIXED. `w:pBdr` box / left-only / right-only / top-bottom / `w:between`-collapse edges and per-edge `w:space` now render in the PDF backend via `PdfTextEngine.DrawParagraphBorders` — a faithful port of the Skia/ImageSharp logic, with height reserved through `MeasureHeight`/`BorderSpaceExcess`. Cleared `paragraph_borders`; the heading/salutation underline rules Word draws (bottom `w:pBdr`) now also render on `cover-letters/02`, `business-plans/12`/`15`, `labels/06` (5 PDF scenarios regenerated). **Bar tabs also ✅ FIXED:** `w:tab w:val="bar"` vertical separators now render in the PDF backend via `PdfTextEngine.DrawBarTabs` — each Bar stop drawn as a full-cell-height rule (spanning spacing-before through spacing-after) so consecutive bar-tab paragraphs connect into one continuous separator; cleared `bar_tabs`. Issue #19 fully resolved.
20. **`a:srcRect` picture crop** — ✅ FIXED. The PDF backend now applies the crop to inline images (`PdfTextEngine.DrawImage`) and to block/floating images (`PdfPageRenderer.DrawRaster`), using the same `ImageCrop.Expand` + `IntersectClip` technique the shape-group path already used (PDFsharp has no source-rectangle API). 65 scenarios carry an `a:srcRect`; of the 17 whose render changed, 15 moved closer to Word — `business-plans/13` −0.2455, `wedding/05` −0.0445, `business-plans/12` −0.0354, `business-plans/02` −0.0265, `cards/16` −0.0222, `agendas-minutes/16` −0.0193. `newsletters/01`/`14` tick up slightly because the raster's crop framing also differs from Word there (the PDF now matches the raster exactly) — a shared limitation, not a PDF regression. **Negative values (padding) now also honoured, all backends:** Word treats a negative `a:srcRect` edge as growing the source outward — the picture shrinks inside its frame, letterboxed on that edge. `ReadCrop` used to clamp negatives to 0 (`business-plans/02`'s section-marker arrow is a 41×30 landscape PNG in a tall portrait frame via `t="-9453" b="-175911"`; the clamp stretched it full-frame into the "3× the height bold chevron" finding). `ImageCrop.Expand`'s math handles negative fractions naturally, so the PDF and HTML paths fixed themselves; Skia/ImageSharp source-rectangle fast paths route padding crops through `Expand` + clip / compose-onto-transparent-canvas instead (a source rect can't extend beyond the bitmap). Six scenarios carry negatives — `business-plans/02` (every page improves, all 3 backends), `wedding/05` (mixed 72% crop + 1.3% padding, −0.005/page all 3 backends), `menus/07`/`inline_group_rotation`/`newsletters/04`/`postcards/04` (sub-pixel, visually neutral).
21. **Picture rotation** — ✅ MOSTLY FIXED. The PDF backend now rotates inline images (`PdfTextEngine.DrawImage`) and anchored/floating images (`PdfPageRenderer.RenderFloatingImage`) around their centre, matching the raster backends: `image_rotation/01` and `letters/13`'s vertical banner now rotate, and the wedding scenarios' rotated florals now match the raster. (Rotation still reserves the un-rotated footprint, so a rotated image can overlap neighbouring text — a documented, all-backend limitation.) **Still open (PDF):** rotated **text boxes** are not rotated — `labels/06`'s "ADMIT ONE" captions (60 text boxes at 90°/270°) render horizontal; that's a separate text-box-transform path.
22. **Font substitution** — ✅ FIXED. Two `PdfFontResolver` defects, both diverging from the shared resolver: (a) the index bucketed styles as `bold = weight ≥ 600` with last-file-wins, so `Arial_900` ordinally overrode `Arial_700` and **every Arial-bold request rendered Arial Black** (agendas-minutes/19's wrapped "MEETING DATE:", brochures/04, newsletters/14); (b) unresolved families skipped the curated `FontHelpers.FontFallbacks` map and fell to the ordinally-first bundled file — **`Aharoni_700`, a bold face** — so "Grandview Display" (letters/08 whole-letter-bold + odd digit proportions), "Microsoft YaHei" (business-plans/12), and newsletters/08's script fonts all rendered heavy. The resolver now mirrors the shared `FontResolver` directory mode: faces are indexed with their OS/2 weight/width/italic and scored via `FontHelpers.ScoreFace` (bold picks 700 over 900), the `FontFallbacks` aliases fire with the shared `weightFallbackThreshold` rule, and the last resort is `DefaultFontSettings.DefaultFont` at the requested style. 28 scenarios re-baselined: 23 closer to Word (`wordart` −0.126, `business-plans/12` −0.045, `newsletters/14` −0.042, `complex_tables` −0.032), 5 within noise of the raster's own framing, none changed page counts. Remaining divergence: `ConversionOptions.FontFallback` (the user hook) is still not consulted by the PDF's process-global resolver.
23. **Small caps** — ✅ FIXED. `w:smallCaps` now renders in the PDF backend: `PdfTextEngine.Layout` runs paragraph runs through the shared `SmallCapsExpander` (lowercase → uppercased at 0.8× size) before layout, matching the raster backends. Cleared `small_caps`; applied on `feature_capture/01` (whose remaining diffs are unrelated). The PDF text layer stores the uppercased glyphs, so a small-caps run copy-pastes as all-caps (inherent to drawing small caps without an OpenType `smcp` feature). **Also ✅ FIXED:** `w:bidi` paragraphs now right-align when their alignment is the leading edge (`PdfTextEngine` mirrors the raster's RTL flip — glyphs are still not BiDi-reordered, there is no shaper); the non-breaking hyphen U+2011 maps to `-` so it renders instead of tofu (and stays unbreakable, since the tokenizer only breaks on whitespace); and soft hyphens U+00AD are dropped from the drawn text. Cleared `rtl_paragraph`, `hyphenation_soft`, `hyphenation_nonbreaking` and brochures/01's phone number — issue #23 fully resolved.

### HTML export

24. **Header/footer content omitted entirely** — ⚠ STALE: re-verified 2026-07-19 — `header`, `footer`, `header_footer` and `header_banner_table` all render their header/footer bands with ink counts comparable to Word (band-level pixel scan; differences are font-weight AA, not missing content). The findings predate the header/footer implementation. `even_odd_headers/*` unverified individually; SDT-wrapped galleries were a real residual fixed under #1(c).
25. **Anchored/floating objects linearized in flow order** — art detaches from its text and stacks at the document top or mid-flow, causing overlaps and empty frames (cards/02/18/19, labels/05/06/08/11, agendas-minutes/16, newsletters/01/02/07/11/12, brochures/01, letters/13, business/03/06, menus/05).
26. **White/light text emitted without its backing shape → invisible white-on-white** (agendas-minutes/02, brochures/03/07/08, cards/19, labels/11/14, menus/03, newsletters/01/08/10, resumes/02, business-plans/08 green-on-green).
27. **Numbering formats lost** — ⚠ STALE (roman/letter part): re-verified 2026-07-18 — agendas-minutes/04/05/06/14 all render I./II./III./IV. + a./b. correctly in the current baselines (`FormatNumber` handles upperRoman/lowerRoman/upperLetter/lowerLetter, style-attached numbering resolves); the "renders as decimal" findings predate a fix. Still open: multi-item restarts at "1." where Word continues, and `business-plans/15`'s TOC formats (see #12).
28. **Inter-paragraph spacing collapsed** — ⚠ STALE: re-verified 2026-07-19 — `letters/01` renders distinct paragraphs with Word-comparable gaps (blank line before the salutation, spacing between body paragraphs; the block sits ~35px lower overall, the #2-family vertical drift), and `empty_paragraphs`' "Text after empty paragraphs." lands within ~13px of Word — blank lines are preserved, not dropped. The findings predate the spacing work (margin collapsing, docDefaults spacing, contextual spacing).
29. **Tab stops collapse to a single space** — ⚠ STALE: re-verified 2026-07-19 — `tab_stops` renders dot leaders, column stops and right-aligned page numbers pixel-close to Word, and `decimal_tabs/01` aligns every decimal point at Word's x (only its line spacing differs — issue #2 family); `bar_tabs` was fixed with #19. The findings predate the tab implementation. Possibly-live remnant: `resumes/14`'s inline dates (unverified); TOC dot-leader issues are tracked under #12.
30. **Section page color scoping wrong** — ⚠ RECLASSIFIED: none of the listed documents carries `w:background` (and most are single-section), so there is no section-colour mechanism to scope — their "page colours" are full-page floating shapes/pictures anchored behind the text, and the stops/wrong-colour symptoms are those shapes being dropped or mispositioned. This is the floating/anchored-art class (systemic #5/#25), not a colour-scoping defect.
31. **CSS styling in AltChunk/HTML-source tables dropped** — header fills, row shading, cell colors, interior borders, width/padding attributes (html_table_*, html_complex, html_css_*, html_inline_styles) — affects the raster/PDF backends identically since they share the HTML parser.

### Word-reference (expected_*.png) anomalies worth re-checking rather than "fixing"

32. `cards/04`: backends draw a tree + second bird flock that Word's render omits, and `newsletters/12` draws an olive stripe Word hides — likely shapes Word suppresses (behindDoc/hidden or layoutInCell rules) that Morph draws; verify against the DOCX before treating Word as wrong.
33. `feature_capture/01`: Skia/ImageSharp render a drop cap where Word's reference shows none (Word appears to ignore the dropCap property here); same scenario's shadow effect renders as a duplicated text copy in ImageSharp.

---

## Per-scenario findings

### agendas-minutes/01

- MEDIUM | all | p1 | vertical compression: schedule-table rows ~10% shorter and section gaps tighter, so the ADDITIONAL INFORMATION section ends ~130px (0.85in) higher than Word
- MINOR | skia,imagesharp | p1 | word spacing visibly looser than Word throughout body text, including a stray gap before the trailing period ("...to take notes .")
- MINOR | pdf | p1 | expanded letter-spacing dropped on SCHEDULE / ADDITIONAL INFORMATION headings (render ~12% narrower than Word)
- MINOR | html | - | classroom illustration stacked above the title instead of floating to the right of it

### agendas-minutes/02

- ✅ FIXED | all | p2 | bottom wave artwork no longer mirrored — the picture's a:xfrm/@flipH now applies, putting the opaque royal-blue crest on the left like Word (see systemic #6b)
- MEDIUM | pdf | p1 | bold weight lost: FINANCIAL MEETING/AGENDA title, Date/Time/Facilitator labels and all roster names render regular weight
- MEDIUM | skia,imagesharp | p1 | Date/Time/Facilitator values ("September 9", "11:00 am", "Mirjam Nilsson") render bold where Word has regular
- MINOR | pdf | p1 | letter-spaced title "FINANCIAL MEETING" renders ~15% narrower (tracking dropped)
- MINOR | skia,imagesharp | p1 | double-width word gap in title "FINANCIAL  MEETING"
- MINOR | all | p1 | agenda table rows slightly tighter, cumulative ~half-line upward drift by the last row
- MAJOR | html | - | header and roster text invisible: navy page background renders as a separate empty block, and the white text (FINANCIAL MEETING, AGENDA, date block, 8 roster names/titles) lands below it on the white page background

### agendas-minutes/03

- MINOR | all | p1 | cumulative upward drift ~1 line height by page bottom (Secretary / Date of approval signature block sits higher than Word)
- CLEAN: html

### agendas-minutes/04

- MEDIUM | all | p1 | cumulative vertical compression ~2 line heights: agenda list and CONCLUSION section end noticeably higher than Word
- MINOR | pdf | p1 | expanded letter-spacing dropped on MEETING AGENDA / AGENDA DETAILS headings (~12% narrower)
- MINOR | skia,imagesharp | p1 | double-width word gap in spaced-caps headings ("MEETING  AGENDA")
- MEDIUM | html | - | list numbering format lost: roman numerals I.–IV. render as 1.–4. and letter sub-items a./b. render as 1./2.

### agendas-minutes/05

- MINOR | all | p1 | content drifts up ~1.5 line heights by the action-items table
- MINOR | all | p1 | corner triangle decorations (top-right, bottom-left) offset ~15-20px from Word positions
- MINOR | pdf | p1 | expanded letter-spacing dropped on MARKETING & SALES TEAM / MEETING MINUTES / AGENDA ITEMS headings (render narrower)
- MINOR | skia,imagesharp | p1 | extra-wide word gaps in spaced-caps headings
- MEDIUM | html | - | roman-numeral agenda list I.–VI. renders as decimal 1.–6.

### agendas-minutes/06

- MEDIUM | all | p1 | cumulative vertical compression ~3 line heights: ADJOURNMENT section ends ~0.5in higher than Word
- MINOR | all | p1 | triangle decoration clusters (top-left, mid-right) slightly offset from Word positions
- MINOR | pdf | p1 | expanded letter-spacing dropped on spaced-caps headings (title renders narrower)
- MINOR | skia,imagesharp | p1 | extra-wide word gaps in spaced-caps headings
- MAJOR | html | - | list numbering broken: I.–VI. renders as 1., 1., 1., 1., 2., 3. (first four top-level items each restart at 1) and a)/b)/c) sub-items render as 1./2./3.

### agendas-minutes/07

- MEDIUM | pdf | p1,p2 | body text wraps at different points than Word due to narrower character widths (ADVISORY COMMITTEE paragraph 4 lines vs Word's 5; "Parent Education Programs – Counselors" bullet fits one line vs Word's two)
- MINOR | all | p1 | content below the header sits ~1 line lower than Word
- MINOR | skia,imagesharp | p1,p2 | word spacing looser than Word with stray space before periods after names ("August Bergqvist .", "Allan Mattsson .")
- CLEAN: html

### agendas-minutes/08

- MEDIUM | all | p1 | Cumulative line-spacing drift: body sections creep upward down the page, PRINCIPAL'S REPORT block ends ~35px (~1.3 line heights) higher than Word
- MINOR | skia,imagesharp | p1,p2 | Expanded letter-spacing rendered as oversized word gaps in title/headings ("PTA  MEETING   MINUTES", "APPROVAL  OF  MINUTES") instead of Word's per-letter tracking
- MINOR | pdf | p1,p2 | Expanded letter-spacing dropped on title/headings — "PTA MEETING MINUTES" renders noticeably narrower than Word
- MAJOR | html | - | Page-break decoration (blue band + orange ring shape) is emitted mid-flow and drawn across the "COMMITTEE REPORTS" heading, overlapping the text
- MINOR | html | - | Header decorative shapes distorted: orange half-donut renders as solid semicircle, title-band ring renders as rounded-square ring

### agendas-minutes/09

- MEDIUM | all | p1 | Cumulative upward drift: Secretary / Date of approval signature block sits ~46px (~2 line heights) higher than Word
- MINOR | html | - | "Meeting Minutes" title is centered across the page instead of sitting left-aligned next to the logo

### agendas-minutes/10

- MEDIUM | all | p1 | Cumulative upward drift: agenda table rows and "Additional Information" block end ~37px (~1.4 line heights) higher than Word
- CLEAN: html

### agendas-minutes/11

- MEDIUM | all | p1 | BUDGET paragraph wraps mid-word: "today's" is split across lines as "In to / day's meeting" (Word breaks before the whole word "today's")
- MINOR | skia,imagesharp | p1 | Letter-spaced title/headings render with oversized word gaps ("MEETING  MINUTES", "ADMIN   MEETING", "APPROVAL   OF  MINUTES")
- MINOR | pdf | p1 | Expanded letter-spacing dropped — "MEETING MINUTES" title and subtitle render narrower than Word
- CLEAN: html

### agendas-minutes/12

- MINOR | all | p1 | Right edge of the [Date] gray bar and "Meeting Notes" dark banner is ~10-15px off vs Word (bar width mismatch, visible as a solid strip in the diff)
- MINOR | all | p1 | Bullet continuation lines ("want.]", "this one.]") are indented ~40px deeper than Word instead of aligning with the bullet text start
- MINOR | all | p1 | Slight upward drift: Discussion/Roundtable sections end ~1 line height higher than Word
- CLEAN: html

### agendas-minutes/13

- MEDIUM | all | p1 | Agenda table rows progressively shift/compress upward; table bottom border ends ~35px (>1 row of text) higher than Word
- MINOR | skia,imagesharp | p1 | Letter-spaced headers render with doubled word gaps ("BALSAM  ELEMENTARY", "PTA  MEMBERS")
- MINOR | pdf | p1 | Tracking reduced on letter-spaced header text ("BALSAM ELEMENTARY", subtitle) — text narrower than Word
- CLEAN: html

### agendas-minutes/14

- MEDIUM | all | p1 | "Attendees: Helbe Sokk, ..." renders as two lines ("Attendees:" alone, names on next line) vs Word's single line, pushing all following content ~1 line lower
- MEDIUM | all | p1 | Wide roman numerals fuse to heading text with no separator: "III.APPROVAL OF MINUTES...", "IV.OPEN ISSUES", "VI.ADJOURNMENT" (PDF also "II.ROLL CALL", "V.NEW BUSINESS"); Word shows a clear gap after every numeral
- MINOR | skia,imagesharp | p1 | "MEETING  AGENDA" title word gap wider than Word
- MINOR | pdf | p1 | Title tracking reduced — "MEETING AGENDA" narrower than Word
- MAJOR | html | - | List numbering wrong: every numbered heading and sub-item renders as "1." (roman I.-VI. and letter a)-c) formats lost, each item restarts at 1)
- MAJOR | html | - | "Attendees:" label text missing — only the name list renders under the title
- MINOR | html | - | Decorative teal stripes at top and bottom of the page are missing

### agendas-minutes/15

- MEDIUM | skia,imagesharp | p1 | Numbered-list continuation lines indented deeper than Word (wrap lines don't return to the hanging-indent column), changing item 4's wrap ("...ribbon, click / an Insert option." vs Word's "...click an / Insert option.")
- MINOR | all | p1 | Body text drifts a few px lower down the page and the action-items table (dark green header bar) sits ~10px lower than Word
- CLEAN: html

### agendas-minutes/16

- MEDIUM | pdf | p1 | Decorative art rescaled/shifted inward: top-right pink leaf ~20% smaller and no longer bleeding off the page top/right edge; bottom-right and left-edge dot clusters likewise shifted so corner ovals aren't edge-cropped
- MINOR | all | p1 | Whole content block sits ~13px higher than Word; DATE/TIME/MEETING CALLED rows slightly shorter so the offset grows to ~17px by NEXT MEETING
- [known] MINOR | skia,imagesharp | p1 | Residual pixel-level differences on the pink leaf/dot decorations (Skia SVG render vs ImageSharp PNG fallback, documented in notes.md)
- MAJOR | html | - | Anchored decorative images (dot clusters + leaf) render as in-flow blocks at the top-left of the document, pushing the "Minutes" title ~850px down, instead of corner-anchored overlays

### agendas-minutes/17

- MINOR | all | p1 | Entire layout (title, info rows, three bullet columns) rendered with a small uniform down/right shift (~5px), with the second/third bullet columns also offset horizontally a few px
- MINOR | html | - | First divider rule renders below the "Meeting time" row instead of above it, and both divider rules extend to the viewport's far left edge past the content margin

### agendas-minutes/18

- MEDIUM | skia,imagesharp | p1 | Numbered-list continuation lines indented deeper than Word (continuation not aligned to the hanging-indent column, e.g. "typing. Don't include..." in item 1)
- MINOR | all | p1 | Vertical spacing slightly tighter than Word; drift accumulates down the page so the action-items table rows sit ~15-20px higher by the last row
- MINOR | html | - | Action-items table header labels ("Action items", "Owner(s)", "Deadline", "Status") are centered over their columns instead of left-aligned as in Word

### agendas-minutes/19

- [known] MEDIUM | all | p1 | Contact-table rows (especially the empty ones) render shorter than Word (~25pt vs ~30pt), so rows drift progressively upward and the table ends well above Word's (documented in notes.md)
- ✅ FIXED | pdf | p1 | bold now renders Arial Bold, not Arial Black — "MEETING DATE:" fits on one line again (see systemic #22)
- CLEAN: html

### align_justified

- MEDIUM | all | p1 | Justified paragraph wraps into 3 lines instead of Word's 4 (renders fit more words per line, e.g. line 1 ends "...clean block" vs Word's "...creating a"), so Word's stretched word-spacing look is lost
- MINOR | skia | p1 | Wrapped line 2 begins with an untrimmed leading space (" appearance."), leaving its left edge indented off the justified margin
- CLEAN: html

### align_mixed

- MINOR | all | p1 | paragraph spacing slightly under Word's, so blocks drift progressively upward — last justified paragraph sits ~22px (~0.8 line) higher than in Word
- CLEAN: html

### align_right

- MINOR | all | p1 | second right-aligned paragraph sits ~7px higher than Word (inter-paragraph spacing slightly small)
- CLEAN: html

### bar_tabs

- ✅ FIXED | pdf | p1 | both bar-tab vertical separator lines now render (flanking the tabbed columns, spanning the three bar-tab paragraphs and stopping at the plain paragraph) — was: completely missing. See systemic #19.
- MINOR | skia,imagesharp,pdf | p1 | text lines drift upward progressively (~5-10px by the last paragraph) versus Word
- MAJOR | html | - | bar-tab vertical separator lines are not rendered at all and the tabbed columns collapse to single spaces ("Column one Column two Column three")

### block_quote

- MEDIUM | all | p1 | quote's first line wraps at a different point than Word — Morph fits "yet, keep" on line 1 ("...found it yet, keep / looking. Don't settle.") where Word breaks after "found it"
- MINOR | all | p1 | quote block and "— Steve Jobs" attribution drift upward cumulatively (~17px at the attribution line)
- CLEAN: html

### brochures/01

- MAJOR | pdf | p1 | contact line renders a notdef/tofu box in place of the hyphen in "(206) 555-0100" (non-breaking hyphen not mapped by the PDF font)
- ✅ FIXED | pdf | p1,p2 | display text no longer falls to the heavy default face — the display family now resolves through the curated fallback/weight scoring (see systemic #22)
- MEDIUM | all | p2 | bullet before "GET THE EXACT RESULTS YOU WANT" is drawn as a small teal dot instead of Word's larger pink/magenta dot
- MINOR | all | p1,p2 | diagonal hatch fills (orange top-left shape, pink bottom shapes) are drawn at a noticeably finer stripe pitch than Word (~11-12 stripes vs Word's ~9 over the same span)
- MINOR | all | p1,p2 | body/contact text blocks sit ~5-10px lower than Word, drift growing down each panel
- MAJOR | html | - | second page's navy panel/artwork ends mid-content: "USE ICONS TO ADD" and "MAKE IT YOURS" headings are cut in half at the boundary, their white body paragraphs are invisible on the white page background, and the speech-bubble and hatched-wave shapes behind the quote are missing
- MAJOR | html | - | "EVENT SERIES NAME" heading is overlapped by the light-blue blob graphic (z-order wrong), truncating "SERIES" and "NAME" to "SERIE"/"NA"

### brochures/02

- MAJOR | all | p1,p2 | Word's red duotone recolor is not applied to any photo (p1 swimmer, p2 underwater diver and poolside-hug photos) — all render in original blue tones
- MEDIUM | pdf | p1,p2 | photos lose Word's tight crop and show a zoomed-out view (p1: swimmer smaller with lane rope visible; p2 hug photo: full diving platform visible)
- MEDIUM | skia,pdf | p1,p2 | short blue divider rule (below "Meet director: Ravi Costa" on p1, below the Day-2 finals list on p2) rendered as a multi-row hatched/striped block instead of a solid line (ImageSharp matches Word)
- MINOR | all | p1,p2 | small block shifts: red title lines sit ~20px lower on skia/imagesharp, date/venue and schedule text offset ~10px on all backends
- MAJOR | html | - | photos keep original blue colors — red duotone recolor missing
- MEDIUM | html | - | red dashed decorative graphic overlaps the "Event officials" text lines (Judge's coordinator / Meet director)
- MINOR | html | - | divider rule renders as a tiny hatched box, and the "August 12th - 14th" line crowds the title's descenders

### brochures/03

- MAJOR | all | p1 | decorative shapes missing: white outline starburst, solid teal starburst, thin teal outline starburst, and the teal star cluster beside the title; the white 8-point star is displaced from page center to the far-left edge
- MAJOR | all | p1,p2 | greyscale circle-clipped photos (hand writing p1, pencil/ruler p2) rendered as unclipped full-color rectangles at the wrong position
- ✅ FIXED | all | p1 | title "Technology For all" now renders white on the navy background — automatic (colourless-chain) text resolves white against a dark `w:background`. See systemic #7.
- ✅ FIXED | all | p2 | "Relecloud" title and the "Founded in 2001..." body paragraph now render white on navy (automatic colour against dark `w:background`; subtitle was already correct). See systemic #7.
- MAJOR | skia,imagesharp | p2 | heading rendered "Eventitinerary" — the space between the words is dropped (PDF correct)
- MEDIUM | all | p2 | page content sits too high: Relecloud block ~0.3-0.45in up, itinerary rows ~0.25in up, and "ConnectAbove"/"Launch Event" footer links 60px too high (tucked under the card instead of centered in the navy band)
- MEDIUM | all | p2 | white outline starburst shifted from behind the photo circle onto the far-left teal strip; mid-page teal starburst wrong size/position (skia/imagesharp enlarge it, pdf draws a small complete star)
- MINOR | pdf | p1,p2 | photo interior crop wider than Word and the other backends (more scene, hands smaller)
- MINOR | pdf | p2 | heading stars drawn as faint outlines instead of solid white, left 8-point star teal instead of white, and bottom-right starburst drawn complete instead of clipped at the card edge
- MINOR | skia,imagesharp | p1 | "For all" and following title lines sit ~25px lower (extra gap after "Technology")
- MAJOR | html | - | "Technology For all" and "Relecloud" render black on navy (near-invisible), and the Event-itinerary schedule renders dark-on-dark — the light teal card background is missing behind it
- MAJOR | html | - | second and third photos render as unclipped full-color rectangles (greyscale circle treatment missing)

### brochures/04

- MAJOR | all | p1 | construction-site photo (bottom of left column) missing entirely
- MAJOR | all | p1 | "Title" line of "Brochure Title" and "Brochure Subtitle" not rendered; the remaining "Brochure" word overlaps the house photo
- MAJOR | all | p1,p2 | roof-chevron accent shapes missing everywhere (above the quote, above the brochure title, above each "Headline 1")
- MAJOR | all | p2 | brick-wall photo drawn full-width and ~1.5in too tall — the quote box's last lines and the third column's closing paragraph render on top of the bricks, and the white/tan diagonal cutout over the bricks is missing
- MEDIUM | all | p1 | house photo not clipped by the diagonal navy edge — extends ~40% lower with a straight bottom edge
- ✅ FIXED | pdf | p1,p2 | headings render Arial Bold, not Arial Black, and the quote box wraps at 4 lines again (see systemic #22)
- MINOR | all | p1 | "Company Email"/"Company Website" rendered hyperlink-blue instead of black
- MAJOR | html | - | construction photo missing, and the brick-wall image overlaps the address block and the "Brochure Title"/subtitle area
- MEDIUM | html | - | roof-chevron accents missing
- MINOR | html | - | Email/Website styled as underlined blue hyperlinks and address block left-aligned instead of centered

### brochures/05

- MAJOR | all | p2,p3 | placeholder paragraphs beside the product photos are missing (p2: texts beside BUFFET-STYLE MEALS and PLATED DINNERS; p3: all three "To replace any placeholder text..." blocks); only the appetizers paragraph survives
- MEDIUM | all | p4 | spice-tray and soup photos lose Word's tight crop — the full image is shown zoomed out with visibly smaller subjects
- MINOR | all | p1 | body paragraphs wrap at different words (same line count, different break points)
- MINOR | all | p1,p2,p3,p4 | headings/text blocks sit ~5-12px higher than Word throughout
- MAJOR | html | - | same photo-side placeholder paragraphs missing (p2/p3 content)
- MEDIUM | html | - | orange background panel behind the CONTACT US / logo block missing
- MINOR | html | - | table-of-contents dot leaders missing

### brochures/06

- MAJOR | all | p1,p2 | decorative freeform art missing everywhere: striped bar above the purple panel, dash-pattern blocks (beside panel p1, over couple photo p2), hot-air balloon line art (p1 panel, p2 watermark), stripe rules beside/below the quote area, and the small accent bars (white bar under "VACATION", olive bars under "THE FOLLOWING:" and "OUR SERVICES INCLUDE")
- MAJOR | all | p2 | olive-green quote box with white text "We don't merely book your travel..." and "- Henriette Andersen" attribution entirely missing
- MAJOR | pdf | p2 | top-right couple photo rendered ~30% narrower and shifted ~110px right, bleeding to/clipped at the right page edge (left portion of Word's crop lost)
- MEDIUM | all | p2 | right-column reflow: "To replace any of the pictures" paragraph wraps 4 to 5 lines in a narrower block, and the services list ("Passport Expediting"..."Trip Insurance") sits 20-80px lower than Word
- MINOR | all | p1 | "MARGIE'S TRAVEL" title, panel address block, and right-side photo uniformly shifted down ~8-10px
- MINOR | skia,imagesharp | p2 | couple photo drawn slightly taller with content shifted ~8px vs Word's crop
- MAJOR | html | - | quote box text "We don't merely book your travel..." and attribution missing from the HTML export
- MAJOR | html | - | all decorative vector shapes (balloon art, stripe bars, dash patterns, accent bars) missing from the HTML export

### brochures/07

- ✅ FIXED | all | p1 | the balloons photo now fills the yellow panel at Word's exact position (cell-attached rendering + the layoutInCell paragraph-anchor clamp, systemic #5; p1 error 0.63 → 0.14 per backend).
- MAJOR | all | p1 | oversized misplaced lightbulb photo drawn as a strip across the page bottom (clipped by the page edge) and covering the last line of the left-rail paragraph
- MAJOR | all | p2 | lightbulb photo missing from the top-left two-thirds; the yellow panel fills that area instead
- MEDIUM | all | p1,p2 | body text (lorem paragraphs, contact text, right-rail paragraphs) rendered visibly bolder/heavier than Word, with shifted wrap points
- MINOR | all | p1,p2 | text blocks (CONTACT US group, LOREM IPSUM columns, ABOUT US group) uniformly shifted ~10px
- MAJOR | html | - | ABOUT US section's yellow panel background missing, so its white quote block (large " mark + white lorem text) is invisible/absent in the export
- MEDIUM | html | - | body text renders bold where Word shows regular weight

### brochures/08

- MAJOR | all | p1,p2 | navy/blue duotone recolor lost on every photo (skyline, ceiling structure, bottom building band, wavy panel, grid building) — all rendered plain greyscale
- MAJOR | all | p1 | "Contoso Logo" white-framed box and text missing from the orange block at bottom-right
- MEDIUM | all | p1,p2 | thin heading rules missing: under "JOIN OUR TEAM" (p1), under "OUR STORY", under the "MAKE IT YOURS..." title, and the orange rule above the CONTACT US paragraph (p2)
- MINOR | all | p2 | numbered client list vertical spacing looser than Word (~50px vs ~38px between items)
- MAJOR | html | - | photos overlap text: bottom building photo covers the address block and the "OUR STORY" / "MAKE IT YOURS" / "CONTACT US" headings
- MAJOR | html | - | "MAKE IT YOURS" body paragraphs invisible (white text without the orange panel background) and numbered list items show bare "1. 2. 3." with no item text
- MAJOR | html | - | "Contoso Logo" framed box missing from the export
- MEDIUM | html | - | photos greyscale (navy duotone lost) in the export

### bullet_list

- MINOR | all | p1 | bullet-list line spacing slightly tighter than Word; items drift up cumulatively ~10px by the fourth bullet
- CLEAN: html

### business-plans/01

- MAJOR | all | p1 | dark vertical accent bar left of the "ONE PAGE BUSINESS PROPOSAL" title missing
- MAJOR | skia | p1 | missing-glyph tofu boxes rendered after "Contoso, Ltd." and "Casey Jensen" (not present in imagesharp/pdf)
- MEDIUM | all | p1 | vertical spacing collapsed: title sits ~0.4in higher and the contact rail + section columns ~1in higher than Word
- MEDIUM | all | p1 | body and contact text rendered bold where Word uses regular weight, shifting wrap points inside paragraphs
- MAJOR | html | - | dark vertical accent bar missing from the HTML export
- MEDIUM | html | - | body text renders bold where Word shows regular weight

### business-plans/02

- MAJOR | skia,imagesharp | p2,p3,p4,p5,p6,p7 | Page count 7 vs expected 6: extra ~0.5in gap between the header photo and EXECUTIVE SUMMARY on p2 pushes MARKET OPPORTUNITY off the page, every following page shows the prior Word page's sections, and EXIT STRATEGY/MILESTONES & ROADMAP/NEXT STEPS/contact block overflow onto an extra page 7 (PDF paginates correctly at 6).
- ✅ FIXED | all | p2,p3,p4,p5,p6 | Footer page numbers ("2".."6", bottom-left) now render on every body page — the footer was wrapped in a root-level `w:sdt` (page-number gallery) the header/footer parse skipped. Sits ~60px lower than Word (footer-distance positioning, separate). See systemic #1(c).
- ✅ FIXED | all | p1,p2,p3,p4,p5,p6 | Section-marker arrow glyph now matches Word's small thin chartreuse "→" (41×29px vs Word's 40×29px) — the arrow is a landscape PNG letterboxed in a tall frame via negative `a:srcRect` (`t="-9453" b="-175911"`), which `ReadCrop` used to clamp to 0, stretching it full-frame into an oversized bold chevron. See systemic #20. (The raster backends still draw it ~150px lower than Word on p2 — that's the page-count/extra-gap finding above, not the arrow.)
- MAJOR | pdf | p2 | Header wheat-field photo rendered sharp/high-contrast instead of Word's soft-focus/blurred picture treatment — bottom half of the photo is structurally different (solid diff block, hard=21.5%); Skia/ImageSharp match Word.
- MEDIUM | skia,imagesharp | p1 | Cover title word gaps collapsed — "SUNKEN VALLEY FARM BUSINESS PLAN" reads as "SUNKENVALLEYFARM BUSINESSPLAN" (verified in zoom), and the title block sits ~20px lower than Word.
- MINOR | skia,imagesharp | p1 | Footer contact block (rule line, JI-MIN AN, phone/site columns) shifted down ~0.2in as a unit.
- MINOR | pdf | p1,p2,p3,p4,p5,p6 | Uniform small downward drift (~10px) of body content producing ghost doubling of text and table rules.
- MEDIUM | html | - | Hero wheat photo shows the sharp un-blurred original, missing Word's soft-focus treatment.
- MEDIUM | html | - | Financial projection values ($750,000 / $1.2 MILLION / $2.0 MILLION) rendered in italic; Word renders them upright.

### business-plans/03

- MAJOR | all | p1 | "Prepared by" address block sits ~1in lower than Word: final line "Frankfort, KY 09876" is pushed off-page and lost, and "2345 Creek Road" is half-clipped at the bottom page edge in Skia, ImageSharp, and PDF (verified in zoom of all three originals).
- MEDIUM | all | p1 | Right-column sections (SUMMARY through FINANCIAL SUMMARY) drift progressively lower, ending ~0.3-0.5in below Word's positions, with re-wrapped line breaks (same line counts).
- CLEAN: html

### business-plans/04

- MEDIUM | all | p1 | Title "ONE PAGE PROPOSAL" rendered in a visibly lighter/regular serif weight and slightly wider instead of Word's heavy bold display weight (HTML export shows the correct bold, confirming the source asks for it).
- MEDIUM | all | p1 | Content below the intro drifts down cumulatively ~0.3-0.5in: 2x2 section-grid rows taller with inner borders misaligned vs Word, Prepared for/Prepared by row and outer frame bottom sit ~0.4in lower.
- CLEAN: html

### business-plans/05

- MEDIUM | skia,imagesharp | p1 | Body content progressively compressed upward — section headings and the PREPARED BY/PREPARED FOR blocks end up to ~0.5in higher than Word (line spacing too tight for the display serif), with word-level rewraps.
- MINOR | skia,imagesharp | p1 | Irregular doubled inter-word gaps in body paragraphs (e.g. "designed to  improve", "Using  best  practices" in SOLUTION) where Word shows single spaces; PDF is normal.
- MINOR | pdf | p1 | Full-page cream background tint minutely off (em=0.99 — nearly every pixel faintly differs, invisible side-by-side).
- MINOR | pdf | p1 | Small downward drift (~10px) doubling the divider rules and footer block positions.
- MINOR | html | - | Same doubled inter-word gaps as the raster backends (e.g. "Using  best  practices", "scalable  solutions"); Word shows single spaces.

### business-plans/06

- MEDIUM | all | p1,p2 | Document-wide bold loss: cover title "CLIENT PROPOSAL", "Prepared for:/by:" labels, grey numerals 01-05 and yellow section headings all render regular/light instead of Word's heavy bold (title ink 25-50% lower)
- MINOR | all | p1 | title block sits ~20-30px lower than Word
- MINOR | skia,imagesharp | p1 | contact block ~40px lower than Word (PDF matches Word's position)
- MINOR | all | p2 | whole section stack shifted down uniformly ~40-50px; PROBLEM STATEMENT paragraph breaks lines at different words (same line count)
- MEDIUM | html | - | yellow cover background terminates mid-section 01, slicing through the SUMMARY paragraph (first lines on yellow, last on white)
- MEDIUM | html | - | title "CLIENT PROPOSAL" and numerals 01-05 render light instead of Word's heavy bold (same bold-loss as rasters)
- MINOR | html | - | "Prepared for:/by:" row starts immediately under "PROPOSAL" with no gap (crowded vs Word's clear spacing)

### business-plans/07

- ✅ FIXED | pdf | p1 | title "BUSINESS PROPOSAL" resolves to its light weight via weight scoring instead of a heavy bold substitute (see systemic #22); the ~40px vertical offset remains
- MINOR | skia,imagesharp | p1 | title rendered ~15% narrower than Word (condensed glyph widths, x-extent 766 vs 871)
- MINOR | all | p1 | intro paragraph, four section blocks and footer contacts sit 30-50px lower than Word (footer band itself correctly placed)
- MINOR | all | p1 | "PREPARED FOR:/BY:" labels render bold vs Word's regular caps (ink +27-42%)
- MEDIUM | html | - | pale-green footer band starts mid-contact-block (labels and first contact lines sit above/outside it) instead of enclosing the whole PREPARED section, and stops at content width
- MINOR | html | - | title line spacing collapsed so "PROPOSAL" caps touch the "BUSINESS" baseline
- MINOR | html | - | "PREPARED FOR:/BY:" labels bold vs Word regular

### business-plans/08

- MAJOR | all | p1 | accent line drawn as a vertical line hanging from the top edge (x~389, ~570px long) instead of Word's horizontal line at upper-left (y~178, left edge to ~79% width) — orientation/position rotated 90 degrees
- MAJOR | pdf | p1 | last contact lines "Seattle, WA 89101" / "Santa Fe, NM 11121" pushed past the page bottom and entirely absent
- MAJOR | skia,imagesharp | p1 | "Seattle, WA 89101" / "Santa Fe, NM 11121" clipped mid-glyph at the page bottom edge (contact-block line spacing inflated ~38% pushes them into the margin)
- MEDIUM | skia,imagesharp | p1 | title "Business proposal" shifted right ~75px and down ~30-55px (Word has it flush at left margin)
- MEDIUM | pdf | p1 | second title line "proposal" indented ~75px right of "Business" (Word has both lines flush left); title also ~55px low
- MEDIUM | all | p2 | body paragraphs render bold vs Word's regular green text, changing intra-paragraph line-break positions
- MINOR | all | p2 | section stack drifts upward, ~80px high by section 5
- MAJOR | html | - | sections 1 (Summary) and 2 (Problem Statement) unreadable: green heading/body text rendered on the green cover background (only the "1."/"2." markers visible)
- MAJOR | html | - | title lines overlap — "proposal" glyphs collide with "Business" and the line is indented right
- MAJOR | html | - | top accent line missing (only a stray dot remains near its start position)
- MEDIUM | html | - | body paragraphs bold vs Word regular
- MINOR | html | - | list numbers "3./4./5." rendered tiny beside large section headings (Word renders number and heading at the same size)

### business-plans/09

- ✅ FIXED | all | p2,p3,p4 | every table now numbers 1-5 like Word — counters keyed by abstract num with w:startOverride reset at first use, and the PDF now draws the markers on the run-less No.-column paragraphs. See systemic #11.
- MAJOR | pdf | p2,p3,p4 | table row numbers missing entirely — every "No."/"Step" cell is blank
- MEDIUM | all | p2,p3 | footer page number now increments per page (was stuck at "3") but shows the physical page number, not Word's section-restarted 1 (p2) / 2 (p3) — section restart still unwired [systemic #1 residual (a)]
- MAJOR | all | p4 | "PUT THE PLAN INTO ACTION" table broken: header row ("Step/Action/Due date/% complete") plus the Action and Due-date columns missing; only a collapsed two-column Step+"%" fragment renders at top-left
- MEDIUM | all | p3 | "PUT THE PLAN INTO ACTION" heading pulled onto the bottom of p3 (Word starts the section on p4)
- MEDIUM | all | p1 | cover title "TARGET AUDIENCE PROFILING PLAN" and "INTERNAL DOCUMENT" render bold vs Word's light weight (ink +18% ImageSharp, +38-39% Skia/PDF)
- MEDIUM | skia | p2 | heading "QUESTIONS TO NARROW DOWN YOUR TARGET AUDIENCE" wraps to two lines (single line in Word, ImageSharp and PDF)
- MINOR | skia,imagesharp | p1,p2,p3 | headings show doubled word gaps ("INTERNAL  DOCUMENT", "DEVELOP  A PLAN", "TEST  THE  PLAN")
- MINOR | all | p2,p3 | footer sits ~68px lower than Word
- ✅ FIXED | html | - | cross-table numbering restarts per table (abstract-keyed counters, systemic #11).
- MAJOR | html | - | final "PUT THE PLAN INTO ACTION" table reduced to a Step+"%" fragment (header row, Action and Due-date columns missing)
- MEDIUM | html | - | blue cover background extends into the body and cuts through the QUESTIONS FOR CONSUMERS table
- MEDIUM | html | - | cover title bold vs Word's light weight

### business-plans/10

- MAJOR | skia,imagesharp | p3,p4,p5 | Page count 4 vs expected 5: "List/Define all pertinent items" pulled from p4 up onto p3, and Word's entire p5 (CAMPAIGN SIGN-OFF heading, intro line, 9-row signature table, trailing note) is merged onto p4 (Word leaves the lower half of p4 empty), so p5 is missing
- MAJOR | pdf | p3,p4,p5 | Pagination distribution wrong despite matching page count: bullet "List all pertinent items." pulled onto p3, CAMPAIGN SIGN-OFF heading plus all 9 signature rows render on p4 (Word puts the whole sign-off section on p5 and leaves p4's lower half empty), leaving p5 with only the trailing "Note: Additional signatures..." paragraph
- MAJOR | pdf | p3,p4 | List bullets render as a tiny middle-dot instead of Word's solid round bullet ("· List all pertinent items." / "· List all metrics and expectations.")
- MEDIUM | all | p2,p3 | Italic paragraphs render upright: "Use the Tactical Marketing Plan...", "In this section, you need to define...", "Use this section to brainstorm...", and the BUDGET paragraph "Compile a list of pertinent items..." all lose their italic (HTML export keeps it)
- MEDIUM | all | p2,p3,p4 | Table grid incomplete: header row and first body row of each table (PLAN OVERVIEW, NECESSARY EVENT RESOURCES, APPROVAL) render with no outer box or vertical cell borders — only the horizontal rule under the header — while Word draws a full grid on every row
- MEDIUM | all | p2,p3,p4 | Table-style bold lost: header-row text ("Practice:"/"Name", "Resource"/"Role"/"Estimated Work Hours", "Title"/"Name"/"Date 1"/"Date 2") and first-column labels ("Name of Campaign:", "Campaign Manager:", "Subject Matter Expert:") render regular weight instead of bold
- MEDIUM | pdf | p1 | Cover subtitle "ADVANCING INTERNATIONAL STRATEGIES" sits almost touching the title — title block ~20px lower and the ~40px gap before the subtitle collapses to ~10px
- MINOR | skia,imagesharp | p1,p2,p3,p4 | Letter-spaced headings render with doubled word gaps ("MARKETING  PLAN" on the cover, "PLAN  OVERVIEW", "TARGET  MARKET", "CALL  TO  ACTION", "CAMPAIGN  SIGN-OFF")
- MINOR | pdf | p2,p3,p4 | Headings render with visibly tighter letter-spacing than Word (e.g. "PLAN OVERVIEW" ~25% narrower)
- MINOR | imagesharp | p2,p3 | Mixed typefaces inside paragraphs: scattered words ("to", "your", "or", "process") render in a different substitute font than the rest of the sentence
- MINOR | all | p2,p3,p4 | Footer page number positioned ~40px lower on the page than Word's
- MAJOR | html | - | Cover date line "April 4, 20XX" missing — only "Version 3.0" renders above the title block
- MEDIUM | html | - | Same table defects as raster backends: header row and first body row of each table lack box/vertical borders, and header/first-column bold is lost

### business-plans/12

- MAJOR | all | p3,p4,p5,p6,p7,p8,p9,p10,p11,p12,p13,p14,p15,p16,p17,p18 | footer page number missing on every content page (Word shows "3".."18" bottom-right; all three backends render nothing there)
- MAJOR | all | p2 | TOC page numbers absent from every entry (Word right-aligns 3,4,5,6,8,10,11,12,15,16,18) and the dot leaders run all the way to the right margin instead of stopping at a number
- MEDIUM | all | p2 | TOC entries rendered underlined (hyperlink style; Word shows no underline) with wider entry spacing so the list ends ~0.3in lower
- MEDIUM | all | p2 | "TABLE OF CONTENTS" heading rendered gray instead of black and the thick black rule below the heading is missing
- ✅ FIXED | all | p2,p3 + section headings | the down-right section-marker arrows now assemble into Word's clean solid ↘ — group-transform-scaled stroke widths + square caps on the PDF group-line pen (see systemic #6c)
- MAJOR | pdf | p1,p2 | photos drawn with wrong crop/zoom inside their frames — cover taxi photo is zoomed-in/panned versus Word and the p2 full-height airplane sidebar photo shows a completely different framing (source-crop rectangle ignored)
- MAJOR | skia,imagesharp | p1 | cover text block pushed ~1in down: colored-arrows logo sits clipped at the bottom page edge and "First Up Consultants" is pushed off-page entirely (missing)
- MEDIUM | pdf | p1 | cover title block sits ~0.6in lower than Word and "First Up Consultants" wraps onto two lines ("First Up" / "Consultants")
- MAJOR | all | p9 | SWOT four-color donut-ring graphic with center "SWOT" label completely missing (the four lists render, the chart does not)
- MINOR | all | p9 | SWOT list bullet markers lose their category colors (red/orange/green/blue in Word; gray in all backends; PDF also draws them noticeably smaller)
- MEDIUM | skia,imagesharp | p4,p5,p6,p8,p10,p11,p12 | numbered section headings lose the tab gap after the number — rendered run-together as "1.EXECUTIVE SUMMARY" (Word: "1.   EXECUTIVE SUMMARY")
- MEDIUM | pdf | p4,p5,p6,p8,p10,p11,p12 | heading number rendered at roughly half the heading's size (tiny "1." before "EXECUTIVE SUMMARY")
- ✅ FIXED | pdf | p2,p4,p5,p6,p8,p9,p10,p11,p12,p14,p15,p16,p17,p18 | bold runs and headings now resolve to the correct 700-weight faces (see systemic #22)
- MINOR | pdf | p3,p4,p5,p6,p8,p9,p10,p11,p12,p14,p16,p18 | body text measures slightly wider, so many paragraphs break lines one word earlier than Word (line counts mostly unchanged)
- MEDIUM | skia,imagesharp | p3,p4,p5,p6,p8,p9,p10,p11,p12,p16,p18 | wrapped continuation lines of bulleted paragraphs indented ~3 characters deeper than Word, shifting wrap points and adding an extra line to several bullets
- MEDIUM | skia,imagesharp | p6,p7 | tighter list spacing pulls the last two lines of the "Note the difference…" sub-bullet ("law practice … various billing rates.") from page 7 back onto page 6, so page 7 starts at a different point than Word
- MINOR | all | p3,p4,p5,p6,p8,p9,p10,p11,p12,p16,p18 | vertical spacing slightly tighter than Word — content position drifts up to ~1 line higher by page bottom on bullet-heavy pages
- MEDIUM | all | p14 | extra blank table column inserted between the JUN and JUL columns of the profit-and-loss table (Word shows 13 contiguous month columns), compressing the other columns
- MEDIUM | all | p17 | row-label bolding inverted in the blank P&L appendix table — Word bolds the input rows (Estimated Product Sales, Less Sales Returns & Discounts, Service Revenue, Other Revenue, etc.) and leaves computed rows normal; all backends bold Net Sales/Cost of Goods Sold/Gross Profit/Total Expenses/Income Before Taxes/Income Tax Expense instead ("Office-Based Agency" also loses bold)
- MEDIUM | pdf | p15 | table header cells "COST/ MONTH", "ONE-TIME COST" and "TOTAL COST" wrap to two lines (single line in Word)
- MINOR | skia | p15 | table header cell "TOTAL COST" wraps to two lines (single line in Word)
- MINOR | all | p13,p15,p17 | appendix/start-up tables render with slightly shorter rows, ending up to ~1.5 rows higher than Word
- MAJOR | html | - | SWOT donut-ring graphic missing — only the four STRENGTHS/WEAKNESSES/OPPORTUNITIES/THREATS lists render
- MEDIUM | html | - | blank P&L appendix table has the same inverted bolding as the raster backends (computed rows bold, input rows normal — opposite of Word)
- MINOR | html | - | SWOT list bullets black instead of their category colors
- MINOR | html | - | numbered section headings render the number much smaller than the heading text (tiny "3." before "BUSINESS DESCRIPTION")

### business-plans/13

- MAJOR | all | - | Page-count mismatch: skia 23 and imagesharp 23 pages, pdf 22 pages vs Word 21 — the table of contents does not fit beside the photo on p2 and spills onto its own page, shifting all subsequent content one page late (skia/imagesharp add a second extra page when the "SWOT analysis" bullet and quadrant table split around p10-p11)
- MAJOR | all | p2,p3 | TOC entries lose their right-aligned page numbers (3-21) and render as underlined hyperlink-styled lines with much larger line spacing than Word's plain two-column TOC
- MAJOR | all | p1 | Cover's full-width light-grey band behind "Small Business Plan" / "SAND + POLISH CONTRACTORS" is missing — title block sits on plain white
- MEDIUM | all | p1 | Cover renders a footer ("SAND + POLISH CONTRACTORS" + page number "2") that Word suppresses on the first page
- MINOR | all | p1-p21 | Footer PAGE number now increments per page (was frozen at "4"); the remaining gap vs Word's 2-21 is the +2 page-count divergence [systemic #2], not the field
- MEDIUM | pdf | p1,p2 | Cover and TOC-page photos rendered at noticeably larger zoom/different crop than Word (building and arm enlarged, cover title pushed ~0.5 in lower)
- MINOR | skia,imagesharp | p1 | Cover title and subtitle sit ~0.3-0.5 in lower than Word although the photo bottom edge matches
- MEDIUM | skia | p5-p23 | Skia drops run-level bold throughout the body: lead-ins "Opportunity:", "Company summary:", "Order fulfillment:", "Pricing:", "SWOT analysis:", "Step 1-4", "The Executive Summary should be written last", "Projected start-up costs" all render regular weight (imagesharp and pdf render them bold)
- MAJOR | skia,imagesharp | p11 | SWOT quadrant table rows overlap: "OPPORTUNITIES" heading overprints the "Price, value, quality" bullet of the STRENGTHS list (pdf renders the gap correctly on its p10)
- MAJOR | all | p16,p17,p18 | Landscape P&L table's JUL column too narrow — values clipped mid-glyph ($22,500 → "$22,5(", $22,850 → "$22,8", $13,850 → "$13,8", $8,000 → "$8,00(", $12,100 → "$12,1(", $1,125 truncated) in skia/imagesharp p17-p18 and pdf p16
- MEDIUM | all | p16,p17 | P&L row label "Marketing/Advertising" does not wrap and collides with the JAN value "$400"
- MAJOR | skia,imagesharp | p17,p18,p19 | Fixed row heights clip wrapped second lines of table cells: "& Discounts", "Sold*", "Taxes", "Expense" cut off/overlapped by the next row, and blank-table headers "COST/ MONTH" / "ONE-TIME COST" lose their second line (pdf shows all of these fully)
- MINOR | all | p2-p21 | Expanded character letter-spacing dropped in header "SMALL BUSINESS PLAN", footer "SAND + POLISH CONTRACTORS", and section headings — skia/imagesharp substitute wide word gaps, pdf collapses spacing entirely
- MEDIUM | html | - | Cover's grey title-band background missing in the HTML export (title/subtitle on plain white); all other content, images, tables, and TOC page numbers are intact

### business-plans/15

- MEDIUM | all | p2-p19 | Footer page-number field now increments per page (was constant "17") but shows the physical page number, not Word's section-restarted 1-17 starting at the Executive summary page — section restart still unwired [systemic #1 residual (a)]
- MAJOR | all | p2-p18 | TOC line spacing far looser than Word: the TOC that fits on Word's single p2 overflows onto an extra page (p3), so every body page lands one page later than Word until the backends re-sync on p19 by packing Break-even analysis and Miscellaneous documents onto one page.
- MAJOR | all | p4-p10,p19 | Bold side-headings are drawn on the same baseline as adjacent body text, overlapping glyphs — e.g. "Highlights" over "Note: to delete any tip…" (p4), "Location" over "franchise, or an expansion…" (p5), "Interior", "Hours of operation", "Suppliers", "Management", "Fiscal management", "Start-up/acquisition summary", "Market segmentation", "Competition", "Pricing", "Advertising and promotion", "Strategy and implementation" (p5-p10), and on p19 the "Miscellaneous documents" heading plus its lead paragraph print through the break-even "Note: If the sales dollars…" line.
- MAJOR | all | p17 | Body text overruns the bottom margin: the "Taxes Payable…sheet." line prints through the footer text and the final "Payroll Accrual—Salaries and wages…" bullet is clipped at the page bottom edge.
- MAJOR | skia,imagesharp | p19 | Last bullet "Photographs" collides with the footer line at the page bottom.
- MEDIUM | all | p11-p18 | All-caps formatting lost in table headers: "START-UP EXPENSES"→"Start-up expenses" (p11), "MONTH 1-12"→"Month 1-12" (p12-p13), "IND. %/JAN.-DEC./ANN. TOT./ANNUAL %"→"Ind. %/Jan.…" (p15), "STARTING MONTH, YEAR—…/BUDGET/AMOUNT OVER BUDGET"→mixed case (p16), "ASSETS/LIABILITIES"→"Assets/Liabilities" (p18).
- MEDIUM | skia,imagesharp | p2,p4 | Word spaces in large display headings collapse to near zero — "Tableof contents", "Executivesummary", "Descriptionof business" (PDF renders these spaces correctly).
- MEDIUM | all | p1 | Cover title block indented ~0.9" further right than Word so "Business Plan" wraps onto two lines (3-line title vs Word's 2), pushing the divider rule and "Caneiro Group" down; the Email/Phone/Address block sits ~0.9" lower than Word.
- MEDIUM | all | p2,p3 | Footer bar ("BUSINESS PLAN | APRIL 25, 20XX" + number) is drawn on the TOC pages where Word suppresses the footer entirely.
- MINOR | all | p1 | Title underline rule spans only margin-to-margin instead of extending to the page's left edge as in Word.
- MINOR | all | p3-p19 | Footer line sits ~0.2" lower on the page than Word's footer position on every page.
- MEDIUM | html | - | TOC top-level sections are all numbered "1." ("1. Executive summary", "1. Description of business", "1. Marketing", "1. Appendix") instead of Word's sequential I./II./III./IV.
- MEDIUM | html | - | Table headers lose all-caps formatting ("Start-up expenses", "Month 1…", "Ind. %/Jan.…", "Starting month, year—…", "Assets/Liabilities" vs Word's "START-UP EXPENSES", "MONTH 1", "IND. %", "ASSETS/LIABILITIES").

### business/01

- MEDIUM | all | p1 | memo header table columns too narrow — "Holiday closure" wraps to two lines under RE and the COMMENTS paragraph wraps to 3 lines vs Word's 2
- MEDIUM | pdf | p1 | "Ayano Harada" in the FROM field additionally wraps onto two lines (PDF only)
- MEDIUM | all | p1 | footer block (CANEIRO GROUP, Tel/Fax, black rule) indented ~125px right of Word's left-margin position
- MINOR | all | p1 | date "05.26.2023" and its underline rendered ~20px higher than Word
- MINOR | imagesharp | p1 | comments text breaks between "May 29" and superscript "th", stranding the ordinal suffix at the start of the next line
- CLEAN: html

### business/02

- MEDIUM | all | p1 | COMMENTS label + paragraph row sits ~28px higher than Word (gap below the divider rule collapsed)
- MINOR | pdf | p1 | To/From/CC/Date/Re rows spaced slightly wider, drifting ~12px lower by the "Re:" row
- MINOR | all | p1 | beige panel edges inset ~5-7px, losing Word's left-edge bleed
- MEDIUM | html | - | COMPANY NAME heading renders on white above the beige panel instead of inside it (shaded background starts too low)
- MINOR | html | - | thin divider rule between the header fields and COMMENTS is missing

### business/03

- MEDIUM | skia,pdf | p1 | cover photo and Report Title box shifted ~25px right, photo bleeding to the right page edge (Word keeps a white margin)
- MEDIUM | all | p1 | cover overlay box geometry off: white Company Name box ~40px narrower and navy Report Title box ~35px wider than Word, both shifted up ~15-25px
- MEDIUM | all | p2 | middle text column and top-right sample block start ~57px further left and are wider than Word, changing wrap points (sample block wraps 4 lines vs Word's 5)
- MINOR | all | p1,p2 | page content sits ~20-25px higher than Word while the footer page number sits ~25px lower
- MINOR | imagesharp | p1 | third body paragraph wraps at different words (lines end "...quodsi docendi." / "...Malis") though line count matches
- MEDIUM | html | - | cover collage flattened: Company Name and Report Title boxes render stacked below the photo instead of overlapping it
- MINOR | html | - | "Report Title" in the navy box renders double-struck/heavier than Word's light-weight title

### business/04

- MAJOR | all | p1 | decorative graphics absent: top banner bar, watercolor circle with Contoso logo, and bottom-left watercolor blob all missing (PDF draws only a barely-visible ghost of the circle/logo)
- MINOR | all | p1 | footer address block ~25px higher than Word
- MAJOR | html | - | same decorative graphics (banner bar, Contoso circle, watercolor blob) missing from the HTML export
- MINOR | html | - | footer address left-aligned instead of centered

### business/05

- ✅ FIXED | all | p1 | both corner staircase graphics now render at Word's exact positions and sizes (cell-anchored group, systemic #5) — and in Word's orientation: their 180°-rotated nested groups drew mirrored until nested-group rotation composition landed (0.12→0.05 per backend).
- MAJOR | html | - | same corner graphics missing in HTML
- MINOR | html | - | footer address left-aligned instead of Word's centered-right placement

### business/06

- MAJOR | all | p1 | header ribbon spans the full page width behind the LOGO box (Word has it on the right half only, LOGO on white) and the angled wedge pattern is duplicated/tiled on both top and bottom ribbons
- MEDIUM | all | p1 | footer address block ~55-60px higher than Word
- MINOR | skia,imagesharp | p1 | body block (Memo heading + paragraphs) ~25px higher than Word
- MAJOR | html | - | "Memo" heading overlapped and obscured by the banner ribbon graphic
- MEDIUM | html | - | banner ribbon drawn as two overlapping/offset copies (same duplication defect as raster)
- MINOR | html | - | LOGO placeholder rendered as bare text without its outlined box

### cards/01

- MEDIUM | all | p1 | small gift icons on the left card halves placed ~120px too far left and ~35-70px too high vs Word
- ✅ FIXED | all | p1 | "Celebrate!" captions now centre beneath the green panels at Word's x-position (pPrDefault w:jc, systemic #9).
- MEDIUM | all | p1 | bottom card's green panel and caption sit ~45px higher than Word, shrinking the fold gap between the two card faces
- MINOR | all | p1 | green gift panels offset ~8px up and ~5px right with slightly different size than Word
- MEDIUM | all | p2 | inside placeholder text box ~40px narrower than Word — text wraps to 6 lines vs 5 (✅ now centred, systemic #9; the narrow measure remains)
- ✅ FIXED | html | - | "Celebrate!" captions and placeholder paragraphs now export centred (systemic #9).

### cards/02

- MAJOR | all | p1 | cherry-blossom sketch drawn behind the card/frame so both photo frames are empty — only the branch tip that extends past the card edge is visible at the far left of the page
- MAJOR | all | p1 | scroll-banner picture missing from both tickets; its three stars render as three orange squares, and on the lower ticket the squares sit at the ticket's top edge instead of mid-ticket. PARTIALLY improved by nested-group rotation composition (the scroll+stars sub-groups are rotated 348° and now tilt together as a unit, 0.22→0.12 per backend); the scroll art itself still diverges
- MAJOR | all | p1 | orange rounded ticket borders, orange photo-frame inner borders and notched ticket/frame outlines all missing — tickets and frames drawn as plain sharp-cornered rectangles, ticket background rectangle shifted up-left and oversized
- MAJOR | all | p1 | "150220YY" rendered twice per ticket (once at ticket left edge, once below the box) while the white code box is empty and offset right — Word shows a single code centred inside the box
- MEDIUM | all | p1 | dotted tear-off separator line above the code box missing
- MAJOR | skia,imagesharp | p1 | inter-word spaces collapsed: "ADMIT ONE" renders as "ADMITONE" and "Keep ticket stub" as "Keepticketstub"
- MEDIUM | pdf | p1,p2 | text rendered bold where Word uses regular weight ("Keep ticket stub" on p1, card-back placeholder paragraph on p2, which also changes its line wraps)
- MEDIUM | all | p2 | thin notched border outline around both ticket backs completely missing
- MEDIUM | all | p2 | ticket-back placeholder text left-aligned instead of centred, and the text block plus thumbs-up hand sketch sit ~0.5in higher than Word
- MEDIUM | imagesharp | p2 | placeholder text wraps at different words than Word ("just" pulled up to the first line)
- MINOR | all | p2 | polka-dot background pattern misaligned — dots at visibly different positions across both card backs
- MAJOR | html | - | blossom and scroll images not placed in their frames/tickets — stacked at the top-left of the export; both photo frames empty
- MAJOR | html | - | ticket content displaced: first ticket block contains only the three orange squares, the ADMIT ONE / Keep ticket stub / code texts fall below or outside their grey ticket blocks, an extra duplicate "150220YY" pair appears at top-left, and an extra third copy of the placeholder paragraph appears at the bottom
- MAJOR | html | - | stars render as orange squares (scroll images blank) and all orange ticket/frame borders are missing

### cards/03

- MEDIUM | all | p1 | yellow corkscrew streamer at top-right rendered horizontally mirrored (curl at top-left, tail sweeping to bottom-right; Word has curl top-right, tail bottom-left)
- MINOR | all | p1 | whole composition slightly offset (title ~5-8px lower, gift illustration shifted a few px), visible as ghost outlines across every shape in the diff
- MEDIUM | html | - | same horizontally mirrored yellow streamer in the HTML export

### cards/04

- MAJOR | all | p1 | extra bare-tree sketch (with red apple at its base) rendered at top-right that does not appear in Word's output
- MAJOR | all | p1 | extra second flock of birds rendered at bottom-left that does not appear in Word's output
- MAJOR | all | p1 | small green leaf beside "From…" missing
- MEDIUM | all | p1 | "Thinking of You…" and "From…" captions rendered ~50px (~2 line heights) higher than Word — caption sits above the ground line instead of below it
- MAJOR | html | - | red berry dots detached from the tree, scattered as a loose cloud at mid-left over the extra bird flock, leaving the berry tree's canopy bare
- MAJOR | html | - | extra bare tree and extra bird flock also present, and the green leaf beside "From…" is missing

### cards/05

- ✅ FIXED | all | p1,p3,p5,p7 | season captions ("Autumn"/"Spring"/"Summer"/"Winter") now centre under the pictures (pPrDefault w:jc, systemic #9).
- ✅ FIXED | all | p2,p4,p6,p8 | placeholder paragraphs now centre — the short last line sits at Word's centred position (systemic #9).
- MEDIUM | all | p1,p2,p3,p4,p5,p6,p7,p8 | light-gray fold guide lines (vertical rule at page center x≈825 and horizontal rule at page center y≈637) missing on every page
- MINOR | all | p1,p2,p3,p4,p5,p6,p7,p8 | whole card content block (picture+caption on odd pages, placeholder text on even pages) drawn ~10-15px (~0.1in) higher than Word; picture content/framing itself is faithful
- MEDIUM | html | - | center alignment lost in export: season captions and placeholder paragraphs all left-aligned (content, images and ordering otherwise complete)

### cards/06

- MAJOR | all | p1 | top card draws the 5-candle group twice — an extra oversized duplicate offset up/left produces 10 overlapping candles instead of 5, with the tallest flame cut off at the card's top edge
- MEDIUM | all | p1 | candle artwork slightly oversized and not clipped to the card: candle bases spill up to ~0.25in below the teal card rectangle onto the white page (both cards, worst on bottom card; Word ends them exactly at the card edge)
- MEDIUM | all | p2 | thin teal vertical divider rule (x≈688, spanning both cards) between invitation text and recipient-address block missing on both cards
- MEDIUM | all | p2 | invitation text blocks drawn too high with slightly tighter line spacing — offset grows from ~0.35in (top card first line) to ~0.75in (bottom card last line) above Word's positions
- MEDIUM | all | p1,p2 | dashed light-gray crop/fold guide lines at the card boundaries missing on both pages
- MAJOR | html | - | "It's a Birthday Party!" heading drawn across/overlapping the candle artwork on both cards (Word places it in its own column to the right of the candles)
- MAJOR | html | - | first card shows the duplicated 10-candle set (5 in Word)
- MEDIUM | html | - | second card's candle image overflows below the teal card area and overlaps the following "Your Name/Address" text block
- MEDIUM | html | - | teal divider rule missing on both invitation backs

### cards/07

- MEDIUM | all | p1 | "Celebrate!" captions ✅ now centre under the pictures (systemic #9); card 2's caption still sits ~90px higher
- MEDIUM | all | p1 | second card's teal picture block placed ~80px higher than Word (inter-card gap collapses from ~170px to ~100px); first card's picture starts 8-22px higher and both pictures are ~12px (2%) wider
- MEDIUM | all | p1 | small bunting clipart on the card backs (left half) placed ~235px (1.6in) further left and 70-150px higher than Word on both cards
- MEDIUM | all | p2 | placeholder paragraph ✅ now centres (systemic #9) but still wraps into 6 lines in a narrower measure vs Word's 5, on both cards
- CLEAN: html

### cards/08

- MINOR | all | p1,p3 | watercolor card-face photos shifted vertically as a whole (Skia/ImageSharp ~17px up, PDF ~5px down); size and content otherwise match Word
- MEDIUM | all | p2,p4 | placeholder message rendered in a heavy bold rounded typeface instead of Word's thin light face, flush-left instead of centered, wrapping 2 lines into 3 (both cards)
- MEDIUM | html | - | placeholder message shows the same wrong heavy bold typeface and left alignment (Word uses thin light centered text)

### cards/09

- ✅ FIXED | all | p1 | green card bodies sat rotated 45° into overlapping diamonds — each card group carries child `@rot` that a dropped nested-group `rot="2700000"` was meant to cancel; with nested rotation composed they sit square in Word's neat 5×2 grid, triangles in place (nested-group rotation composition, systemic #5; 0.22→0.05 per backend)
- MEDIUM | all | p1 | blue/grey corner triangle accents render close to Word but with small residual size/position differences at some card edges
- MEDIUM | all | p1 | card text blocks drift progressively upward down the sheet (up to ~45px by row 5) and the white name-underline rule ends up striking through "Seattle, WA 54321" on lower cards
- MAJOR | html | - | green card shapes displaced to the right of their text so the white name/address text sits on white background and is largely unreadable

### cards/10

- MINOR | skia,imagesharp | p1 | "THank You" artwork drawn ~13px higher than Word (x-position and size exact; PDF matches Word)
- CLEAN: pdf, html

### cards/11

- MEDIUM | all | p1 | "Celebrate!" captions ✅ now centre under the pictures (systemic #9); card 2's caption still sits ~85px higher
- MEDIUM | all | p1 | second card's balloon picture block placed ~85px higher than Word (inter-card gap collapses from ~167px to ~100px); first card's picture starts 8-22px higher and both pictures are ~12px (2%) wider
- MEDIUM | all | p1 | small balloon clipart on the card backs (left half) placed ~240px (1.6in) further left and 70-150px higher than Word on both cards
- MEDIUM | all | p2 | placeholder paragraph wraps into 6 flush-left lines in a narrower measure instead of Word's 5 centered lines, on both cards
- CLEAN: html

### cards/12

- MEDIUM | all | p1,p2 | "THANK YOU" and the pink message ✅ now centre (systemic #9); both blocks still sit ~20px higher than Word
- MEDIUM | all | p1,p2 | Horizontal dashed fold-guide line across mid-page (y=824) missing in all backends (the vertical divider line at x=637 does render)
- MEDIUM | html | - | Both card-art images are hoisted out of the table to the top of the document, so the THANK YOU headings and pink messages render separately after/without their card backgrounds
- MINOR | html | - | Fold/cut guide borders (vertical divider, dashed mid-page line) not exported
- ✅ FIXED | html | - | THANK YOU headings and messages now centre in their cells (cell alignment on the td, systemic #9).

### cards/13

- MAJOR | all | p1 | Thin white outline borders of all 10 business cards missing, leaving card boundaries invisible on the sheet
- MEDIUM | all | p1 | Text grid vertically compressed (row pitch ~285px vs Word ~305px) while the white banners/squares stay at Word's positions: text drifts progressively up to ~110px by the bottom row, so titles float above their white banner and the banner instead overlaps the name/address lines
- MEDIUM | html | - | White title banners render as separate blocks overlapping the contact lines (plus one stray empty banner at the very top of the page) instead of sitting behind the "Fabrikam, Inc." titles
- MEDIUM | html | - | Card outline borders missing, cards blend into the blue background

### cards/15

- MEDIUM | all | p1 | Bottom-half card graphic shifted up ~85px (teal square top at y=739-742 vs Word 825, "Celebrate!" caption follows); top square also up 22px (skia,imagesharp) / 8px (pdf)
- ✅ FIXED | all | p1 | "Celebrate!" captions now centre beneath the squares (systemic #9), both cards.
- MEDIUM | all | p1 | Small cake icon in the left column displaced ~236px left (x=73 vs Word 309) and ~70px (top) / ~134px (bottom) up, sitting near the page's left edge
- MEDIUM | all | p1,p2 | Fold/cut guide lines (horizontal dashed line at mid-page y=824 and vertical divider at x=637) missing entirely
- MEDIUM | all | p2 | Placeholder paragraph wraps in a ~80px-narrower box producing 6 left-aligned lines vs Word's 5 centered lines (wrap width 272px vs 351px); block also ~25px higher
- MINOR | all | p1 | Teal squares rendered ~12px wider than Word (right edge x=1248 vs 1236)
- MINOR | html | - | "Celebrate!" captions and placeholder paragraphs left-aligned instead of centered
- MINOR | html | - | Fold/cut guide borders not exported

### cards/16

- MEDIUM | pdf | p1,p3,p5 | right-panel clip-art illustration (santa+elf / santa+penguin / kid+snowman) rendered ~17% narrower than Word at correct height (aspect-preserved instead of stretched: e.g. suit width 228px vs 272px on p1), on both card halves — figures look visibly slimmer
- MINOR | skia,imagesharp | p1,p3,p5 | top-card illustration placed ~12-13px higher than Word (bottom-card copy is correctly placed)
- MINOR | skia,imagesharp | p1,p3,p5 | bottom-card "Merry Christmas" heading sits lower than Word (~18px skia, ~10px imagesharp; top-card heading ~6px low in skia only)
- MINOR | skia,pdf | p1,p3,p5 | 1px lightened fold rule across the page middle (y≈825) missing (present in Word's render and reproduced by ImageSharp)
- ✅ FIXED | html | - | "Merry Christmas" headings and the "We hope you have a wonderful holiday!" messages now export centred (cell alignment on the td, systemic #9).

### cards/18

- MEDIUM | all | p1 | both quarter-fold guide rules missing: solid light-gray vertical rule down the page centre (x≈635, full height) and dashed light-gray horizontal rule across mid-page (y≈822, full width)
- MINOR | all | p1 | candle bodies drawn ~18px taller than Word (tops extend up toward the flames, bottoms aligned), identical in all three backends, both card halves
- MINOR | all | p1 | "Happy Birthday!" script text sits ~30px higher than Word relative to the swoosh/candles (diff shows doubled text), both card halves
- MAJOR | html | - | flame-glow circles detach from the candles and render as a separate cluster at the top-left of the document instead of behind the flames
- MAJOR | html | - | "Happy Birthday!" texts detached from their cards: the first overlaps the middle of the second candle illustration, the second floats alone below both illustrations (anchored positions lost)
- MEDIUM | html | - | fold guide rules missing entirely

### cards/19

- MAJOR | pdf | p2,p4 | corner diagonal-stripe triangle motif missing from all 10 cards on each page (present in Word and both raster backends)
- ✅ FIXED | pdf | p1,p2,p3,p4 | display serif text renders in the upright Bodoni face matching the raster backends, not a smaller italic substitute (see systemic #22)
- MINOR | pdf | p1,p3 | chevron background pattern drawn with noticeably heavier/thicker lines than Word
- MINOR | skia,imagesharp | p1,p2,p3,p4 | card content sits ~15-20px higher than Word (title text top-aligned instead of vertically centered in its box on p1/p3; contact block and background pattern correspondingly offset on p2/p4)
- MAJOR | html | - | card art decoupled from card text: hatch-pattern blocks render as separate stacked images with EMPTY title boxes, and all card text renders afterward as a separate block (text never appears inside its card)
- MAJOR | html | - | p3 card titles invisible: the white "VanArsdel, Ltd." text renders on the white page background instead of inside the dark boxes, leaving a large blank gap where the 10 titles should be
- MEDIUM | html | - | p4 cards: only the "CEO" line is right-aligned (floats detached at far right); name and contact lines stay left-aligned, while Word right-aligns the whole card

### column_breaks

- MEDIUM | all | p1,p2 | text following each column break starts at the top of the new column, one line higher than Word (Word starts the post-break column one line down)
- MEDIUM | all | p2 | "Third column (or new page if only 2 columns)." renders as one unwrapped line overflowing the column width; Word wraps it to two lines inside the narrow column
- CLEAN: html

### comments/01

- MAJOR | all | p1 | comment markup missing entirely: right-side gray comments pane, balloon "Commented [R1]: Looks good to me.", pink highlight box on the commented text, and the dashed connector line are all absent
- MEDIUM | all | p1 | body text drawn full-size at the normal top margin instead of Word's shrunk-to-fit-markup layout (Word scales the body down and places it lower to reserve the markup column)
- MAJOR | html | - | comment content "Commented [R1]: Looks good to me." missing from the HTML export (only the body sentence is present)

### compatibility_mode_14

- MEDIUM | skia,imagesharp | p1 | "GENERAL PRACTITIONER" subtitle letter-spacing broken: large gap after the leading G and P with the remaining letters unspaced, subtitle much narrower than Word's evenly-tracked version (same artifact widens the word gap in the "WORK EXPERIENCE" heading)
- MEDIUM | pdf | p1 | "GENERAL PRACTITIONER" subtitle drops the expanded letter-spacing entirely — renders at normal tracking, roughly 40% of Word's width
- MINOR | pdf | p1 | summary paragraph rendered ragged-right instead of justified and wraps at different words ("Known for" pulled up to line 1); line count unchanged
- MEDIUM | html | - | education/work entry headings (Jasper University, Bellows College, Lamna Healthcare, Tyler Stein MD, City Hospital) render italic; they are upright in Word
- MINOR | html | - | blank space above each education/work entry heading is collapsed, entries run together noticeably tighter than Word

### complex_document

- MEDIUM | all | p1 | intro paragraph wraps to 2 lines vs Word's 3 (backends fit more words per line) and section spacing runs slightly tighter, so all content below sits progressively higher — ~2 lines by "5. Conclusion"
- CLEAN: html

### complex_spacing

- MAJOR | all | - | page count 6 vs Word's 7 — content runs ~2/3 page ahead by p3 and Word's p7 content lands on the backends' p6; no content is lost
- MEDIUM | skia,imagesharp | p1,p4,p5,p6 | hanging-indent paragraphs not outdented: first line drawn at the left indent instead of outdented left of it (Word puts the first line at left−hanging, into the margin), and continuation lines are pushed right by the hanging amount — Combination 7's mirror/hanging column narrows and wraps into 10 lines vs Word's 7
- MEDIUM | all | p1 | "Mirror indents enabled with left 1440" paragraph indented ~1 inch in all backends; Word renders it flush at the left margin
- MEDIUM | all | p1 | text fits more words per line than Word: first-line-indent-720 paragraph wraps 2 lines vs Word's 3 (PDF additionally collapses both hanging paragraphs 3→2 lines); wrap points shift on every page
- MINOR | html | - | mirror-indents paragraph likewise indented ~1.3in while Word shows no indent

### complex_tables

- MEDIUM | all | p1,p2 | document title and all numbered section headings render smaller, blue, and non-bold (theme heading colors) instead of Word's larger bold black text
- MEDIUM | all | p1,p2 | vertical compression pulls the complex-merge table and the "5. Calendar-Style Layout" heading onto p1 (PDF also pulls its intro sentence), leaving p2 nearly empty except the calendar — Word's p2 holds the complex-merge table plus all of section 5
- MINOR | all | p1 | "COMPLEX MERGE" header cell text wraps to two stacked lines; single line in Word
- MINOR | all | p2 | calendar's merged "15-16 Event" cell wraps to two lines, making the last calendar row ~50% taller than Word's single-line version
- MEDIUM | html | - | title and section headings render blue/non-bold vs Word's bold black (same styling defect as the raster/PDF backends)

### content_control_inline

- MAJOR | all | p1 | inline content controls rendered as block-level form widgets — each "Label: value" line splits into three lines and dropdown/date get bordered input-box chrome (dropdown arrow, shaded field) where Word shows plain inline text "☒ Yes", "Medium", "2025-06-15"
- MEDIUM | html | - | inline controls likewise broken out of their sentences: checkbox glyph, "Yes"/"No", "Medium", "2025-06-15", "John Doe" each emitted as its own paragraph instead of flowing inline after the label

### cover-letters/01

- MEDIUM | all | p1 | header contact line wraps to two lines ("someone@example.com" drops to a second line; Word fits "T: … // W: … // E: …" on one line), pushing the whole letter below down one line
- MINOR | all | p1 | paragraph 2 wraps the whole word "evidence-based" to the next line where Word breaks at the hyphen ("…implementing evidence-" / "based medicine…")
- MEDIUM | html | - | greeting "DEAR JOZI KOS," rendered italic (Word shows upright bold)
- MINOR | html | - | inter-paragraph spacing collapsed — body paragraphs run together with no gap

### cover-letters/02

- MEDIUM | all | p1 | entire content column sits higher than Word — ~0.25" at the "MANASI GOYAL" header, growing to ~0.35" (about 2 line heights) by the signature, from tighter vertical spacing
- MAJOR | html | - | decorative flower graphic at bottom-right missing entirely
- MEDIUM | html | - | cream page background lost — page renders white
- MINOR | html | - | inter-paragraph spacing collapsed — paragraphs run together

### cover-letters/03

- MAJOR | skia,imagesharp | p1 | title drops the word space and renders "SheetalParmar" instead of "Sheetal Parmar"
- MEDIUM | all | p1 | right-side lighter sidebar stripe starts ~0.4" below the top edge (dark notch in top-right corner) instead of bleeding off the top of the page as in Word
- MEDIUM | skia,pdf | p1 | body wraps one word earlier per line — paragraph 1 becomes 7 lines vs Word's 6 (orphan "care."), paragraph 2 becomes 6 vs 5, landing the signature ~2 lines lower
- MINOR | pdf | p1 | title "Sheetal Parmar" letter advances slightly wider than Word (~5% wider overall)
- MINOR | imagesharp | p1 | letter body drifts about half a line lower by the signature
- MAJOR | html | - | right-side lighter sidebar stripe missing entirely (uniform navy background)
- MINOR | html | - | inter-paragraph spacing collapsed — paragraphs run together

### cover-letters/04

- MINOR | skia,imagesharp | p1 | word gap in the title "DANIELLE BRASSEUR" renders about twice as wide as Word's
- MINOR | imagesharp | p1 | paragraph 2 pulls "at" up to line 1 ("…as a Bookkeeper at") where Word breaks before it, leaving continuation lines starting with a stray leading space
- MINOR | html | - | inter-paragraph spacing collapsed — date, address, greeting and paragraphs run together
- CLEAN: pdf

### cover-letters/05

- MEDIUM | all | p1,p2,p3 | body paragraph/line spacing wider than Word — right-column letter drifts progressively down, "Sincerely,/Yuuri Tanaka" ends ~1 line lower (imagesharp) to ~2 lines lower (skia, pdf)
- MINOR | skia,pdf | p1,p2,p3 | first body paragraph wraps "Enthusiasm" to line 2 where Word keeps it on line 1
- MAJOR | html | - | corner decoration shapes lose their per-page theme colors — page-1 (teal/green/yellow) and page-2 (teal/magenta/orange) clusters all render in page-3's grey/black palette with only faint colored outlines
- MAJOR | html | - | grey diagonal decoration shapes overlap the second section's address text ("123 Elm Avenue / City, State 98052")
- MINOR | html | - | inter-paragraph spacing collapsed — paragraphs run together

### cover-letters/06

- MAJOR | all | p1 | top banner background missing — full-width pink band and purple rounded smiley panel not drawn, leaving confetti and smiley floating on white
- MAJOR | all | p1 | bottom decorative border (thin lavender/purple/pink segment bar plus pink band) missing entirely
- MAJOR | all | p1 | contact icons wrong: location-pin and phone glyphs missing, envelope icon rendered as a solid filled purple box
- MEDIUM | pdf | p1 | headings "[Your Name]" and "Dear [Recipient Name]" render noticeably larger/bolder than Word
- MINOR | all | p1 | letter body drifts ~1 line lower than Word by the signature
- MAJOR | html | - | same decor loss as raster: pink banner band, purple rounded panel and bottom border strip all missing
- MAJOR | html | - | pin/phone icons missing and envelope a solid box; contact block also loses its right alignment

### cover-letters/07

- MAJOR | all | p1 | Black decorative bars bleeding off the top and bottom page edges are missing entirely (page renders as plain cream top to bottom)
- MEDIUM | all | p1 | Short black underline rule below "Manager" is missing
- MINOR | imagesharp | p1 | First paragraph wraps at different words than Word (line 1 ends "…Manager position", next line starts with a stray leading space)
- MINOR | all | p1 | Letter body drifts upward slightly with tighter paragraph spacing, ending ~0.7 line higher at "Victoria Burke"
- MAJOR | html | - | Black top/bottom bars and the rule below "Manager" are missing in the HTML export
- MINOR | html | - | Inter-paragraph spacing collapsed — body paragraphs run together with no blank space between them

### cover-letters/08

- MEDIUM | all | p1 | "Enclosure" renders upright instead of italic
- MEDIUM | skia,imagesharp | p1 | Accumulated tighter line/paragraph spacing makes the signature block ("Angelica Astrom / December 13, 20XX / Enclosure") end ~1.5 lines higher than Word
- MINOR | pdf | p1 | Signature block ends ~0.8 line higher than Word
- MINOR | skia,imagesharp | p1 | Letter-spaced headings ("ANGELICA ASTROM", "UI/UX DESIGNER", "DEAR JOSEPH PRICE :") show over-wide word gaps and a stray gap before the colon; closing paragraph's wrapped line starts with a leading space (" your review,")
- MINOR | html | - | Blank-line spacing before/inside the signature block collapsed ("Sincerely," sits tight against the paragraph and the name)

### cover-letters/09

- MAJOR | all | p1 | Circular profile photo at the top of the navy sidebar is missing in all backends
- MAJOR | all | p1 | Sidebar content redistributed: "DIAN NUGRAHA" sits ~0.4in higher and the contact rows spread down the column so the email and website rows land on the yellow/pink waves (white-on-yellow, barely legible) instead of on the navy panel
- MEDIUM | all | p1 | Decorative wave shapes mis-rendered: pale pink strip runs down the sidebar's right edge and a faint full-width pink band crosses the very bottom of the page; top pink region is taller and bottom waves start higher than Word
- MEDIUM | all | p1 | Bullet "Knowledge of the latest technology in [industry or field]?" wraps with the "?" orphaned alone on the next line (Word breaks at "[industry or / field]?")
- MEDIUM | all | p1 | Letter text drifts upward ~1–2 lines by the "Sincerely, / Dian Nugraha / Enclosure" block
- MINOR | imagesharp | p1 | Second how-to paragraph wraps at different words than Word (" place it appropriately." line starts with leading space)
- MAJOR | html | - | Circular profile photo missing in the HTML export
- MEDIUM | html | - | "DIAN NUGRAHA" heading overlaps the light-pink wave, leaving the first letters white-on-pink with poor contrast
- MINOR | html | - | Paragraph spacing partially collapsed (how-to paragraphs run together)

### cover-letters/10

- MAJOR | all | p1 | Yellow triple-crescent Contoso logo missing from the black header band
- MEDIUM | all | p1 | Black header band starts ~0.7in below the page top (white strip above it) instead of bleeding off the top edge as in Word
- MEDIUM | all | p1 | Horizontal rule between "10 April 20XX" and the Adatum address is missing
- MEDIUM | skia,imagesharp | p1 | First body paragraph wraps to 6 lines vs Word's 5 (breaks at different words; wrapped lines gain stray leading spaces)
- MEDIUM | skia | p1 | Date, Adatum address block, and header contact info render in a visibly heavier weight than Word (imagesharp/pdf match)
- MINOR | pdf | p1 | Line spacing slightly larger — "Sara Steale" ends ~0.7 line lower than Word
- MAJOR | html | - | Entire black header band with Contoso contact info and logo is missing from the HTML export
- MEDIUM | html | - | The single date underline is repeated as rules under "10 April 20XX", "Adatum Corp." and "210 Stars Ave."
- MINOR | html | - | Cream page background not applied (white page)
- MINOR | html | - | Spacing collapsed between "Warm regards," and "Sara Steale"

### cover-letters/11

- MEDIUM | all | p1 | Title renders "Astrom" in the same bold weight as "Angelica" (Word shows "Astrom" in a light weight)
- MEDIUM | all | p1 | "Enclosure" renders upright instead of italic
- MEDIUM | skia,imagesharp | p1 | Accumulated tighter section/line spacing — letter and sidebar content end ~3 lines higher than Word by "Enclosure"
- MINOR | pdf | p1 | Content ends ~0.5 line higher than Word
- MINOR | skia,imagesharp | p1 | Letter-spaced headings ("UI/UX DESIGNER", "DEAR JOSEPH PRICE:") show over-wide word gaps
- MEDIUM | html | - | "Astrom" bold instead of light in the HTML title (italic "Enclosure" is correct in HTML)
- MINOR | html | - | Blank-line spacing around "Sincerely,"/signature collapsed

### cover-letters/12

- MEDIUM | all | p1 | "In my current role…" paragraph wraps to 5 lines vs Word's 4 ("residents." pushed to its own line); final paragraph also rewraps mid-sentence ("Thank / you" with a stray leading space on " on family health")
- MINOR | all | p1 | Entire content block sits ~1 line lower on the gradient page than Word
- MAJOR | html | - | Full-page four-color gradient background missing (plain white page)
- MAJOR | html | - | The "+" list markers before "9/9/20XX", "Contact" and "Dear Jozi Kos," render as generic round bullets instead of "+" glyphs
- MINOR | html | - | Paragraph spacing collapsed — letter paragraphs run together

### cover-letters/14

- MEDIUM | skia,imagesharp | p1 | Tighter paragraph spacing — letter ends ~1.5 lines higher than Word at "Tonnie Thomsen" (wrapped line " that supports students'…" also starts with a stray space)
- MINOR | pdf | p1 | Letter ends ~0.8 line higher than Word
- MINOR | skia,imagesharp | p1 | Right-hand recipient address ("4321 Maplewood Ave / Nashville, TN 65432") sits ~half a line higher than Word relative to the LILLI ALLIK block
- MINOR | html | - | Paragraph spacing collapsed (greeting, paragraphs and signature run tight)

### cover-letters/15

- MEDIUM | pdf | p1 | Green double-line page borders duplicated: an extra pair of vertical lines is drawn at the extreme left and right page edges in addition to the correct inset pairs
- MEDIUM | skia | p1 | Right-hand green double border is drawn at the very page edge instead of ~0.5in inset (left border is correctly placed)
- MINOR | skia,imagesharp | p1 | Letter body drifts up ~0.5 line by the "Chanchal Sharma / January 13, 20XX" block
- MINOR | html | - | Paragraph spacing collapsed (section headings and paragraphs run tight)

### cover-letters/16

- MEDIUM | skia,imagesharp | p1 | word spaces collapsed in recipient address block — "HIRING MANAGER"/"ESG FINANCIAL SERVICES"/"5678 MOUNTAIN DRIVE" render as "HIRINGMANAGER"/"ESGFINANCIALSERVICES"/"5678MOUNTAINDRIVE" (PDF renders the spaces correctly)
- MEDIUM | all | p1 | body text drifts progressively lower than Word; "Sincerely,/Donna Robbins" block sits ~34px (≈1 line height) lower
- MEDIUM | imagesharp | p1 | paragraph 2 wraps at different points than Word (line 5 ends "QuickBooks, and" and line 6 ends "of a diverse", pulling "and"/"diverse" up a line)
- MINOR | all | p1 | leading space at start of wrapped lines not trimmed — lines "in my ability to provide…" (para 1) and "women and minority owned…" (para 3) are indented one space width off the left margin
- CLEAN: html

### custom_margins

- MINOR | all | p1 | paragraph spacing slightly tighter than Word; the 4-line block drifts upward, last line "Notice the larger margins…" ~16px higher than Word (margins themselves correct)
- CLEAN: html

### decimal_tabs/01

- MEDIUM | all | p1 | line spacing far too tight — Word spaces the 4 rows at ~47px pitch, Morph ~30px, so "Dates 0.05" ends >1 line height higher (decimal-point alignment of the values is correct)
- MEDIUM | html | - | decimal tab alignment lost — values render immediately after labels separated by a single space ("Apples 12.50") instead of an aligned column

### deep_nested_list

- ✅ FIXED | all | p1 | level-3 (■) and level-5 (▸) bullets now render real glyphs on all three backends via the embedded Bullets.ttf (▸/► triangles added to it; markers route there when the text faces lack the glyph). See systemic #11.
- MEDIUM | all | p1 | list line spacing tighter than Word; drift accumulates to ~60px (>1 line) by the last item "Level 3 - Item B.1.a"
- MINOR | html | - | level-4 and level-5 bullets both render as browser-default squares instead of Word's smaller square and triangle

### document_capture/01

- MAJOR | pdf | p1 | footnote and endnote content entirely missing — no separator lines, no "This is a footnote."/"This is an endnote." anywhere on the page
- MAJOR | skia,imagesharp | p1 | footnotes/endnotes rendered as invented "Footnotes"/"Endnotes" bold heading sections with "1." numbering placed directly after body — Word's separator lines absent and the footnote is not pinned to the page bottom
- MAJOR | all | p1 | superscript footnote/endnote reference marks missing in body text — Word shows "Footnote ref1"/"Endnote refi", Morph renders plain "Footnote ref"/"Endnote ref"
- ✅ FIXED | all | p1 | the italic "x" (m:oMathPara display math) now centres horizontally at Word's position (OMML m:jc default centerGroup). See systemic #9.
- ✅ FIXED | html | - | the italic "x" line now exports centred (notes and reference marks otherwise correctly exported).

### dot_points

- MINOR | all | p1 | line spacing slightly tighter than Word; cumulative upward drift reaches ~30px (≈0.7 line) at item F (per-level bullet fonts/glyphs •/o/▪ all render correctly)
- MINOR | html | - | bullet glyphs at levels 4-6 all render as squares instead of repeating Word's •/o/▪ cycle

### embedded_font

- MINOR | all | p1 | slight cumulative upward drift of the text block (~14px by the Consolas line); Segoe UI/Times New Roman/Consolas typefaces themselves all resolve and render correctly
- CLEAN: html

### empty_paragraphs

- MINOR | all | p1 | gap left by the empty paragraphs is ~17px (≈0.7 line) too short — "Text after empty paragraphs." sits visibly higher than Word
- MINOR | all | p1 | body text rendered ~9% narrower than Word (both lines end ~30px short of Word's line ends)
- MEDIUM | html | - | empty paragraphs dropped entirely — the two sentences render back-to-back with no blank gap (Word shows ~3 blank lines between them)

### even_odd_headers/01

- MINOR | html | - | ODD HEADER / EVEN HEADER text omitted from HTML export (only the two body lines present)
- CLEAN: skia, imagesharp, pdf

### even_odd_headers/02

- MEDIUM | all | p1,p2,p3,p4 | footer text (ODD FOOTER / EVEN FOOTER) placed ~46px (≈0.3in, ~2 line heights) lower than Word on every page; header and body positions match
- MINOR | html | - | header and footer text omitted from HTML export (only the four body-content lines present)

### explicit_break_blank_page

- MINOR | all | p1 | text rendered ~10% narrower than Word — second sentence ends ~70px (≈0.5in) left of Word's line end (glyph advance widths, no rewrap here)
- CLEAN: html

### feature_capture/01

- MAJOR | skia,imagesharp | p1 | giant 3-line drop-cap "D" rendered where Word shows no drop cap ("Drop cap paragraph" reads as one normal line in Word), pushing the paragraph text and table ~0.5in lower
- MAJOR | imagesharp | p1 | "ALL FEATURES" drawn twice — yellow-highlighted bold copy plus an offset gray duplicate below-right (shadow effect rendered as a second text copy); Word shows a single plain small-caps line
- MEDIUM | skia | p1 | "ALL FEATURES" drawn with a yellow highlight/glow and heavier bold-looking glyphs; Word renders plain small caps with no highlight
- MEDIUM | pdf | p1 | "All features" paragraph rendered at the left margin — right alignment lost
- MEDIUM | pdf | p1 | small-caps formatting lost — renders mixed-case "All features" instead of "ALL FEATURES"
- MEDIUM | all | p1 | rotated table header cell not wrapped: single vertical "Header" line instead of Word's two stacked vertical lines "Hea/der", making the header row ~65% taller
- MINOR | html | - | "All features" paragraph left-aligned instead of right-aligned
- MINOR | html | - | rotated header cell rendered as horizontal bold centered text (rotation lost)

### field_codes_simple/01

- MEDIUM | html | - | HTML (and Markdown) export still emits the cached "Page 1 of 3" instead of "Page 1 of 1" — the exporters keep the cached value by design (no pagination) [systemic #1 residual (d)]; raster/PDF now render "of 1" correctly

### first_line_indent

- MEDIUM | all | p1 | first paragraph wraps at a different word: line 1 ends "...first line starts further to" instead of Word's "...first line starts" (text renders narrower, pulling two extra words onto the first line)
- CLEAN: html

### font_families

- MINOR | all | p1 | line spacing slightly tight — the six sample lines drift progressively upward, with "Comic Sans MS font" sitting ~half a line height higher than Word
- CLEAN: html

### font_sizes

- MINOR | all | p1 | line spacing slightly tight — cumulative upward drift down the size list, "36pt text" baseline ends ~16px higher than Word
- CLEAN: html

### footer

- MEDIUM | all | p1 | footer "Document Footer - Confidential" is rendered ~45px (0.3") lower than Word, nearly a line height closer to the page bottom edge
- MAJOR | html | - | footer text "Document Footer - Confidential" is missing entirely from the HTML export

### form_checkboxes

- MAJOR | all | p1 | form checkbox glyphs missing — Word shows an empty box before "Option 1 (unchecked)" and a checked box before "Option 2 (checked)", but all backends render only the label text (labels shift left into the glyph position)
- MAJOR | html | - | checkbox symbols also absent in HTML export; only the option labels render

### form_dropdowns

- MINOR | all | p1 | "Select an option: Option A" line sits ~10px higher than Word (paragraph spacing slightly tight)
- CLEAN: html

### form_text_fields

- MINOR | all | p1 | "Name:" and "Date:" lines sit ~10px higher than Word (paragraph spacing slightly tight)
- CLEAN: html

### hanging_indent

- MEDIUM | skia,imagesharp | p1 | Hanging indent not applied to first line: paragraph renders with first line at the 0.5" continuation indent (x=227 vs Word x=152) and continuation line at 1.0" (x=302 vs 227) — entire paragraph sits 0.5" right of Word, first line never outdents back to the margin
- MINOR | pdf | p1 | Indents correct (152/225 matches Word) but line 1 wraps one word later — "indented." fits on line 1 (narrower glyph advances), leaving line 2 shorter than Word's
- CLEAN: html

### header

- MAJOR | html | - | Page-header content missing from HTML export: bold centered "Document Header" line absent; only the two body paragraphs are emitted
- CLEAN: skia, imagesharp, pdf

### header_banner_table

- MINOR | skia,imagesharp | p1 | Header banner slightly taller than Word: slate marking bar ~6px taller and light-blue spacer row 22px tall starting 13px lower (Word: 15px tall at y=92) — banner bottom edge ends ~20px lower, gap between bars widened 9px→16px
- MINOR | pdf | p1 | Same banner inflation but larger: slate bar ~14px taller (bottom y=96 vs 82), spacer row ends 29px below Word's, and "SAMPLE // BANNER" text sits correspondingly lower within the bar
- MAJOR | html | - | Entire header banner table (slate "SAMPLE // BANNER" bar + spacer rows) missing from HTML export; only body heading and paragraphs emitted

### header_footer

- MEDIUM | all | p1,p2 | Paragraph spacing tighter than Word: page 1 fits paragraphs 1-28 vs Word's 1-24 (body also starts ~33px higher), so page 2 begins at paragraph 29 instead of 25 (page count still 2/2)
- MEDIUM | all | p1,p2 | Footer line "© 2024 Company Name. All rights reserved." renders ~51px lower than Word (y=1582-1600 vs 1531-1550 on a 1650px page)
- MAJOR | html | - | Header ("Company Name" / "Internal Document") and footer ("© 2024 Company Name. All rights reserved.") both missing from HTML export; body paragraphs 1-30 complete

### header_row_repeat/01

- MEDIUM | all | p1,p2,p3 | Table rows slightly shorter than Word, accumulating one extra row per page: p1 ends at Person 25 (Word: 24), p2 spans 26-50 (Word: 25-48), p3 starts at 51 (Word: 49); header row correctly repeats on p2/p3 in all backends and all 60 rows present
- MINOR | html | - | Repeated header cells "ID / Name / Notes" rendered centered in HTML while Word renders them left-aligned in their cells

### headings

- MINOR | all | p1 | Cumulative tight heading/paragraph spacing: block drifts upward down the page, final "Normal paragraph under heading 4." sits ~28px (≈1 line) higher than Word; fonts, weights, sizes and wraps all match
- CLEAN: html

### html_basic_formatting

- MEDIUM | all | p1 | Paragraph spacing compressed vs Word (~44px line pitch vs ~57px at 150dpi), so the 14-line block progressively drifts up and ends ~3.5 line heights higher than Word
- CLEAN: html

### html_complex

- MAJOR | all | p1 | Data table header row loses its #4472C4 blue background and white bold text — rendered plain black-on-white
- MAJOR | all | p1 | Alternating row shading (#F0F0F0 on the "Gadget B" row) missing
- MAJOR | all | p1 | "In Stock" cell colors lost — green "Yes"/red "No" rendered black
- MEDIUM | all | p1 | Table interior cell gridlines missing (only the outer frame is drawn despite border=1 with border-collapse)
- MEDIUM | all | p1 | Table rendered auto-width (~490px) instead of width:100% (~1020px); Price right-alignment and In Stock centering also lost
- MAJOR | all | p1,p2 | All h2 section headings ("1. Formatted Text Section" … "5. Styled Boxes") lose their CSS color #4472C4 and render black
- MEDIUM | all | p1 | Gradient image drawn 312x234px instead of Word's 234x175 (HTML px treated as pt, ~33% oversize)
- MEDIUM | all | p1,p2 | "Visit our website for more information." paragraph pushed off p1 (below the image in Word) to the top of p2
- MAJOR | all | p2 | Info/Warning/Error styled boxes lose their background fills (#E7F3FF/#FFF3CD/#F8D7DA) and colored borders — only the colored text lines remain
- MEDIUM | all | p1 | Intro paragraph wraps to 3 lines vs Word's 2 (superscript/subscript phrase pushed to an extra line)
- MAJOR | html | - | h2 headings rendered black instead of #4472C4
- MAJOR | html | - | Table styling lost in export: no header background/white text, no row shading, Yes/No colors black, no interior gridlines, auto width instead of 100%
- MAJOR | html | - | Info/Warning/Error box backgrounds and borders missing (text colors kept)

### html_css_alignment

- MEDIUM | all | p1 | Table rendered content-width (~390px) instead of width:100% (~1020px in Word), and height:100px ignored (row 33px tall vs Word's 61px)
- MEDIUM | all | p1 | Cell padding missing — "Top aligned/Middle aligned/Bottom aligned" run together touching the borders as "Top alignedMiddle alignedBottom aligned"
- MEDIUM | all | p1 | Interior column borders missing despite border=1 (outer frame only; Word shows all three cells ruled)
- MINOR | all | p1 | Justified paragraph breaks after "entire line" instead of Word's "fill the" (same 2-line count, different break word)
- MEDIUM | all | p1 | Paragraph spacing compressed — content ends ~2.5 line heights higher than Word
- MEDIUM | html | - | Table interior cell borders and width:100% lost in export (single content-width box)

### html_css_borders

- MAJOR | all | p1 | All seven CSS paragraph borders missing (1px solid black, 2px red, 3px dashed blue, 2px dotted green, 4px double purple, top-red/bottom-blue, 5px orange left bar) — plain unboxed text lines, and with the borders/padding gone the stack compresses ~2in upward
- MEDIUM | all | p1 | Table per-cell border styling lost: one uniform thin box, no 2px thick border on "Cell with thick border" vs 1px gray on "Cell with thin border", and no divider line between the two cells
- MAJOR | html | - | Same seven paragraph borders missing in the HTML export
- MEDIUM | html | - | Table cell border weight/color distinction and inner divider missing in the HTML export

### html_css_colors

- MAJOR | all | p1 | Background fills missing: #FFFFCC band behind "Light yellow background" and #E0E0E0 band behind "Div with background and padding" (Word draws both full-width)
- MAJOR | all | p1 | "Light gray bg, dark blue text" rendered black instead of darkblue — extended named color dropped while red/blue/green/orange/purple, hex and rgb() colors all work
- MEDIUM | all | p1 | Paragraph spacing compressed — last line ends ~3 line heights higher than Word
- MAJOR | html | - | Yellow and div gray backgrounds missing in HTML export
- MAJOR | html | - | darkblue text color rendered black in HTML export

### html_css_margin_padding

- MEDIUM | all | p1 | margin-left:50px and 100px indents ignored — both paragraphs sit flush at the left margin (Word shows the staircase)
- MAJOR | all | p1 | Backgrounds/borders missing: #EEE band on the 20px-margin paragraph, #DDD padded-div band, and the #CCE5FF fill + #0066CC border box on the 15px-padding paragraph
- MEDIUM | all | p1 | 20px div padding and 30px vertical margins collapsed — "Content inside padded div" not inset and "Paragraph with extra vertical margins" sits tight against its neighbors
- MAJOR | html | - | Same three backgrounds and the blue border missing in HTML export
- MEDIUM | html | - | 50px/100px left-margin indents lost in HTML export

### html_font_tag

- MEDIUM | all | p1 | Paragraph spacing compressed — block ends ~3 line heights higher than Word (font sizes 1-7, red/blue/purple colors, and Arial/Times/Courier/Georgia faces are all faithful)
- CLEAN: html

### html_headings

- MEDIUM | all | p1 | Heading/paragraph spacing compressed — content ends ~2 line heights higher than Word (heading sizes and weights faithful)
- MEDIUM | html | - | Heading 4 and Heading 6 rendered italic in the HTML export (upright bold in Word and all raster/PDF outputs)

### html_images

- MEDIUM | all | p1 | All four embedded images rendered ~33% larger than Word (px dimensions treated as pt instead of 0.75pt), pushing each subsequent caption/image progressively further down the page
- CLEAN: html

### html_inline_styles

- MAJOR | all | p1 | Yellow background band on "Text with yellow background" and light-red background band on "Red text on light red background" both missing (Word draws full-width shading)
- MAJOR | html | - | Same yellow and light-red backgrounds missing in HTML export
- MEDIUM | all | p1 | CSS font sizes ignored: "Larger text at 18pt" and "Smaller text at 8pt" both render at default body size
- MEDIUM | html | - | Same 18pt/8pt font sizes ignored in HTML export
- MEDIUM | all | p1 | CSS font families ignored: "Times New Roman font" and "Courier New monospace font" lines render in the default document font (monospace lost)
- MEDIUM | html | - | Same Times New Roman/Courier New font families dropped in HTML export
- MEDIUM | all | p1 | CSS bold (font-weight) and italic (font-style) ignored — both lines render regular upright
- MEDIUM | html | - | Same bold/italic dropped in HTML export
- MEDIUM | all | p1 | text-decoration ignored: "Underline via text-decoration" has no underline and "Strikethrough via text-decoration" has no strike line
- MEDIUM | html | - | Same underline/strikethrough dropped in HTML export
- MEDIUM | all | p1 | Paragraph spacing ~20% tighter than Word throughout, so the text block ends ~3 line heights higher

### html_links

- MEDIUM | all | p1 | Paragraph spacing tighter than Word (all link styling itself correct), so the 8-line block ends ~0.6 in higher than Word's render
- CLEAN: html

### html_lists

- MEDIUM | all | p1 | List items render in the serif default font instead of Word's sans-serif (Aptos-style) list font, and at a visibly smaller size
- MEDIUM | all | p1 | Spacing around lists missing: no blank gap after "Unordered list:"/before "Ordered list:" (Word has clear gaps), item leading slightly looser, whole block ends higher
- MINOR | all | p1 | Bullet glyph noticeably smaller/lighter than Word's large Symbol-font solid bullet
- MEDIUM | html | - | List items in serif default font vs Word's sans-serif list font

### html_nested_lists

- MAJOR | all | p1 | Third-level bullets (Item 1.2.1 / 1.2.2) drawn as hollow circles instead of Word's filled squares
- MEDIUM | all | p1 | List text renders in serif default font instead of Word's sans-serif list font
- MEDIUM | all | p1 | Blank gaps before "Nested ordered lists:" and "Mixed nested lists:" headings missing (renders run sections together at uniform item spacing)
- MEDIUM | html | - | List items in serif default font vs Word's sans-serif list font

### html_paragraphs

- MEDIUM | all | p1 | Empty paragraph rendered as a full blank line before "Paragraph after an empty paragraph." — Word collapses it to a normal single paragraph gap
- MINOR | skia,imagesharp | p1 | Leading spaces not trimmed: last paragraph "Paragraph with leading spaces (may be trimmed)." starts indented ~2 spaces, Word (and PDF) render it flush left
- MINOR | all | p1 | Paragraph spacing slightly tighter than Word overall (block ends ~1 line higher)
- CLEAN: html

### html_table

- MEDIUM | all | p1 | Thin outer border drawn around the table although Word renders this table completely borderless
- MEDIUM | all | p1 | Table far more compact than Word: row heights roughly half, near-zero cell padding (adjacent cells' text almost touching, e.g. "Row 2, Cell 1Row 2, Cell 2"), narrower columns
- MEDIUM | all | p1 | Cell text in serif default font instead of Word's sans-serif cell font
- MEDIUM | html | - | Outer table border drawn though Word shows the table borderless
- MEDIUM | html | - | Cell text in serif default font vs Word's sans-serif

### html_table_cell_margin_css

- MAJOR | all | p1 | 2x2 table grid disintegrates: each cell drawn as a separate box offset by its CSS margin with fragmented partial borders (floating "Left margin 20px" box top-right, bare over/underline on "Top/bottom margin", bracket-shaped border around "Default") instead of Word's coherent complete grid
- MEDIUM | skia | p1 | "Margin 10px, Padding 5px" cell text wraps to two lines (single line in Word, ImageSharp and PDF)
- MEDIUM | all | p1 | Cell text in serif default font instead of Word's sans-serif cell font
- MEDIUM | html | - | Inner cell gridlines missing — only a single outer border drawn where Word shows a full light-gray grid around every cell, and cell padding much smaller
- MEDIUM | html | - | Cell text in serif default font vs Word's sans-serif

### html_table_cell_padding_css

- MAJOR | all | p1 | interior cell borders missing — only a single black outer rectangle is drawn, while Word renders a light-gray border around every cell
- MEDIUM | all | p1 | text rendered in serif Times instead of Word's sans-serif
- MEDIUM | all | p1 | CSS cell padding under-applied: table ~20% narrower and rows ~25% shorter than Word, right-column text ends flush against the table border
- MEDIUM | skia | p1 | "20px all sides" wraps to two lines (single line in Word, ImageSharp and PDF)
- MAJOR | html | - | interior cell borders missing (outer box only)
- MEDIUM | html | - | cell padding CSS entirely dropped — all rows collapse to tight single-text-line height
- MEDIUM | html | - | serif Times instead of Word's sans-serif

### html_table_cellpadding

- MAJOR | all | p1 | interior cell borders missing — single black outer rectangle only vs Word's per-cell light-gray borders
- MEDIUM | all | p1 | text rendered in serif Times instead of Word's sans-serif
- MEDIUM | skia | p1 | "Cell with 15px padding" wraps to two lines (single line in Word, ImageSharp and PDF), making row 1 taller
- MAJOR | html | - | interior cell borders missing (outer box only)
- MEDIUM | html | - | cellpadding=15 dropped — rows collapse to tight text height
- MEDIUM | html | - | serif Times instead of Word's sans-serif

### html_table_styled

- MAJOR | all | p1 | header row blue background with white bold text missing — header renders as plain black text on white
- MAJOR | all | p1 | alternating light-blue row shading (John Doe / Bob Johnson rows) missing
- MAJOR | all | p1 | cell gridlines/borders missing — only a thin outer rectangle is drawn
- MEDIUM | all | p1 | styled table collapses to auto-fit width (~25% of Word's full-text-width table); centered Age and right-aligned Salary columns become left-aligned
- MEDIUM | all | p1 | fixed-width table ignores the 100px/200px column widths — cells shrink so "Fixed 100pxFixed 200pxFlexible" runs together as one string
- MEDIUM | all | p1 | text rendered in serif Times instead of Word's sans-serif
- MAJOR | html | - | header blue background/white text and alternating row shading missing
- MAJOR | html | - | cell borders missing (outer box only)
- MEDIUM | html | - | table width styling ignored — full-width table and 100px/200px fixed columns render auto-fit, Age/Salary alignment lost
- MEDIUM | html | - | serif Times instead of Word's sans-serif

### hyperlinks

- MINOR | all | p1 | whole text block sits ~4-5px higher and runs ~5-8% narrower than Word (lines end short of Word's line ends; link color/underline and line breaks correct)
- CLEAN: html

### hyphenation_auto

- MEDIUM | all | p1 | automatic hyphenation not applied: paragraph 1 renders 3 lines vs Word's 4 (missing "telecommunica-" end-of-line break) and paragraph 2 lacks Word's "hy-/phens" break, shifting all following lines up
- CLEAN: html

### hyphenation_nonbreaking

- MAJOR | pdf | p1 | non-breaking hyphens (U+2011) render as missing-glyph boxes with wrong advance — "non▯breaking" twice and phone number "1▯800▯555▯1234", with following text overlapping ("1234" collides with "where", "breaking" runs into "hyphen")
- MINOR | all | p1 | paragraph 1 wraps one word later than Word ("...will not break at the / hyphen." vs Word's "...at / the hyphen.")
- CLEAN: html

### hyphenation_soft

- MAJOR | pdf | p1 | soft hyphens (U+00AD) drawn as visible mid-word hyphens: "soft hy-phens", "in-vis-ible", "Inter-nation-al-ization"
- MINOR | all | p1 | paragraph 1 wrap point shifts by one word vs Word (each backend breaks after a different word; line count unchanged)
- MINOR | imagesharp | p1 | wrapped continuation line "at that point." begins with a stray leading space, indenting it relative to other lines
- CLEAN: html

### hyphenation_suppressed

- MEDIUM | all | p1 | automatic hyphenation missing in paragraph 3: Word breaks "Telecommu-/nications" and "syl-/lables" but backends end line 1 early at "again." and redistribute the paragraph's lines (same line count, clearly different breaks)
- MINOR | all | p1 | paragraphs 1-2 wrap one word differently than Word (slightly narrower text advances)
- CLEAN: html

### icon_svg

- MINOR | all | p1 | star icon drawn slightly larger than Word (skia ~4%, imagesharp/pdf ~9%: 86px vs 79px) and positioned up to ~12px higher/left
- CLEAN: html

### icon_with_text

- MINOR | all | p1 | second paragraph ("Icons can be placed inline...") sits 10-13px higher than Word because the line containing the inline star icon gets less line height
- CLEAN: html

### icons_multiple

- MAJOR | imagesharp,pdf | p1 | red and green star icons both rendered blue — all three icons drawn identical to the first (SVG recolor/variant lost); Word and Skia show blue/red/green
- CLEAN: skia, html

### image_cropping/01

- ✅ FIXED | pdf | p1 | a:srcRect centre-50% crop now applied — the enlarged cropped centre is drawn and clipped to the frame (matching Word), was: full source image scaled into the frame. See systemic #20.
- MAJOR | html | - | same crop ignored in HTML export — image shown uncropped (small "Sample", full gradient) instead of the centre-50% crop
- MINOR | skia,imagesharp | p1 | crop window content shifted ~10-15px within the frame ("Sample" starts at left edge in Word, indented and slightly higher in renders)
- MINOR | all | p1 | image block drawn ~10px higher on the page (gap below "Below is a sample image:" caption smaller than Word)

### image_rotation/01

- MAJOR | skia,imagesharp | p1 | 45°-rotated image not clipped to its layout box: full diamond drawn (386px tall vs Word's 257px clipped band), top corner overlaps and partially obscures the "Below is a sample image:" text, bottom corner extends ~44px lower than Word; rotation center also ~20px higher
- MAJOR | pdf | p1 | rotation ignored entirely — image drawn as an axis-aligned unrotated rectangle
- MAJOR | html | - | rotation missing in HTML export — image shown as plain unrotated rectangle

### image_wrap_square

- MAJOR | all | - | page count 3 vs expected 2 — the two-column "Columns" section body text does not fit at the bottom of page 2 and spills onto an extra page 3 (Word fits it on page 2)
- MAJOR | pdf | p1 | square-wrapped globe ("Web Access Symbol") image missing entirely — paragraph text wraps around an empty reserved space
- MINOR | imagesharp | p1 | Links paragraph wraps differently: "downloadable" pulled up to the first line ("...or even downloadable / documents...") vs Word's break after "even"
- MINOR | all | p1,p2 | cumulative vertical drift of blocks (~10-20px up on p1, down on p2, pie chart slightly offset) with structure intact
- MEDIUM | html | - | Simple Tables data rows styled like headers — all body cells bold and centered (Word shows left-aligned regular text; the complex table below is correct)
- MINOR | html | - | last line of the "Some images, such as charts or graphs..." paragraph rendered centered ("link on the image.") instead of left-aligned

### inline_group_crop

- MAJOR | all | p1 | chalkboard background, wood frame, and decorative border drawn ~98px (0.65in) right of Word's position and clipped at the right page edge (right wood strip and right corner notches lost), while the group's text and photos stay at Word's coordinates, leaving them off-center on the board
- ✅ FIXED | all | p1 | menu text now renders Word's white (Menu title, Appetizer/Main Course/Dessert headings, body copy) — the white `w:docDefaults` colour is honoured. See systemic #7.
- ✅ FIXED (colour) | html | - | board text now exports white; the dark chalkboard behind it is still not painted by the HTML export, so it reads white-on-white there (background emission is a separate gap).
- MEDIUM | html | - | board's thin double-line inner frame border and the wavy mint divider lines between sections missing from HTML export

### inline_group_rotation

- MAJOR | all | p1 | dark chalkboard panel drawn far oversized/mispositioned, covering nearly the whole page and hiding the wood-grain border (wood only peeks through small corner notches)
- MAJOR | all | p1 | decorative double border frame (mint-green lines with ornamental corners plus red accent line) missing entirely
- ✅ FIXED (colour) | all | p1 | text now renders white on the dark panel (white docDefaults honoured; see systemic #7). "Menu" still lacks its white+mint outlined style (glyph outline styling, separate).
- MAJOR | all | p1 | mint wavy divider squiggles between the Appetizer/Main Course/Dessert sections missing
- MEDIUM | all | p1 | text column shifted ~60-80px left of Word's centered placement (headings and instruction paragraph left-aligned toward panel edge)
- ✅ FIXED (colour) | html | - | text now exports white; the dark panel behind it is still missing from the HTML export.
- MAJOR | html | - | decorative mint border frame and wavy section-divider squiggles missing

### inline_image

- MINOR | all | p1 | whole content block (both text lines + image) sits ~5-7px higher than Word, leaving the image bottom edge misaligned
- CLEAN: html

### inline_shape_arrows

- MEDIUM | skia,imagesharp | p1 | colored-arrow row drawn ~27px left and ~25px above Word's position (arrow sizes themselves correct, gap to "Arrow variants:" label visibly too small)
- MEDIUM | pdf | p1 | all four colored arrows ~10% smaller than Word (bbox 134px vs 148px) and shifted up-left ~20px
- MINOR | all | p1 | "Thinner stroke" arrow and its label paragraph sit ~10-17px higher than Word
- CLEAN: html

### italic_text

- MINOR | all | p1 | the single text line renders ~10% narrower than Word (ends ~44px earlier), no wrap change
- CLEAN: html

### labels/01

- MEDIUM | all | p1 | line spacing inside every label much looser than Word — the 4-line Name/address block spreads to fill the entire cell height, ending ~20px lower and visibly misaligning text with the icons
- MINOR | all | p1 | column icons drawn slightly offset/rescaled within their cells (outline ghosting on every icon)
- CLEAN: html

### labels/02

- MEDIUM | all | p1 | light-gray border outlines around each of the 8 label cells missing (Word draws ~#DFDFDF rules; no backend draws any)
- MINOR | skia,imagesharp | p1 | TO:/FROM: text block sits ~8px higher and chevron artwork ~2-4px offset vs Word
- MEDIUM | html | - | light-gray label cell borders missing

### labels/03

- MEDIUM | all | p1 | dotted tear-line rules across the top and bottom of every ticket missing (clearly visible on Word's green tickets)
- MEDIUM | all | p1 | ticket text leading ~1.5x looser than Word — "TICKET" sits ~15px lower and the 3-line block spreads down the ticket
- MEDIUM | html | - | dotted tear-line rules missing from all tickets
- ✅ FIXED | html | - | ticket text now centres within each ticket (cell alignment on the td, systemic #9).

### labels/04

- MAJOR | all | p1 | hexagon decorative artwork on the labels is mis-rendered as faint gray vertical-streak rectangles (one dark gray smudge blob visible near the top-left labels); the dotted hexagon outlines and small solid light-blue hexagon accent are missing from every label
- MINOR | all | p1 | left accent bars filled flat cyan #1CADE4 instead of Word's vertical gradient into deeper blue #248BCB
- MAJOR | html | - | same hexagon artwork failure: gray streak smudge on the first label and no hexagon pattern/blue hexagon accents on any label
- MINOR | html | - | blue accent bars vertically misaligned with their label text (bar tops start ~a line below "Name") and flat cyan instead of gradient

### labels/05

- MAJOR | all | p1 | Address text ("Name / Address / City ST ZIP Code") renders at the top-left of each label, overlapping the tree-pattern background and label edge, instead of centered inside the red dashed box — all 30 dashed boxes are left empty
- MAJOR | html | - | Label text detached from labels: all 30 label graphics render background+empty dashed box only (single column), with the 30 "Name / Address / City ST ZIP Code" text blocks dumped afterwards as a separate plain-text grid

### labels/06

- MAJOR | pdf | p1 | Rotated "ADMIT ONE" edge captions are drawn horizontally (rotation ignored); the two centre-edge instances overlap into garbled "ADMIT ODMIT ONE" and right-edge ones spill slightly past the ticket edge
- MEDIUM | all | p1 | "EVENT NAME" text block sits ~26px (about one line) too high inside every ticket, with an enlarged gap between the EVENT and NAME lines
- MINOR | all | p1 | Whole ticket grid shifted up ~13px on the page
- MINOR | skia,imagesharp | p1 | Centre "ADMIT ONE" pair crowds right against the middle divider instead of sitting inset within each ticket's inner edge
- MAJOR | html | - | Tickets decomposed into unassembled fragments: empty blue ticket shapes first, then a ~21-row dump of star glyphs, then ~20 stacked "ADMIT ONE" lines, then bare "EVENT NAME" blocks

### labels/07

- MAJOR | all | p1 | Several background collage shapes missing (orange striped top-left panel, dark-red swoosh top-right, central orange band/green circle cluster) leaving large white voids, while remaining artwork bleeds to the page edges instead of keeping Word's white margin
- MAJOR | all | p1 | Hand-drawn dark ink borders on all 8 name boxes not rendered (boxes are bare white rectangles, invisible where they overlap the white voids)
- MEDIUM | all | p1 | "[Name]" sits at the top-left of each box instead of centered
- MAJOR | html | - | All eight "[Name]" texts are absent from the HTML export (zero occurrences in the file)
- MAJOR | html | - | Same background collage shapes missing as raster (large white voids in the artwork)

### labels/08

- MAJOR | all | p1 | Ticket background shapes drawn hugely oversized/misplaced: they overlap each other edge-to-edge, the white gutters between tickets vanish, and extra partial notched shapes bleed off the left/top/bottom page edges
- MAJOR | all | p1 | Ticket fill is flat dark purple (86,60,86) instead of Word's lighter linen-textured purple (~112,90,112)
- MAJOR | html | - | Overlapping mass of purple ticket shapes followed by detached near-invisible white texture images, then all "YOUR EVENT NAME / TICKET" text blocks rendered separately below in cream-on-white (barely legible)

### labels/09

- MEDIUM | pdf | p1 | All 10 ticket titles wrap differently: "BACK-2-SCHOOL NIGHT / RAFFLE" instead of Word's "BACK-2-SCHOOL / NIGHT RAFFLE" (PDF text runs narrower)
- MINOR | all | p1 | Uniform few-pixel shifts of ticket text, rules and borders throughout (visible ghosting in diff, structure intact)
- MINOR | html | - | Stub ticket numbers ("00 01" etc.) partially clipped by the narrow red stub columns

### labels/10

- MAJOR | all | p1 | Stadium/pill label shape rendered as a sharp-cornered rectangle on all 30 labels (rounded ends lost)
- MAJOR | all | p1 | Text block sits far too high: "YOUR NAME" overflows above the label shape into the row gap (columns 1 and 3) or hugs the very top (column 2), leaving the lower half of every label empty where Word centers the block
- MEDIUM | all | p1 | Teal rule that belongs directly under "YOUR NAME" renders detached lower in the label (under "Street Address" or "City, St Zip" depending on column)
- MEDIUM | html | - | Labels render as plain rectangles with "YOUR NAME" overflowing above each block and text left-aligned instead of centered
- MINOR | html | - | Teal rule under "YOUR NAME" missing entirely

### labels/11

- ✅ FIXED | all | p1 | the purple brush-stroke backgrounds now render behind all 30 labels with the white text legible on them (relativeHeight z-ordering + cell-anchored group handling, systemic #5).
- MAJOR | html | - | label text invisible: white text emitted on the white page because the brush image is placed above the text block instead of behind it
- MEDIUM | html | - | the 30 brush images render stacked in a single left-hand column instead of the 3-across label grid

### labels/12

- MAJOR | all | p1 | decorative cutlery/swirl flourish image below every label's text is missing on all 30 labels
- MEDIUM | all | p1 | label columns pulled inward (~30px: col1 text sits 32px right, col3 34px left of Word, rules move with them) and the whole grid starts ~20px lower
- MAJOR | html | - | flourish ornament images missing (text and column rules are present)

### labels/13

- ✅ FIXED | all | p1 | purple curved-arrow clip-art now honours its a:xfrm/@flipV and curves up-right like Word in all five purple-arrow labels (see systemic #6b)
- MAJOR | html | - | same purple arrow rendered vertically flipped in the HTML export (the HTML exporter applies no transforms — rotation or flips — to pictures)
- MINOR | all | p1 | arrow images drawn a few px (~4-8px) off their Word position inside each label (ghosting on every icon)
- MINOR | imagesharp | p1 | name/address text block sits ~5-10px lower than Word on every label

### labels/14

- ✅ FIXED | all | p1 | the label sheet renders — navy cards, lavender/pink/yellow waves and white captions on all 6 labels (cell-anchored group hoisted + doc-parser group authority, systemic #5). Wave layout inside the cards now matches Word too: each card's wave sub-group carries `rot="16200000"` (270°) that the transform walk used to drop — waves flowed horizontally instead of vertically down the card's right side (nested-group rotation composition, 0.51→0.053 per backend).
- MAJOR | html | - | background artwork missing and text emitted white-on-white — export appears completely blank

### labels/15

- MINOR | all | p1 | script "from" glyph drawn ~9px further left and slightly wider than Word, nearly touching the preceding label's text; text rows/columns otherwise align within 1-2px
- MINOR | html | - | cream page background stops about two-thirds down the strip, leaving the last rows of labels on a white background

### labels/16

- MEDIUM | all | p1 | every bear icon drawn 30px (~0.2 in) lower than Word — icons hang below their label text instead of centering beside it (text rows themselves align exactly)
- MAJOR | all | p1 | stray pink-bear fragment (ears/head outline) visible behind the upper-left of the first label's blue bear — extra image content not present in Word
- MAJOR | html | - | all 30 bear icons missing from the HTML export (colored text only)

### left_indent

- MINOR | all | p1 | text rendered ~10% narrower (lines end ~60px short of Word) and paragraph pitch tighter so the 4-line block ends ~17px higher; the 0/0.5"/1" indents themselves are exact
- CLEAN: html

### letters/01

- MAJOR | all | p1 | decorative header and footer shape bands rendered in wrong colors (vivid purple-blue/lime-green/white instead of Word's slate-blue/charcoal/lavender palette)
- MEDIUM | all | p1 | sender and recipient address blocks use ~40% larger line pitch, pushing all following content progressively lower (~4-5 text lines by the signature)
- MAJOR | html | - | decorative header/footer shape graphics missing entirely
- MINOR | html | - | right-aligned sender block (Date/Your Name/Address/Phone) rendered left beside the logo instead of right-aligned

### letters/02

- MAJOR | all | p1 | dark-brown rounded border frame with spirograph swirl ornament missing entirely (page renders plain white)
- MINOR | all | p1 | body text block uniformly shifted down ~half a line
- MAJOR | html | - | same brown frame/swirl ornament missing entirely
- MINOR | html | - | right-aligned "Letter of Recommendation" title and Date lose their alignment (title centered-left, Date lands beside recipient block)

### letters/03

- MEDIUM | all | p1 | word "Service" broken mid-word across lines without hyphen ("...reach out to our Customer S / ervice team") in paragraph 2
- MINOR | all | p1 | body text block uniformly shifted down ~two-thirds of a line
- MAJOR | html | - | top and bottom blue gradient banner graphics missing entirely

### letters/04

- MAJOR | all | p1 | footer contact strip pushed down into the navy footer band — "Portland, OR 76543" rendered overlapping the band, phone/email baseline clipped by it
- MEDIUM | all | p1 | expanded letter-tracking of header lost ("A M A R I  R I V E R A" / "D i g i t a l  M a r k e t i n g"): skia/imagesharp substitute one huge word gap, pdf renders compact normal spacing
- MEDIUM | all | p1 | body content drifts progressively lower (~2-3 line heights by the signature) though wrapping matches
- MAJOR | html | - | recipient address block and date rendered overlapping the navy header banner (first lines sit on top of the band)

### letters/05

- MAJOR | all | p1 | logo cluster malformed: blue circle and orange dot drawn as squares, and purple circle + teal square outline that Word hides are exposed
- MAJOR | all | p1,p2,p3 | orange square outline at top-right page edge missing
- MAJOR | all | p1,p2,p3 | all dashed decorative shapes missing (teal dashed arc top-left, teal dashed segment mid-right, purple dashed segment bottom-left)
- MAJOR | skia,pdf | p2,p3 | teal triangle outline at left margin missing (rendered on p1 only)
- MAJOR | imagesharp | p3 | teal triangle outline at left margin missing (present on p1,p2)
- MAJOR | html | - | logo cluster malformed into a blue blob (overlapping circle + rectangle), orange dot as square, hidden teal/purple shapes exposed
- MAJOR | html | - | purple decorative circles overlap the "Taylor Phillips" address text in the second letter section
- MEDIUM | html | - | decorative shapes inconsistent across sections: dashed elements absent everywhere and the third letter section has no shapes at all

### letters/06

- MINOR | all | p1 | "SCHOOL OF" and "FINE ART" display titles shifted a few px with tracking deviations (skia/imagesharp widen the word gap, pdf slightly tighter than Word)
- CLEAN: html

### letters/07

- MEDIUM | all | p1 | second body paragraph wraps to 4 lines vs Word's 3 (text breaks earlier, column effectively narrower)
- MEDIUM | pdf | p1 | first paragraph wraps to 6 lines vs Word's 5, pushing the closing signature/address block ~2 lines lower
- MEDIUM | skia,imagesharp | p1 | header title inter-word spacing nearly collapsed ("ESG FinancialServices")
- MINOR | html | - | paragraph spacing collapsed so the letter reads as one continuous block (no gaps before "Adrian's...", "Sincerely," or the address)
- MINOR | html | - | left address column loses its right-alignment and the header pattern band stops short of full page width

### letters/08

- MEDIUM | all | p1 | first body paragraph wraps to 3 lines vs Word's 2 ("...recent visit to New / York.") and second paragraph breaks at different words, shifting the letter body
- MEDIUM | all | p1 | large signature "Joseph Price" rendered in bold/heavy weight instead of Word's light strokes
- ✅ FIXED | pdf | p1 | letter no longer renders whole-bold: "Grandview Display" resolves through the FontFallbacks alias to Grandview instead of falling to the bold default face (see systemic #22)
- ✅ FIXED | pdf | p1 | digits now render at the same proportions as adjacent letters (same root cause — the bold-default fallback's digit metrics; see systemic #22)
- MINOR | html | - | signature "Joseph Price" bold vs Word's light weight
- MINOR | html | - | inter-paragraph spacing collapsed — body paragraphs run together

### letters/09

- MEDIUM | all | p1 | first body paragraph wraps to 5 lines vs Word's 4 ("...advanced financial / forecasting."), pushing body text and the footer contact block ~1 line lower
- MINOR | html | - | lavender page background does not cover the leftmost ~95px (white strip at left edge)
- MINOR | html | - | paragraph spacing collapsed — salutation and paragraphs run together

### letters/10

- MAJOR | all | p1 | white content card missing — page renders almost entirely grey (only a white strip at top), while Word shows a large white card inset on the grey background
- MEDIUM | all | p1 | body wraps differ (first paragraph 5 lines vs Word's 4, breaks at "regional / manager"), shifting text and footer rule/contact block lower
- MAJOR | html | - | signature image broken — placeholder "Image of signature" shown instead of the script signature
- MINOR | html | - | grey page background / white card styling not exported (plain white page)
- MINOR | html | - | recipient block lines separated by full blank-line gaps vs Word's tight block

### letters/11

- MAJOR | all | p1 | orange flower/asterisk logo at top-left missing
- MAJOR | all | p1 | multicolor decorative tile strip (flowers/waves/palms) across the page bottom missing
- MINOR | all | p1 | body lines break at slightly different words (para 1 breaks after "Importers" vs "Importers to"), same line counts
- MINOR | html | - | recipient address block lines separated by paragraph gaps vs Word's tight lines

### letters/12

- MAJOR | skia,imagesharp | p1 | logo renders "VanArsdelLtd." — space after the comma dropped and comma collides with the "L" (PDF renders it correctly)
- MEDIUM | all | p1 | body text drifts progressively lower — "Jordan Mitchell / CEO" signature ends ~2 lines below Word (bottom contact block stays in place)
- MEDIUM | all | p1 | bottom-right diagonal-stripe corner decoration shifted ~0.5 inch toward the corner and partially cut off — noticeably less visible than Word
- MAJOR | html | - | bottom-right diagonal-stripe corner decoration missing entirely
- MINOR | html | - | paragraph spacing collapsed — paragraphs run together

### letters/13

- MAJOR | pdf | p3 | left-edge vertical banner missing — drawn instead as an unrotated horizontal strip mid-page, overlapping/obscuring the "You can also change the colors..." paragraph
- MINOR | skia,imagesharp | p1,p2,p3 | whole content block (text and NP logo) sits ~1-1.5 lines higher than Word
- MINOR | pdf | p1,p2 | content block ~1 line higher than Word
- MINOR | all | p1,p2,p3 | hatched (striped) yellow banner wedges render as solid fills — skia loses both wedges of the leftmost tile, imagesharp/pdf the top wedge (pdf p3 banner absent entirely)
- MINOR | imagesharp | p1,p3 | several paragraphs wrap at different words than Word (e.g. "...personal taste. Go /", "built-in font / combination")
- MAJOR | html | - | first letter copy's body text squeezed into a ~170px-wide column at the right edge, wrapping every 2-4 words; the three letter copies get inconsistent column widths
- MINOR | html | - | page-3 left-edge banner rendered as an inline horizontal strip (side placement/rotation lost)
- MINOR | html | - | paragraph spacing collapsed — recipient block, date and salutation run together

### line_breaks

- MINOR | all | p1 | line spacing slightly tighter than Word — lines drift upward progressively, final paragraph ~1/3 line higher
- CLEAN: html

### line_numbers_continuous

- ✅ FIXED | pdf | p1 | line-number gutter now renders (was: entirely missing) — values 1–20 off by one vs Word's 2–21 (#17, same as the raster backends). See systemic #18.
- MAJOR | skia,imagesharp | p1 | Line-number values off by one: rendered 1–20 vs Word's 2–21 (Word applies the lnNumType start offset, Morph does not)
- MEDIUM | all | p1 | Line pitch ~10% tighter than Word (≈47px vs 52px at 150dpi), so the 20-line block drifts upward progressively and ends ~2 line heights higher
- MINOR | all | p1 | Body text runs ~9% narrower than Word (e.g. "Line 1 - continuous line numbering." 346px vs 382px), every line ending falls short
- MINOR | skia,imagesharp | p1 | Margin number digits rendered noticeably smaller than Word's (Word draws them at body-text size)
- MINOR | html | - | Line-number gutter omitted entirely in HTML export (per-layout-line feature; HTML reflows)

### line_numbers_count_by_5

- ✅ FIXED | pdf | p1 | line numbers now render (was: missing entirely) — shown at 1, 6, 11, 16 vs Word's 5, 10, 15, 20 (#17: off-by-one + interval anchored to w:start, same as the raster backends). See systemic #18.
- MAJOR | skia,imagesharp | p1 | Count-by-5 numbering shows wrong values on wrong lines: 1, 6, 11, 16 beside Paragraphs 1/6/11/16 vs Word's 5, 10, 15, 20 beside Paragraphs 4/9/14/19
- MEDIUM | all | p1 | Line pitch ~10% tighter, 20-line block ends ~2 line heights higher than Word
- MINOR | all | p1 | Body text ~9% narrower than Word, line endings fall short
- MINOR | skia,imagesharp | p1 | Margin digits smaller than Word's body-size digits
- MINOR | html | - | Line-number gutter omitted entirely in HTML export

### line_numbers_custom_distance

- ✅ FIXED | pdf | p1 | line numbers now render at the custom 0.5in distance (was: missing entirely) — values 1–20 off by one vs Word's 2–21 (#17, same as the raster backends). See systemic #18.
- MAJOR | skia,imagesharp | p1 | Line-number values off by one: 1–20 vs Word's 2–21 (the 0.5in number-to-text distance itself is honoured)
- MEDIUM | all | p1 | Line pitch ~10% tighter, 20-line block ends ~2 line heights higher than Word
- MINOR | all | p1 | Body text ~9% narrower than Word, line endings fall short
- MINOR | skia,imagesharp | p1 | Margin digits smaller than Word's
- MINOR | html | - | Line-number gutter omitted entirely in HTML export

### line_numbers_restart_page

- ✅ FIXED | pdf | p1 | line numbers now render (was: missing entirely) — values 1–18 off by one vs Word's 2–19 (#17, same as the raster backends). See systemic #18.
- MAJOR | skia,imagesharp | p1 | Line-number values off by one: 1–18 vs Word's 2–19
- MEDIUM | all | p1 | Line pitch ~10% tighter, 18-line block ends ~1.7 line heights higher than Word
- MINOR | all | p1 | Body text ~9% narrower than Word, line endings fall short
- MINOR | skia,imagesharp | p1 | Margin digits smaller than Word's
- MINOR | html | - | Line-number gutter omitted entirely in HTML export

### line_numbers_restart_section

- ✅ FIXED | pdf | p1,p2 | line numbers now render on both pages (was: missing entirely) — values off by one vs Word (#17, same as the raster backends). See systemic #18.
- MAJOR | skia,imagesharp | p1,p2 | Line-number values off by one on both sections: 1–15 vs Word's 2–16 (restart-per-section itself works)
- MAJOR | skia,imagesharp | p1 | Stray orphan line number "16" rendered beside an empty line below "Section 1, Paragraph 15." where Word shows nothing (section-break paragraph gets numbered)
- MEDIUM | all | p1,p2 | Line pitch ~10% tighter, 15-line block ends ~1.4 line heights higher than Word on each page
- MINOR | all | p1,p2 | Body text ~9% narrower than Word, line endings fall short
- MINOR | skia,imagesharp | p1,p2 | Margin digits smaller than Word's
- MINOR | html | - | Line-number gutter omitted entirely in HTML export (both sections' text content present and ordered correctly)

### line_numbers_suppressed

- ✅ FIXED | pdf | p1 | line numbers now render on the five numbered lines with the two suppressed paragraphs correctly skipped (was: missing entirely) — values 1–5 off by one vs Word's 2–6 (#17, same as the raster backends). See systemic #18.
- MAJOR | skia,imagesharp | p1 | Line-number values off by one: 1–5 vs Word's 2–6 (suppression of the two suppressLineNumbers paragraphs is correctly honoured)
- MINOR | all | p1 | Line pitch slightly tighter — last line "Line 5 - Final normal paragraph." sits ~0.6 line height higher than Word
- MINOR | all | p1 | Body text ~9% narrower than Word
- MINOR | skia,imagesharp | p1 | Margin digits smaller than Word's
- MINOR | html | - | Line-number gutter omitted entirely in HTML export

### line_spacing

- MEDIUM | all | p1 | Double-spacing (2.0) paragraph renders on one line instead of two — Word wraps "readability." to a second line but Morph's ~9% narrower text fits the whole sentence on line one, so the wrapped line (and the double-spaced gap before it) is absent
- CLEAN: html

### line_spacing_at_least

- MEDIUM | all | p1 | "At least" line spacing under-applied: gaps between the 12/18/24/36pt paragraphs are ~15–25% smaller than Word's (e.g. 24pt→36pt gap ≈66px vs Word's ≈88px at 150dpi), leaving the 36pt paragraph ~1 line height higher
- MINOR | all | p1 | Body text ~9% narrower than Word, line endings fall short
- CLEAN: html

### line_spacing_exactly

- MEDIUM | all | p1 | "Exactly" line spacing under-computed: lines drift progressively upward vs Word — 24pt-spaced line ~6px high, 36pt-spaced line ~18-21px (nearly a full line) high, so the block ends clearly higher
- CLEAN: html

### long_paragraph

- MEDIUM | all | p1 | paragraph wraps differently from Word: text advance too narrow so every line fits 1-2 more words, line breaks differ from line 1 and total is 25 lines vs Word's 27
- MEDIUM | all | p1 | line pitch ~14% tighter (30.2px vs 35.3px at 150dpi), paragraph ends ~196px (~5 line heights) higher than Word
- CLEAN: html

### menus/01

- MAJOR | all | p1 | grey floral line-art illustration covering the top-left quarter of page 1 is missing entirely (area renders blank)
- MEDIUM | all | p1,p2,p3 | entire page content (text and, on p2/p3, the floral art) sits ~30-65px (150dpi) higher than Word with slightly compressed section spacing; offset is largest at the p3 title (~60px)
- MAJOR | html | - | light-grey page background missing: all text renders on white, only the first flower image block carries the grey (Word shows grey behind all 3 pages)

### menus/02

- MAJOR | all | p1 | bottom fireworks band badly mis-rendered: large light-blue fine-line burst missing, dotted bursts drawn as oversized solid brown polka dots covering the left half, and one of the two large dark line-bursts on the right missing
- MEDIUM | all | p1 | header ("New Year's Eve / CELEBRATION / MENU") and all menu items rendered left-aligned at a fixed indent instead of centered, putting the text column ~0.5-1.5" left of Word's position
- MAJOR | html | - | same fireworks mis-render in HTML export (brown dot spray, missing blue line burst and dark burst)
- MEDIUM | html | - | menu header and items left-aligned instead of centered

### menus/03

- MAJOR | all | p1 | "EVENT INTRO" / "EVENT DATE" labels and the large gold "EVENT TITLE" heading are missing; their gold rule lines render but misplaced and mis-sized
- MAJOR | skia,pdf | p1 | full-height gold divider line between the two columns is missing (only a short gold tick at top-right remains); ImageSharp draws it
- MEDIUM | all | p1 | both text columns shifted left (instructions column ~25% of page width left of Word) and vertically compressed (menu column ends ~65px high)
- MINOR | skia,imagesharp | p1 | numbered steps render the number at the left indent with the step text centered separately, leaving a large gap (Word centers "2. Press Ctrl+C" as one unit; PDF matches Word)
- MINOR | skia,pdf | p1 | full-page navy background tint fractionally off Word's (em≈1.0 but below visible threshold)
- MAJOR | html | - | all content renders below the navy panel on the white page, leaving the navy block empty and every white-colored text run invisible (only gold headings and step titles visible)
- MAJOR | html | - | "EVENT TITLE" / "EVENT INTRO" / "EVENT DATE" also missing from the HTML export

### menus/04

- MAJOR | all | p1 | faint vegetable-doodle pattern behind the title/header band is missing entirely in all backends
- MEDIUM | all | p1 | colored meal cells end ~43px (~0.29") short on the right (fill to x=567 vs Word's 610), leaving a white strip along every week table
- MINOR | all | p1 | week tables drawn ~13-20px higher with slightly shorter rows; upward drift accumulates down each table
- MAJOR | html | - | table layout broken: the colored meal-entry column collapses to ~30px stubs instead of wide writing areas
- MAJOR | html | - | vegetable-doodle header background missing in HTML too
- MINOR | html | - | stray light-grey rectangle rendered below the week tables

### menus/05

- MAJOR | pdf | p1,p3 | DESSERTS section drifts ~1in low and its body text is drawn on top of the bottom decorations (birds/leaves on p1, cornucopia+birds on p3)
- MAJOR | skia,imagesharp | p3 | DESSERTS body text overlaps the cornucopia and bird decorations at the page bottom
- MEDIUM | skia,imagesharp | p1 | sections accumulate ~80px downward drift — DESSERTS heading/body sit ~3 line heights lower than Word, last line grazes the bird decoration
- MINOR | all | p2 | two-column sections drift down slightly (~10-25px by the DESSERTS block), structure intact
- MINOR | all | p3 | side border decorations (corn, latte cup, leaves, wheat, berries) shifted ~10-15px from Word positions
- MEDIUM | html | - | page-1 and page-3 section headings render as "Appetizer"/"First Course" in a fallback bold font, losing the decorative all-caps display font Word uses (page-2 headings are correct)
- MEDIUM | html | - | menu title/text emitted below the green blob instead of overlaid on it, and page-2's orange blob is linearized above page-1 content (anchored art separated from its text)

### menus/06

- MAJOR | all | p1 | red accent bar above the "BISTRO MENU" title (bleeds off top edge in Word) missing entirely
- MAJOR | all | p2 | pale-blue full-page background missing — page renders white (p1/p3 keep it)
- MAJOR | all | p3 | full-width red bars at the top and bottom of the page missing entirely
- MAJOR | pdf | p3 | sheep logo at bottom right rendered white instead of red — nearly invisible on the pale background (Skia/ImageSharp render it red)
- MEDIUM | all | p1,p2,p3 | expanded letter-spacing not applied: "BISTRO MENU" title, red section headings ("DRINKS", "Salads", ...) and "EST." / "1981" all render tighter than Word's tracked-out text
- MINOR | all | p1,p2,p3 | menu items drift progressively downward (~half a line by page bottom)
- MAJOR | html | - | red accent bars missing (p1 top bar, p3 top and bottom bars)
- MEDIUM | html | - | pale-blue background covers only page-1 content; page-2/3 content renders on white

### menus/07

- MAJOR | all | p1 | chalkboard background shape shifted right ~100px and clipped at the right page edge — wood margin visible only on the left (Word centers the board with wood on all sides)
- ✅ FIXED | all | p1 | "Menu", "Appetizer", "Main Course", "Dessert" now render white chalk lettering on the dark board (white docDefaults honoured). See systemic #7.
- MAJOR | all | p1 | inner decorative frame (gray double border with green and red corner accent curves) missing entirely
- MAJOR | all | p1 | mint wavy separator squiggles between sections missing
- MEDIUM | all | p1 | heading/caption text block shifted left ~70-110px and captions left-aligned instead of centered
- MINOR | all | p1 | food photos shifted right ~15-25px
- ✅ FIXED (colour) | html | - | headings and captions now export white; the dark board behind them is still missing from the HTML export.
- MAJOR | html | - | inner decorative frame and mint wavy separators missing

### menus/08

- ✅ FIXED | all | p1 | "EVENT TITLE" and section headings (APPETIZER, FIRST COURSE, MAIN COURSE, DESSERT) now render white on the navy background (white docDefaults honoured). See systemic #7.
- ✅ FIXED | all | p1 | menu item lines ("List or describe...", "Appetizer item") now render white (white docDefaults honoured). See systemic #7.
- MEDIUM | all | p1 | both text columns lose their centered placement — left menu block shifted ~1.4in left to hug the page edge, right instruction block shifted ~2in left with different line wraps ("...select the whole / cell.)")
- ✅ FIXED (colour) | html | - | EVENT TITLE, section headings and EVENT INTRO/EVENT DATE labels now export white; the navy panel behind them is still missing from the HTML export.
- MEDIUM | html | - | item text now exports white (✅ colour fixed, systemic #7); the left menu column still loses its centered alignment

### menus/09

- ✅ FIXED | all | p1 | "Menu", "Appetizer", "Main Course", "Dessert" now render white chalk lettering (white docDefaults honoured). See systemic #7.
- MAJOR | all | p1 | inner decorative frame (light double border with mint/red corner curves) missing entirely
- MAJOR | all | p1 | two mint wavy separator squiggles between sections missing
- MEDIUM | all | p1 | content block (headings, captions, circular icons) shifted left ~0.5in and captions left-aligned instead of centered under headings
- MINOR | all | p1 | chalkboard ~16px narrower and ~9px shorter than Word (right/bottom edges pulled in)
- ✅ FIXED (colour) | html | - | headings and captions ("Home cook name...", "Describe your...") now export white; the dark board behind them is still missing from the HTML export.
- MAJOR | html | - | inner decorative frame and wavy separators missing

### mixed_breaks

- MEDIUM | all | p3 | "Content after column break." starts ~1.5 line heights higher than Word, which places the text lower on the page after the column break
- CLEAN: html

### multiple_images

- MINOR | all | p1 | content block drifts upward from slightly tighter paragraph spacing, leaving the "Sample" image ~20px (about half a line) higher than Word
- CLEAN: html

### multiple_pages

- MEDIUM | all | p1,p2,p3,p4,p5 | line-break points differ in every paragraph — backends fit more words per line (first line ends "...eiusmod tempor" vs Word's "...eiusmod"), i.e. text measures narrower than Word
- MEDIUM | all | p1,p2,p3,p4,p5 | vertical spacing is tighter so 12 paragraphs fit per page vs Word's 11 — flow pulls ahead one paragraph per page and page 5 holds only paragraphs 49-50 vs Word's 45-50
- CLEAN: html

### multiple_paragraphs

- MINOR | all | p1 | Paragraph spacing slightly tighter than Word, producing a cumulative upward drift (~8-10px by the 4th paragraph); line breaks and structure intact
- CLEAN: html

### nested_list

- ✅ FIXED | all | p1 | Level-3 list marker for "Sub-sub-item 2.1.1" now renders the filled square (Bullets.ttf routing). See systemic #11.
- MINOR | all | p1 | List lines drift progressively upward (~15px by the last item) from slightly tighter line/paragraph spacing
- CLEAN: html

### newsletters/01

- MAJOR | all | p1,p2,p3,p4 | White frame borders missing from every photo; images rescale to fill the full frame box so the visible crop differs (e.g. p3 mother-and-daughter photo shows extra scene at smaller scale, p1 kitchen photo sits flush with the tan panel edge)
- MEDIUM | all | p2 | Right-column photo box rendered much shorter (landscape instead of Word's taller portrait box), pulling its caption and the whole "Adding your own message" section up ~130px
- MEDIUM | all | p1 | Left-column caption, "Happy holidays from our family to yours!" heading and body text sit ~50px higher than Word due to the resized kitchen photo
- MEDIUM | all | p1 | Sidebar pull-quote "A favorite family phrase..." wraps to 4 lines vs Word's 3
- MEDIUM | skia,imagesharp | p4 | Right-column article ("Write with ease using Editor" heading + body) sits ~2 lines higher than Word
- MEDIUM | pdf | p1,p4 | Right-column blocks drift ~2 lines lower than Word (p1 pull-quote, p4 big photo + caption end noticeably lower)
- MINOR | pdf | p1 | Sidebar bullet markers render as much smaller dots than Word's solid bullets
- MINOR | all | p1,p2,p3,p4 | "Page N" footer sits ~25-35px higher, overlapping the content panel bottom instead of sitting on the grey footer band
- MINOR | all | p1,p2,p4 | Rounded white snow-drift blobs at the page edges are narrower/shifted (worst on PDF, e.g. wide red sliver left of the p1 bottom-left blob, p4 corner pillars)
- MINOR | all | p1,p2,p4 | Body paragraphs re-wrap at different words than Word (line counts mostly unchanged)
- MAJOR | html | - | "Our family newsletter" title and "December 20XX" date are invisible — emitted as white (#ffffff) text with no red background behind them
- MAJOR | html | - | Background panels detach from content: an empty green/pink panel composition renders at the very top of the document, and page 1/2/4 text flows on plain white without its red/green page backgrounds (only page 3's red block wraps its photos)
- MEDIUM | html | - | Decorative illustrations (Santa, snowman, penguins, elves) render as detached images floating between sections instead of positioned inside their page compositions

### newsletters/02

- MAJOR | all | p2 | "The observer" byline and paragraphs are shifted ~80px left, overlapping the right edge of the numbers photo
- MEDIUM | all | p2 | Main-column paragraphs wrap to more lines than Word (bold lede 6 lines vs 5; following paragraph 5 vs 4)
- MEDIUM | all | p2 | "Work with the industry's best" column renders wider with fewer, longer lines, so the column ends ~1in higher than Word
- MEDIUM | all | p2 | Numbers photo content is zoomed in (tighter crop showing fewer digits than Word's version of the same image)
- MINOR | all | p1 | Entire page content (masthead, sidebar, hero image, article) sits ~10px higher than Word
- MEDIUM | html | - | Hero network-figures image renders above "The Review" masthead — wrong content order vs the document

### newsletters/03

- MAJOR | all | p1,p2,p3,p4 | Every black-and-white photograph is missing (p1 cover skyscraper photo, p2 skyline + ceiling photos, p3 abstract photo, p4 both full-width banner photos) — areas render as flat blue rectangles or blank white; captionless placeholders only
- ✅ FIXED | all | p1,p2,p3,p4 | decorative hexagons now render as hexagons (blue-filled, thin blue-outline, white outlines over photos, black outline, and the small hex beside "INSIDE THIS ISSUE") via `PresetShapeGeometry` — see systemic #6
- MAJOR | all | p4 | The wrong-aspect blue rectangle (should be a wide photo banner) extends down into and overlaps the "RISING STARS IN ARCHITECTURE" heading text
- MEDIUM | all | p1,p3 | Body text wraps to more lines than Word (p1 INDUSTRY NEWS lead paragraph 2 lines -> 3, both paragraphs; p3 HARNESSING "Have other images..." paragraph gains a line pushing "Once the image..." down); remaining pages show shifted break points from the same ~7% wider text
- MAJOR | html | - | All photographs missing (blue rectangles/blank), hexagon shapes rendered as rectangles
- MAJOR | html | - | "RISING STARS IN ARCHITECTURE" heading overlaps the blue rectangle placeholder
- MINOR | html | - | Inter-paragraph spacing lost — consecutive body paragraphs run together with no gap

### newsletters/04

- MAJOR | all | p1,p2,p3 | All inset photos missing: p1 round celery/smoothie photo in the Breaking-news sidebar, p2 round jug photo in sidebar and square portrait of the woman in the "next hot" section, p3 round herb photo in the grey box — blank space left, captions still render
- MAJOR | all | p3 | Grey "Breaking news" table section grows past the bottom margin: column text is clipped mid-line at the page edge and the page footer ("3 ——— Issue 10") is missing entirely
- MAJOR | all | p3,p4 | Spurious dark table cell borders drawn around section cells (boxes around byline column, text columns, pull-quote circle cell, "Save time..." cell, grey-box cells) — Word renders these tables borderless
- MEDIUM | all | p1,p2,p3,p4 | Body text renders ~7% wider than Word, changing line breaks everywhere and redistributing text across the newspaper columns (scoop/next-hot columns and sidebars break at different paragraphs, blocks end noticeably lower)
- MINOR | all | p1,p2,p3,p4 | Full-width banner photos slightly oversized vs Word (right edge extends further, solid strip in diffs; captions shift down accordingly)
- MAJOR | html | - | Same four inset photos missing (round celery, round jug, woman portrait, round herb); captions orphaned
- MAJOR | html | - | Pull-quote circular beige background missing — quote renders as plain text in a bordered cell
- MEDIUM | html | - | Spurious table cell borders visible around the "next hot" and Breaking-news section cells
- MINOR | html | - | Inter-paragraph spacing lost — paragraphs run together with no gap

### newsletters/05

- MAJOR | all | p3 | pale-blue full-height sidebar band and pale-blue bottom/corner quarter-circle shapes missing entirely (rendered white, only the dark-blue shapes drawn) — expected fill (206,229,246) samples as (255,255,255) in all three backends
- MEDIUM | all | p1,p3 | body copy under "Welcome back to school!" wraps to one extra line (17 text bands vs Word's 16), block ends 26-80px lower
- MEDIUM | skia,imagesharp | p1,p3 | "Welcome back to school!" heading and body start ~45-50px lower than Word (extra gap inserted below the school photo)
- MEDIUM | skia,imagesharp | p1,p3 | sidebar "Ms. Tanaka" contact block ~22px and "Upcoming Events" block ~48px lower than Word (PDF within 10px)
- MEDIUM | all | p2,p4 | sidebar "Fall highlights" block sits ~210-235px (~1.5 inch) higher than Word
- MEDIUM | skia,imagesharp | p2,p4 | "Our next area of focus" heading and following paragraphs ~35-38px lower than Word (extra gap below classroom photo; PDF matches)
- MINOR | all | p1,p3 | school photo ~1% narrower than Word (right edge 7-9px short), lighting the whole photo in the diff
- MINOR | all | p2,p4 | classroom photo shifted ~9px left at identical size
- MAJOR | html | - | first (green) edition's chrome rendered in the blue edition's colors: pale-blue sidebar band and dark-blue corner shapes instead of lime band and dark-green shapes (text accents stay correctly green)
- MEDIUM | html | - | decorative page-corner shapes render mid-flow and overlap body text ("Recent highlights" heading/paragraph runs across a pale-blue quarter-circle)

### newsletters/06

- MAJOR | all | - | page-count mismatch: all three backends produce 6 pages vs Word's 4 (each 2-page edition spills onto a 3rd page; text sets ~10% wider so every column wraps earlier and blocks run longer)
- MAJOR | all | p1,p2,p3,p4,p5,p6 | every decorative line-art icon (balloons, bell, backpack, stacked books, globe) renders as a solid navy filled square in a square frame instead of the circled line drawing
- MAJOR | all | p5 | "HIGHLIGHTS" section heading overlaps the adjacent column's body text (word "viverra" hidden behind the heading) on the yellow-edition second page
- MEDIUM | all | p1,p4 | masthead navy bar text loses its per-letter tracking: skia/imagesharp render bold words with huge word gaps, pdf renders compact bold text with no letter-spacing
- MEDIUM | all | p1,p4 | masthead contact line wraps to two lines ("www.sycamoremiddle.org" drops to its own line) vs one line in Word
- MEDIUM | all | p2,p5 | "NOTES FROM THE COUNSELORS" left column collapses to ~1-2 words per line (column far too narrow) and the text snakes to the bottom of the page
- MEDIUM | all | p3,p6 | overflow spill pages render on a white background instead of the section's page color (light blue / yellow)
- MINOR | all | p1,p4 | dotted-ornament window around "SYCAMORE NOTES" title is taller than Word's (dot rows beside the title partially dropped); small "SYCAMORE NOTES" strip logo on p2/p5 wraps to two lines
- MAJOR | html | - | page backgrounds wrong/missing: first edition gets a yellow background instead of light blue, and all content after the first section break renders on white (no blue/yellow backgrounds)
- MAJOR | html | - | all decorative icons render as filled squares (same placeholder issue as raster backends)
- MINOR | html | - | big title renders as "SYCAMORENOTES" — the word space collapses to the same width as the letter-spacing gaps

### newsletters/07

- MAJOR | all | p2 | kitchen photo missing Word's warm color treatment and tighter crop — renders in cool neutral grey-blue tones with a wider field of view
- MEDIUM | all | p1 | "MODERN LIVING" masthead, subtitle and sidebar headings ("WHAT'S NEW", "TAKE A LOOK INSIDE"...) lose expanded letter-spacing: skia/imagesharp show bold words with big word gaps, pdf compact bold
- MEDIUM | all | p1 | translucent grey sidebar band draws in front of the living-room photo, tinting its right portion (expected (234,230,227) vs (211,211,213)); Word draws the photo on top
- MEDIUM | imagesharp,pdf | p1,p2 | body and sidebar text rendered in bold weight throughout vs Word's regular Century-Gothic-style face
- MEDIUM | all | p1,p2 | body text sets visibly larger than Word so paragraphs wrap to extra lines (lead paragraph 6 lines vs Word's 5)
- MINOR | all | p2 | paint-roller photo ~8-10% shorter at identical width (vertically squashed/cropped: 203px vs 183-187px tall)
- MEDIUM | html | - | content order wrong: living-room photo renders above the "MODERN LIVING" masthead/title block instead of below it
- MEDIUM | html | - | kitchen photo shows the same cool/uncropped rendering as the raster backends (Word's warm filter absent)
- MINOR | html | - | black accent rule renders overlapping the "Your guide to buy or rent" subtitle text; paint-roller photo shown at a taller aspect than Word's wide crop

### newsletters/08

- MAJOR | all | p1 | cover photo (drill/blueprints/screws image filling the left half of the page) entirely missing — area left white
- MEDIUM | all | p1 | right-column masthead block ("HOUSE & HOME NEWS / WINTER ISSUE / EDITION 09, VOL. 10") and intro paragraphs sit ~40px (≈2 line heights) higher than Word
- ✅ FIXED | pdf | p1,p2 | bold text resolves to proper 700-weight faces instead of a heavier/wider substitute (see systemic #22)
- MINOR | all | p1,p2 | decorative swoosh/band boundaries off by several px and the light-blue contact strip plus its text sit ~15px lower than Word
- MAJOR | html | - | cover photo missing from the export (only the logo placeholder image is emitted)
- MAJOR | html | - | page title "HOUSE & HOME NEWS" invisible — dark-navy h1 lands on the dark-navy background shape, only a letter fragment shows through the light swoosh
- MAJOR | html | - | "Join us on this journey..." paragraph invisible — white text on white background (no shape behind it)
- MEDIUM | html | - | background shapes misaligned with content: contact footer line has no light-blue band behind it, and "From interior design trends..." white text sits on pale blue instead of navy

### newsletters/09

- MAJOR | skia,imagesharp | - | page count 5 vs Word's 4 — all content lags one page behind from p2 onward
- MAJOR | pdf | - | page count 6 vs Word's 4, including a near-blank page 5 containing only the "Page 3" footer rule
- MAJOR | all | p1 | masthead "NEWS TODAY" wraps onto two lines (Word: single line), doubling the banner height and triggering the reflow
- MEDIUM | all | p1,p2,p3,p4 | body text wraps 1-2 words earlier per line in every column (wider glyph advances); headlines "New program launches" and "The scoop of the day" also wrap to two lines
- MAJOR | all | p1 | bottom teaser band (School budget / Police prevent crime / Athlete sets record) pushed to the page edge and clipped mid-content; bodies spill to p2 (PDF spills the entire band including headers)
- MEDIUM | all | p3,p4 | photo captions detach from their images and collide with neighbouring content (caption box overlaps the section rule above "Mirjam Nilsson" on p3; caption floats over the "scoop of the day" columns on p4)
- MEDIUM | skia,imagesharp | p5 | "Page" footer rule drawn mid-page striking through the right column's text, which continues below it
- MEDIUM | pdf | p6 | same "Page 4" footer rule drawn through the final column's text
- MAJOR | html | - | several article bodies dropped entirely: "Community rallies for charity" two-column body, Vanja Jovanovic's "The latest breaking news of the day" body (empty table row in export), and the Takuma Hayashi article's left-column paragraphs
- MEDIUM | html | - | teaser-band table column widths wrong — "Police prevent crime" column is ~one word wide, wrapping every word (Word has three equal columns)
- MINOR | html | - | full four-sided borders drawn around the bridge sidebar box and the pull-quote box where Word shows only top/bottom rules

### newsletters/10

- MEDIUM | imagesharp,pdf | p1 | the four green section headings ("Something that made me smile today…", "Currently dealing with...", "Thankful for...", "Looking forward to...") rendered bold instead of Word's light weight
- MINOR | all | p1 | content drifts progressively upward, ~15px by the bottom rule (each section slightly shorter than Word); DD/MM/YYYY and all rules offset
- MINOR | all | p1 | banner leaf photo cropped/scaled slightly differently and "My Journal" inter-word gap wider than Word
- MAJOR | html | - | "My Journal" title invisible — white h1 rendered below the leaf banner on the white page background instead of overlaid on the image
- MEDIUM | html | - | section headings bold dark-green instead of Word's light weight

### newsletters/11

- MAJOR | all | p1 | Hero greenhouse photo (woman in hoop-house, spans ~60% of page width in Word) is clipped to a ~55px sliver at the left page edge — the photo area renders as blank cream background
- MAJOR | all | p2 | Group photo (5 people) in the right column is clipped to a ~55px sliver at the right page edge — photo area renders blank, only edge sliver drawn
- MINOR | all | p1,p2 | Small uniform vertical offsets of text blocks (~1 line): p2 columns and header sit ~20px higher than Word, p1 byline "By Robin Zupanc" sits ~1 line lower
- MEDIUM | html | - | Floating photos emitted in wrong order: hero photo appears before the "LAWN AND LANDSCAPE" masthead, group photo appears before the "Tony's landscapes and more" headline

### newsletters/12

- MAJOR | all | p1 | Extra element: olive stripe graphic drawn full-height (y 37-706) down the left margin beside the title — Word does not display it at all
- MAJOR | skia | p1 | "ISSUE NO | MONTH - MONTH YEAR | VOLUME" line drawn overlapping the bottom of "TITLE HERE" (glyphs collide)
- MEDIUM | imagesharp,pdf | p1 | Title lines shifted ~40-50px down so "ISSUE NO..." line touches "TITLE HERE" (Word has ~45px clear gap)
- MAJOR | all | p1 | Purple dash/squiggle overlay at the hiker photo's top-right corner missing
- MAJOR | all | p1 | Light-grey hot-air-balloon line-art watermark behind the "OUR SERVICES" columns missing
- MAJOR | all | p2 | White dash/squiggle overlay on the couple photo's left edge missing
- MAJOR | all | p2 | Vertical purple dashed column right of the 4-photo grid missing (9k purple px in Word, 0 in all backends)
- MAJOR | all | p2 | Text right of the olive quote box is positioned left/too wide and drawn over the olive stripe graphic (text/graphic overlap, different line wraps than Word)
- MINOR | all | p2 | "MARGIE'S TRAVEL OFFERS..." section and 01-04 items shifted up ~1 line; quote text re-wrapped inside its box
- MAJOR | html | - | Pull-quote text ("We don't merely book your travel..." + "- Henriette Andersen") missing entirely; olive quote box renders empty
- MAJOR | html | - | Absolutely-positioned blocks collide: couple photo covers TOPIC 01 sidebar text, olive stripe/quote block drawn over hero photo, photo-grid images overlap TOPIC 03 and body text
- MEDIUM | html | - | Decorative overlay graphics missing (balloon watermark, purple squiggles on photo, white photo dashes, purple dash column)

### newsletters/13

- MAJOR | all | p1 | Arch-topped still-life photo (candle/baskets) at top center missing in all backends (only the background hatch pattern shows)
- MEDIUM | all | p1 | Expanded letter-spacing lost: "I N T E R I O R / D E S I G N / E X P O" and "D A Y  O N E".."F O U R" headings render with tight tracking instead of Word's wide tracking (HTML export preserves it, raster/PDF do not)
- MAJOR | html | - | Same arch still-life photo missing from the HTML export

### newsletters/14

- MAJOR | all | p1 | Child graduation photo (top right, overlapping the green header band) missing in all backends (orange sidebar extends up into its space)
- MAJOR | all | p1 | Coral fill of the "DECEMBER" banner missing — white label is left sitting on the green band/white boundary, barely legible
- MEDIUM | skia,imagesharp | p1 | Inter-word spaces collapsed in the green quote box: "gymnasiumwill", "experiencesfor", 'athletes"-'
- MEDIUM | pdf | p1 | Title rendered in a visibly wider/heavier font: "North Jenkins Newsletter" wraps 2 lines → 3 and the last line "Newsletter" overflows below the green banner onto white; "SPORTS & ACTIVITES"/"Holiday Recitals" headings similarly oversized (p2 too)
- MEDIUM | pdf | p2 | Kids photo vertically squashed to ~70% of Word's height (red-sweater area y887-1194 vs 803-1243; width unchanged) with content shifted ~85px down
- MINOR | skia,imagesharp | p1 | Second title line "Newsletter" indented 20px right of line 1 (Word left-aligns both at x=92)
- MAJOR | html | - | Child graduation photo missing
- MAJOR | html | - | Orange "Holiday Recitals" sidebar text clipped at its left edge — first characters of every line cut off ("y Recitals", "s out", "nel, include...")
- MAJOR | html | - | Page-2 footer text "You can easily change the formatting..." missing; its orange block is mispositioned, overlapping the quote area and the "SPORTS & ACTIVITES" heading
- MEDIUM | html | - | "DECEMBER" banner rendered as red outline only (no coral fill) and the quote loses its green box background (sits directly on orange)

### numbered_list

- MINOR | all | p1 | List line pitch ~10% tighter than Word — items drift upward cumulatively, item 4 sits ~22px (≈1 text line) higher
- CLEAN: html

### numbered_list_restart

- MEDIUM | all | p1 | Numbering itself correct (restart at 1, start at 10), but line pitch ~10% tighter accumulates over the 12 items — final item "12. Blue" ends ~59px (≈2.5 lines) higher than Word
- CLEAN: html

### numbered_list_tracking

- MEDIUM | all | p1 | List line spacing ~10% tighter than Word (47px vs 52px pitch at 150dpi); drift accumulates down the page so the last item "Gamma" sits ~63px (~1.2 line heights) higher than in Word
- MINOR | all | p1 | Glyph advance widths ~10% narrower than Word, so each list line ends slightly short of Word's (no wrap changes)
- CLEAN: html

### office_math

- MEDIUM | all | p1 | Built-up OMML fraction 1/2 (numerator stacked over denominator with fraction bar) is flattened to inline text "1/2"
- MEDIUM | all | p1 | Equations rendered in the italic body sans font instead of Cambria Math serif, and math operator spacing is dropped ("a²+b²=c²" instead of "a² + b² = c²")
- MINOR | html | - | Same math linearization in the HTML export: fraction as plain "1/2" and compact "a²+b²=c²" in italic body font

### page_a4

- MINOR | all | p1 | Text rendered ~10% narrower than Word (first line 465px vs 516px wide at 150dpi), so both lines end noticeably short; positions and wrapping otherwise identical
- CLEAN: html

### page_borders/01

- MINOR | all | p1 | Page border box drawn ~3px (~1.5pt) closer to the page edge on top/left (border rectangle slightly larger than Word's); thickness matches
- MAJOR | html | - | Decorative page border is missing entirely from the HTML export (only the sentence is emitted, no border around content)

### page_breaks

- MINOR | all | p1,p2,p3 | Text ~10% narrower than Word (e.g. p1 line 1 183px vs 204px), same systematic tracking deviation on every page; page-break placement and page count are correct
- CLEAN: html

### page_landscape

- MINOR | all | p1 | Text ~10% narrower than Word (line 1 429px vs 477px); landscape orientation and layout otherwise correct
- CLEAN: html

### page_legal

- MINOR | all | p1 | Text ~10% narrower than Word (line 1 413px vs 458px); legal page size and layout otherwise correct
- CLEAN: html

### page_letter

- MINOR | all | p1 | body text tracks ~8-9% narrower than Word, so each single-line paragraph ends ~0.4in short of Word's line end (no rewrap)
- CLEAN: html

### page_numbers

- MEDIUM | all | p1,p2 | paragraph spacing tighter than Word: page 1 holds paragraphs 1-28 vs Word's 1-25, so page 2 starts at paragraph 29 instead of 26
- MEDIUM | all | p1,p2 | footer line positioned ~0.3in lower than Word, close to the page bottom edge
- CLEAN: html

### paragraph_borders

- ✅ FIXED | pdf | p1 | paragraph borders now render — full box, thick blue left bar, red right bar, top/bottom rules, w:between group boxes, and the padded "16pt space on every edge" box all draw (was: all missing). See systemic #19.
- MEDIUM | all | p1 | vertical spacing around bordered paragraphs compressed (the 16pt-padded box is visibly shorter than Word's); cumulative drift leaves the last paragraph ending ~1in higher than Word
- MEDIUM | html | - | the three w:between paragraphs render as three separate fully-boxed paragraphs with white gaps instead of one merged box with single shared rules between adjacent paragraphs

### paragraph_spacing

- MINOR | all | p1 | paragraph gaps slightly tighter than Word (content drifts up ~0.1in by the 4th paragraph) and text tracks slightly narrower
- CLEAN: html

### pct_pos_offset

- MINOR | all | p1 | caption line tracks ~8% narrower than Word (ends ~0.3in short); the red and blue percentage-positioned squares themselves align with Word within a few px
- CLEAN: html

### postcards/01

- MINOR | skia,imagesharp | p1 | bottom two postcard images drawn with a whole-image vertical offset of ~0.1in vs Word (heavy displacement ghost over both bottom images; top row and PDF are aligned)
- MINOR | all | p2 | postcard-back placeholder text and hand-drawn address rules sit ~0.05-0.1in lower than Word, most visibly in the bottom row of cards
- CLEAN: html

### postcards/02

- MEDIUM | skia,imagesharp | p1 | bottom row of postcard images shifted up ~24px (inter-row gap 52px vs Word's 76px), so the two rows sit visibly closer together; PDF matches Word
- MINOR | skia,imagesharp | p2 | bottom-row card placeholder text and address lines sit ~7px higher than Word (same row-gap cause as p1)
- CLEAN: pdf, html

### postcards/03

- MEDIUM | skia,imagesharp | p1 | bottom row of postcard images shifted up ~23px (row gap collapsed, same defect as postcards/02); PDF matches Word's positions
- MEDIUM | all | p2 | placeholder "Click or tap here to enter text." rendered in a substituted bold dark sans, much larger than Word's small light script font, wrapping to 2 lines instead of 1
- MINOR | skia,imagesharp | p2 | bottom-row placeholder text and address lines sit ~7px higher than Word
- MEDIUM | html | - | same placeholder font substitution: heavy dark rounded font instead of Word's light handwriting script

### postcards/04

- MAJOR | all | p2 | boy photo rendered uncropped/too wide (393px vs Word's 315px at identical height, exposing extra background scenery) so it abuts the cupcake photo where Word has an 85px gap
- MEDIUM | all | p1,p2,p3 | vertical layout compressed on every page: card panels ~25px shorter, title-to-photo and inter-card gaps ~25-35px smaller, cumulating so the second card sits 60-140px higher than Word
- MINOR | all | p2 | cupcake photo ~5% wider (429px vs 408px) than Word
- MINOR | all | p3 | rightmost (tilted-boy) photo ~9px wider than Word
- MAJOR | html | - | p2-style cards show the same uncropped boy photo (wide field of view with extra trees) instead of Word's tight crop

### resumes/01

- MEDIUM | skia,imagesharp | p1 | subtitle expanded letter-spacing applied only at run boundaries, rendering "G ENERAL P RACTITIONER" instead of Word's evenly letter-spaced "G E N E R A L P R A C T I T I O N E R"
- MEDIUM | pdf | p1 | subtitle letter-spacing dropped entirely, making "GENERAL PRACTITIONER" markedly narrower than Word
- MEDIUM | pdf | p1 | summary paragraph re-wraps with narrower text ("Known for" pulled up to line 1; all three lines break at different words than Word)
- MINOR | all | p1 | body content drifts upward slightly (~8-15px by the bottom of the page)
- MEDIUM | html | - | employer/school heading lines ("Jasper University", "Bellows College", "Lamna Healthcare | General Practitioner", etc.) rendered italic where Word shows them upright bold

### resumes/02

- MINOR | all | p1 | header text and both body columns sit ~8-13px higher than Word (uniform upward drift; artwork and divider positions otherwise match)
- MAJOR | html | - | header text block (KAI CARTER, GENERAL PRACTITIONER, CONTACT, phone/website/email) not visible — large blank white area below the black band where the white-on-black text should be
- MEDIUM | html | - | X-brush artwork sits at the left edge of the black header band instead of the right side as in Word

### resumes/03

- MEDIUM | all | p1 | Dashed rules rendered solid: header rule, rule below summary, and the dotted vertical column divider all lose their dash pattern
- MEDIUM | skia | p1 | Rule below the summary paragraph spans only the left column (stops at the column divider) instead of the full content width
- MEDIUM | skia,imagesharp | p1 | SKILLS entries vertically compressed (bar touches label, no gap between entries) so the block ends ~135px higher than Word; PDF spacing matches Word
- MEDIUM | all | p1 | Education text "Laude" broken mid-word ("...Biology, Cum Lau / de, outstanding...") where Word wraps before "Laude"
- MEDIUM | all | p1 | Right-column HOBBIES/CONTACT blocks drift progressively lower (~1.5 line heights in skia/imagesharp, ~2.5 in pdf by the CONTACT block)
- MEDIUM | html | - | Bold upright runs render italic: job titles (Lamna Healthcare / General Practitioner etc.), university names, skill labels, and Phone/Website/Email sub-headers
- MINOR | html | - | Email address styled as blue underlined hyperlink; Word shows plain black text

### resumes/04

- MAJOR | all | p1 | Circular profile photo at top of sidebar not rendered (lavender blob drawn, photo absent)
- MAJOR | all | p1 | Last 2-3 lines of OBJECTIVE text overlap the yellow/pink wave shapes at the sidebar bottom (white-on-yellow, barely readable); waves also drawn ~100px higher than Word
- MEDIUM | all | p1 | Sidebar contact entries (address/phone/email/website) spaced ~2x further apart than Word and start where the photo should be, pushing OBJECTIVE down
- MEDIUM | skia,imagesharp | p1 | COMMUNICATION paragraph wraps to 6 lines vs Word's 7; PDF matches Word
- MAJOR | html | - | Profile photo missing
- MINOR | html | - | White address text sits over the light lavender blob (low contrast); in Word it sits on the navy panel

### resumes/05

- MEDIUM | all | p1 | Vertical spacing compressed throughout: right-column sections end ~107px (4+ lines) higher than Word (REFERENCES at y1331 vs 1438) and the sidebar box is ~100px shorter
- MINOR | html | - | Date-range lines (JAN 20XX – AUG 20XX etc.) render italic; Word shows upright

### resumes/06

- MAJOR | all | p1,p2,p3 | Top-left decorative rectangle drawn as a wide horizontal block (~285x90) offset below the page top instead of a tall strip (~88x420) flush with the top-left page edge (white on p1, blue p2, black p3)
- MEDIUM | all | p1,p2,p3 | Education/skills rows roughly double-spaced (Creativity/Leadership/Problem Solving) with thicker bars; the Problem Solving row lands ~90px lower on top of the bottom decorative rectangle (on p3 the black bar merges invisibly into the black block)
- MAJOR | html | - | Page-1's white cut-out shapes render black: black corner square on the blue section, and a black bottom bar that covers the following section's contact lines (taylor@example.com hidden)
- MAJOR | html | - | Corner strip and bottom rectangle missing entirely for the 2nd and 3rd page sections (only one pair of shapes rendered)

### resumes/07

- MEDIUM | skia,imagesharp | p1 | Bold weight lost on most template rows — College, location / Company, location / Graduation year / most Month Year runs / Project title / Activity / Leadership experience / SKILLS labels render regular; PDF keeps bold
- MEDIUM | all | p1 | Letter-spaced section headings lose character tracking; skia/imagesharp show an oversized word gap instead ("PROFESSIONAL    EXPERIENCE"), pdf renders them compact
- MINOR | all | p1 | Italic sub-lines (Role, Bachelor of Arts Degree GPA, Relevant course work:) drawn ~0.25" further left than Word, starting left of their parent rows
- MINOR | html | - | SKILLS lines entirely bold — the value text after "Programming languages:" etc. should be regular weight

### resumes/08

- MEDIUM | all | p1 | "CONNORS" in the name rendered at the same bold weight as "MORGAN"; Word renders it in a light weight (name also becomes wider)
- MEDIUM | all | p1 | Tracking lost on spaced-caps text (UI/UX DESIGNER subtitle, SENIOR UI/UX DESIGNER job titles, section headings): skia/imagesharp produce uneven word gaps, pdf compact
- MEDIUM | all | p1 | Vertical compression: sidebar sections (ABOUT ME/EDUCATION/SKILLS) end ~112-135px (4-5 lines) higher than Word and the left column ~30-50px higher
- MEDIUM | html | - | "CONNORS" bold instead of light weight
- MINOR | html | - | Thin separator rules missing (above EXPERIENCE and between sidebar sections CONTACT/ABOUT ME/EDUCATION)

### resumes/09

- MEDIUM | pdf | p1 | Three white separator rules between sidebar contact entries (Location/Phone/Email/Website) missing
- MEDIUM | pdf | p1 | Sidebar contact entries drift progressively lower (Website ~90px / ~2 lines below Word); skia/imagesharp match Word
- MINOR | pdf | p1 | Bullet glyphs rendered noticeably smaller than Word's round bullets
- MINOR | all | p1 | Main content column uniformly shifted ~26px left of Word's position
- MINOR | html | - | Sidebar separator rules between contact entries missing

### resumes/10

- MAJOR | all | p1,p2,p3 | Accent circle shape bleeding off the top-left page edge (red p1, blue p2, green p3) is missing entirely in all three backends
- MEDIUM | skia,imagesharp | p2 | Whole page-2 content block renders ~28px (~1.5 line heights) higher than Word (p1/p3 are aligned)
- MEDIUM | pdf | p1,p2,p3 | Progressive downward drift through the page — SKILLS/ACTIVITIES sections end ~20-26px (~1 line) lower than Word
- MINOR | skia,imagesharp | p1,p2,p3 | SKILLS bullet markers are black instead of the page accent color (red/blue/green)
- MINOR | pdf | p1,p2,p3 | SKILLS bullet markers are black and noticeably smaller dots than Word's accent-colored round bullets
- MAJOR | html | - | Accent circle shape missing from HTML export (all three color variants)
- MINOR | html | - | SKILLS bullets black instead of accent color

### resumes/11

- MEDIUM | all | p1 | Expanded letter-spacing on "Chanchal Sharma" and "OFFICE MANAGER" is not applied — headline renders ~20% narrower with normal tracking (raster backends also show a doubled word gap in "OFFICE MANAGER")
- MEDIUM | skia,imagesharp | p1 | Vertical spacing compressed from the EXPERIENCE section down — sections creep progressively higher, SKILLS block ends ~110px (~0.75 in) above Word's position (PDF matches Word within 1px)
- MINOR | all | p1 | Name block starts ~15px lower than Word
- MINOR | html | - | Short section-divider rules above EDUCATION and SKILLS missing (only the contact-row rule is kept)

### resumes/12

- MAJOR | all | p1 | Coral ornament group top-right renders garbled — extra overlapping circles/arcs drawn at wrong position/scale on its left side, extending toward the name (Word shows a clean pattern grid)
- MAJOR | all | p1 | Short coral underline rule below "Manager" is missing in all three backends
- MINOR | pdf | p1 | "VICTORIA BURKE" name block sits ~20px lower than Word
- MAJOR | html | - | Ornament garbled with extra circle elements that overlap the "VICTORIA" name text
- MAJOR | html | - | Coral underline below "Manager" missing

### resumes/13

- MAJOR | skia,imagesharp | - | Page count 4 vs Word's 5 — compressed vertical layout pulls content up one page from p2 onward (p3 shows Word's p4 content, p4 shows Word's p5 content)
- MAJOR | pdf | - | Page count 6 vs Word's 5 — looser layout pushes Teaching experience off p2, content lags ~1 page behind and overflows onto an extra page 6
- MINOR | all | - | Footer page-number field now recomputes per page (was cached "3" everywhere); the remaining gap vs Word's 1-5 is the page-count divergence [systemic #2], not the field
- MAJOR | skia,imagesharp | p2 | Overflowing content ("Publications" heading and intro paragraph, plus a section rule) is drawn through/below the footer row and clipped at the bottom page edge
- MEDIUM | all | p3,p5 | Sidebar headings wrap mid-word — "Presentations and invited l/ectures", "Professional t/raining", "Professional a/ffiliations" (Word breaks between whole words; skia/imagesharp on their p3, pdf on its p5)
- MEDIUM | all | p1 | "ELECTRICAL ENGINEER" subtitle loses its expanded letter-spacing and renders slightly larger (raster backends add an extra-wide word gap)
- CLEAN: html

### resumes/14

- MEDIUM | pdf | p1 | list bullets render as tiny middle-dots instead of Word's round filled bullets (all three bulleted lists)
- MEDIUM | pdf | p1 | vertical spacing drifts: sections sit progressively lower than Word, ~0.3in (~2 line heights) by "Skills & abilities"
- MINOR | skia,imagesharp | p1 | header word-spacing off: "PROFESSIONAL TITLE" word gap too wide, contact-line "Philadelphia, PA" / pipe separators cramped
- MINOR | html | - | right-aligned tab dates ("20XX – 20XX", "20XX") render inline after the job/degree titles instead of at the right margin

### resumes/15

- MAJOR | pdf | p1 | lavender paragraph shading missing entirely: name-banner band and the shading behind "Experience", "Skills", "Education", "Activities" headings all absent
- MEDIUM | all | p1 | expanded letter-spacing not applied to the name and section headings (text renders narrower; skia/imagesharp draw the heading shading boxes at the letter-spaced width so they extend past the text)
- MEDIUM | html | - | full-width lavender band behind "Janna Gardner" missing (section-heading shading is present)

### resumes/16

- MEDIUM | all | p1 | "Chanchal Sharma" name: Word's expanded letter-spacing missing and glyphs render ~15% larger, name block sits ~20px lower
- MEDIUM | html | - | Skills 3-column table collapsed: cells run together on single lines ("Project management Data analysis Communication")

### resumes/17

- MEDIUM | all | p1 | two-column body misplaced: column divider and right column (Skills/Hobbies/Profile) sit ~0.8in left of Word's position, and the narrower left column wraps its text differently
- MEDIUM | all | p1 | expanded letter-spacing missing throughout ("General Practitioner", "Work Experience"/"Skills" headings, right-column items) — text noticeably narrower than Word
- CLEAN: html

### resumes/18

- MEDIUM | all | p1 | date column ("20XX – 20XX", "June 20XX") rendered bold; Word shows regular weight
- MEDIUM | skia,imagesharp | p1 | sections drift progressively upward (~0.35in by LEADERSHIP) from tighter spacing between Experience/Education entries
- MEDIUM | html | - | experience/education tables collapsed: date cell merges onto the title line as one bold run ("20XX – 20XX Senior Editor, Surat, Gujarat"), losing the two-column layout

### resumes/19

- MAJOR | all | p1,p2,p3 | Skills bulleted list (Creativity, Leadership, Organization, Problem solving, Teamwork) missing on every page — Contact section moves up directly under the "Skills" heading
- MEDIUM | all | p1,p2,p3 | 2nd and 3rd Experience entries drift progressively lower (~0.33in by the third entry) from oversized gaps between entries
- MINOR | skia,imagesharp | p1,p2,p3 | word gap in the name nearly collapsed ("RobinZupanc")
- MAJOR | html | - | Skills bulleted list missing in all three repeated sections (heading renders with no items)
- MAJOR | html | - | colored content-panel backgrounds wrong: first panel grey instead of light blue, and the yellow (2nd) and grey (3rd) panels missing entirely

### rtl_paragraph

- MEDIUM | pdf | p1 | w:bidi paragraphs ("RTL Paragraph Heading" and the bidi body sentence) render left-aligned instead of right-aligned
- MEDIUM | html | - | bidi heading and paragraph render left-aligned instead of right-aligned
- CLEAN: skia, imagesharp

### section_break_continuous

- MEDIUM | skia,imagesharp | p1,p2 | Section 2 (heading plus paragraphs 2-7) flows onto page 1 immediately below section 1 instead of starting at the top of page 2 as Word does (section 2's default next-page break rendered as continuous); page 2 therefore begins at paragraph 8 instead of the "Section 2 content" heading.
- MEDIUM | all | p1,p2 | Paragraph pitch ~10% tighter than Word — text block drifts progressively upward, last paragraph on each page sits ~2 line-heights higher than Word (PDF keeps Word's per-page content split despite the drift).
- CLEAN: html

### section_break_odd_page

- MEDIUM | all | p1,p2 | Tighter paragraph pitch packs paragraphs 1-29 onto page 1 versus Word's 1-26, so page 2 runs paragraphs 30-51 instead of 27-51 (three paragraphs redistributed from page 2 to page 1); section 2 still starts correctly on page 3.
- CLEAN: html

### small_caps

- MEDIUM | pdf | p1 | small-caps formatting not applied — "Mixed Case Heading" and the fox sentence render as ordinary mixed-case lowercase text instead of small capitals (Skia/ImageSharp render it correctly)
- CLEAN: skia, imagesharp, html

### tab_stops

- MEDIUM | skia | p1 | TOC line "Chapter 2: Background and motivation" — the right-tab page number "12" overflows and wraps onto its own second line (fits on one line in Word), pushing subsequent content down
- MEDIUM | all | p1 | vertical spacing below the TOC is compressed — blocks drift progressively upward, the Signature line ends ~0.5" higher than Word in ImageSharp/PDF (~0.3" in Skia, partly offset by the extra wrapped line)
- MEDIUM | html | - | tab formatting collapses to single spaces — TOC dot leaders, tabbed column alignment (Name/Role/Team/Location), left/center/right tab positions, and the signature underline fill are all lost

### table_alignment/01

- MINOR | all | p1 | centered and right-aligned tables sit slightly higher (up to ~15px by the third table) and a few px further right than Word — small cumulative spacing drift, alignment itself correct
- MEDIUM | html | - | table alignment lost — the centered and right-aligned tables render left-aligned (all three tables at the left margin with identical widths)

### table_autofit_no_widths

- MEDIUM | all | p1 | autofit column widths distributed differently from Word — "Full Name" column too narrow (header and "Jane Smith" wrap to two lines vs one in Word) while "Hire Date" is too wide (dates fit one line vs Word's two), changing row heights and making the table end ~0.25" higher
- CLEAN: html

### table_borders

- MINOR | all | p1 | autofit table rendered ~10% narrower than Word (~1.78" vs 2.0" wide, cell text sits closer to the right cell borders); grid/border pattern itself correct
- CLEAN: html

### table_cell_margin_per_cell

- MEDIUM | all | p1 | "Left margin emphasis" text sits at the top of cell 2 (~33px too high at 150dpi): Word applies the row's largest top cell margin (cell 1's 15pt) to both cells, the renders honor only cell 2's own 2.5pt top margin
- MINOR | all | p1 | autofit table under-sized: 470x80px vs Word's 507x89 (both columns ~7% narrower, row shorter with smaller bottom padding)
- MEDIUM | html | - | per-cell margins dropped: cell 1's large top margin and cell 2's large left margin are not represented — both texts render with identical compact padding, losing the emphasis the cells demonstrate

### table_cell_padding

- MEDIUM | all | p1 | table under-sized: every row 72-73px tall vs Word's 93-96 (~23% shorter) and every column 248px vs 268 (~7% narrower), so the table is 746x218px vs Word's 803x284 and starts ~7px higher
- CLEAN: html

### table_cell_padding_varied

- MEDIUM | all | p1 | table under-sized: rows 114-115px vs Word's 136 (~15% shorter) and columns 221/270/285px vs 240/292/302 (table 776px vs 834px wide); the per-cell padding insets themselves are qualitatively correct
- MEDIUM | html | - | varied per-cell padding lost: all six cells render with identical compact padding, so "Large padding (20pt)", "More left/right" and "No padding" cells all look the same

### table_cell_spacing/01

- MEDIUM | all | p1 | cell-spacing gaps collapsed vertically: outer table border sits only ~4px from the cell borders vs Word's ~11px and cell boxes are 38-40px tall vs 47-48, so the detached-border table is 146px tall vs Word's 188 and starts ~17px higher (width and horizontal gaps are close)
- MEDIUM | html | - | detached-border effect lost entirely: renders as an ordinary collapsed-border grid with single shared lines and no gaps (tblCellSpacing ignored)

### table_colors

- MINOR | all | p1 | all three columns ~10% narrower than Word (header band 278px wide vs 311, table right edge x=430 vs 461); row heights, header blue, white header text and alternating light-blue shading all correct
- CLEAN: html

### table_default_cell_margin

- MEDIUM | skia | p1 | columns 288px vs Word's 305 make "Large margin cell N,N" wrap to two lines in every cell (Word fits one line), growing rows to 145px vs 136
- MEDIUM | imagesharp,pdf | p1 | rows 114-115px vs Word's 136 (~16% shorter, table bottom at y=425 vs 475) with columns ~6% narrower; text still fits one line
- MEDIUM | html | - | large default cell margins dropped — cells render with small default padding, losing the spacious look that is this document's feature

### table_default_cell_margin_start_end

- MINOR | all | p1 | table uniformly slightly under-sized: columns 98px vs Word's 105 and rows 48px vs 52-53 (~7-8% smaller in both directions), structure otherwise identical
- CLEAN: html

### table_default_style

- MEDIUM | all | p1 | every row ~20% shorter than Word (header 58px vs 70, data rows 52-55 vs 65-68), so the styled table ends at y=423 vs Word's 495 (~0.5in too high); width, header blue, white bold header text and inter-row rules are correct
- MINOR | all | p1 | bottom double border drawn as two touching lines that read as one ~4px thick bar (second line rendered pale in skia/pdf) instead of Word's two clearly separated rules
- MINOR | html | - | bottom border renders as a single thick solid bar instead of a double rule

### table_default_style_inside_h

- MINOR | all | p1 | table rows each ~2-3px shorter than Word so the two inside-horizontal borders and following rows sit progressively higher; table bottom edge ends ~6-9px above Word's (y279 vs y285)
- CLEAN: html

### table_default_style_outer_borders

- MEDIUM | all | p1 | double-line top/bottom outer border drawn at ~half Word's line pitch so the two lines merge into a single bar: PDF collapses both edges to solid ~2px black bars, Skia's top edge becomes one grey bar, ImageSharp's bottom edge merges; Word shows two clearly separated lines spanning ~8px
- MINOR | all | p1 | bottom border sits ~9-12px higher than Word (table bottom y261 vs y273) — last row does not reserve the double-border height
- MINOR | html | - | double outer border emitted as CSS "1.5pt double", below the ~3px browsers need to draw two lines, so it renders as a single thin line

### table_diagonal_borders/01

- MINOR | all | p1 | whole table sits ~17px too high: the ~8pt gap Word leaves between the intro paragraph and the table is dropped (table top border at y179 vs Word y196; label itself at identical y)
- MINOR | all | p1 | diagonal borders and cell borders drawn ~2x thicker and fully saturated (2-3px solid black / bold red+blue X) versus Word's ~1px grey/light hairlines; directions and red-tl2br/blue-tr2bl colors are correct
- MAJOR | html | - | diagonal cell borders completely missing in HTML export — all three cells render as plain bordered boxes with no tl2br/tr2bl/X lines (nothing emitted in the markup)

### table_explicit_heights

- MINOR | all | p1 | everything below the title sits ~8px high because the title→subtitle paragraph gap is ~5px tighter than Word; the explicit row heights themselves are exact (100/200/150pt row pitches match Word to the pixel)
- MINOR | all | p1 | all text renders ~10% narrower than Word (e.g. subtitle line 403px vs Word's 445px; every label ends visibly short of Word's), same glyph height
- MEDIUM | html | - | table column widths not preserved — bare `<table>` collapses columns to content width, so "Cell 2" hugs column 1's text instead of Word's ~half-page-wide columns

### table_grid_styling_padding

- MEDIUM | all | p1 | column widths differ from Word: Full Name column narrower (header "Full Name" and "Jane Smith" wrap to 2 lines; Word keeps both on 1) while Hire Date column is wider (all four dates fit on one line; Word wraps all four dates to 2 lines)
- MEDIUM | all | p1 | rows with two text lines are ~16px (~26%) taller than Word (77-79px vs Word's uniform 62px, header 78 vs 62), making the table ~46px taller overall (bottom border y507 vs y461)
- MINOR | all | p1 | whole table displaced ~12px right: Word draws the left border at x137 (offset left of the margin by the cell left padding) while all backends start it at the margin x149; right edge likewise 1126 vs Word 1138
- CLEAN: html

### table_indent

- MEDIUM | all | p1 | ~12px of vertical spacing is lost at every label+table section, accumulating down the page — the final (centred) table's top border is at y988 vs Word y1047 (~59px, >2 line heights); the tblInd indents (480/720/1440 dxa), centred alignment, table widths and 52px row heights themselves all match Word
- MINOR | all | p1 | all text renders ~10% narrower than Word (final label line 640px vs Word 707px wide)
- MEDIUM | html | - | table indentation and alignment lost in HTML export: the 480/720/1440-dxa indented tables and the centred table all render flush left, and the "100% width" baseline table collapses to content width (no width/margin emitted on four of five tables)

### table_layout_tall_row

- MAJOR | all | p1,p2 | tall second table row's company block ("Company Name", "123 Main Street") is absent from page 1 and rendered whole at top-right of page 2 instead of splitting at the page break like Word, which also exposes an extra "City, State 12345" line that Word's exact row height clips (never visible in Word).
- MEDIUM | all | p2 | letter body (Recipient Name through Title) starts ~170px (~4 line heights) lower than Word because the deferred tall row occupies the top of page 2.
- MINOR | html | - | blank-line/paragraph spacing of the letter collapsed — Date, Dear Recipient, body and closing paragraphs are tightly stacked with none of Word's inter-paragraph gaps.

### table_multipage

- MEDIUM | all | p1,p2 | table page-break lands two rows late: slightly shorter row heights let Rows 24-25 fit on page 1 (Word breaks after Row 23), so page 2 holds only Rows 26-29 vs Word's Rows 24-29.
- CLEAN: html

### table_of_contents/01

- MINOR | all | p1 | TOC entry spacing slightly tighter than Word — lines drift upward progressively, ~13-18px (~0.4 line) by the last entry (Appendix A).
- MINOR | all | p1 | dot leaders stop a visible gap short of the page numbers and start flush against the entry text, whereas Word runs the dots up to the number and leaves a small gap after the text.
- MINOR | html | - | TOC tab leaders and right-aligned page numbers dropped — numbers render inline immediately after each entry ("Introduction 1").

### table_of_contents/02

- MINOR | all | p1 | TOC entry spacing slightly tighter than Word — lines drift upward ~10-15px by the last entry (Chapter 5).
- MINOR | all | p1 | leaders start flush after the entry text (Word leaves a small gap before the hyphen/underscore/middle-dot leaders) and the middle-dot leader ends short of the "40" that Word joins to the number.
- MINOR | html | - | TOC tab leaders and right-aligned page numbers dropped — numbers render inline after each chapter title.

### table_of_contents/03

- MINOR | all | p1 | TOC table row ~10px shorter than Word (bottom border at y≈449 vs Word's 460; top border also ~4px higher), with cell content shifted up a few px.
- MINOR | all | p1 | dot leaders in the narrow cell stop short of the right cell border, whereas Word runs the dots flush to the border.
- MINOR | html | - | TOC tab leaders dropped — page numbers (1, 4, 9, 15, 27) render inline after each entry instead of leader-aligned (Word clips them at the narrow cell edge).

### table_page_break

- MEDIUM | all | p1 | the 20 filler paragraphs render with tighter spacing, so the stack compresses and table Row 1 begins ~100px (~2.5 line heights) higher than Word; row-to-page distribution on p2 (Rows 2-7) and p3 (Rows 8-10) matches Word.
- CLEAN: html

### table_text_direction

- MINOR | all | p1 | table rows slightly shorter than Word — header/Q1/Q2 row bottoms sit ~5-12px high and the table bottom border ends ~12px early
- MEDIUM | html | - | rotated header-cell text "Quarter" rendered horizontal in HTML export (vertical bottom-to-top text direction lost)

### table_two_column_layout

- MEDIUM | all | p1 | vertical spacing inside cells tighter than Word — right-column "Line 1..10" list drifts upward reaching a full line by Line 10, and the table bottom border sits ~40px higher than Word
- MINOR | html | - | empty-paragraph gaps inside cells collapsed (no blank line after "Left Column"/"Right Column" headings or before "Line 1", which Word renders)

### three_columns

- MEDIUM | all | p1 | column distribution differs — items split 16/14/0 across the three columns vs Word's 14/15/1, leaving the third column completely empty (tighter line/paragraph spacing packs more items per column)
- MINOR | all | p1 | in-item wrap points differ — first line fits an extra word (e.g. Item 1 breaks after "narrow" instead of Word's break after "for")
- CLEAN: html

### tracked_changes/01

- MAJOR | all | p1 | tracked deletion "removed." not rendered at all (Word shows it in red strikethrough at end of line)
- MEDIUM | all | p1 | tracked insertion "inserted" rendered as plain black text — red underlined revision styling missing
- MINOR | all | p1 | left-margin change bar (vertical revision line) missing
- MAJOR | html | - | tracked deletion "removed." absent from HTML export
- MEDIUM | html | - | tracked insertion "inserted" shown without any revision styling in HTML

### two_columns

- MEDIUM | all | p1 | column break shifted — column 1 holds Paragraphs 1-11 vs Word's 1-10, so column 2 ends ~115px (about 1.5 paragraph blocks) higher than Word
- MINOR | all | p1 | in-paragraph wrap points differ — lines fit an extra word (e.g. "eiusmod" pulled up onto line 2 of each paragraph vs Word breaking after "Sed do")
- CLEAN: html

### wedding/01

- MEDIUM | all | p1 | invitation cards start ~0.26" higher than Word (intro-paragraph spacing compressed) and the text inside the cards drifts further up (~0.4" by the SATURDAY/RECEPTION lines) from tighter line spacing
- MEDIUM | skia,imagesharp | p1 | letter-spaced small-caps lines ("THE PLEASURE...", "SATURDAY, THE 20TH OF MAY", "RECEPTION TO FOLLOW") rendered with large uneven word gaps (distributed justification) instead of Word's even letter tracking
- MAJOR | pdf | p1 | small "TO" rendered at SARA's baseline overlapping the SARA letterforms instead of on its own line between the two names
- MEDIUM | pdf | p1 | letter-spacing dropped on all small-caps card lines (text much narrower) and card header rewraps as "...IS REQUESTED AT THE / MARRIAGE OF" vs Word's break after "PRESENCE IS"
- MINOR | all | p1 | intro paragraph rewraps: "Create New Theme Colors" kept on line 2 so line 3 starts with an orphaned period (". Select your own colors...")
- MAJOR | html | - | card text (THE PLEASURE.../SARA/TO/EVAN/date block) rendered on white below the two watercolor background images instead of overlaid on them

### wedding/02

- MAJOR | skia,imagesharp | p1 | pink petal image mirrored horizontally (gathered/stamen end at bottom-right vs Word's bottom-left) and shifted ~0.7" left, both cards
- MEDIUM | skia,imagesharp | p1 | floral leaves displaced: big leaf under the petal moved from right of column to column-left (~1.8"), and the two leaves below the pink banner moved from center-left to the right column edge
- MAJOR | pdf | p1 | floral clusters swapped vertically: petal+big-leaf cluster renders BELOW the pink banner (petal straddles the card divider and is flipped vertically) while the below-banner leaves render at the card top, both cards
- MEDIUM | all | p1 | PLEASE JOIN + pink banner block shifted up (~0.4" card 1, ~0.7" card 2), banner slightly shorter, rosebud now touching/overlapping the banner top edge
- MAJOR | skia,imagesharp | p2 | left poppy group flipped vertically — red bloom above the green/yellow calyx fan, Word has bloom below the fan (both cards)
- MAJOR | pdf | p2 | left poppy group mirrored horizontally (calyx fan on right of bloom) plus a spurious white triangle wedge blanking part of the spiky-stem art (both cards)
- MEDIUM | all | p2 | yellow DATE/TIME/LOCATION banner ~1/3 shorter (compressed line spacing) and shifted up ~0.35" (card 1) / ~1" (card 2); card-2 banner overlaps the right poppy and covers the small leaves beneath it
- MEDIUM | all | p1,p2 | text left inset (~0.5") lost: "PLEASE JOIN", banner DATE/TIME/LOCATION text, and "Registered at:/RSVP" block all flush with the column/banner edge instead of indented
- MEDIUM | all | p2 | table rows end higher than Word, so the bottom leaf pair renders below the card's bottom border (outside the card)
- MINOR | pdf | p1,p2 | thin gray bounding-box outlines drawn around the rotated floral images
- MAJOR | html | - | all floral images render as a stacked column down the left margin detached from the invitation tables (not composed around the text); poppy group also vertically flipped and clipped to half its width

### wedding/03

- MEDIUM | all | p1 | small gold-rings image displaced ~1.5" left to the page margin edge and ~0.5" up (card 1) / ~1" up (card 2)
- MEDIUM | all | p1 | second card's content (pink picture + caption) sits ~0.5" higher than Word (first row shorter)
- MINOR | all | p1 | pink rings picture rendered ~3% larger and shifted slightly up/right
- MEDIUM | all | p2 | placeholder paragraph rendered left-aligned in a ~25% narrower measure, wrapping to 6 lines vs Word's centered 5 lines (both cards)
- MEDIUM | html | - | placeholder paragraph left-aligned instead of centered

### wedding/04

- MAJOR | all | p1,p2 | green section rules missing: horizontal rules attached to every section heading (and p1's left vertical bracket line) not drawn — only the long center column divider renders
- MAJOR | all | p1 | top floral garland mis-composed: pieces repositioned and a rotated white-flower image blanks a band through neighboring art (Skia diagonal swath at top-right, ImageSharp diagonal band mid-right, PDF horizontal white band right of the title)
- MEDIUM | all | p1,p2 | checklist line spacing compressed — columns end ~0.85-0.95" higher than Word (e.g. "Obtain a marriage license" / "Remember to eat something"); p2 first item fits one line vs Word's two
- MEDIUM | all | p1 | right-column items lose the gap between checkbox and text (box glued to text: "☐Choose the members of your wedding party.")
- MEDIUM | all | p2 | edge floral sprigs shifted ~0.5-0.7" from Word positions (left cluster lower, right clusters higher; PDF bottom-right cluster rearranged)
- MINOR | all | p1,p2 | checkbox squares rendered ~20% smaller than Word's
- MAJOR | html | - | column divider rule renders full page height crossing the title and garland, and the header garland stacks as three repeated bands above the title instead of one composed arrangement
- MEDIUM | html | - | red section headings rendered italic (upright in Word)

### wedding/05

- MAJOR | pdf | p1,p2 | watercolor header washes distorted into streaky, oversaturated horizontal bands with wrong shape/extent (p1 right panel bleeds down behind "PARENTS OF THE BRIDE"; p2 both MENU and TABLE panels). *Partially improved:* the washes' `a:srcRect` mixes a 72% top crop with a negative (padding) bottom edge that was being clamped away, over-stretching them ~4.5% vertically — honouring it improved every page ~−0.005 in all 3 backends (systemic #20); the streaky-band texture itself remains.
- MEDIUM | all | p1 | "SATURDAY / 10.25.20XX" date block rendered grey instead of teal
- MEDIUM | pdf | p1 | date block also rendered bold where Word uses regular weight
- MEDIUM | all | p1,p2 | lists start too high: wedding-party list ~0.6in above Word's position on p1, menu list ~1 line high on p2 (gap below the wash heading too small)
- MINOR | skia,imagesharp | p1,p2 | letter-spaced headings show extra-wide word gaps ("KAYLA  +  JACOB", "PARENTS  OF THE BRIDE", "BEST  MAN", "MAIN  COURSE")
- MEDIUM | html | - | "SATURDAY / 10.25.20XX" grey instead of teal
- MEDIUM | html | - | text centered in Word (date, events list, wedding-party and menu lists) exported left-aligned
- MEDIUM | html | - | watercolor washes exported as standalone images stacked above the panels instead of backgrounds behind the headings

### wedding/06

- MAJOR | skia,imagesharp | p1 | corner poppy image rotated ~180° on both cards (yellow-striped calyx ends up bottom-left instead of top-right) and drawn ~0.4in further left so it is no longer clipped by the page edge
- MEDIUM | pdf | p1 | falling-floral group items rendered with wrong rotations/sizes/positions (peach petal enlarged and rotated ~90°, rosebud enlarged/rotated, leaves enlarged, incl. right-panel middle leaf)
- MEDIUM | all | p1,p2 | card rows too short: fold/borders sit ~1in higher than Word and second-card content ~0.9in high; p2 bottom flower clusters straddle the card borders, p1 card2's poppy dips into the pink banner corner
- MEDIUM | all | p2 | invitation text block left-aligned instead of centered on both cards
- MEDIUM | all | p2 | invitation text line spacing compressed ~30%, block ends ~0.5in higher
- MEDIUM | all | p2 | left floral cluster displaced ~0.9in left and ~0.5in up, poppy nearly flush with panel edge
- MEDIUM | html | - | invitation text left-aligned instead of centered
- MEDIUM | html | - | date block ("Saturday the Twenty-First of June" ... "Reception to Follow") rendered italic; upright in Word
- MINOR | html | - | decorative florals exported as a vertical stack of standalone images outside the card panels

### wedding/08

- MAJOR | all | p1 | green circled "&" badge between bride's and groom's names missing
- MEDIUM | all | p1,p2 | centered text rendered flush-left (names and address/dinner lines on p1; all Ceremony/Participants/Additional Text lists on p2)
- MEDIUM | all | p1,p2 | card panels shorter than Word (p1 borders end ~2in early, p2 ~0.4in) with content blocks 0.4-0.8in higher (Thanks-and-Dedication and time/venue blocks)
- MEDIUM | all | p1 | "dinner and dancing to follow" rendered upright instead of italic
- MAJOR | html | - | green circled "&" badge missing
- MEDIUM | html | - | centered text exported left-aligned
- MEDIUM | html | - | "00:00 PM" and "VENUE/PLACE" italicized in HTML; upright in Word

### wedding/09

- MEDIUM | all | p1 | invitation banners pinned to top of card rows instead of vertically centered (card1 ~1.5in high, card2 ~2.9in high); card frames end at ~70% page height leaving the bottom unframed
- MAJOR | all | p1 | card2's relocated "on our wedding day"/banner corner collides with the poppy image (text drawn over the flower; pdf card1 poppy also overlaps the yellow "&" box)
- MAJOR | skia,imagesharp | p1 | poppy image rotated ~180° on both cards (calyx bottom-left instead of top-right)
- MEDIUM | pdf | p1 | left floral strips scrambled: items enlarged, rotated differently, and repositioned versus Word (rosebud/petal/leaves)
- MEDIUM | all | p2 | interior invitation text left-aligned instead of centered on both cards
- MEDIUM | all | p2 | invitation text line spacing compressed ~30%, block ends ~0.5in higher
- MEDIUM | all | p2 | poppy cluster displaced up-left to the panel edge; bottom purple/yellow cluster sits on the card boundary/page bottom instead of inside the cards
- MEDIUM | html | - | interior invitation text left-aligned instead of centered
- MEDIUM | html | - | date block ("Saturday the Twenty-First of June" ...) rendered italic; upright in Word
- MINOR | html | - | decorative florals exported as a vertical stack of standalone images outside the card panels

### wedding/10

- ✅ FIXED | all | p2,p3,p4,p5 | footer page numbers ("2"–"5") now render at Word's exact bottom center-right position — the footer was SDT-wrapped (page-number gallery). See systemic #1(c).
- MAJOR | pdf | p1,p2,p3,p4,p5 | checkbox symbols on every checklist item render as notdef/tofu tall rectangles instead of Word's "□" squares
- MAJOR | pdf | p1 | floral header graphic mis-composed (giant branch/rose in a vertical arrangement instead of Word's compact horizontal spray) and overlaps the "Wedding"/"PHOTO CHECKLIST" title, extending down into the PRE-CEREMONY band
- MAJOR | html | - | floral header graphic mis-composed the same way (vertical branch-and-rose arrangement instead of Word's horizontal spray above the title)
- MEDIUM | pdf | p1,p2,p3,p4,p5 | expanded letter-spacing lost on all section headings (PRE-CEREMONY, BRIDE, WEDDING PARTY, AT THE CEREMONY, FORMAL PHOTOS, FAMILY PHOTOS, ATTENDANTS, RECEPTION PHOTOS render with tight tracking)
- MINOR | all | p1 | the two "Candid photos..." placeholder items' checkboxes render light grey instead of dark like every other row in Word
- MINOR | all | p1,p2,p3,p4,p5 | checklist rows drift vertically up to ~10px from Word's positions (cumulative spacing difference down the page)

### wedding/11

- MAJOR | all | p1 | left card's venue/time block "4:30 pm in the afternoon / Cherrywood Chapel / 1234 Cherry Road / Austin, Texas" missing entirely (not moved to p2)
- MAJOR | html | - | same venue/time block ("4:30 pm in the afternoon / Cherrywood Chapel / 1234 Cherry Road / Austin, Texas") missing from the HTML export
- MAJOR | all | p1,p2 | watercolor artwork oversized and un-clipped: right card's top blob bleeds left of the card border, left card's bottom blob spills below/right of the card bottom edge
- MEDIUM | all | p1 | line wraps differ: "The pleasure of your company..." sentence wraps to 4 lines vs Word's 2, and "SAVE THE DATE" wraps to two lines vs Word's one
- MEDIUM | all | p1,p2 | card border geometry off: border lines extend past card corners (right card's left border runs below the card) and bottom borders sit higher than Word
- MEDIUM | pdf | p1,p2 | teal "SATURDAY"/"10.25.20XX" date lines (both cards) and "ACCEPT | DECLINE" render bold where Word shows regular weight
- MEDIUM | html | - | the four watercolor images render as detached standalone blocks above/beside the cards instead of as artwork inside the card frames
- MINOR | all | p2 | couple photo content cropped tighter (~8-10% zoomed in, less headroom above heads) and photo/text block sits ~20px higher than Word

### wide_table

- MEDIUM | all | p1 | all six table columns ~12% narrower than Word (~54px vs ~61px per column; table right edge at x≈475 vs Word's 519), rows also ~5% shorter
- CLEAN: html

### wordart

- MAJOR | pdf | p2,p3,p4 | All 12 WordArt shapes (Arc Text Up/Down, Circle Text, Wavy WordArt, Chevron Up/Down, Fade Effect, Slanted Up/Down, Can Shape, Inflated, Deflated) render as tiny plain black left-aligned labels — warp geometry, colors, and display size all lost
- MAJOR | skia | p2 | "Arc Text Up" rendered ~15% larger and higher than Word, its glyphs overlap the subtitle line "These use DrawingML WordArt transforms"
- MEDIUM | all | p2,p3,p4 | WordArt items flow one page early vs Word: "Wavy WordArt" appears on p2 instead of p3 and "Slanted Down" on p3 instead of p4, shifting p3/p4 content up ~130px (total page count still matches)
- MEDIUM | skia,imagesharp | p2,p3 | Path-warp WordArt drawn at nominal font size instead of stretched to the shape bbox — p3 items (Wavy WordArt, Chevron Up/Down, Fade Effect, Slanted Up/Down) span ~180px where Word fills ~430px page width (hard ~23%)
- MINOR | skia,imagesharp | p2 | Arc/circle warps off position/size: ImageSharp places "Circle Text" ~85px left of Word's position; Skia draws Arc Text Down and Circle Text ~10-30% larger and shifted right
- [known] MINOR | skia,imagesharp | p2 | residual vertical offset of warped glyphs vs Word (inline-drawing layout-cursor drift documented in notes.md)
- MEDIUM | all | p9 | Colored underlines lost: Word's thick red underline under "UNDERLINED TEXT" and blue double underline under "DOUBLE UNDERLINE" both render as a single text-colored gray/black underline
- MAJOR | pdf | p10,p11 | Highlight backgrounds completely missing — yellow/cyan/green/magenta/red bars behind the five "... Highlight" lines (p10) and the yellow highlight behind "Underline + Highlight" (p11) are absent; text renders on plain white
- MEDIUM | all | p14 | Emboss/shadow character effects flattened: black "EMBOSSED" and "SHADOWED" lose their offset drop shadows in every backend; "IMPRINTED" engrave two-tone lost in ImageSharp/PDF (Skia approximates it with a light offset)
- MINOR | skia,imagesharp | p5,p6,p7,p8,p9,p12,p13,p14,p15 | line spacing slightly larger than Word so each section's content drifts progressively lower (~15-20px by the last line of a block)
- MINOR | pdf | p5,p6,p7,p8,p9,p12,p13,p14,p15 | text renders slightly wider (horizontal ghosting on every heading, e.g. "Section N:" headers look bolder/wider) and vertical drift accumulates to ~40px by the lower lines of each section
- MAJOR | html | - | the 12 WordArt shape texts export as plain small unstyled black paragraphs at the top (no warp, color, or display size), while all other sections keep full styling
- MEDIUM | html | - | red underline and blue double underline lost — both render as plain single dark underlines
- MINOR | html | - | emboss/engrave/shadow effects render flat (no drop shadows on "EMBOSSED"/"SHADOWED", no engrave on "IMPRINTED")

### wordart-envelope

- MAJOR | pdf | p1 | All four WordArt words rendered as small plain black left-margin text ("Inflate", "Deflate", "Can Up", "Can Down") — the large colored warped WordArt graphics are entirely missing (heading/subtitle are fine).
- MAJOR | html | - | The four WordArt words are exported as small plain black text with no color, size, or warp styling — the blue/green/orange/red large display text is lost (heading and subtitle export correctly).
- MEDIUM | imagesharp | p1 | "Can Up" and "Can Down" are squashed to roughly half Word's glyph height — "Can Up" becomes a low flat ribbon hugging the bottom of its band leaving a large blank gap below "Deflate", and "Can Down" bows into a deep flattened smile arc instead of Word's full-height gently-warped letters.
- [known] MEDIUM | skia,imagesharp | p1 | Envelope warp shape deviates from Word on the Can Up/Can Down lines: sin-curve amplitude is much stronger and edge glyphs shrink to ~55% height so the leading capital "C" reads as lowercase, vs Word's near-uniform letter heights with a gentle arch (envelope curve + 0.55 minRatio design documented in notes.md).
- MINOR | skia | p1 | WordArt stack drifts upward with inter-line gaps nearly eliminated — "Can Down" sits ~60px higher than Word and almost touches "Can Up", where Word keeps clear separation between all four lines.

---

## Clean scenarios (faithful on skia, imagesharp, pdf and html)

`align_center`, `align_left`, `all_caps`, `bold_text`, `colored_text`, `document_protection/01`, `gutter_margins/01`, `mixed_formatting`, `section_break_even_page`, `section_break_next_page`, `simple_paragraph`, `simple_table`, `strikethrough_text`, `subscript_superscript`, `table_default_style_first_row_run_color`, `table_default_style_first_row_shading`, `table_vmerge_basic`, `table_vmerge_explicit_heights`, `text_wrapping_break`, `underline_text`, `wedding/07`
