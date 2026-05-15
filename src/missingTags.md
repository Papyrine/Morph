# Missing / Unhandled XML Tags

Scan of `src/Tests/Inputs/**/input/word/*.xml` (2332 files) produced 696 distinct qualified element names. Those not currently handled by the Morph OpenXml parser are listed below, grouped by rendering impact with implementation notes.

Handled-baseline references:
- `src/Morph.OpenXml/Parsing/DocumentParser.cs`
- `src/Morph.OpenXml/Parsing/Parsers/ShapeParser.cs`
- `src/Morph.OpenXml/Parsing/Parsers/InkParser.cs`
- `src/Morph.OpenXml/Parsing/Parsers/ThemeParser.cs`
- `src/Morph.OpenXml/Parsing/Extensions/OpenXmlExtensions.cs`

---

## HIGH IMPACT — content and layout

### Runs / inline content

| Tag | Notes |
|-----|-------|
| `w:hyperlink` | Wrapper around runs with `r:id` → relationship URL. Walk children as runs; optionally apply hyperlink character style (`Hyperlink`) for colour/underline. Anchor form (`w:anchor`) targets `w:bookmarkStart` — no external URL. |
| `w:bookmarkStart` / `w:bookmarkEnd` | Zero-width anchors. Safe to ignore visually but needed as cross-reference targets if field codes like `PAGEREF` / `REF` are ever evaluated. |
| `w:cr` | Soft line break inside a run (equivalent to `w:br` without `type`). Treat identically to `w:br`. |
| `w:footnoteRef` / `w:endnoteRef` | The marker glyph inside a footnote's own paragraph. Renders the numeric/custom mark; paired with `w:footnoteReference` in the body (not in the scanned corpus but a natural companion). |
| `w:separator` / `w:continuationSeparator` | Special footnote/endnote types (attribute `w:type="separator"` etc.) — horizontal rule above footnotes. |

### Footnotes / endnotes pipeline

`w:footnotes`, `w:footnote`, `w:footnotePr`, `w:endnotes`, `w:endnote`, `w:endnotePr`, `w:noEndnote`

Implementation notes:
- Parts live in `footnotes.xml` / `endnotes.xml`. Add relationship lookup in `DocumentParser` and build a lookup `id → ParsedDocument fragment`.
- Body runs reference them via `w:footnoteReference w:id="…"`.
- Render footnotes as a block at page bottom, above the footer. Without pagination logic, simplest first pass: append all footnotes at end of document.

### Character formatting (visible text effects)

| Tag | Notes |
|-----|-------|
| `w:em` | East-Asian emphasis mark (dot/circle above/below each glyph). |
| `w14:textFill` | Gradient/pattern text fill (contains `w14:solidFill`/`w14:gradFill`). |
| `w14:textOutline` | Text outline stroke colour/width. |
| `w14:glow` | Outer glow — radius + colour. |
| `w14:shadow` | Text shadow offset/blur/colour. |
| `w14:reflection` | Mirrored reflection below glyphs. |
| `w14:props3d`, `w14:scene3d`, `w14:bevel`, `w14:camera`, `w14:lightRig` | 3D text — approximate as 2D (skip 3D transform). |
| `w14:ligatures` | OpenType ligature level (`standard`/`standardContextual`/`historicalDiscretionary`). Forward to font shaping features. |
| `w14:numForm` | OpenType numeral form (`default`/`lining`/`oldStyle`). |
| `w14:numSpacing` | `proportional`/`tabular`. |
| `w14:stylisticSets`, `w14:cntxtAlts` | OpenType stylistic sets / contextual alternates. |
| `w14:checkbox`, `w14:checked`, `w14:checkedState`, `w14:uncheckedState` | Modern checkbox SDT (distinct from legacy `w:checkBox` formfield). |

### Paragraph / section layout

<!-- All previously listed tags now implemented or accepted as no-ops. -->
<!-- See docs/word-features.md (Paragraph Decoration / Page Layout sections) for status. -->

### Tables

| Tag | Notes |
|-----|-------|
<!-- Tables: all previously listed tags now implemented or accepted as no-ops. -->
<!-- See docs/word-features.md (Tables section) for status. -->

