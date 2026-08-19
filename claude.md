# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Morph is a .NET library that converts Microsoft Word DOCX documents or HTML content into PNG images, PDF, semantic HTML, or Markdown. Either input can produce any of the four outputs. **All public types live in the single `Morph` namespace** — including the backend and PDF assemblies.

- **DOCX in** — `DocumentConverter` is the abstract base and carries the static `ConvertToHtml` / `ConvertToMarkdown` exporters. Its concrete subclasses `SkiaDocumentConverter` / `ImageSharpDocumentConverter` add `ConvertToImages` / `ConvertToImageData`. `PdfDocumentConverter.ConvertToPdf` (in `Morph.Pdf`) produces PDF.
- **HTML in** — `HtmlConverter` mirrors that shape: static `ConvertToHtml` / `ConvertToMarkdown`, with `SkiaHtmlConverter` / `ImageSharpHtmlConverter` for images and `PdfHtmlConverter.ConvertToPdf` for PDF. HTML parsing is async (AngleSharp), so these return `Task`.
- **Parse once, export many** — `WordDocument` / `HtmlDocument` parse the source a single time and expose `ExportToHtml` / `ExportToMarkdown`, plus `ExportToPdf` as an extension method from `Morph.Pdf`. Prefer these when emitting more than one format.
- **Options** — one record per format, all deriving from the abstract `ExportOptions` (which holds the shared font knobs): `ImageExportOptions`, `HtmlExportOptions`, `MarkdownExportOptions`, `PdfExportOptions`.

## Build & Test Commands

Tests must produce bit-identical output across machines because the suite compares rendered PNGs to checked-in Verify baselines. To guarantee that, all tests run inside a pinned `linux/amd64` Docker image defined by `Dockerfile.test`. **This is the canonical way to run tests — invoking `dotnet test` on the host will produce subpixel drift on a different OS/CPU and fail the Verify comparison.**

```bash
# Run the full scenario + spec suite in the container
./scripts/test.sh

# Run a specific test class (TUnit treenode filter, passed through to dotnet)
./scripts/test.sh dotnet run --project src/Tests --configuration Release \
    -- --treenode-filter "/*/*/SkiaScenarioTests/*"

# Run the static-setting tests (separate single-threaded project)
./scripts/test.sh dotnet run --project src/StaticSettingTests --configuration Release

# Open an interactive shell inside the container
./scripts/test.sh bash

# Build only (no tests)
./scripts/test.sh dotnet build src --configuration Release
```

The wrapper builds `morph-test:latest` on first run, reuses it afterward, mounts the working tree at `/src`, and caches NuGet packages in `./.nuget-cache/` (gitignored). It rebuilds the image by itself when `Dockerfile.test` or the Playwright version changes — it stamps a hash of both on the image as a label and compares — so `MORPH_REBUILD=1` is only needed to force one anyway. `MORPH_IMAGE_READY=1` skips that check entirely, for a caller that built the image itself (CI does).

**If the Docker daemon is not running, stop and ask the user to start it.** Do not start Docker Desktop yourself, and do not work around it by running the scenario suite on the host — the baselines will not match.

### Why the run copies the tree first (`MORPH_DIRECT`)

On a Windows host running the **WSL 2 backend**, Docker Desktop exposes the working tree over 9p/drvfs, which is far slower than the container's own disk — measured here, reading 300 baseline PNGs took 1.54s from the mount versus 0.018s locally, and a *no-op* MSBuild up-to-date check cost ~21s. So `scripts/test.sh` copies the tree to container-local disk, runs there, and syncs changed files back (`scripts/container-run.sh`). That alone took the full suite from **4m34s to 2m15s** when it landed.

Every mount number in this section is a WSL 2 number. The **Docker VMM** backend mounts over virtiofs instead and is far faster — but *still* not fast enough to make the copy pointless. See *Windows: Docker VMM vs the WSL 2 backend* below.

That copy now **persists between runs** in a Docker named volume, so neither half of the per-run fixed cost is paid twice:

