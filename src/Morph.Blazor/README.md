# Morph.Blazor

Reusable Blazor WebAssembly components that convert Word `.docx`, Excel `.xlsx` and PowerPoint `.pptx`
files to **PNG**, **PDF**, **HTML**, **Markdown** or **plain text**, show them as they are — the way a
browser shows a PDF — or edit a Word document the way Word does, entirely in the browser. No file ever
leaves the device.

This is the converter, viewer and editor that power [morph.papyrine.org](https://morph.papyrine.org/),
packaged so any Blazor app can drop them in. The app in `src/Morph.Web` is now only a shell — header,
navigation, theme toggle and footer — around the `MorphConverter`, `MorphViewer` and `MorphEditor`
components from this package.

Every icon the components draw is [Lucide](https://lucide.dev/)'s, through
[IconifyBundle](https://github.com/Papyrine/IconifyBundle): only the icons named are compiled in, each
rendered as an inline `<svg>` in the text colour, so the package fetches no icon font or sprite.

## Install

```
dotnet add package Morph.Blazor
```

## Set up

Three things, all in the host app:

1. Register the services:

    ```csharp
    builder.Services.AddMorph();
    ```

2. Make sure a base-addressed `HttpClient` is registered — the Blazor WebAssembly template already does
   this. The components use it to fetch the bundled fonts and samples out of this package's static web
   assets.

3. Link the stylesheet in `index.html`, **before** the host's own, so the host can override anything in it:

    ```html
    <link rel="stylesheet" href="_content/Morph.Blazor/morph.css" />
    ```

No `<script>` tag is needed: the JavaScript ships as an ES module and the components import it themselves.

Then use it:

```razor
<MorphConverter />
```

That is the whole widget — upload panel (or one of three bundled samples), a live page-image preview,
an output-format picker with per-format options, and a download button. On a viewport wider than 1200px
it also shows the selected format's real output beside the preview: Markdown and plain text inline, PDF
and HTML in an iframe.

### `MorphConverter` parameters

| parameter | default | what it does |
| --- | --- | --- |
| `ShowSamples` | `true` | Offers the bundled sample document, workbook and deck. |
| `ShowResultPane` | `true` | Lets a wide viewport show converted output beside the preview. Off also stops the conversion feeding it. |
| `ResultPaneMinWidth` | `1200` | Viewport width at or above which that pane may show. Match it to the stylesheet if the breakpoint is overridden. |
| `Formats` | every format | Restricts the output formats offered. |
| `InitialTarget` | `OutputFormat.Png` | The format selected on first render. |
| `PreviewDpi` | `110` | Resolution of the on-screen preview. The PNG download uses the user's own choice. |
| `MaxFileSize` | 25 MB | Largest upload the component will read. |
| `ShowIssueLink` | `true` | Whether an unexpected failure offers a pre-filled GitHub issue against the Morph repo. |
| `Class` | — | Extra CSS classes for the root element. Any other attribute is splatted onto it too. |

## Selectable text

Every rendered page, in the converter's preview and in the viewer, carries a **text layer**: a transparent
copy of the page's text laid exactly over the image — the technique PDF.js uses to make a rendered page
selectable. Text can be dragged over, copied and found with the browser's own find, as in a PDF viewer.

The layer is read from the same single layout pass the image is painted from, so every run sits where it
was drawn:

- one absolutely positioned element per line, with its runs inline — the browser's find cannot match
  across two positioned elements, so a word-per-element layer would leave every phrase unfindable;
- sized in container-query units of the page, so it tracks the image at any displayed width or zoom
  without any script running on resize;
- set in the same Aptos faces the browser raster uses, with only the spaces a justified line removed
  re-spaced to their drawn gaps, so selection highlights land on the drawn glyphs;
- copied through its own `copy` handler, so every engine yields the same plain text: words re-spaced
  where the layout removed spaces, a tab between table cells (a copied worksheet row pastes back into a
  spreadsheet as cells), a line break after each paragraph and row, the footer after the body. No
  rich-text flavour is offered, which would otherwise paste invisible transparent-coloured text into an
  editor.

Warped WordArt is one figure with no line geometry, so its whole box stands in for its text.

## Viewer

```razor
<MorphViewer Source="bytes" FileName="report.docx" />
```

`MorphViewer` shows a `.docx`, `.xlsx` or `.pptx` without converting it, with the toolbar of a browser PDF
viewer — and, for a Word document, lets its comments and tracked changes be read and settled in a pane
beside it. Its text is not edited here: that is [`MorphEditor`](#editor)'s. The file
is parsed and laid out once; after that only the pages in view are painted, one at a
time, at the resolution the zoom and the screen's pixel density call for — so a long document opens fast
and stays responsive on the single-threaded WebAssembly runtime, and zooming in sharpens the page rather
than stretching it.

The viewer fills its box: give it a height (the stylesheet's default is `80vh`). Its setup is the
converter's — `AddMorph()`, a base-addressed `HttpClient` and the stylesheet.

| control | what it does |
| --- | --- |
| Sidebar | Page thumbnails; the current page is marked, and a click goes to it. |
| Find (Ctrl+F) | Match count, next/previous (Enter / Shift+Enter), match case. A phrase matches across a line break, a paragraph or a table cell — which the browser's own find cannot do over positioned text. |
| Page navigation | Previous/next, and a page (or slide) number to jump to. |
| Zoom | Out/in through PDF.js's steps; Automatic zoom (page width for portrait, page fit for landscape, never above 125%), Actual size, Page fit, Page width, 50–400%. Ctrl+wheel and a trackpad pinch zoom about the pointer. |
| Rotate | A quarter turn clockwise, pages and thumbnails together. |
| Open (Ctrl+O) | Picks a file; a file dropped anywhere on the viewer opens too. |
| Presentation mode | Fullscreen, one page at a time, fitted to the screen. Arrow keys, Space, Page Down, a click or a swipe advance; Escape leaves. Where the browser refuses fullscreen (an iPhone, an embedded frame) a full-window overlay stands in. |
| Print (Ctrl+P) | Renders every page at `PrintDpi` and hands them to the browser's print dialog, each on its own sheet at the page's size. |
| Download (Ctrl+S) | Saves the file — as it was opened, or as since edited in the review pane. |
| Comments and changes | Word documents only. Opens the review pane; the badge counts the tracked changes and the comment threads not yet resolved. See [Review](#review). |

Keyboard, with focus in the viewer: ←/→ page when the page does not scroll sideways, Home/End go to the
first/last page, Ctrl+`+`/`-`/`0` zoom in/out/back to automatic, Ctrl+A selects the pages' text.

### Pages drawn as SVG

A page is not painted on the WebAssembly runtime (`VectorPages`, the default): `SvgPainter` (in
Morph.ImageSharp, beside `ImageSharpPainter`) writes the laid-out page as SVG in points, and the browser
draws it — at any zoom, so a page is never asked for twice. Writing a page takes tens of milliseconds where
painting it took most of a second. Pagination and positions are still Morph's: every glyph carries its own
`x`, taken from the layout the PNG painter draws, and the text is set in the text layer's Aptos faces
(`Morph Text`). Rules, shading, borders, shapes, gradients and pictures are SVG elements; a picture the
browser cannot take as it is (stored as SVG, tiled, recoloured, a format no browser decodes) and warped
WordArt are embedded as the bitmap the PNG painter draws of them. Thumbnails and printed pages are SVG too.
A page the SVG cannot carry whole (`SvgPage.Complete`) is painted as a PNG.

The SVG stands in an `inert`, `aria-hidden` holder that draws no box (`display: contents`): its text is
the text layer's to select, copy and find, and it leaves out spaces, which draw nothing. The holder is
what makes it inert — `inert` is an HTML attribute and does nothing on an `<svg>`, where the browser's own
find bar counted every word three times (the text layer, the page and its thumbnail). Pages far from the one in view are let go like painted ones (24 kept) and
drawn again when they come near.

`?pages=png` in Morph.Web paints instead; `?pages=svg-timed` sets `MeasurePaint`. `VectorPageComparison`
(Morph.Web.Tests, explicit) compares SVG pages with painted ones over the corpus, and
`SnapshotTests.Benchmark_VectorPages` times both on the real runtime.

## Editor

```razor
<MorphEditor />
```

`MorphEditor` is a word processor for Word documents in the image of Word: a **File** menu and a ribbon of
tabs over the pages, and a status bar under them with the page, a word count and the zoom. It is the
viewer underneath — the same pages, sidebar, find bar and review pane — with a Word document's text
edited where it stands. It takes every `MorphViewer` parameter; with nothing open it offers a new blank
document, Open, and (with `ShowSamples`) the sample document. A workbook or a deck opens to be read.

The ribbon is laid out as Word's: the tabs on a title band with the file's name and Save, the chosen
tab open onto the ribbon under it; each tab's commands in named groups, a group's main command a large
button with its name under its icon and the rest small buttons beside it. In a narrower window the
groups give up room rather than wrap, as Word's do — the style gallery shows fewer styles, then Home's
small buttons keep only their icons — and on a phone the ribbon scrolls sideways.

| ribbon | what it does |
| --- | --- |
| File | New blank document (Word's own, as Word makes it), Open, Save a copy, Export as PNG, PDF, HTML, Markdown or plain text, Print. A new document over edits not yet saved asks first. |
| Home | Undo, Redo; Paste, Cut and Copy (Paste reads the clipboard, which the browser may ask the reader for; refused, the status bar says to use Ctrl+V); Format Painter — pressed, it picks up the character formatting where the caret is and its paragraph's, and the text selected next on the pages takes them: the characters' in place of its own, and a paragraph's too where the selection takes the paragraph whole, as Word's painter does; pressed again, it is off; the style gallery — the styles of Word's gallery drawn as their text looks, two rows at a time with arrows to page through, and a button for every style the document offers; the font (the document's own first, then common ones) and size, grow and shrink font; bold, italic, underline, strikethrough, superscript, subscript; font colour and highlight, each a button for the colour last chosen beside a palette; clear formatting; bullets and numbering, each a button beside its library — Word's six bullets (disc, circle, square, diamonds, arrow, check mark) and seven numberings (1. 1) I. A. a) a. i.), which a list the caret is in takes whole and other paragraphs start or join — with Restart at 1 and Continue numbering under the numbering's; decrease and increase indent; Show/Hide ¶ (Ctrl+Shift+8), Word's formatting marks over the pages — a dot for each space, an arrow for each tab, a pilcrow at each paragraph's end and ¤ at a cell's; alignment; line spacing (1 to 3) and space before and after; paragraph shading, and paragraph borders (bottom, top, left, right, none, all, outside, inside horizontal, at a width — a box round several paragraphs is the same borders on each, as Word draws it); find, and replace (Ctrl+H). The launchers at the foot of the Font and Paragraph groups open Word's dialogs: Font — the font, style, size, colour, underline style (single, words only, double, thick, dotted, dashed, wave) and effects (strikethrough, double strikethrough, superscript, subscript, small caps, all caps, hidden), with a preview; Paragraph (also the spacing menu's Line spacing options…) — alignment, left and right indents in centimetres, a first-line or hanging indent, space before and after in points, line spacing (single, 1.5, double, at least, exactly, multiple), and widow and orphan control, keep with next, keep lines together and page break before. A dialog sets only what was changed in it. |
| Insert | Page break; table, sized on an 8 × 10 grid; picture (PNG, JPEG, GIF, BMP); link, to a web address or to one of the document's bookmarks (`#name`, which the address field offers), and Remove link; Bookmark, Word's dialog — a name for the caret's place or the text selected, the document's bookmarks listed to delete or go to; Header and Footer, which open the section's for typing (made if it has none); Page number — the page's number, or the number of pages, as a field at the caret, which the pages draw for each page they are on; Date & time, Word's dialog — a date in a format of its kinds, from the reader's own clock, as a field that updates or as text. |
| Layout | The page of the section the caret is in: margins (Word's Normal, Narrow, Moderate and Wide, or custom, in centimetres), portrait or landscape, the paper size; columns (one, two or three, a line between them or not); breaks — a page break, or a section break that starts the next section on a new page or on the same one; Wrap text, for the picture selected — in line with text, square, tight, top and bottom, behind text, in front of text. Page setup and wrapping are not tracked. |
| References | Table of contents — Word's Automatic Table 1 before the caret's paragraph: a "Contents" heading, then the body's headings of levels 1 to 3, each a link to it with its page's number, worked out from the pages as they are once the table is in; Update table, which lists the headings again as they now are; Footnote and Endnote: a note at the caret, opened for typing. |
| Table | Shown while the caret is in a table, after the other tabs, as Word's Table Layout is: insert a row above or below, a column left or right; delete the row, the column or the table; merge the cells a selection spans, and split a cell into columns and rows (or a merged cell back into its cells); a column's width in centimetres, distribute the columns, AutoFit to contents, to the window or fixed; the table's borders (all, outside, inside, none, at a width) and the cells' shading. Table formatting is not tracked. |
| Review | Word count; New comment on what is selected; the comments and changes pane; Track changes; Accept all, Reject all. |
| View | Thumbnails; the Ruler — Word's, over the current page: the page in centimetres counted from the left margin, the margins grey, and the paragraph's first-line, hanging, left and right indent markers, dragged in quarter-centimetre steps (Alt for none) to set its indents; zoom; page navigation; presentation mode. |

A ribbon press does not take the focus: it acts on the paragraph being typed in — what was typed is
handed in first — and the caret is back where it was afterwards, the paragraph laid out again in its new
style, list or spacing. With text selected across the pages instead, it acts on every paragraph the
selection touches.

A command that cannot be used where the reader is, is greyed out rather than taken away, as Word's are,
so the ribbon never moves under the pointer: the Table tab's commands outside a table (the tab itself is
there only inside one), Remove link outside a link, Restart at 1 and Continue numbering outside a
numbered list, Wrap text until a picture is selected, Update table until there is a table of contents,
Accept all and Reject all while there are no tracked changes, and every edit in a read-only document. A
button, its menu's items and its key are one command (`MorphEditor.Commands`), enabled by one rule, so a
key does nothing where its button is greyed out; a button's tooltip names its key, as Word's do.

What each does to the file is what Word does, probed against Word over COM: a style is Word's own
definition, added the first time it is used, and applying one clears the paragraph's own formatting and
character formatting that covers the whole of it; a list is Word's nine-level bullet or number list, in
List Paragraph, joining a list of the same kind beside it; Increase Indent goes a list level deeper, or
to the next default tab stop; a page break is a paragraph of its own between the two halves of the one
it divides; a table is Table Grid, as wide as the text, between the two halves; a link is a
`w:hyperlink` in the Hyperlink style. Each is tracked when changes are tracked, and settled in the
review pane like any other change.

### Typing

A click on a paragraph opens it for typing **where it stands on the page**: the same
text in the same face, set to the same measure, so it sits on the picture of itself. Typing happens in
the browser, at the browser's speed; nothing is converted until the reader moves on — to another
paragraph, or with Ctrl+Enter — and then the paragraph is written into the file, the file is laid out
again, and the page redrawn.

| while editing | what it does |
| --- | --- |
| A click on a paragraph | Opens it with the caret where the click was. Text selected within one paragraph — a double click, a drag — opens it with that selected, so typing replaces it. |
| Typing, Backspace, Delete, paste | Edit the text. What is pasted from a web page, Google Docs or Word keeps its paragraphs and line breaks, headings, bulleted and numbered lists with their levels, alignment, links, and bold, italic, underline, strikethrough, superscript and subscript — but not its fonts, sizes or colours, which are the document's (as Word's Merge Formatting). A paragraph pasted into the middle of one joins it and takes its look; one the paste makes whole keeps its own. Plain text — Ctrl+Shift+V, or text copied off the pages — is pasted a line to a paragraph. Text typed or pasted at either end of a link is not part of the link, as in Word. |
| Enter, Shift+Enter, Tab | A new paragraph, formatted as the one it was split from; a line break; a tab. Enter at the end of a heading starts the style the heading names as its next, as Word does, and what is typed there is in that style. |
| Tab, Shift+Tab in a list | Moves the paragraph a level in or out of the list, wherever the caret is in it, as Word's keys do; no tab is typed. In the list's first paragraph they move the whole list a quarter inch in or out instead, as far as the margin. |
| Enter in an empty paragraph of a list | Ends the list: the paragraph is taken out of it, at no indent. |
| Backspace at the start of a list's paragraph, or an indented one | Takes its number or bullet off and leaves the text where it stood; then takes its indent off a step at a time, as Word does — first to where the paragraph above starts its first line, when the text lines up under that paragraph's — and only then joins it to the paragraph before. |
| Ctrl+Alt+1, 2, 3; Ctrl+K | Heading 1, 2 or 3; a link on what is selected. |
| Ctrl+L, E, R, J; Ctrl+1, 2, 5; Ctrl+Shift+L | Align left, centre, right, justify; single, double or 1.5 line spacing; bullets — as Word's keys, on the open paragraph or the paragraphs selected on the pages. |
| Ctrl+[, Ctrl+]; Ctrl+=, Ctrl+Shift+=; Ctrl+Space | Shrink and grow the font; subscript and superscript; clear formatting — on what is selected, or the word the caret is in, as the ribbon's buttons do. |
| Backspace at the start, Delete at the end | Joins the paragraph to the one before or after it. The first of the two keeps its formatting, unless it is empty. |
| A click on a picture in the line | Selects it and frames it: its corner handles size it in proportion, its side handles stretch it one way, and Reset size puts it back to the size its image was made for. Its size shows as it changes. |
| A click on a picture that floats | Selects it and frames it on the page: its right-hand corners size it in proportion from its top left, and Layout > Wrap text sets how it stands with the text. |
| Backspace or Delete beside a picture, a page break, a field or a note's reference | Deletes it, whole, as a selection that takes one in does. A note's reference takes its note with it. |
| ↑ ↓ ← → at the paragraph's edge | Goes on to the paragraph that way. |
| Ctrl+Enter, Escape | Hands the paragraph in; abandons what was typed. With a menu or a dialog open, Escape closes that instead, and what was typed stays. |
| A right-click | Word's right-click menu, where the pointer is. What is under it is selected first, as Word selects it — a picture; text already selected, left as it is; anywhere else, the paragraph there, opened as a click opens it — and the menu offers what belongs there: Cut, Copy and Paste; Font… and Paragraph…; for a picture, Wrap text; in a numbered list, Restart at 1 and Continue numbering; in a table, Insert (a row above or below, a column to the left or right), Delete (the row, the column, the table), Merge cells and Split cells…; Link…, or Remove link in a link; New comment. What belongs but cannot be done there is greyed out, by the ribbon's own rules. Shift+right-click gives the browser's own menu, and its spelling suggestions. |
| Bold, Italic, Underline, Strikethrough (Ctrl+B / I / U) | Applies to what is selected in the open paragraph — or, with none open, to text selected across the pages. With nothing selected it applies to what is typed next. |
| Font, size, grow and shrink, colour, highlight, superscript, subscript, clear formatting | Apply to what is selected — in the open paragraph or across the pages — or, with nothing selected, to the word the caret is in, as Word's do. The paragraph opens again with the same text selected. The font and size shown are those of the text at the caret. |
| Align left, centre, right, justify | Aligns the open paragraph, or the paragraphs of the text selected on the pages. |
| Delete with text selected on the pages | Deletes it, across paragraphs if need be, and joins what is left. |
| Undo, Redo (Ctrl+Z / Y) | Within an open paragraph, what was typed there. Otherwise the last edit made to the file, whatever made it — typing, the ribbon, the review pane. |
| Track changes | Turns the document's own tracking on and off (the same setting Word's button changes). While it is on, what is typed, deleted, formatted or split is recorded as a tracked change under the reader's name, shows in the revision colour, and is listed in the review pane to accept or reject. |

The edit is worked out from the paragraph as it reads before and after, not from the keys that were
pressed, and only what differs is changed in the file. Everything the editor does not show — bookmarks,
the anchors of comments, a run's own spelling of its properties — stays exactly as it was, and a
paragraph handed in unchanged leaves the file unchanged.

What is not text is shown as one character: a picture, a field (a date, a page number, a
cross-reference), a page break, a footnote's reference. The caret steps over it, and Backspace, Delete
or a selection deletes it whole — tracked, when tracking is on, so that rejecting the deletion puts it
back. It cannot be typed into. Text a tracked change has deleted is **left in place**, to be settled in
the review pane, and so is a field that begins in one paragraph and ends in another.

### Unsaved work

While the document has edits that have not been saved, the page asks before it is closed or reloaded,
and the file is kept as a draft in the browser's own storage (IndexedDB) a moment after each edit. After
a reload, a crash or a closed tab, the editor's start page offers it back: Recover opens it, still marked
as edited; Discard lets it go. Saving a copy, or discarding the edits for a new document, removes the
draft. There is one draft, the last document edited and not saved, and like the document it never
leaves the device. Where the browser refuses storage — a private window, blocked site data — there is
no draft, and the prompt before leaving is all there is.

## WOPI

`MorphWopi` is the editor as a [WOPI](https://learn.microsoft.com/en-us/microsoft-365/cloud-storage-partner-program/online/)
client: the component a WOPI host's file opens in, as it would open in Office for the web or Collabora
Online. It is the root component of a WebAssembly app that an ASP.NET Core application serves with
[`Morph.Wopi`](/src/Morph.Wopi/README.md), which publishes the discovery the host reads, answers the action
URL the host posts the access token to, and carries the editor's requests to the host, signed with a proof
key.

```csharp
// the WebAssembly app's Program.cs
builder.RootComponents.Add<MorphWopi>("#app");
```

A Word document opened to edit is locked while it is open, the lock refreshed every ten minutes, and
Save puts it back over itself on the host; a lock that lapsed is taken again before a save. A file the
user may not save, or that someone else has locked, opens read only, with the reason over the editor, and
so do a workbook, a deck and any file opened to view. The host's breadcrumb (`BreadcrumbBrandName`,
`BreadcrumbFolderName`) and its `CloseUrl` go in a bar over the editor; `DisablePrint` and `DisableExport`
take Print and Download away. Leaving the page unlocks the file.

## Review

A Word document's **comments** and **tracked changes** open in a pane beside the pages, one card each, in
the order they occur in the document. The page itself shows what Word prints: an insertion underlined and
a deletion struck through in the revision colour, with a change bar in the margin. While the pane is open
the text each unresolved comment is attached to is highlighted on the page.

| in the pane | what it does |
| --- | --- |
| A card | Choosing one scrolls to its text and highlights it. Clicking revised or commented text on a page chooses its card. |
| Accept / Reject | Settles one tracked change and moves to the next. A change is everything Word recorded for one edit: an insertion running across a bold word and into the next paragraph is three elements and a paragraph mark in the file, and one card here. |
| Accept all / Reject all | Settles every tracked change, the ones in headers, footers and notes included. |
| New comment | Attaches a comment to the text selected on the pages. The selection can start and end anywhere, and run across paragraphs and pages. |
| Reply, Resolve / Reopen | Adds to a thread, or marks it resolved — a resolved thread stays listed, dimmed, and is no longer highlighted. |
| Edit, Delete | Rewords or removes a comment. Deleting the comment that started a thread removes its replies with it. |
| Undo / Redo | Steps back and forward through the edits made since the file was opened. |
| Show | Lists comments, changes, or both. |

Every edit is made to the file itself and the result laid out again, so the pages always show exactly
what Download will save; `OnDocumentChanged` hands the host the same bytes after each edit. Comments are
written the way Word writes them — range marks in the document, `comments.xml`, and the thread and
resolved state in `commentsExtended.xml` — so an edited file opens in Word with its threads intact.

A comment is dated by the **reader's** clock, which the viewer asks the browser for. .NET's own would be
the server's under Blazor Server, and UTC in a WebAssembly app built without time zone data — and Word
reads a comment's date at face value, so one dated in UTC shows there as made hours ago. The UTC time is
written beside it (`commentsExtensible.xml`), as Word does.

What is recognised as a tracked change: inserted, deleted and moved text (paragraph marks included);
character, paragraph, table and page-setup formatting changes; inserted and deleted table rows and cells,
and merged cells. Rejecting a formatting change puts back the formatting it replaced.

Limits worth knowing:

- Text is edited where the pages show it — the body's paragraphs, table cells and text boxes, and
  the headers, footers and footnotes, which a click on them opens as it opens the body. Comments go on
  the body's text, and the word count is the body's. A header's or footer's look is set as the body's
  is; a first-page or even-page header that a document has of its own is edited where the pages show
  it, and the editor makes only the one every page shows.
- Pictures, tables, page breaks and links are put in from the ribbon; a picture, a field or a note
  already there can be deleted but not typed into. A workbook or a deck is not edited.
- A picture that floats is sized from its top left and is not moved; one in a header or footer, in a
  rotated group, or an SVG without a raster copy, is not selected on the page. A picture made to float
  stands at the column's left, level with its paragraph — Word leaves it where it stood in the line —
  and Tight wraps round its box, where Word wraps a little inside it.
- A table of contents numbers its pages by their place in the document, from 1: a section that starts
  its numbering afresh is not followed. What is typed into a table of contents is lost when it is
  updated, as in Word.
- The menus are not walked with the arrow keys: a line is chosen with the pointer, and Escape or a
  press anywhere else closes a menu.
- A pasted table's rows are pasted as paragraphs, their cells a tab apart, and a pasted picture is
  left out.
- An underline style other than single or double, and double strikethrough, are written to the file as
  Word writes them, but the pages draw them as a single underline and no strikethrough.
- The ruler leaves out its indent markers in a table, whose ruler in Word is the cell's, and shows no tab
  stops.
- A font chosen is written to the file and shown in the ribbon, but the pages here are drawn in the
  bundled Aptos faces whatever the font (see [Fonts](#fonts)); Word draws it in the font itself.
- Ctrl+Enter hands the paragraph in, so it is not a page break as in Word; Insert > Page break is.
- Replace replaces text within a paragraph: a match running from one paragraph into the next, or across a
  picture or a field, is left as it is.
- The editor is set in the face the page was drawn in, to the width the page set it, so its lines break
  where the page's do. Justified text is set ragged while it is open, and text turned on its side is not
  opened in place.
- A document's own protection is honoured: one restricted to comments accepts comments and leaves its
  text and its tracked changes alone, one that has every change tracked has them tracked, and a
  read-only one lists everything and changes nothing. `ReadOnly` does the same for any document.
- An ISO 29500 *Strict* document is listed but not edited: saving it would change its conformance class.
- Comments are anchored in the document body. A tracked change in a header, a footer or a note is listed
  and can be settled, but has no place on a page to scroll to.
- Moved text is drawn as a deletion where it was and an insertion where it went, not in Word's own green.

## `MorphViewer` and `MorphEditor` parameters

`MorphEditor` takes every one of these.

| parameter | default | what it does |
| --- | --- | --- |
| `Source` | — | The file's bytes. Changing the reference opens the new file. |
| `FileName` | — | Required with `Source`: its extension says which format the bytes are, and it names the download. |
| `Url` | — | A file to fetch and show when `Source` is not set, through the injected `HttpClient`. |
| `ShowOpenFile` | `true` | Offers Open, and opens a dropped file. |
| `ShowSamples` | `false` | Offers the bundled sample document, workbook and deck while nothing is open. |
| `ShowDownload` | `true` | Offers Download. |
| `ShowPrint` | `true` | Offers Print. |
| `ShowReview` | `true` | Offers the review pane for a Word document. |
| `OpenReview` | `false` | Opens the review pane with a document that has comments or tracked changes. |
| `ReadOnly` | `false` | Shows the document, its comments and its tracked changes without offering to change any of them — in the editor, its text too. |
| `Author` | — | The name new comments and tracked changes are signed with. Left unset, the reader is asked for one, and is "Guest" until they give it. |
| `OnDocumentChanged` | — | Called with the file's bytes after every edit, undo and redo — in the viewer's review pane, or anywhere in the editor. Handing them back as `Source` does not reopen the file. |
| `OnSave` | — | Saves the file where it came from. Given one, Save and Ctrl+S hand it the file instead of downloading it; the file counts as saved once it returns, and what it throws is shown, the file still edited. Download then saves a copy. |
| `ShowNew` | `true` | `MorphEditor` only: offers a new blank document in the File menu. |
| `InitialZoom` | `ViewerZoom.Auto` | The zoom a file opens at: `Auto`, `PageFit`, `PageWidth` or `ActualSize`. |
| `InitialPage` | `1` | The page (or slide) a file opens at. |
| `PrintDpi` | `150` | Print resolution, lowered automatically for a very long document. |
| `MaxRenderDpi` | `288` | The sharpest a zoomed page renders; a page too large for its pixel budget renders lower still. |
| `MaxFileSize` | 25 MB | Largest file the viewer will read. |
| `ShowIssueLink` | `true` | Whether an unexpected failure offers a pre-filled GitHub issue against the Morph repo. |
| `VectorPages` | `true` | Draws pages as SVG the browser rasterises rather than painting PNGs. See [Pages drawn as SVG](#pages-drawn-as-svg). |
| `MeasurePaint` | `false` | With `VectorPages`: also paints each page as a PNG, unseen, and logs both times to the console. |
| `Class` | — | Extra CSS classes for the root element. Any other attribute is splatted onto it too. |

Print is vector text, the pages' SVG; with `VectorPages` off, a raster at `PrintDpi`. A document mixing page sizes prints each at its
own size where the browser supports named pages (Chromium, Firefox); elsewhere every sheet takes the first
page's size.

## Theming

The components declare no colours of their own. Every rule reads a `--morph-*` custom property with a
literal fallback, so they look right with no configuration — and a host that sets those properties always
wins, whatever the stylesheet link order:

| property | what it colours | default |
| --- | --- | --- |
| `--morph-primary` | headings, focus accents, the download button | `#2b579a` |
| `--morph-surface` | raised panels (upload area, option and convert panels) | `#ffffff` |
| `--morph-background` | recessed areas (preview well, select backgrounds) | `#f8f9fa` |
| `--morph-text` | body text | `#333` |
| `--morph-muted` | secondary text (captions, notes, progress labels) | `#6c757d` |
| `--morph-border` | every border and divider | `#dee2e6` |
| `--morph-band` | the editor's title band, which carries its tabs and the file's name | `--morph-primary`, darkened |
| `--morph-on-band` | text and icons on the band | `#ffffff` |
| `--morph-selection` | selected text on a rendered page | `rgb(0 96 223 / 0.3)` |
| `--morph-find` | the viewer's find matches | `rgb(255 200 0 / 0.45)` |
| `--morph-find-current` | the viewer's current find match | `rgb(255 110 0 / 0.55)` |
| `--morph-comment` | the text a comment is attached to, on the page | `rgb(255 214 102 / 0.4)` |
| `--morph-review-current` | the text of the comment or change chosen in the review pane | `rgb(124 92 255 / 0.35)` |
| `--morph-comment-accent` | the edge of a comment's card, and of the text it quotes | `#d9a400` |
| `--morph-change-accent` | the edge of a tracked change's card, and its text | `#d13438` |

To follow a palette the host already has, map them once — including through a light/dark switch, since both
blocks land on the same element:

```css
:root {
    --morph-primary: var(--primary-color);
    --morph-surface: var(--surface-color);
}
```

## Building a custom UI

`MorphConverter` is the batteries-included option, not the only one. Everything under it is public:

- **`ConversionService`** — the conversion itself, over `byte[]` in and `byte[]` out:
  `RenderPngPages`, `ToPdf`, `ToHtml`, `ToMarkdown`, `ToText`, and `BuildDownload` (which picks the right
  extension and MIME type, zipping a multi-page PNG render). Also `Detect`, to identify an upload by
  extension — the browser's reported MIME type is unreliable for Office files.
- **`ConversionService.RenderPages`** — every page for display from one layout pass: the full-page PNG,
  the page size and its `PageTextLayer`. `PageTextLayer.Text` is the page's text as copying it yields.
- **`FontStore.EnsureAsync(http)`** — materialises the bundled fonts and returns the directory to hand
  the renderers. Idempotent, so call it early to take the download off the render's critical path.
- **`DocumentPreview`**, **`FormatSelector`**, **`ExportOptionsPanel`**, **`ConversionProgress`**,
  **`ErrorPanel`** — the individual pieces, each usable on its own. `DocumentPreview.TextLayers` takes the
  pages' layers alongside their image URLs and makes the preview selectable.
- **`MorphInterop`** — the browser bridge: file downloads, blob URLs for an iframe, viewport width, and
  `RenderTextLayerAsync`, which builds a page's text layer into an overlay of a custom page view.

Rendering is CPU-bound and the WebAssembly runtime is single-threaded, so wrap a conversion in
`Task.Run` — it yields once, which lets a busy state paint before the compute begins.

## Fonts

Rendering needs real font files, and a browser has none of its own. The four **Aptos** faces (400/700,
upright and italic) ship as static web assets, are fetched once, and are written into the WASM in-memory
filesystem; that directory is handed to every converter, with every unresolved family
mapped to Aptos. So any file renders — its own fonts (Calibri, Times New Roman, "Aptos Light", …)
**substituted with Aptos**. Layout and structure are preserved; exact glyph shapes are not. Shipping the
real Microsoft fonts isn't an option.

Why a directory rather than the fonts embedded in `Morph.dll`: PdfSharp resolves fonts through its own
global resolver, which can't reach embedded fonts at all, and the ImageSharp path — given no directory —
walks an OS-font fallback chain that throws in the browser the moment a document names a weight the
embedded set doesn't include. A pinned directory sidesteps both. The text exports (HTML, Markdown, plain
text) rasterise nothing but take the directory too: Excel's column-width unit is the widest digit of the
workbook's body font, so a sheet's `td` widths come out of whichever face resolves — left to the OS the
same workbook exports different columns on every machine.

To take the ~940KB off the first render's critical path, preload them during the WASM boot:

```html
<link rel="preload" href="_content/Morph.Blazor/fonts/Aptos_400.ttf" as="fetch" crossorigin />
```

`crossorigin` matters: without it the browser won't reuse the preload and downloads each font twice.
`FontStore.AssetPaths` lists all four.

## Trimming

The package ships MSBuild targets that root the reflection-heavy dependencies
(DocumentFormat.OpenXml, AngleSharp, ImageSharp, PdfSharp) when the host app publishes trimmed. They
resolve types by name at runtime, so a full trim would strip types they need and surface only as a
browser-side crash part-way through a conversion. Nothing to configure — `PublishTrimmed=true` keeps
working.

## AOT

An app can publish with `RunAOTCompilation=true` (the `wasm-tools` workload installed, and trimming on, as
a Blazor WebAssembly publish has by default). The same targets then supply the two workarounds it needs:
PdfSharp is left interpreted, since it is rooted whole and one of its code samples references a library
it does not depend on; and SixLabors.Fonts' `UnmanagedCallConv` attributes are trimmed away
(`Morph.Blazor.AotLinkAttributes.xml`), since Mono's WebAssembly AOT compiler (10.0.13, and 11.0 RC2)
crashes decoding them ([dotnet/runtime#136351](https://github.com/dotnet/runtime/issues/136351)). Both use internal SDK settings; if one stops working, the publish fails with a build error rather
than the app at runtime.

What it is worth: on morph.papyrine.org an edit to an 18-page document is back in 0.53 s rather than
2.5 s, for a download of 33.5 MB rather than 10.5 MB (gzip), a 15–25 minute publish, and a
`dotnet.native.wasm` that changes with every change to the code. Leaving ImageSharp interpreted to save
size does not pay: calls between compiled and interpreted code make ImageSharp 2.7x slower than an
interpreted build. See `src/Morph.Web/README.md`.

## Backends

The package uses **ImageSharp** for PNG and **PdfSharp** for PDF, both pure-managed so they run in
WebAssembly. `Morph.Skia` is deliberately avoided: SkiaSharp needs a native `browser-wasm` build that its
NuGet packages don't ship.

The viewer and the text layer read Morph's laid-out pages directly — `Morph` and `Morph.ImageSharp` grant
this package their internals — so it has to be used with the `Morph` packages of the same version. They
are released together from one repository at one version number.