### Legacy VML drawing (Word 2007-compat fallback and form controls) — `NO PLANNED SUPPORT`

`v:shape`, `v:shapetype`, `v:group`, `v:line`, `v:oval`, `v:rect`, `v:roundrect`, `v:polyline`, `v:textbox`, `v:imagedata`, `v:fill`, `v:stroke`, `v:shadow`, `v:formulas`, `v:path`, `v:handles`, `v:f`, `v:h`, `w10:wrap`, `w10:anchorlock`, `o:fill`, `o:lock`

**Decision: not implementing.** VML is the pre-DrawingML vector format from Word 2007, kept around as a fallback inside `mc:Fallback` blocks and inside legacy `w:pict` content. Modern Word always emits a DrawingML version in the matching `mc:Choice`, which Morph already consumes, so the `mc:Fallback` VML is redundant for any document round-tripped through a 2010+ build of Word. The remaining bare-VML cases are old documents and a handful of form-control rendering details that are out of Morph's scope. Re-implementing a parallel VML pipeline (CSS-style positioning, `o:spt`-driven preset geometries, separate fill/stroke/shadow vocabulary) is a substantial effort with diminishing returns.

If a real document surfaces that needs VML rendering, the contained shape can be wrapped in a synthesized DrawingML representation upstream rather than teaching the renderer a second drawing language.

### Floating drawing extensions

Percentage *sizing* (`wp14:sizeRelH`/`sizeRelV` + `wp14:pctWidth`/`pctHeight`) and *positioning* (`wp14:pctPosHOffset` / `wp14:pctPosVOffset` inside `wp:positionH` / `wp:positionV`, including the common `mc:AlternateContent` Choice/Fallback wrapping) are both consumed — see `docs/word-features.md` "Percentage-Sized & Percentage-Positioned Floating Drawings" for status.

### Charts (entire family missing)

`c:chart`, `c:chartSpace`, `c:plotArea`, `c:doughnutChart`, `c:ser`, `c:val`, `c:cat`, `c:title`, `c:legend`, `c:numCache`, `c:numRef`, `c:strCache`, `c:strRef`, `c:pt`, `c:v`, `c:tx`, `c:dPt`, `c:dLbls`, `c:idx`, `c:order`, `c:layout`, `c:manualLayout`, `c:layoutTarget`, `c:overlay`, `c:varyColors`, `c:roundedCorners`, `c:autoTitleDeleted`, `c:date1904`, `c:lang`, `c:style`, `c:firstSliceAng`, `c:holeSize`, `c:bubble3D`, `c:showVal`, `c:showPercent`, `c:showSerName`, `c:showCatName`, `c:showLegendKey`, `c:showBubbleSize`, `c:showLeaderLines`, `c:showDLblsOverMax`, `c:plotVisOnly`, `c:dispBlanksAs`, `c:formatCode`, `c:ptCount`, `c:xMode`, `c:yMode`, `c:x`, `c:y`, `c:w`, `c:h`, `c:externalData`, `c:extLst`, `c:ext`, `c:spPr`, `c:txPr`, `c:f`, `c14:style`, `c16:uniqueId`, `c16r3:dataDisplayOptions16`, `c16r3:dispNaAsBlank`, plus the full `cs:` chart style vocabulary (`cs:chartArea`, `cs:plotArea`, `cs:legend`, `cs:dataPoint`, `cs:dataLabel`, `cs:gridlineMajor`, …).

Implementation notes:
- Charts live in `chart1.xml` referenced from `w:drawing/a:graphicData` (mime `…chart`).
- Full chart rendering is a large effort — pie, bar, line, scatter, doughnut, bubble, area, radar etc.
- **Pragmatic first step**: the chart part usually embeds a pre-rendered thumbnail via `c:externalData`/chart EMF, or the container `wp:inline`/`wp:anchor` has a fallback DrawingML image. Detect the chart graphic and either rasterize via a chart library or render a placeholder at the correct extent.

### Content controls / glossary

