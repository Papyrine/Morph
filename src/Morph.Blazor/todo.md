# Editor backlog

Open findings from the 2026-11-02 review of `MorphEditor` (browser session plus code reviews of the
typing box, the viewer script, the ribbon and command table, and the .NET edit flow). A finding is
deleted when it lands. Paths are under `src/Morph.Blazor/` unless they say otherwise.

Fixed on 2026-11-02 and not listed: the look read at the opening caret (Grow/Shrink, Ctrl+]/[,
sub/superscript, caps and underline toggles); page selections across a page boundary taking in the
footer and header; Replace All over a repeated header or footer; Remove link beside a link and a
second link in a paragraph; the selected word count; typing lost after Ctrl+Enter; hidden text
breaking hand-in; the README's Delete row.

## Bugs

### Lost or misplaced typing

- **Accept all / Reject all and the review-pane cards relayout without handing the open paragraph
  in.** `reload()` (`wwwroot/morph-viewer.js:507`) cancels a box that is not pending, so what was
  typed is dropped. `ResolveAllAsync` (`Components/MorphViewer.Review.cs:552`) runs from a ribbon button
  that keeps focus, so there is no focus-out commit; the cards do move focus, and the `setTimeout(0)`
  commit in `onFocusOut` (`wwwroot/morph-edit.js:2408`) races the Blazor click, so whichever
  `ReviseAsync` is second returns false with no note (`Review.cs:625`). Hand the paragraph in first
  (`HandInAsync`) on every path that revises.
- **A relayout that cancelled the box leaves a stale target.** The `cancel` callback
  (`morph-viewer.js:2333`) keeps `lastTarget` with the old outline's ordinals and `editTarget()` returns
  it; `FromEditor` (`Components/MorphViewer.Editor.cs:82`) accepts anything in range. Paragraph 7 open,
  accept a deletion of paragraph 2 in the pane, press Ctrl+E: the new paragraph 7 is centred.
- **A hand-in refused because another edit is in flight is lost silently.** `OnEditCommit`
  (`Components/MorphViewer.Edit.cs:240`) nulls the session before `ReviseAsync`, which returns false
  while `revising` with no `editNote`; the script then releases the box. Same race as the first item.
- **Arrow Down/Up off a page's last or first line closes the editor and opens nothing.** `leaveEditor`
  (`morph-viewer.js:3040`) probes about 150px, less than two margins and the page gap, and
  `elementFromPoint` is null off-viewport. The README promises the next paragraph.
- **A behind-text floating picture takes every click on the text over it.** `pictureAt`
  (`morph-viewer.js:2131`) hits any picture rectangle before `onEditClick`; `Pictures()`
  (`Components/MorphViewer.razor.cs:553`) sends no wrap kind, so the script cannot prefer text. Same in
  `onContextMenu`.
- **Keys act on the document behind a modal dialog** (reported by review, not reproduced): `onEditKey`
  runs whenever focus is not in a form field (`morph-viewer.js:1566`) and only Escape checks for a
  dialog. A page selection plus the Paragraph launcher, then a letter, would type over the selection.

### Commands enabled where the core refuses them

Each ends in the error panel or a status note rather than greyed out. The rules are in
`Components/MorphEditor.Commands.cs`.

- Footnote/Endnote inside a note (`ForFootnote`/`ForEndnote`, line 195) vs
  `src/Morph/OpenXml/Editing/DocumentEditor.Stories.cs:116` ("A note goes in the body").
- Table of contents in a header or footer (`Contents.Enabled = OutsideNotes`, line 791) vs
  `DocumentEditor.Fields.cs:192`. The `WhereEachCommandCanBeUsed` snapshot pins the current rule.
- Link with a selection spanning paragraphs (default rule, line 661) vs `DocumentEditor.Inserts.cs:641`.
  Word greys Link and Date & Time with cells selected; `SpansParagraphs` is already in the state.
- Insert > Table enabled with a selection (line 643); Word greys it whenever anything is selected.

### Headers, footers and sections

- **A header or footer paragraph is reopened and reselected on its first page**, not the page the
  reader is on. `Block(paragraph, -1)` (`MorphViewer.Edit.cs:182`) falls back to the first block,
  `ResumeAsync` always passes -1, and `SelectAsync` uses `ReviewMap.Locate`, which returns the first
  page. `EditTarget` carries no page. Centre on a page-5 footer jumps the view to page 1; Bold on
  footer text reselects it on page 1; Left/Right out of a footer paragraph, Tab between header table
  cells, `StoryEndAsync` and `GoToBookmarkAsync` likewise.
- **Header/Footer from inside a header, footer or note acts on the last section.** `SectionOf`
  (`DocumentEditor.Sections.cs:231`) treats a paragraph beside the body as in the last section; the
  header part's `headerReference` says which section it belongs to.
- **Ctrl+Home/End in a header walks every header part as one story.** `MorphViewer.Editor.cs:957`
  groups by `Part`, which is one value for all headers; `DocumentOutline` keeps them per part root.
