# Morph.Web

A Blazor WebAssembly single-page app that converts a Word `.docx`, Excel `.xlsx` or PowerPoint `.pptx`
file to **PNG**, **PDF**, **HTML**, **Markdown** or **plain text** — entirely in the browser. No file ever
leaves the device. Modelled on the [GeoConvert](https://github.com/Papyrine/GeoConvert) web app (layout,
theming, testing and deployment).

## What lives here, and what doesn't

The converter itself is **not** in this project. It is the `MorphConverter` component from
[`Morph.Blazor`](../Morph.Blazor/README.md), the reusable package other apps consume — upload panel,
page preview, format picker, options, result pane and download, plus the conversion services, fonts,
samples, stylesheet and JavaScript behind them, all shipped as static web assets.

This project is the **shell** around it: the header, the light/dark theme toggle
([`ThemePreferenceService`](Services/ThemePreferenceService.cs)), the footer's version / payload-size /
RAM readouts, the routing, and the app-level stylesheet in `wwwroot/css/app.css`. The home page is three
lines:

```razor
@page "/"
<PageTitle>Morph — Office Converter</PageTitle>
<MorphConverter />
```

The two stylesheets divide the same way: `_content/Morph.Blazor/morph.css` draws the converter,
`css/app.css` draws the shell and maps this app's palette onto the package's `--morph-*` custom
properties — which is what carries the dark theme through to the component.

## Live behaviour

Upload a file (or click one of the three **Sample** buttons), see each page rendered as a live preview,
pick an output format, and download. Every input converts to every output, so the source only selects
which of Morph's converter families the bytes route through — see
[`ConversionService`](../Morph.Blazor/Services/ConversionService.cs), which switches on the
[`InputFormat`](../Morph.Blazor/Services/InputFormat.cs) detected from the file extension (the browser's
reported MIME type is unreliable for Office files). A workbook paginates into printed pages and a deck
renders one page per slide, so the upload panel's count reads "3 pages" or "12 slides" off
[`InputFormatInfo.PageNoun`](../Morph.Blazor/Services/InputFormatInfo.cs).

On a wide viewport (≥1200px) every non-PNG format also renders its actual output in a pane beside the page
preview — PDF and HTML in an iframe (blob URL), Markdown and plain text inline — converted on selection
and cached per format for the life of the document (a download of the same format reuses the cached
bytes). The Markdown view swaps each embedded base64 image payload for a short size note
([`MarkdownPreview`](../Morph.Blazor/Services/MarkdownPreview.cs)); the downloaded `.md` keeps the full
data URIs.

## Backend choice — ImageSharp, not Skia

`Morph.Blazor` references **`Morph.ImageSharp`** and **`Morph.Pdf`**, and uses one converter trio per
input:

- `ImageSharpDocumentConverter` / `ImageSharpExcelConverter` / `ImageSharpPowerPointConverter` render to
  PNG. ImageSharp is pure-managed, so it runs in WebAssembly with no native assets. The Skia equivalents
  are deliberately avoided — SkiaSharp needs a native `browser-wasm` build the NuGet packages don't ship.
- `PdfDocumentConverter` / `PdfExcelConverter` / `PdfPowerPointConverter` render to PDF (PdfSharp, also
  pure-managed).
- `DocumentConverter` / `ExcelConverter` / `PowerPointConverter`'s static `ConvertToMarkdown` /
  `ConvertToHtml` (in core `Morph`) produce the text outputs (HTML ships as a self-contained document —
  styles inline, images embedded as data URIs); plain text is derived from the HTML by
  [`TextExtraction`](../Morph.Blazor/Services/TextExtraction.cs) (Morph has no text exporter).

## Fonts