| Tag | Notes |
|-----|-------|
| `w:sdtEndPr` | Terminating run properties for SDT content (formatting applied to text appended after the control). |
| `w:dataBinding` | XPath binding to custom XML — for rendering, treat bound value in `w:sdtContent` as authoritative. |
| `w:tag` | Programmatic tag string — cosmetic, ignore. |
| `w:alias` / `w:aliases` | Display label — partial handling exists (`w:sdtAlias`), verify both forms resolve. |
| `w:docPart`, `w:docParts`, `w:docPartBody`, `w:docPartPr`, `w:docPartUnique` | Glossary/building-block document entries. Usually only relevant if SDT references them; safe to skip body parts in most flows. |

### Web / HTML-origin paragraphs (rare but present)

`w:div`, `w:divs`, `w:divsChild`, `w:divBdr`, `w:bodyDiv`, `w:blockQuote`, `w:marLeft`, `w:marRight`, `w:marTop`, `w:marBottom`

Notes: generated by "Save As Web" in old Word versions. Usually paragraphs inside these divs render fine without div-level styling — treat `w:div` as a passthrough container.

### AlternateContent branching

`mc:AlternateContent`, `mc:Choice`, `mc:Fallback`

Notes: The OpenXml SDK typically pre-processes these, but Morph's parser should explicitly pick `mc:Choice` whose `Requires` namespace is registered (e.g. `wps`, `wpg`, `w14`) and fall back to `mc:Fallback` VML otherwise. If Morph silently walks both, it may render the same shape twice.

---

## MEDIUM IMPACT — cosmetic / secondary visuals

### Image effects (`a14:` and related)

`a14:brightnessContrast`, `a14:colorTemperature`, `a14:saturation`, `a14:sharpenSoften`, `a14:imgEffect`, `a14:imgLayer`, `a14:imgProps`, `a14:shadowObscured`, `a14:hiddenFill`, `a14:hiddenLine`

Notes: Image-adjustment filters. Apply during raster decode (ImageSharp has `.Mutate()` helpers; Skia requires a colour-matrix filter).

### Fills beyond solid colour

`a:gradFill`, `a:gs`, `a:gsLst`, `a:lin`, `a:tile`, `a:tileRect`, `a:fillToRect`, `a:duotone`, `a:grayscl`, `a:alphaModFix`, `a:grpFill`

Notes:
- `a:gradFill` contains `a:gsLst` (gradient stops) + `a:lin` (linear angle) or `a:path` (radial/rectangular).
- `a:gs pos="0..100000"` — `pos` is in thousandths of a percent.
- Skia: `SKShader.CreateLinearGradient` / `CreateRadialGradient`. ImageSharp.Drawing: `LinearGradientBrush` / `RadialGradientBrush`.

### 3D shape effects

`a:scene3d`, `a:sp3d`, `a:bevelT`, `a:camera`, `a:lightRig`, `a:rot`, `a:contourClr`

Notes: Out-of-scope for 2D rendering; accept and drop. Bevel approximation not recommended without a real 3D pipeline.

### Textbox paragraph / default properties

`a:lstStyle`, `a:lvl1pPr` … `a:lvl9pPr`, `a:defPPr`, `a:defRPr`, `a:endParaRPr`, `a:spcBef`, `a:spcAft`, `a:spcPct`, `a:spcPts`, `a:lnSpc`, `a:buNone`, `a:buClrTx`, `a:buFontTx`, `a:buSzTx`, `a:tabLst`

Notes: DrawingML's parallel to `w:pPr` inside shape text. Without these, shape text falls back to run-only formatting (bullets/indent/spacing lost).

### Line ends / dashing

`a:prstDash`, `a:headEnd`, `a:tailEnd`, `a:miter`, `a:round`

Notes: Attributes on `a:ln`. Preset dashes map to dash-array patterns; head/tail end describe arrowheads (type `triangle`/`arrow`/`stealth`/`oval`/`diamond` + length/width).

### Custom geometry helpers

`a:cxnLst`, `a:cxn`, `a:gdLst`, `a:gd`, `a:ahLst`, `a:rect` (inside custGeom), `a:path` (shape), `a:moveTo`, `a:lnTo`, `a:close`, `a:pt`

Notes: Morph already handles `a:cubicBezTo` / `a:quadBezTo`. Missing the rest of the path vocabulary means custom shapes can break mid-path:
- `a:moveTo` + child `a:pt` — start subpath
- `a:lnTo` + child `a:pt` — straight segment
- `a:close` — close subpath
- `a:gd` — guide formulas evaluated from `a:avLst` adjustment values; used when coords reference `+- -` expressions. Parsing math expressions is substantial — common case is numeric literals only.