- **Custom margins rewrites all four sides through two-decimal centimetres.** `OpenMargins`
  (`Components/MorphEditor.razor.cs:357`) formats with `0.##` and `CustomMargins` converts every side
  back: 1080 twips becomes 1077, and the Moderate preset stops showing as checked. Keep the original
  twips for a side whose text is unchanged.

### State and history

- **`ShowAsync` is not atomic** (`MorphViewer.Review.cs:680`). `Adopt` and `documentId` move before
  `sourceBytes`; a throw from `ReloadAsync`/`SetPicturesAsync` leaves new pages over old bytes, and a
  throw from `OnDocumentChanged` or `DocumentRevisedAsync` leaves an edit on screen with no undo entry
  and no draft.
- **The non-relayout `ShowAsync` path has no generation guard** (`Review.cs:695`): a file opened while
  Track changes or a comment edit is in flight ends with the new pages and the old bytes.
- **Stale `beforeEdit` decides where undo puts the caret.** `TargetAsync` sets it for callers that make
  no edit (Format Painter pick-up, picture target, a cancelled link or bookmark dialog) and
  `ReviseAsync` consumes it for edits that set none. `WrapPictureAsync` also nulls `floatingPicture`
  before the revise, so a refused wrap leaves the frame up and "Select a picture first" on the next try.
- **Redo's place is wrong or missing**: `FormatTextAsync` (`Editor.cs:2405`) bypasses `PlacedAfter`, so
  redo of Bold in an open paragraph restores no selection; `GoToBookmarkAsync` and `StoryEndAsync` go
  through `ResumeAsync` while `placingAfter` is still true and rewrite the last step's `After`.
- **Ribbon Undo/Redo never grey out** (default rule) while the review pane's do, on the same screen.
  Needs `CanUndo`/`CanRedo` in `EditorState` (the script's stack while a paragraph is open, else
  `undo.Count`).
- **Replace after a hand-in replaces a different match.** `ReplaceOneAsync` hands in first; a changed
  paragraph relays out and `RunSearchAsync` resets `currentMatch` to the first match on the page.
- **Inserts go in at a selection's end instead of replacing it** (`FromEditor` takes the caret from
  `selected.to`), and a link's inserted text leaves the caret before it where Word leaves it after.

### WOPI

- **`DisableExport` and `DisablePrint` are bypassed by Ctrl+S and Ctrl+P.** `morph-viewer.js:1584` and
  `:1689` are unconditional; `SaveAsync` (`MorphViewer.razor.cs:989`) falls to `DownloadAsync` with no
  `OnSave`, and `PrintAsync` never checks `ShowPrint`. The flags gate only markup.
- **The host's document is drafted into IndexedDB** (`Components/MorphViewer.Drafts.cs:63`) with no
  opt-out; under WOPI there is no start page to offer it, so a tab closed unsaved leaves the file in the
  browser indefinitely.
- **Opening another file over unsaved edits neither asks nor keeps the draft reachable.** Only New asks;
  `OnFileSelectedAsync`, a dropped file and a `Source` change open straight away and the previous draft
  is overwritten by the first edit to the new file.

### Typing box (`wwwroot/morph-edit.js`)

- **F8 extend mode survives Delete/Backspace**, after which letters are swallowed by `extendTo`
  (line 981); a click also leaves it on, and there is no status-bar indicator.
- **Overtype and repeat-delete step by UTF-16 code unit** (lines 1080, 1303), splitting surrogate pairs.
- **Drag-and-drop of text within the box does not move it**: `deleteByDrag` re-renders and the following
  `insertFromDrop` falls back to the deletion point (lines 2031, 1920); HTML drag data goes through
  `PasteAsync`, so a dragged field comes back as plain text.
- **Backspace beside a floating picture's invisible anchor** selects the hidden span and the second
  press deletes the picture (`renderAtom` 448, `selectAtom` 2043, `removable` 694). Overtype types over
  it silently.
- **Ctrl+Backspace / Ctrl+Delete group a line break with adjacent punctuation**: `wordRange` (1465)
  classifies `\n` as a mark.
- **Enter at a heading's end skips AutoCorrect and AutoComplete** (`onParagraphKey` 2312 prevents the
  `insertParagraph` branch at 1976).
- **Copy from the box uses the browser's default**: a page-break atom copies as the words "Page break",
  a field as its result, and the HTML carries the box's `cqw` styling. The pages copy plain text only.
- **Triple-click in a box Enter has split selects the next block** (`onClickCount` 1710 uses `caret.to`).
- **A pending look (Ctrl+B between spaces) is dropped for composed input**: `adopt()` (1591) never
  consults `pendingFace`.
- **Cmd is treated as Ctrl for navigation on macOS** (lines 761, 791, 810, 845, 127).
- **Changes the browser makes itself are not undoable in the box**: `onInput` → `adopt()` records no
  undo entry for a spellcheck replacement or a drop.

### Viewer script (`wwwroot/morph-viewer.js`)

