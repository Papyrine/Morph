# Morph.Blazor

Reusable Blazor WebAssembly components that convert Word `.docx`, Excel `.xlsx` and PowerPoint `.pptx`
files to **PNG**, **PDF**, **HTML**, **Markdown** or **plain text** — entirely in the browser. No file
ever leaves the device.

This is the converter that powers [morph.papyrine.org](https://morph.papyrine.org/), packaged so any
Blazor app can drop it in. The app in `src/Morph.Web` is now only a shell — header, theme toggle and
footer — around the `MorphConverter` component from this package.

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
- **`FontStore.EnsureAsync(http)`** — materialises the bundled fonts and returns the directory to hand
  the renderers. Idempotent, so call it early to take the download off the render's critical path.
- **`DocumentPreview`**, **`FormatSelector`**, **`ExportOptionsPanel`**, **`ConversionProgress`**,
  **`ErrorPanel`** — the individual pieces, each usable on its own.
- **`MorphInterop`** — the browser bridge: file downloads, blob URLs for an iframe, viewport width.

Rendering is CPU-bound and the WebAssembly runtime is single-threaded, so wrap a conversion in
`Task.Run` — it yields once, which lets a busy state paint before the compute begins.

## Fonts

Rendering needs real font files, and a browser has none of its own. The four **Aptos** faces (400/700,
upright and italic) ship as static web assets, are fetched once, and are written into the WASM in-memory
filesystem; that directory is handed to both the PNG and PDF converters, with every unresolved family
mapped to Aptos. So any file renders — its own fonts (Calibri, Times New Roman, "Aptos Light", …)
**substituted with Aptos**. Layout and structure are preserved; exact glyph shapes are not. Shipping the
real Microsoft fonts isn't an option.

Why a directory rather than the fonts embedded in `Morph.dll`: PdfSharp resolves fonts through its own
global resolver, which can't reach embedded fonts at all, and the ImageSharp path — given no directory —
walks an OS-font fallback chain that throws in the browser the moment a document names a weight the
embedded set doesn't include. A pinned directory sidesteps both. The text exports (HTML, Markdown, plain
text) don't rasterise, so they need no fonts.

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
