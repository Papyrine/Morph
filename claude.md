# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Morph is a .NET library that converts Microsoft Word DOCX documents into PNG images. The public API lives in the `WordRender` namespace. The main entry point is `DocumentConverter`, which exposes `ConvertToImages` (file/stream → PNG files) and `ConvertToImageData` (file/stream → byte arrays).

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
```

**Prerequisites:** .NET SDK 10.0 (preview). See `global.json` for exact version. Bundled fonts in `src/Fonts/` must be installed on CI (see `src/appveyor.yml`).

## Architecture

The conversion pipeline is **Parse → Render**:

1. **Parsing** (`src/Morph/Parsing/`): `DocumentParser` reads OOXML via DocumentFormat.OpenXml and builds a `ParsedDocument` containing a tree of `DocumentElement` types (defined in `DocumentElements.cs`). Sub-parsers handle shapes, ink, themes, and HTML (AltChunk).

2. **Rendering** (`src/Morph/Rendering/`): `PageRenderer` lays out elements into pages and draws them using SkiaSharp. `TextRenderer` handles typography. `RenderContext` holds rendering state (DPI, page settings, compatibility mode).

## Code Style

- C# preview features enabled (`LangVersion: preview`), nullable enabled, implicit usings
- `TreatWarningsAsErrors: true` with `EnforceCodeStyleInBuild: true`
- Use `var` everywhere, expression-bodied members, file-scoped namespaces
- No accessibility modifiers on internal members (`dotnet_style_require_accessibility_modifiers = never`)
- Private fields/constants use camelCase (no underscore prefix)
- Braces required for all control structures
- See `.editorconfig` for full rules

## Testing

- **Framework:** TUnit (not xUnit/NUnit) with `[Test]` and `[MethodDataSource]` attributes
- **Scenario tests** (`ScenarioTests.cs`, DEBUG-only): parameterized over 2000+ directories in `src/Tests/Inputs/`, each containing `input.docx` and `expected_*.png` reference images. Uses Verify + ImageMagick for pixel-level comparison
- **Spec tests** (`src/Tests/SpecTests/`): unit tests for specific OOXML specification features
- **RenderHelper** (`src/RenderHelper/`): .NET Framework 4.8.1 project that generates reference images using Microsoft Word via COM interop (Windows-only, not part of normal test runs)

## Package Management

Central Package Management (CPM) is enabled. All package versions are defined in `src/Directory.Packages.props` — project files reference packages without version numbers.

## Solution

The solution file is `src/Morph.slnx`. All source is under `src/`.