### Other

| Tag | Notes |
|-----|-------|
| `a:srcRect` | Picture crop rectangle (`l`/`t`/`r`/`b` in thousandths). Apply to source image before drawing. |
| `a:fillRef` / `a:lnRef` / `a:effectRef` / `a:fontRef` | Shape-style indirection into theme style matrices (`a:fmtScheme/fillStyleLst` etc.). Resolve via `ThemeParser`. |
| `a:sym` | Symbol character escape in DrawingML text. |
| `a:pos` | Position hint used inside certain layout elements — context-dependent. |
| `a:extraClrSchemeLst`, `a:objectDefaults`, `a:spDef`, `a:lnDef`, `a:txDef` | Theme-level shape/text defaults. Only matters if shape has no explicit style AND references default. |
| `a:picLocks`, `a:spLocks`, `a:grpSpLocks`, `a:cxnSpLocks` | Edit-lock metadata — ignore. |

---

## LOW IMPACT — safe to ignore (editor metadata / defaults / plumbing)

### Revision / editor metadata

`w:rsid`, `w:rsids`, `w:rsidRoot`, `w14:docId`, `w15:docId`, `a16:creationId`, `w:proofState`, `w:proofErr`, `w:revisionView`

### Style metadata (reached via typed API — not literal tag handling needed)

`w:latentStyles`, `w:lsdException`, `w:uiPriority`, `w:semiHidden`, `w:unhideWhenUsed`, `w:qFormat`, `w:hidden` (style flag), `w:autoRedefine`, `w:link`, `w:basedOn`, `w:next`, `w:category`, `w:aliases`, `w:styleLink`, `w:numStyleLink`

### Settings (editor-only — no visual effect)

`w:zoom`, `w:view`, `w:defaultTabStop` (⚠ paragraphs without custom tabs use this — medium if tabs matter), `w:characterSpacingControl`, `w:clrSchemeMapping`, `w:themeFontLang`, `w:decimalSymbol`, `w:listSeparator`, `w:removePersonalInformation`, `w:removeDateAndTime`, `w:useFELayout`, `w:displayHorizontalDrawingGridEvery`, `w:displayVerticalDrawingGridEvery`, `w:drawingGridHorizontalSpacing`, `w:drawingGridVerticalSpacing`, `w:optimizeForBrowser`, `w:allowPNG`, `w:relyOnVML`, `w:savePreviewPicture`, `w:formsDesign`, `w:doNotValidateAgainstSchema`, `w:doNotDemarcateInvalidXml`, `w:defaultTableStyle`, `w:bordersDoNotSurroundHeader`, `w:bordersDoNotSurroundFooter`, `w:displayBackgroundShape`, `w:doNotShadeFormData`, `w:noPunctuationKerning`, `w:doNotAutoCompressPictures`, `w:doNotExpandShiftReturn`, `w:doNotIncludeSubdocsInStats`, `w:paperSrc`, `w:hideSpellingErrors`, `w:hideGrammaticalErrors`, `w:stylePaneFormatFilter`, `w:stylePaneSortMethod`, `w:numIdMacAtCleanup`, `w:attachedTemplate`, `w:activeWritingStyle`, `w:webSettings`

### Font table (potentially useful for font matching)

`w:fonts`, `w:font`, `w:altName`, `w:panose1`, `w:family`, `w:pitch`, `w:charset`, `w:sig`, `w:notTrueType`, `w:embedRegular`, `w:embedBold`, `w:embedItalic`, `w:embedSystemFonts`, `w:embedTrueTypeFonts`

Implementation notes: if fonts aren't resolving well, reading `w:altName` (substitute family) and `w:panose1` (Panose-1 classification) could improve substitution fallback.

### Document variables / glossary / web

`w:docVars`, `w:docVar`, `w:glossaryDocument`, `w:behaviors`, `w:behavior`, `w:types`, `w:docPartUnique`

### VML compat defaults (headers)

`o:shapedefaults`, `o:shapelayout`, `o:idmap`, `o:colormru`

### Math defaults (no `m:oMath` content present in corpus)