| per run | before | now |
| --- | --- | --- |
| get the tree in (~1.8GB, 8,164 files) | 27s — full `tar` every run | **3.5s** — `rsync`, only what changed |
| build | 14s — `bin`/`obj` thrown away with the container | **3.4s** — incremental |

The volume is keyed on the host path, so clones and worktrees keep their own. **`MORPH_CLEAN=1` discards it** — that is the answer to any suspicion of stale state, and costs one cold run (~22s sync, still no worse than the old `tar`).

This matters most on the narrow filtered runs that dominate iteration, where the fixed cost *was* the wall clock: a 40-test run is now **15.5s** end to end, against the ~45s the same shape measured before. (On Docker VMM the same run is **8.4s**, because the rsync that dominates its fixed cost nearly vanishes.)

`MORPH_DIRECT=1` works in the mounted tree instead, with no persistent copy. Use it on a **Linux host**, where the mount is native and the copy is pure overhead, or when a run must see the live tree. `./scripts/test.sh bash` selects it automatically, since an interactive shell wants the live tree and its `.git`.

One caveat when switching modes: the two keep separate `bin`/`obj`, so the first run after a switch rebuilds. In the mounted tree that rebuild lands on 9p and is slow — the ~93s above included one.

The sync-back is an mtime sweep rather than a fixed list of paths, because the suite writes more than the obvious artifacts — notably `compare.md` / `compare-all-images.md`, which are **tracked** files regenerated by `ScenarioMarkdownGenerator` during the run. Host `*.received.*` files are cleared and replaced from the container's set, so a snapshot that starts passing does not leave a stale `.received.*` behind to corrupt `regenerate-baselines.sh`'s promotion count.

### Windows: Docker VMM vs the WSL 2 backend

Docker Desktop 4.86+ offers **Docker VMM** on Windows as an alternative to WSL 2
(Settings > General > Virtual Machine Manager). It is worth having: the mount goes from
9p/drvfs to **virtiofs**, and the narrow runs that dominate iteration get **1.77x**
faster. Measured 2026-08-18 on Docker Desktop 4.87.0, a 12-core / 32GB host, with both
VMs at 12 cores and within 1.4% on RAM (15,829 vs 16,051 MiB) so the backend was the
only variable.

| | WSL 2 | Docker VMM | |
| --- | --- | --- | --- |
| narrow run (40 tests) | 14.9s | **8.4s** | 1.77x |
| full suite (3,679 tests) | 184.8s | **175.2s** | 1.05x |
| full suite, `MORPH_DIRECT=1` | 407.0s | **239.3s** | 1.70x |
| container start | 0.52s | 0.38s | 1.36x |

The bind mount itself, which is the only surface that actually changed:

| | WSL 2 (`v9fs`) | Docker VMM (`virtiofs`) | |
| --- | --- | --- | --- |
| walk 8,131 files | 1.477s | 0.012s | 123x |
| stat 300 files | 0.400s | 0.003s | 133x |
| read 300 PNGs (75MB) | 0.780s | 0.037s | 21x |
| write 500 files | 1.818s | 0.167s | 11x |
| `rsync` sync-in | 5.124s | 0.079s | 65x |

**The copy is still worth it.** This is the load-bearing result, because a 65x faster
mount reads like a reason to delete `container-run.sh`. It is not: `MORPH_DIRECT=1` is
still **37% slower** than the copy path under Docker VMM (239s vs 175s). VMM narrows the
gap from 2.2x to 1.37x without closing it, so the copy stays the default on Windows under
either backend.

**Most of the mount speedup is caching, not raw throughput.** On a genuinely cold walk
virtiofs is only ~1.8x faster (0.806s vs ~1.45s). The 100x+ figures are warm, and they
are warm because virtiofs lets the guest kernel hold the dentry cache while 9p does not —
WSL 2's three repeats were flat (1.452 / 1.477 / 1.573) where VMM's collapsed
(0.521 / 0.011 / 0.012). Since every run walks the same tree, warm is the case that
matters, but do not quote the warm ratio as throughput.

