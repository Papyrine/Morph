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

The wrapper builds `morph-test:latest` on first run, reuses it afterward, mounts the working tree at `/src`, and caches NuGet packages in `./.nuget-cache/` (gitignored). Set `MORPH_REBUILD=1` to force a rebuild after editing `Dockerfile.test`.

**If the Docker daemon is not running, stop and ask the user to start it.** Do not start Docker Desktop yourself, and do not work around it by running the scenario suite on the host — the baselines will not match.

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

The conversion pipeline is **Parse → Render**, split across multiple assemblies:

**Core** (`src/Morph/`): the model (`ParsedDocument` and the `DocumentElement` hierarchy, one type per file under `src/Morph/Parsing/`), shared rendering base (`RenderContextBase`, `FontCacheLoader`, `FontHelpers`, `TableLayout`), the `ExportOptions` records, `ConversionResult`, the text exporters (HTML/Markdown), **and both parsers**:
- **DOCX** (`src/Morph/OpenXml/`): `DocumentParser` reads OOXML via DocumentFormat.OpenXml and builds a `ParsedDocument`. Sub-parsers handle shapes, ink, themes, and HTML (AltChunk).
- **HTML** (`src/Morph/Html/`): `HtmlParser` converts HTML to `DocumentElement` trees via AngleSharp. `HtmlConverter` is the abstract base for HTML→raster converters.

Because both parsers live in core, `Morph` depends on both `DocumentFormat.OpenXml` and `AngleSharp`, and every downstream assembly transitively drags both.

**Rendering backends** — each has its own `PageRenderer`, `TextRenderer`, `RenderContext`, **and the public entry-point converters** (DOCX→PNG and HTML→PNG live together in the same engine assembly):
- **SkiaSharp** (`src/Morph.Skia/`): SkiaSharp + Svg.Skia. Entry points `SkiaDocumentConverter` (DOCX→PNG) and `SkiaHtmlConverter` (HTML→PNG).
- **ImageSharp** (`src/Morph.ImageSharp/`): SixLabors.ImageSharp / ImageSharp.Drawing / Fonts. Entry points `ImageSharpDocumentConverter` and `ImageSharpHtmlConverter`.

**PDF** (`src/Morph.Pdf/`): `PdfRenderer` plus the DOCX→PDF and HTML→PDF converters, via PdfSharp.

For a complete feature-by-feature mapping to code locations, see `docs/word-features.md`.

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
- **Scenario tests** (`SkiaScenarioTests.cs` / `ImageSharpScenarioTests.cs`, DEBUG-only): parameterized over 2000+ directories in `src/Tests/Inputs/`, each containing `input.docx` and `expected_*.png` reference images. Uses Verify + ImageMagick for pixel-level comparison. Both backends are tested independently. The test harness's `ModuleInitializer` sets `DefaultFontSettings.DeterministicRendering = true` so Skia glyph rasterization is identical across machines (greyscale AA, integer x positions, no hinting) — without this the verified PNGs drift between local and CI due to platform subpixel differences. When updating baselines, keep this setting enabled.
- **Spec tests** (`src/Tests/SpecTests/`): unit tests for specific OOXML specification features
- **Export scenario tests** (`src/Tests/SpecTests/Export/ExportScenarioTests.cs`): snapshot the HTML/Markdown/PDF exporters per scenario. PDF snapshots route through Verify.PDFium (`VerifyPDFium.Initialize()` in `ModuleInitializer`), expanding each into `pdf_result.verified.txt` (page count/sizes/document properties), `pdf_result.verified.pdf`, and per-page `pdf_result#page_*.verified.png` rendered by PDFium — the page images feed `compare-all-pdf.md`.
- **RenderHelper** (`src/RenderHelper/`): .NET Framework 4.8.1 project that generates reference images using Microsoft Word via COM interop (Windows-only, not part of normal test runs). Claude may run it for a **single scenario** (e.g. to seed `expected_0001.png` for a newly added input). Do not run the whole-suite `GenerateExpectedImages` test. The project uses NUnit/VSTest, but `global.json` pins `Microsoft.Testing.Platform` so `dotnet test` fails — invoke `vstest.console.exe` directly against the built DLL. Example: `"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" src/RenderHelper/bin/Debug/net481/RenderHelper.dll /TestCaseFilter:"FullyQualifiedName~<scenarioName>"` (build first with `dotnet build src/RenderHelper/RenderHelper.csproj`).
- **Static-setting tests** (`src/StaticSettingTests/`): isolated project that mutates process-wide settings on `DefaultFontSettings` (e.g. the render-locked default font). Runs single-threaded via `[assembly: ParallelLimiter<SingleThreaded>]` and a `[BeforeEvery(HookType.Test)]` hook in `ResetHook.cs` resets the static state between tests. Must stay in its own assembly so the `renderOccurred` latch does not leak from scenario tests. Run with `dotnet run --project src/StaticSettingTests`.

## Feature Documentation

`docs/word-features.md` is the comprehensive feature matrix listing every DOCX feature with implementation status, code locations, and specification links. `docs/floating-art-pipeline.md` documents the cross-cutting floating/anchored-art architecture (parse-path authority rules, nested transforms, z-order, clipping) plus a decision log of attempted-and-reverted approaches — update it when changing that pipeline. `docs/fidelity-audit.md` records how renders are compared against Word and how a rendering change is judged.

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