`m:mathPr`, `m:mathFont`, `m:brkBin`, `m:brkBinSub`, `m:defJc`, `m:dispDef`, `m:intLim`, `m:naryLim`, `m:smallFrac`, `m:wrapIndent`, `m:lMargin`, `m:rMargin`

### Accessibility / sketch / tracking / theme-family

`adec:decorative`, `ask:lineSketchStyleProps`, `ask:type`, `thm15:themeFamily`, `w15:appearance`, `w15:chartTrackingRefBased`, `int2:intelligence`, `int2:intelligenceSettings`, `int2:bookmark`, `int2:observations`, `int2:onDemandWorkflows`, `int2:state`

### Anchor plumbing (implicitly traversed — already working)

`wp:simplePos`, `wp:effectExtent`, `wp:docPr`, `wp:cNvGraphicFramePr`, `wp:extent`, `wp:align`, `wp:wrapSquare`/`wrapTight`/`wrapTopAndBottom` (only `wrapNone` observed in index but parsers handle others), `pic:cNvPicPr`, `pic:cNvPr`, `pic:nvPicPr`, `wps:cNvCnPr`, `wps:cNvPr`, `wps:cNvSpPr`, `wpg:cNvGrpSpPr`, `wpg:cNvPr`, `a:graphic`, `a:graphicData`, `a:graphicFrameLocks`, `a:avLst`, `a:fillRect`, `a:stretch`

---

## Top candidates to implement next

Updated after the audit + recent feature work (see `docs/word-features.md` for the canonical status).

1. **Custom-XML data binding** (`w:dataBinding`) — populates SDT content from bound data islands.
2. **Remaining run formatting gaps**: `w:em` (East-Asian emphasis mark), `w14:textFill` (gradient text fill).
3. **Image adjustments** (`a14:brightnessContrast`, `a14:saturation`, etc.) — Word's "Picture Format → Adjustments" filters.
4. **Percentage *position* offsets** (`wp14:pctPosHOffset`/`pctPosVOffset`) — sizing is in; positioning is still EMU-only.
5. **Chart rendering beyond placeholder** — currently renders as empty space matching the drawing extent; either ship a minimal renderer or surface an `mc:Fallback` thumbnail.

(Legacy VML — `v:shape`, `v:rect`, `v:imagedata`, etc. — is intentionally not on this list; see the dedicated section above.)

Recently completed (no longer on the list):

- `w:hyperlink`, footnotes / endnotes, `w:tblPrEx`, `w:cnfStyle`, `w:tblHeader`, `w:tblCellSpacing` (detached-border model, verified via `Tests/Inputs/table_cell_spacing/01`), `w:tl2br` / `w:tr2bl` diagonal cell borders (verified via `Tests/Inputs/table_diagonal_borders/01`), `w:hideMark`, `w:noWrap` (cell), `w:tblCaption` / `w:tblDescription` (accessibility metadata, no-op), `w:tblOverlap` (floating-table-only, no-op for inline), `w:mirrorIndents` (parsed; renderer doesn't yet swap indents), `w:framePr` (drop-cap subset only; absolute positioning is a no-op), East-Asian layout flags `w:wordWrap` / `w:kinsoku` / `w:overflowPunct` / `w:autoSpaceDE` / `w:autoSpaceDN` / `w:adjustRightInd` (no-op), `w:pgNumType` (no-op until fields are evaluated), `w:ulTrailSpace` (already matches default-on behaviour), `w:vanish` / `w:specVanish` (hidden runs dropped at parse), `w:webHidden` (no-op), `w:position` (baseline shift), `w:bdr` (per-run border), `w:emboss` / `w:imprint` / `w:outline` (run-effect bundle), `w:effect` (animated text, no-op), `wp14:sizeRelH` / `wp14:sizeRelV` + `wp14:pctWidth` / `wp14:pctHeight` (percentage sizing for floating drawings), `w:smallCaps`, `w:dstrike`, `w:kern`, `w14:textOutline` / `glow` / `shadow` / `reflection`, custom tab stops, gradient shape fills (`a:gradFill`), image crop (`a:srcRect`), image rotation, even/odd page headers + footers (verified end-to-end via `Tests/Inputs/even_odd_headers/02`).