The full suite barely moves (1.05x) because it is CPU-bound rasterisation against
container-local ext4, which the backend does not touch. Only the fixed cost changes,
which is why the narrow run — nearly all fixed cost — improves 1.77x.

**Setup, and three ways it bites:**

1. **File sharing is manual.** Docker VMM shares no host path unless it is listed in
   Settings > Resources > File sharing; add the repo directory there. Without it a bind
   mount **hangs indefinitely rather than erroring** (killed at 2 minutes here). The
   documented *"file is not shared from the host"* error only appears for some paths, so
   a hanging `docker run` is the symptom to recognise.
2. **Memory is manual.** WSL 2 sizes itself from `.wslconfig` — absent, it took ~15.8GB
   here. Docker VMM uses Settings > Resources > Memory, which **defaults to 2GB**, below
   Docker's own 4GB minimum for VMM. The memory slider does not exist while WSL 2 is
   selected, so the order is: switch backend first, then set memory, then restart again.
3. **Switching means a fresh VM disk.** Images and named volumes do not migrate:
   ~141s to rebuild `morph-test:latest`, plus a 2.1GB NuGet restore into a new cache
   volume on the first build. Budget ~5 minutes, once.

Docker VMM is still **Beta**. `useLibkrun` is the settings field that tells you which
backend is live (`wslEngineEnabled` still reads `true` under VMM and means nothing); the
kernel is the independent tell — `...-microsoft-standard-WSL2` vs `...-linuxkit`.

To re-measure after a Docker Desktop upgrade, or to evaluate any other backend change,
use `scripts/vmm-bench/` — see its README. It records the controls (container-local ext4
and pure CPU) that prove two runs were comparable, which is what makes a result like
"1.05x on the full suite" trustworthy rather than noise.

### Apple Silicon: enable Rosetta

On Apple Silicon (`arm64`), Docker Desktop must be configured to use Rosetta for `linux/amd64` emulation, **not** QEMU. QEMU's user-mode emulation crashes .NET 10's MSBuild with an `AccessViolationException` in its string-intern cache (SIGABRT mid-build). Rosetta is stable and ~native speed.

1. Docker Desktop → Settings → General → enable **Use Rosetta for x86/amd64 emulation on Apple Silicon**.
2. Restart Docker Desktop.
3. Confirm by running `./scripts/test.sh dotnet build src --configuration Release` — it should complete in ~30s. If it aborts with `qemu: uncaught target signal 6`, Rosetta is not active.

### Regenerating baselines after a rendering change

When a deliberate change to the converter is expected to shift output, regenerate `*.verified.*` files **inside the container** using:

```bash
./scripts/regenerate-baselines.sh
```

The script deletes existing `results_*.verified.*` snapshots, runs the suite once (expected to fail and produce `*.received.*` files), promotes the received files to verified, then re-runs to confirm stability.

### Running outside the container

Non-scenario unit/spec tests do not depend on rendering output and can be run directly with `dotnet test src/Tests --treenode-filter ...` for fast iteration on a machine that has the .NET 10 SDK installed. But any test that touches `SkiaScenarioTests` or `ImageSharpScenarioTests` **must** use the container — the Verify comparison will fail on any machine whose rasterization diverges from the linux/amd64 baseline (which is everywhere except inside this image).

### Known papercut: file mode flips

Docker Desktop's bind-mount layer on macOS occasionally flips file modes from 644 to 755 on files near the container's writes (e.g. `.editorconfig`, `src/Shared.sln.DotSettings`). Content is unchanged. Restore with `git checkout -- <file>` after a test run if it happens.

### TUnit filter syntax (`--treenode-filter`)

TUnit uses `--treenode-filter`, not `--filter`. The filter path format is:
```
/{assembly}/{namespace}/{class}/{method}
```