Rendering needs real font files, and a browser has none of its own. The four **Aptos** faces (400/700,
upright/italic) ship with `Morph.Blazor` as static web assets, are fetched once, and are written into the
WASM in-memory filesystem; that directory is handed to **every** converter via
`ExportOptions.FontDirectory`, with every unresolved family mapped to Aptos. So any file renders —
its own fonts (Calibri, Times New Roman, "Aptos Light", …) **substituted with Aptos**; layout and
structure are preserved, exact glyph shapes are not. Shipping the real Microsoft fonts to a public site
isn't an option. See [`FontStore`](../Morph.Blazor/Services/FontStore.cs) and
[`ConversionService`](../Morph.Blazor/Services/ConversionService.cs).

Why a directory rather than the fonts embedded in `Morph.dll`: PdfSharp resolves fonts through its own
global resolver that can't reach Morph's embedded fonts at all, and the ImageSharp path, given no
directory, walks an OS-font fallback chain that throws in the browser (and on a clean CI runner) the
moment a document names a weight the embedded set doesn't include. A pinned directory sidesteps both.
The text exports (HTML, Markdown, plain text) rasterise nothing but are handed the directory too: Excel's
column-width unit is the widest digit of the workbook's body font, so a sheet's `td` widths come out of
whichever face resolves — left to the OS the same workbook exports different columns on every machine.

`index.html` preloads the four faces from `_content/Morph.Blazor/fonts/` so the download starts during
the WASM boot rather than after it.

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

Trimming is **on** (`PublishTrimmed=true`), but selective: the BCL, the Components framework and Morph's
own assemblies (verified reflection-free) shrink to what's reached, while the reflection-heavy
libraries — DocumentFormat.OpenXml, AngleSharp, ImageSharp and PdfSharp — are rooted whole, since
full-trim silently strips types they resolve by name and it surfaces only as a browser-side crash
mid-conversion. Those roots live in `Morph.Blazor/build/Morph.Blazor.targets` and ship inside that
package, so every consuming app gets them; this app imports the file by path, because a `ProjectReference`
doesn't pick up a referenced project's MSBuild assets the way a `PackageReference` does.

## In Morph.slnx, but excluded from the container build

`Morph.Web` and `Morph.Web.Tests` are in `src/Morph.slnx`, but — like `RenderHelper` — marked
`<Build Solution="Release|*" Project="false" />`, so they're skipped in **Release** solution builds. The
canonical rendering suite builds inside a pinned `linux/amd64` Docker image with no `wasm-tools` workload
(its full-solution build is `dotnet build src -c Release`), where a Blazor WASM project can't build. The
exclusion keeps that container flow untouched. The web projects still build locally in Debug and are built
by path (`dotnet build src/Morph.Web.Tests`) in `.github/workflows/deploy-blazor.yml`.

`Morph.Blazor` is **not** excluded: it is a plain Razor class library rather than a Blazor WASM app, so it
needs no `wasm-tools` workload and builds — and packs — everywhere, including that container and the NuGet
publish workflow.

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

Most of what they cover now lives in `Morph.Blazor` rather than here; they stay in this project because
they also drive the published app end to end.

- **bUnit** component/markup snapshots (deterministic; committed `.verified.html`/`.txt`), including the
  package's own `MorphConverter`, `ExportOptionsPanel`, `FormatSelector`, `ConversionProgress` and
  `ErrorPanel`.
- **Service** tests over `ConversionService` / `TextExtraction`, parameterised across all three inputs
  (each of DOCX/XLSX/PPTX → each format) against the bundled samples in
  [`Sample`](../Morph.Web.Tests/Sample.cs).
- **Playwright** end-to-end snapshots that boot the real WASM runtime (single-threaded, per the section
  above), upload a document, render a preview, and download PDF (exercising the in-memory font path).
  `SampleRendersPreview` runs for all three inputs, because each routes through a different Morph parser
  and the published build is trimmed — a parser that lost a reflected-on type fails only there. Page
  screenshots compare via SSIM, so sub-pixel platform drift is tolerated.

To reset a snapshot after an intentional change, review then rename the `*.received.*` to `*.verified.*`.

## Deploy

`.github/workflows/deploy-blazor.yml` builds, tests, publishes (with the source `<base href="/">`, since
the custom domain `morph.papyrine.org` serves the site at its root), and pushes to GitHub Pages on every
push to `main`.
