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

# Run a single test by name
dotnet run --project src/Tests -- --filter "Name=Scenario"

# Limit test parallelism to half the CPU count to avoid resource contention
dotnet run --project src/Tests --configuration Debug -- --maximum-parallel-tests $(( $(nproc) / 2 ))
```

**Prerequisites:** .NET SDK 10.0 (preview). See `global.json` for exact version. Bundled fonts in `src/Fonts/` must be installed on CI (see `src/appveyor.yml`).

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