Parameter values are **NOT** part of the filter path — they only appear in the display name, which is not filterable. Wildcards (`*`) work in any segment but `**` is only allowed as the final segment.

```bash
# Run only the scenario test classes (skip the ~540 spec/unit tests)
./scripts/test.sh dotnet run --project src/Tests --configuration Debug \
    -- --treenode-filter "/*/*/*ScenarioTests/*"

# Run only the Skia scenario tests
./scripts/test.sh dotnet run --project src/Tests --configuration Debug \
    -- --treenode-filter "/*/*/SkiaScenarioTests/*"
```

**To target a single parameterized scenario (e.g. `cover-letters/02` alone)** — since the filter can't match parameter values, temporarily narrow `GetScenarioDirectories()` in `SkiaScenarioTests.cs` / `ImageSharpScenarioTests.cs`:
```csharp
.Where(_ => _.EndsWith(@"cover-letters\02\input.docx"))
```
Then combine with `--treenode-filter "/*/*/*ScenarioTests/*"` to skip the spec tests. Revert the `Where` when done.

Brackets (`[...]`) in treenode filters are for property-bag filters (e.g. `[Category=Foo]`), not parameter matching — don't confuse them with LINQ-style filtering.


**Prerequisites:** Docker Desktop (with Rosetta enabled on Apple Silicon — see above). The container ships its own .NET SDK matching `global.json`; no host install is required for the canonical workflow. For host-side `dotnet test` shortcuts, the host needs .NET SDK 10.0.300+ locally; see `global.json` for the exact pin. Tests load fonts from the bundled `src/Fonts/` directory via `ExportOptions.FontDirectory`, so no OS-level font install is needed.

## Architecture

The conversion pipeline is **Parse → Layout → Paint**, split across multiple assemblies. Every rendered
output — DOCX or HTML in, PNG or PDF out — runs the one shared layout engine described below; the older
per-backend Parse → Render path is deleted. The text exporters (HTML/Markdown) deliberately reflow instead
and never paginate.

**Core** (`src/Morph/`): the model (`ParsedDocument` and the `DocumentElement` hierarchy, one type per file under `src/Morph/Parsing/`), shared rendering base (`RenderContextBase`, `FontCacheLoader`, `FontHelpers`, `TableLayout`), the `ExportOptions` records, `ConversionResult`, the text exporters (HTML/Markdown), **and both parsers**:
- **DOCX** (`src/Morph/OpenXml/`): `DocumentParser` reads OOXML via DocumentFormat.OpenXml and builds a `ParsedDocument`. Sub-parsers handle shapes, ink, themes, and HTML (AltChunk).
- **HTML** (`src/Morph/Html/`): `HtmlParser` converts HTML to `DocumentElement` trees via AngleSharp. `HtmlConverter` is the abstract base for HTML→raster converters.

Because both parsers live in core, `Morph` depends on both `DocumentFormat.OpenXml` and `AngleSharp`, and every downstream assembly transitively drags both.

**The layout engine** (`src/Morph/Layout/`): one backend-independent pagination —
`CanonicalParagraphMeasurer` measures from the font's own OpenType metrics, `Fragmenter` paginates into a
retained `LaidOutDocument` of absolutely-positioned `PlacedItem`s, and each backend's thin `<Backend>Painter`
draws that tree without measuring or breaking anything. **This is the ONLY path to a rendered page —
PNG, PDF and every backend.** The production renderers that preceded it (`SkiaPageRenderer`,
`ImageSharpPageRenderer`, both `TextRenderer`s, `PdfTextEngine`, `PdfPageRenderer` and the
`PageRendererBase` under them) were deleted in 2026-08 once the engine covered the whole corpus and the
PDF flip landed at aggregate +0.0017 against Word. All three backends therefore paginate identically —
the three-way page-count divergence the engine was built to end is over. There is no fallback path and no
engine kill switch. See `docs/layout-engine.md` — the architecture reference for this subsystem, plus the
landing history of how it got there.