- **The ruler is drawn at a stale scale after zoom or resize**: `applyScale` (1131) never calls
  `drawRuler`, `onResize` (1178) calls it before scaling, and `onRulerDown` closes over that scale, so a
  drag after a zoom sets indents off by the zoom ratio. `drawRuler` (2507) also measures the current page
  where the model is the caret's page.
- **A key on a page selection races the 120ms selection debounce** (`onSelectionChange` 2007): a letter
  typed within 120ms of a double-click is swallowed with "Select some of the document's text first".
- **A released editor wipes the ribbon state of the editor now open** (`release` 2391 →
  `showEditState(null)`): after a style press, Bold and alignment indicators blank until the caret moves.
- `controlDown` is set on `root` and cleared on `document` (304, 315), and `onWheel` (1420) reads
  `deltaMode` before `deltaY`: on Firefox with focus outside the viewer Ctrl+wheel takes the pinch branch.
- `placeMenu` (3015) clamps `top` to 0; in a 320px-high viewer the context menu's last items are clipped.
- Ctrl+= is subscript in the editor, so the README's Ctrl+plus zoom is unreachable except on the numpad.
- Ctrl+S/P/F/O call `preventDefault` even with no document or `ShowOpenFile=false`.
- `markCurrent` (1342) `scrollIntoView` can scroll the host page on every page change.
- Ctrl+F and Ctrl+H do nothing until the pages are clicked, since the key listener is on the viewer root.

### Dialogs, menus and ribbon papercuts

- New comment is enabled at any body caret but only shows "Select the text to comment on first";
  Word comments the word at the caret (`Word(target)` already finds it). Same for the context menu line.
- Enter does not press OK in the Paragraph and Font dialogs (`OnFormDialogKeyDownAsync`,
  `Components/MorphEditor.Dialogs.cs:760`) nor Date & time, while Margins, Split, Bookmark and Link
  submit on Enter.
- The column width box (`MorphEditor.razor:1123`) never shows the current column's width, keeps the
  typed value as the caret moves, drops bad input silently, and the `max="55"` is only on the input:
  1000 writes 567,000 twips.
- Paste is hidden rather than greyed when `navigator.clipboard.readText` is absent
  (`morph-viewer.js:2433`); the context menu's Paste line hides too, leaving its separator.
- Date & time: "Update automatically" defaults on (Word's is off), and the formats are invariant-culture
  `d/MM/yyyy` regardless of `ExportOptions.Culture` or the browser locale
  (`Components/MorphEditor.Fields.cs:10`, `:22`).
- The edit-link case starts the address at `https://` rather than the link's existing address.
- The Bookmark dialog's note omits the 40-character limit `IsBookmarkName` enforces.
- The Insert > Table grid re-renders the whole editor per cell hover (`@onmouseover`, 80 cells), each
  render recomputing `State()`, `SpansParagraphs` and the status bar's `Count()`.
- Review > Changes is hidden, not greyed, when `CanResolve` is false; the README says greyed.
- `Subscript`/`Superscript` `Checked` read the opening caret's look; the script's pressed-state loop only
  touches `data-edit-command` buttons.
- A click or key during the ~0.5s relayout does nothing with no feedback (`OnEditHit`, `RequireAsync`
  return silently while `revising`).

### Accessibility and markup

- The File button sits inside `role="tablist"` without `role="tab"`; tabs have no `aria-controls`/`id`
  pairing with the `tabpanel`.
- The Font and Font size dropdowns announce only "Font"/"Font size": the value is CSS generated content
  (`.editor-shown::before`), not in the accessibility tree.
- The status bar is `role="status" aria-live="polite"` and contains the zoom and word-count controls, so
  every zoom or page change re-announces the whole bar.
- Overtype is a plain `<span>`; Word's status item toggles it on click.

## Missing features

In reach, the core already has the method:

- Font and size typed directly into the ribbon boxes (`Format(Font/SizePoints)`; the Font dialog takes free text)
- Insert Symbol (`Editing.Text`) and Blank page (two page breaks)
- Layout indent and spacing spinners (`FormatParagraphs`)
- Next/previous footnote (walk `outline.Paragraphs` by part as `StoryEndAsync` does)
- Previous/next change on the Review tab (the pane already steps)
- Fit-width and whole-page zoom buttons (`ViewerZoom` fit modes exist)
- A Change case button (Shift+F3 `changeCase` exists in the script)
- Keyboard navigation in menus (Escape only today)
- Dragging a floating picture to move it (the frame is `pointer-events: none`; `dragPicture` only resizes)
- Touch pinch-zoom (only Ctrl+wheel and Safari gesture events)
- Shift+arrow, Shift+End and Ctrl+Shift+Home/End selection leaving the open paragraph; Ctrl+Up/Down
- A "caret lost" fallback when `leaveEditor`/`Locate` finds nothing, instead of closing the editor
- A status-bar indicator for Extend mode

Absent from the core as well: shapes, text boxes, equations, cover page, captions, cross-references,
column breaks, hyphenation, line numbers, picture position, sort, text effects, citations, spelling
(browser spellcheck only), show-markup options, compare, read mode, web layout, split and one/two-page
views.
