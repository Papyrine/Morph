# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Morph is a .NET library that converts Microsoft Word DOCX documents or HTML content into PNG images. The DOCX public API lives in the `WordRender` namespace (entry point: `DocumentConverter`). The HTML public API lives in the `HtmlRender` namespace (entry point: `HtmlConverter`). Both expose `ConvertToImages` and `ConvertToImageData`.

## Build & Test Commands

```bash
# Build (always pass the `src` directory or an absolute path to `src/Morph.slnx`).
# `dotnet build` with no arg or with a relative slnx path resolved against a
# subdirectory cwd will fail with MSB1009 — the slnx file isn't auto-discovered
# from arbitrary cwds the way a single .csproj is.
dotnet build src --configuration Release

# Run all tests (TUnit via Microsoft.Testing.Platform; tests are an executable)
dotnet run --project src/Tests --configuration Release

# Run tests with dotnet test
dotnet test src/Tests

# Limit test parallelism to half the CPU count to avoid resource contention
dotnet run --project src/Tests --configuration Debug -- --maximum-parallel-tests $(( $(nproc) / 2 ))
```

### TUnit filter syntax (`--treenode-filter`)

TUnit uses `--treenode-filter`, not `--filter`. The filter path format is:
```
/{assembly}/{namespace}/{class}/{method}
```

Parameter values are **NOT** part of the filter path — they only appear in the display name, which is not filterable. Wildcards (`*`) work in any segment but `**` is only allowed as the final segment.

```bash
# Run only the scenario test classes (skip the ~540 spec/unit tests)
dotnet run --project src/Tests --configuration Debug -- --treenode-filter "/*/*/*ScenarioTests/*"

# Run only the Skia scenario tests
dotnet run --project src/Tests --configuration Debug -- --treenode-filter "/*/*/SkiaScenarioTests/*"
```

**To target a single parameterized scenario (e.g. `cover-letters/02` alone)** — since the filter can't match parameter values, temporarily narrow `GetScenarioDirectories()` in `SkiaScenarioTests.cs` / `ImageSharpScenarioTests.cs`:
```csharp
.Where(_ => _.EndsWith(@"cover-letters\02\input.docx"))
```
Then combine with `--treenode-filter "/*/*/*ScenarioTests/*"` to skip the spec tests. Revert the `Where` when done.

Brackets (`[...]`) in treenode filters are for property-bag filters (e.g. `[Category=Foo]`), not parameter matching — don't confuse them with LINQ-style filtering.


**Prerequisites:** .NET SDK 10.0 (preview). See `global.json` for exact version. Tests load fonts directly from the bundled `src/Fonts/` directory via `ConversionOptions.FontDirectory`, so no OS-level font install is needed.

## Architecture

The conversion pipeline is **Parse → Render**, split across multiple assemblies:

**Core** (`src/Morph/`): the model (`ParsedDocument`, `DocumentElement` types in `DocumentElements.cs`), shared rendering base (`RenderContextBase`, `FontCacheLoader`, `FontHelpers`, `TableLayout`), `ConversionOptions`, `ConversionResult`, the text exporters (HTML/Markdown), **and both parsers**:
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
- **RenderHelper** (`src/RenderHelper/`): .NET Framework 4.8.1 project that generates reference images using Microsoft Word via COM interop (Windows-only, not part of normal test runs). Claude may run it for a **single scenario** (e.g. to seed `expected_0001.png` for a newly added input). Do not run the whole-suite `GenerateExpectedImages` test. The project uses NUnit/VSTest, but `global.json` pins `Microsoft.Testing.Platform` so `dotnet test` fails — invoke `vstest.console.exe` directly against the built DLL. Example: `"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" src/RenderHelper/bin/Debug/net481/RenderHelper.dll /TestCaseFilter:"FullyQualifiedName~<scenarioName>"` (build first with `dotnet build src/RenderHelper/RenderHelper.csproj`).
- **Static-setting tests** (`src/StaticSettingTests/`): isolated project that mutates process-wide settings on `DefaultFontSettings` (e.g. the render-locked default font). Runs single-threaded via `[assembly: ParallelLimiter<SingleThreaded>]` and a `[BeforeEvery(HookType.Test)]` hook in `ResetHook.cs` resets the static state between tests. Must stay in its own assembly so the `renderOccurred` latch does not leak from scenario tests. Run with `dotnet run --project src/StaticSettingTests`.

## Feature Documentation

`docs/word-features.md` is the comprehensive feature matrix listing every DOCX feature with implementation status, code locations, and specification links.

**When adding, modifying, or removing a DOCX feature, update the feature matrix:**
1. Update the feature status (`DONE` / `PARTIAL` / `TODO`)
2. Update parse/model/render locations and audience notes
3. Update the summary statistics at the bottom
4. Add new test directory name to the Test row

## Package Management

Central Package Management (CPM) is enabled. All package versions are defined in `src/Directory.Packages.props` — project files reference packages without version numbers.

## Solution

The solution file is `src/Morph.slnx`. All source is under `src/`.
