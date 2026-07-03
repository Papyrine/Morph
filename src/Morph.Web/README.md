# Morph.Web

A Blazor WebAssembly single-page app that converts a Word `.docx` to **PNG**, **PDF**, **HTML**,
**Markdown** or **plain text** — entirely in the browser. No file ever leaves the device. Modelled on the
[GeoConvert](https://github.com/Papyrine/GeoConvert) web app (layout, theming, testing and deployment).

Live behaviour: upload a `.docx` (or click **Try a sample document**), see each page rendered as a live
preview, pick an output format, and download. On a wide viewport (≥1200px) every non-PNG format also
renders its actual output in a pane beside the page preview — PDF and HTML in an iframe (blob URL),
Markdown and plain text inline — converted on selection and cached per format for the life of the
document (a download of the same format reuses the cached bytes). The Markdown view swaps each embedded
base64 image payload for a short size note ([`MarkdownPreview`](Services/MarkdownPreview.cs)); the
downloaded `.md` keeps the full data URIs.

## Backend choice — ImageSharp, not Skia

The app references **`Morph.ImageSharp`** and **`Morph.Pdf`**:

- `ImageSharpDocumentConverter` renders DOCX → PNG. ImageSharp is pure-managed, so it runs in
  WebAssembly with no native assets. `SkiaDocumentConverter` is deliberately avoided — SkiaSharp needs a
  native `browser-wasm` build the NuGet packages don't ship.
- `PdfDocumentConverter` renders DOCX → PDF (PdfSharp, also pure-managed).
- `DocumentConverter.ConvertToMarkdown` / `ConvertToHtml` (in core `Morph`) produce the text outputs
  (HTML ships as a self-contained document — styles inline, images embedded as data URIs); plain text is
  derived from the HTML by [`TextExtraction`](Services/TextExtraction.cs) (Morph has no text exporter).

## Fonts

Rendering needs real font files, and a browser has none of its own. The four **Aptos** faces (400/700,
upright/italic) are shipped as static assets under `wwwroot/fonts/`, fetched once, and written into the
WASM in-memory filesystem; that directory is handed to **both** the PNG and PDF converters via
`ExportOptions.FontDirectory`, with every unresolved family mapped to Aptos. So any document renders —
its own fonts (Calibri, Times New Roman, "Aptos Light", …) **substituted with Aptos**; layout and
structure are preserved, exact glyph shapes are not. Shipping the real Microsoft fonts to a public site
isn't an option. See [`FontStore`](Services/FontStore.cs) and
[`ConversionService`](Services/ConversionService.cs).

Why a directory rather than the fonts embedded in `Morph.dll`: PdfSharp resolves fonts through its own
global resolver that can't reach Morph's embedded fonts at all, and the ImageSharp path, given no
directory, walks an OS-font fallback chain that throws in the browser (and on a clean CI runner) the
moment a document names a weight the embedded set doesn't include. A pinned directory sidesteps both.
The text exports (HTML, Markdown, plain text) don't rasterise, so they need no fonts.

## Single-threaded runtime

`WasmEnableThreads` is deliberately **not** set. The multithreaded runtime needs `SharedArrayBuffer` —
i.e. a cross-origin-isolated page — and a service-worker shim can't reliably guarantee that isolation
across every serving context; when it isn't isolated the threaded runtime aborts at startup
(`mono_wasm_register_ui_thread` stack-bounds assertion). The single-threaded runtime boots reliably
anywhere with no isolation, no service worker, and no `wasm-tools`/emcc relink.

The trade-off: a conversion runs on the UI thread, so the page is briefly unresponsive while it works.
Each conversion is still wrapped in `Task.Run`, which yields once so the "Rendering…" state paints before
the compute begins — the spinner shows, then a short freeze, then the result. For 1–3 page documents
that's a blink.

Trimming is **off** (`PublishTrimmed=false`): DocumentFormat.OpenXml and AngleSharp resolve types by
reflection, which full-trim silently breaks. The bundle is larger but correct.

## In Morph.slnx, but excluded from the container build

`Morph.Web` and `Morph.Web.Tests` are in `src/Morph.slnx`, but — like `RenderHelper` — marked
`<Build Solution="Release|*" Project="false" />`, so they're skipped in **Release** solution builds. The
canonical rendering suite builds inside a pinned `linux/amd64` Docker image with no `wasm-tools` workload
(its full-solution build is `dotnet build src -c Release`), where a Blazor WASM project can't build. The
exclusion keeps that container flow untouched. The web projects still build locally in Debug and are built
by path (`dotnet build src/Morph.Web.Tests`) in `.github/workflows/deploy-blazor.yml`.

## Run locally

The single-threaded runtime needs no cross-origin isolation, so **any** static host serves it — no COOP/
COEP headers, no service worker. Publish and serve the output:

```bash
dotnet publish src/Morph.Web -c Release -o publish
# then serve publish/wwwroot with any static file server
```

(`dotnet run --project src/Morph.Web` also works once the
`Microsoft.AspNetCore.Components.WebAssembly.DevServer` package is added.) The snapshot tests below spin
up a host and drive the app, so they're the easiest way to see it exercised end-to-end.

## Test

Tests live in `src/Morph.Web.Tests` (TUnit + bUnit + Verify + Playwright), mirroring GeoConvert:

```bash
# Build (also publishes the Blazor app the Playwright tests serve) and run.
dotnet build src/Morph.Web.Tests --configuration Release
dotnet src/Morph.Web.Tests/bin/Release/net10.0/Morph.Web.Tests.dll
```

- **bUnit** component/markup snapshots (deterministic; committed `.verified.html`/`.txt`).
- **Service** tests over `ConversionService` / `TextExtraction` (DOCX → each format).
- **Playwright** end-to-end snapshots that boot the real threaded runtime, upload a document, render a
  preview, and download PDF (exercising the in-memory font path). Page screenshots compare via SSIM, so
  sub-pixel platform drift is tolerated.

To reset a snapshot after an intentional change, review then rename the `*.received.*` to `*.verified.*`.

## Deploy

`.github/workflows/deploy-blazor.yml` builds, tests, publishes (with the source `<base href="/">`, since
the custom domain `morph.papyrine.org` serves the site at its root), and pushes to GitHub Pages on every
push to `main`.