**Rendering backends** — each is a thin drawing layer over the engine: the `<Backend>Painter` draws the
`LaidOutDocument`, the `<Backend>RenderContext` owns the drawing primitives and font/image caches, and the
`<Backend>WordArtDrawer`/`<Backend>WordArtRasterizer` pair draws WordArt (the rasterizer feeds PDF
embedding). The public entry-point converters live here too (DOCX→PNG and HTML→PNG in the same assembly):
- **SkiaSharp** (`src/Morph.Skia/`): SkiaSharp + Svg.Skia. Entry points `SkiaDocumentConverter` (DOCX→PNG) and `SkiaHtmlConverter` (HTML→PNG).
- **ImageSharp** (`src/Morph.ImageSharp/`): SixLabors.ImageSharp / ImageSharp.Drawing / Fonts. Entry points `ImageSharpDocumentConverter` and `ImageSharpHtmlConverter`.

**PDF** (`src/Morph.Pdf/`): `PdfRenderer` plus the DOCX→PDF and HTML→PDF converters, via PdfSharp.
`PdfRenderer` paginates with the shared `Fragmenter` and draws with `PdfPainter`, exactly like the raster
backends; the byte-reproducibility post-processing (`MakeDeterministic` / `TrimPages` / `Normalize`) lives
there too.

**Blazor** (`src/Morph.Blazor/`): the reusable browser front end, packaged for NuGet as `Morph.Blazor`.
`MorphConverter` is the whole widget (upload → page preview → format picker → download);
`ConversionService` wraps Morph's converters over `byte[]` in / `byte[]` out, and `FontStore` materialises
the bundled Aptos faces into the WASM in-memory filesystem because a browser has no OS fonts. Its
`wwwroot/` ships the stylesheet, an ES module of JavaScript, those fonts and three sample files as static
web assets, served to a host app under `_content/Morph.Blazor/`. It is a plain Razor class library, so —
unlike `Morph.Web` — it builds and packs everywhere, including the linux/amd64 test container. Public
types stay in the single `Morph` namespace like every other assembly, which is why the option-panel
component is `ExportOptionsPanel`: `ExportOptions` is already Morph's own record. See
`src/Morph.Blazor/README.md`.

**Web app** (`src/Morph.Web/`): the Blazor WASM app at morph.papyrine.org. Since the converter was
extracted into `Morph.Blazor` this project is only the shell — header, theme toggle, footer — around
`<MorphConverter />`. See `src/Morph.Web/README.md`.

For a complete feature-by-feature mapping to code locations, see `docs/word-features.md` — render
locations that name the deleted production raster code describe history; the engine painters are the only
path for every output format, PDF included.

## Code Style

- C# preview features enabled (`LangVersion: preview`), nullable enabled, implicit usings
- `TreatWarningsAsErrors: true` with `EnforceCodeStyleInBuild: true`
- Use `var` everywhere, expression-bodied members, file-scoped namespaces
- No accessibility modifiers on internal members (`dotnet_style_require_accessibility_modifiers = never`)
- Private fields/constants use camelCase (no underscore prefix)
- Braces required for all control structures
- Always use underscores (`_`) for unused lambda parameters (e.g., `_ => _.Method()`)
- See `.editorconfig` for full rules

## Testing

