# Morph.Blazor

Reusable Blazor WebAssembly components that convert Word `.docx`, Excel `.xlsx` and PowerPoint `.pptx`
files to **PNG**, **PDF**, **HTML**, **Markdown** or **plain text** — or show them as they are, the way a
browser shows a PDF — entirely in the browser. No file ever leaves the device.

This is the converter and viewer that power [morph.papyrine.org](https://morph.papyrine.org/), packaged so
any Blazor app can drop them in. The app in `src/Morph.Web` is now only a shell — header, navigation,
theme toggle and footer — around the `MorphConverter` and `MorphViewer` components from this package.

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
viewer. The file is parsed and laid out once; after that only the pages in view are painted, one at a
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
| Download (Ctrl+S) | Saves the original file. |

Keyboard, with focus in the viewer: ←/→ page when the page does not scroll sideways, Home/End go to the
first/last page, Ctrl+`+`/`-`/`0` zoom in/out/back to automatic, Ctrl+A selects the pages' text.

### `MorphViewer` parameters

| parameter | default | what it does |
| --- | --- | --- |
| `Source` | — | The file's bytes. Changing the reference opens the new file. |
| `FileName` | — | Required with `Source`: its extension says which format the bytes are, and it names the download. |
| `Url` | — | A file to fetch and show when `Source` is not set, through the injected `HttpClient`. |
| `ShowOpenFile` | `true` | Offers Open, and opens a dropped file. |
| `ShowSamples` | `false` | Offers the bundled sample document, workbook and deck while nothing is open. |
| `ShowDownload` | `true` | Offers Download. |
| `ShowPrint` | `true` | Offers Print. |
| `InitialZoom` | `ViewerZoom.Auto` | The zoom a file opens at: `Auto`, `PageFit`, `PageWidth` or `ActualSize`. |
| `InitialPage` | `1` | The page (or slide) a file opens at. |
| `PrintDpi` | `150` | Print resolution, lowered automatically for a very long document. |
| `MaxRenderDpi` | `288` | The sharpest a zoomed page renders; a page too large for its pixel budget renders lower still. |
| `MaxFileSize` | 25 MB | Largest file the viewer will read. |
| `ShowIssueLink` | `true` | Whether an unexpected failure offers a pre-filled GitHub issue against the Morph repo. |
| `Class` | — | Extra CSS classes for the root element. Any other attribute is splatted onto it too. |

Print is a raster at `PrintDpi` rather than vector text. A document mixing page sizes prints each at its
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
| `--morph-selection` | selected text on a rendered page | `rgb(0 96 223 / 0.3)` |
| `--morph-find` | the viewer's find matches | `rgb(255 200 0 / 0.45)` |
| `--morph-find-current` | the viewer's current find match | `rgb(255 110 0 / 0.55)` |

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

## Backends

The package uses **ImageSharp** for PNG and **PdfSharp** for PDF, both pure-managed so they run in
WebAssembly. `Morph.Skia` is deliberately avoided: SkiaSharp needs a native `browser-wasm` build that its
NuGet packages don't ship.

The viewer and the text layer read Morph's laid-out pages directly — `Morph` and `Morph.ImageSharp` grant
this package their internals — so it has to be used with the `Morph` packages of the same version. They
are released together from one repository at one version number.
