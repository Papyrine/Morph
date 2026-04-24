# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Morph is a .NET library that converts Microsoft Word DOCX documents or HTML content into PNG images. The DOCX public API lives in the `WordRender` namespace (entry point: `DocumentConverter`). The HTML public API lives in the `HtmlRender` namespace (entry point: `HtmlConverter`). Both expose `ConvertToImages` and `ConvertToImageData`.

## Build & Test Commands

```bash
# Build
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

**Core model** (`src/Morph/`): `ParsedDocument`, `DocumentElement` types (defined in `DocumentElements.cs`), shared rendering base (`RenderContextBase`, `FontCacheLoader`, `FontHelpers`, `TableLayout`), `ConversionOptions`, `ConversionResult`. No heavy dependencies.

**Parsers:**
- **DOCX** (`src/Morph.OpenXml/`): `DocumentParser` reads OOXML via DocumentFormat.OpenXml and builds a `ParsedDocument`. Sub-parsers handle shapes, ink, themes, and HTML (AltChunk).
- **HTML** (`src/Morph.Html/`): `HtmlParser` converts HTML to `DocumentElement` trees via AngleSharp. `HtmlConverter` abstract base class.

**Rendering backends** — each has its own `PageRenderer`, `TextRenderer`, and `RenderContext`:
- **SkiaSharp** (`src/Morph.Skia/`): rendering engine using SkiaSharp + Svg.Skia
- **ImageSharp** (`src/Morph.ImageSharp/`): rendering engine using SixLabors.ImageSharp / ImageSharp.Drawing / Fonts

**Entry points** (thin wrappers combining a parser with a rendering backend):
- `src/Morph.OpenXml.Skia/` — `WordRender.Skia.DocumentConverter` (DOCX → PNG via SkiaSharp)
- `src/Morph.OpenXml.ImageSharp/` — `WordRender.ImageSharp.DocumentConverter` (DOCX → PNG via ImageSharp)
- `src/Morph.Html.Skia/` — `HtmlRender.Skia.HtmlConverter` (HTML → PNG via SkiaSharp)
- `src/Morph.Html.ImageSharp/` — `HtmlRender.ImageSharp.HtmlConverter` (HTML → PNG via ImageSharp)

The HTML packages have no transitive dependency on `DocumentFormat.OpenXml`.

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
- **Scenario tests** (`SkiaScenarioTests.cs` / `ImageSharpScenarioTests.cs`, DEBUG-only): parameterized over 2000+ directories in `src/Tests/Inputs/`, each containing `input.docx` and `expected_*.png` reference images. Uses Verify + ImageMagick for pixel-level comparison. Both backends are tested independently
- **Spec tests** (`src/Tests/SpecTests/`): unit tests for specific OOXML specification features
- **RenderHelper** (`src/RenderHelper/`): .NET Framework 4.8.1 project that generates reference images using Microsoft Word via COM interop (Windows-only, not part of normal test runs)
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