- **Framework:** TUnit (not xUnit/NUnit) with `[Test]` and `[MethodDataSource]` attributes
- **Scenario tests** (`SkiaScenarioTests.cs` / `ImageSharpScenarioTests.cs`, DEBUG-only): parameterized over the 330 directories in `src/Tests/Inputs/word/`, each containing `input.docx` and `expected_*.png` reference images. The corpus is split by input format under `Inputs/` — `word/`, `excel/`, `powerpoint/` — because the themed category names collide across formats; `ScenarioInputs` is the single discovery seam. Uses Verify plus an in-repo comparer (`Compare/PageComparison.cs`, `Compare/Ssim.cs`, `Compare/PngDecoder.cs`) for pixel-level comparison — ImageMagick was removed because Magick.NET 14.15 silently changed `ErrorMetric.Absolute` semantics and its SSIM added ~10 minutes per run. Both backends are tested independently. The test harness's `ModuleInitializer` sets `DefaultFontSettings.DeterministicRendering = true` so Skia glyph rasterization is identical across machines (greyscale AA, integer x positions, no hinting) — without this the verified PNGs drift between local and CI due to platform subpixel differences. When updating baselines, keep this setting enabled.
- **Spec tests** (`src/Tests/SpecTests/`): unit tests for specific OOXML specification features
- **Export scenario tests** (`src/Tests/SpecTests/Export/ExportScenarioTests.cs`): snapshot the HTML/Markdown/PDF exporters per scenario. PDF snapshots route through Verify.PDFium (`VerifyPDFium.Initialize()` in `ModuleInitializer`), expanding each into `pdf_result.verified.txt` (page count/sizes/document properties), `pdf_result.verified.pdf`, and per-page `pdf_result#page_*.verified.png` rendered by PDFium — the page images feed `compare-all-pdf.md`.
- **RenderHelper** (`src/RenderHelper/`): .NET Framework 4.8.1 project that generates reference images using Microsoft Word, Excel and PowerPoint via COM interop (Windows-only, not part of normal test runs). One test per format: `GenerateExpectedImage` (Word), `GenerateExpectedExcelImage`, `GenerateExpectedPowerPointImage`. Claude may run it for a **single scenario** (e.g. to seed `expected_0001.png` for a newly added input). Do not run the whole-suite `GenerateExpectedImages` test. The project uses NUnit/VSTest, but `global.json` pins `Microsoft.Testing.Platform` so `dotnet test` fails — invoke `vstest.console.exe` directly against the built DLL. Example: `"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" src/RenderHelper/bin/Debug/net481/RenderHelper.dll /TestCaseFilter:"FullyQualifiedName~<scenarioName>"` (build first with `dotnet build src/RenderHelper/RenderHelper.csproj`).
- **Parallelism** (`src/Tests/CoreCountLimit.cs`): `[assembly: ParallelLimiter<CoreCountLimit>]` caps concurrent tests at the core count. TUnit's auto-detected default is 4x that, which oversubscribes a suite whose heavy tests are rasterisation and out-of-process Chromium — measured on 12 cores the default was consistently the slowest setting, by between 9% and 23% depending on machine conditions, while anything from 1x to 2x cores was flat. Note that absolute suite times drift enough between runs (3m05s to 3m47s for one configuration in a single session) that **only back-to-back runs are comparable**. Override for an experiment with `--maximum-parallel-tests <n>`; keep the measured table in that file current if you change it.
- **Static-setting tests** (`src/StaticSettingTests/`): isolated project that mutates process-wide settings on `DefaultFontSettings` (e.g. the render-locked default font). Runs single-threaded via `[assembly: ParallelLimiter<SingleThreaded>]` and a `[BeforeEvery(HookType.Test)]` hook in `ResetHook.cs` resets the static state between tests. Must stay in its own assembly so the `renderOccurred` latch does not leak from scenario tests. Run with `dotnet run --project src/StaticSettingTests`.

### Excel references need an A4 printer

**Excel cannot paginate without a printer driver, and it takes the paper size from the PRINTER, not
from the workbook.** Word and PowerPoint have neither problem, so this bites only when regenerating
`Inputs/excel/**/expected_*.png`.

Two failure modes, the second far worse than the first:

1. With no printer installed (or the Print Spooler stopped), `ExportAsFixedFormat` throws
   *"No printers are installed"*.
2. With a printer whose paper is Letter, Excel **silently exports Letter pages** — ignoring both
   `PageSetup.PaperSize = xlPaperA4` set over COM (which reads back as A4 afterwards) and an
   explicit `paperSize="9"` in the sheet XML. The references look perfectly correct and simply never
   match the parser's A4 pages, which suppresses SSIM and skews the error metric rather than failing.

