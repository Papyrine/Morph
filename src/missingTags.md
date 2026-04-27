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
| `w:dstrike` | Double strikethrough. Draw two parallel strike lines (use font metrics ×1.3 spacing). |
| `w:smallCaps` | Lowercase rendered as scaled uppercase (~0.75em). Apply during text shaping before Skia/ImageSharp text layout. |
| `w:vanish` | Hidden text. Skip the run entirely (unless `settings.xml` has `w:showHiddenText`). |
| `w:specVanish` | Structural hidden (TOC markers etc.). Treat same as `w:vanish`. |
| `w:webHidden` | Hidden in web view — render normally for print/image. |
| `w:emboss` | 3D emboss effect. Approximate: draw darker glyph offset ↘ then lighter glyph. |
| `w:imprint` | Engrave (inverse emboss). |
| `w:outline` | Stroke-only text. Set paint to stroke with zero fill. |
| `w:effect` | Animated text (`blinkBackground`, `sparkle` etc.) — render as plain text (effect is animation-only). |
| `w:em` | East-Asian emphasis mark (dot/circle above/below each glyph). |
| `w:kern` | Kerning threshold in half-points; only kern fonts ≥ value. Most engines kern by default — wire into typography settings. |
| `w:position` | Baseline shift in half-points (+up, -down). Distinct from `w:vertAlign` (super/sub with resize). |
| `w:bdr` | Per-run border — draw rectangle around the run's measured box. |
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

| Tag | Notes |
|-----|-------|
| `w:tabs` / `w:tab` (inside `w:pPr`) | Custom tab stop list. Each `w:tab` has `w:val` (start/center/end/decimal/bar), `w:pos` (twips), optional `w:leader`. Text layout must honour these at `w:tab` run elements. |
| `w:framePr` | Floating text frame (pre-drawing era). `w:w`/`w:h`/`w:x`/`w:y`/`w:wrap`. Render as absolutely positioned paragraph. |
| `w:pgNumType` | Page number format/start. Only relevant if fields are evaluated. |
| `w:mirrorIndents` | Swap left/right indents on even pages. |
| `w:adjustRightInd` | Auto-adjust right indent for East Asian chars. |
| `w:wordWrap` | Disable mid-word break for East Asian. |
| `w:overflowPunct`, `w:kinsoku` | East Asian line-break rules. |
| `w:autoSpaceDE` / `w:autoSpaceDN` | Auto space between East Asian and Latin/numerals. |
| `w:noWrap` (cell) | Prevent cell text wrapping (auto-fit cell to content instead). |
| `w:ulTrailSpace` | Underline trailing spaces. |
| `w:evenAndOddHeaders` (settings) | ⚠ Actually high impact — enables `evenPage`/`oddPage` header/footer references. Currently all pages probably use default. |

### Tables

| Tag | Notes |
|-----|-------|
| `w:tblPrEx` | Row-level table property exceptions (row overrides table defaults). Merge over `w:tblPr` when resolving row. |
| `w:cnfStyle` | Conditional format flags — picks the right `w:tblStylePr` (`firstRow`/`lastRow`/`firstCol`/`lastCol`/`band1Horz`/`band2Horz`/…). Needed for banded-table rendering. |
| `w:tl2br` / `w:tr2bl` | Diagonal cell border (attribute on `w:tcBorders`). Draw line corner-to-corner. |
| `w:tblHeader` | Row repeats as header on page break — matters once pagination exists. |
| `w:tblCellSpacing` | Non-zero → detached border model (gaps between cells). |
| `w:hideMark` | Hide end-of-cell paragraph mark — cosmetic but affects cell height measurement when cell is empty. |
| `w:tblCaption` / `w:tblDescription` | Accessibility metadata — ignore for rendering. |
| `w:tblOverlap` | Whether floating tables may overlap — ignore unless floating tables are implemented. |
| `w:tblStyleRowBandSize` | Companion to the already-handled `w:tblStyleColBandSize`. |

### Legacy VML drawing (Word 2007-compat fallback and form controls)

`v:shape`, `v:shapetype`, `v:group`, `v:line`, `v:oval`, `v:rect`, `v:roundrect`, `v:polyline`, `v:textbox`, `v:imagedata`, `v:fill`, `v:stroke`, `v:shadow`, `v:formulas`, `v:path`, `v:handles`, `v:f`, `v:h`, `w10:wrap`, `w10:anchorlock`, `o:fill`, `o:lock`

Implementation notes:
- Many older docs, headers/footers, and `w:pict` contents use VML rather than DrawingML.
- Inside `mc:AlternateContent`, DrawingML is in `mc:Choice` and VML in `mc:Fallback` — if `mc:Choice` is consumed, VML can be ignored; but `w:pict` blocks outside AlternateContent have VML only.
- `v:shape` has a `style` attribute with CSS-like `position:absolute; left:Xpt; top:Ypt; width:Wpt; height:Hpt`.
- `v:shapetype` + `o:spt` / `v:formulas` define a reusable geometry; map common `o:spt` values to DrawingML preset geometry IDs instead of re-implementing VML path language.
- `v:imagedata r:id=…` is the VML equivalent of a DrawingML picture — reuse image-relationship resolution.

### Floating drawing extensions

| Tag | Notes |
|-----|-------|
| `wp14:sizeRelH` / `wp14:sizeRelV` | Relative sizing container — `relativeFrom="page"/"margin"/…`. |
| `wp14:pctWidth` / `wp14:pctHeight` | Percentage of the reference (stored ×1000, e.g. `50000` = 50%). |
| `wp14:pctPosHOffset` / `wp14:pctPosVOffset` | Percentage position offsets. |

Without these, percentage-scaled images end up at the fallback `wp:extent` pixel size, which may be zero or wrong.

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

1. **`w:hyperlink`** — trivial wrapper; ubiquitous in every real document.
2. **Footnotes / endnotes** (`w:footnote*`, `w:endnote*`, `w:footnoteRef`, separators) — currently silently dropped.
3. **VML shape family** (`v:shape`, `v:shapetype`, `v:line`, `v:rect`, `v:roundrect`, `v:textbox`, `v:imagedata` + `w10:wrap`) — required for legacy docs, signatures, many header/footer decorations.
4. **Charts** (`c:*` / `cs:*`) — either a minimal chart renderer or a fallback that surfaces the chart's embedded thumbnail image.
5. **Table enhancements**: `w:tblPrEx`, `w:cnfStyle`, `w:tl2br`/`w:tr2bl`, `w:tblHeader`, `w:tblCellSpacing`.
6. **Percentage-sized floating drawings** (`wp14:pctWidth`/`pctHeight`, `wp14:sizeRelH`/`sizeRelV`).
7. **Run formatting**: `w:smallCaps`, `w:dstrike`, `w:vanish` (skip), `w:kern`, `w:position`, `w:bdr`, `w:em`, plus `w14:textFill`/`textOutline`/`glow`/`shadow`.
8. **Custom tab stops** (`w:tabs`/`w:tab` inside `w:pPr`) — affects indentation fidelity.
9. **Gradient fills** (`a:gradFill` + `a:gs`/`a:lin`) — widely used for shape backgrounds.
10. **Image crop / adjustments** (`a:srcRect`, `a14:brightnessContrast`, `a14:saturation`, etc.).