The harness pins A4 (`DefaultPageSize.UseLetterSize = false` in the Tests `ModuleInitializer`), so the
printer has to agree:

```powershell
Set-PrintConfiguration -PrinterName 'Microsoft Print to PDF' -PaperSize A4
```

`Microsoft Print to PDF` is the right driver — built into Windows and free of hard margins. Enabling
it needs elevation:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName Printing-PrintToPDFServices-Features -All
```

After generating, confirm the pages really are A4 rather than trusting the run to have passed — at
150 DPI an A4 page is 1240x1754 and at 96 DPI it is 794x1123, against Letter's 1056x1632 and 816x1056.

## Ad hoc Word probes

**When a rendering rule is in doubt, do not infer it from a corpus scenario — build a fixture that isolates it and ask Word.** Production scenarios vary in a dozen ways at once, so a measurement taken off one silently attributes an effect to the wrong cause. Two rules were settled backwards this way before probes corrected them: an HTML table's grid was read as collapsed off a single reference when Word actually draws detached per-cell boxes, and body text that looked like it should follow the host document's font turned out to be Times New Roman everywhere except inside tables and lists.

The loop:

0. **AMPLIFY THE VARIABLE.** Declare the thing under test far larger than any real document would
   — a 6pt border, a 48pt font, a 2cm cell margin — and, where it takes a magnitude, measure it at
   **two or more** values. This is the highest-value rule in this section and the one most often
   skipped, because a realistic value looks like the honest thing to test. At 150 DPI a 0.75pt
   border is 1-2px, and antialiasing is then indistinguishable from a second line, a lighter
   shade, or a gap: the measurement cannot separate the hypotheses, so whichever one you brought
   with you survives. Four border rules were settled wrong this way in a single session —
   `w:sz` read as the stack total (right at `sz=6` only because a floor coincidentally produced
   the same answer), the per-line rule then over-generalised to table cells, a highlight modelled
   on `outset` that Word does not draw at all, and the three-D bevels modelled as two separated
   lines when Word draws one contiguous block. Every one of them was visible immediately at 6pt,
   and every one had passed a plausible-looking measurement at 0.75pt. **A single width can tell
   you a model fits; only a second width can tell you it is the right model.**

1. **Build the fixture by cloning a known-good package** rather than authoring OOXML by hand — read an existing `input.docx` with `zipfile`, swap only the part under test (e.g. `word/afchunk.htm`), write it back out. The probe then differs from a passing scenario in exactly one thing.
2. **Vary one axis per fixture, and put the variants in ONE document** where they don't interact — four tables differing only in `cellpadding` and `border` answer four questions in a single Word render, and stacking them vertically leaves horizontal geometry independent.
3. **Drop it at `src/Tests/Inputs/word/_probe_<name>/input.docx`** and render it with the RenderHelper filter above (`GenerateExpectedImage` is parameterized over every `Inputs/word/**/input.docx`, so a new directory is discovered automatically).
4. **Measure the PNG, don't eyeball it.** Detect rules by counting near-white-failing pixels down a column or across a row; sample glyph starts by scanning for ink past the rule. Eyeballing produced the wrong collapsed/detached call.
   - **For text metrics, read the XPS instead of the PNG.** `MORPH_KEEP_XPS=1` makes RenderHelper leave `word_output.xps` beside the PNGs, and its `<Glyphs>` elements carry Word's exact per-glyph advances (`Indices`, in 1/100 em of `FontRenderingEmSize`) and baseline origins — a lossless readout where ink measurement carries ±1px noise. Two decoding traps: `UnicodeString` is XML-entity-encoded (`&quot;` is ONE glyph), and some paragraphs arrive as consecutive single-glyph runs with no `Indices` (recover advances from `OriginX` deltas). `scripts/generate-word-advances.py` is a working parser. The em size Word declares there is itself a finding: it is the nominal size rounded to whole pixels on the 120-dpi layout grid (8pt Calibri → `7.8`), which is how the #43 advance model was caught.
5. **Render the same probe through Morph on the host** for the comparison — a scratch console app referencing `Morph.Skia` with `ImageExportOptions { Dpi = 150, FontDirectory = "src/Fonts" }`. Layout is a faithful proxy on the host; only anti-aliasing differs, so positions and wraps can be trusted without Docker.
6. **Move the probe directories out of `src/Tests/Inputs/word/` before running the suite or committing** — while they are there they are corpus members and will fail for want of baselines.

Probe findings are durable knowledge: record the measured numbers in the relevant doc, because they are what makes the next attempt cheap.

**Ask the parser, not the XML, what a scenario contains.** Grepping `document.xml` for the feature
under test is unreliable — it misses anything inherited from a style, a `docDefaults`, or a part
you did not think to scan, and it silently reports absence. `labels/08` scanned as `single`-only
twice while carrying 40 `3pt double` borders through a table style. Its
`html_result.verified.html` (or `md_result.verified.md`) is a dump of what the parser actually
built, and answered in one grep.

### Amplified diagnostic fixtures

The same amplification applies to the **purpose-built** corpus fixtures, and permanently: a
scenario whose whole job is to exercise one feature should declare that feature at a size the
reference image can settle an argument about. `border_style_variants` was rebuilt this way
(`sz=24`/`sz=18` for its style enumerations) after its original `sz=6` proved unable to show what
correct looked like.

The distinction is what the fixture is FOR:

- **Diagnostic fixtures** — hand-authored, typically a 3-part package with no theme or media
  (`table_borders`, `table_text_direction`, `wide_table`, `paragraph_borders`, `complex_tables`).
  Amplify freely. Several still test at `w:sz="4"` (0.5pt) or 2.5pt cell margins and would settle
  nothing in a dispute; fatten them the next time one of them is the fixture in an investigation.
  The `color_transform_*` set is the pattern to copy: one scenario per rule, each declaring its
  variable at magnitudes that separate the candidate models by 30-60 per channel, each recording
  Word's measured values in its own `notes.md`.
- **Real-world templates** — the letters, résumés, brochures and newsletters that make up most of
  the corpus. **Never amplify these.** Their value is that they are what users actually feed in;
  editing them for legibility destroys the fidelity reference.

## Feature Documentation

`docs/word-features.md` is the comprehensive feature matrix listing every DOCX feature with implementation status, code locations, and specification links. `docs/floating-art-pipeline.md` documents the cross-cutting floating/anchored-art architecture (parse-path authority rules, nested transforms, z-order, clipping) plus a decision log of attempted-and-reverted approaches — update it when changing that pipeline. `docs/fidelity-audit.md` records how renders are compared against Word and how a rendering change is judged. `docs/html-import.md` covers the HTML input path shared by AltChunk and the HTML converters — the block-CSS model, the Word-derived constants (paragraph pitch, image px→pt), and the attempted-and-reverted CSS box work.

`src/todo.md` is the live fidelity backlog: **open findings only**. When a finding lands, delete it and move anything durable into the docs above (or `src/page_counts.md` for page-count experiments) — the file must never accumulate records of shipped work.

**When adding, modifying, or removing a DOCX feature, update the feature matrix:**
1. Update the feature status (`DONE` / `PARTIAL` / `TODO`)
2. Update parse/model/render locations and audience notes
3. Update the summary statistics at the bottom — the category table, the `**Total**` row and the mermaid pie all have to move together
4. Add new test directory name to the Test row

Step 3 is enforced: `FeatureMatrixSummaryTests` recounts the tally from the `` #### Feature `STATUS` `` headings and fails with the exact deltas if any of the three disagree. It has caught a hand-edit slip and a clean-but-wrong merge, so do not hand-patch one of the three and assume the rest follow.

## Package Management

Central Package Management (CPM) is enabled. All package versions are defined in `src/Directory.Packages.props` — project files reference packages without version numbers.

## Solution

The solution file is `src/Morph.slnx`. All source is under `src/`.
