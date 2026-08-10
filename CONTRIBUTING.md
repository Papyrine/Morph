# Contributing to Morph

Morph converts Word DOCX documents and HTML content into PNG images via two
rendering backends (SkiaSharp and SixLabors.ImageSharp). The test suite
compares rendered PNGs to checked-in Verify baselines, so any change that
shifts rendering output requires a baseline regeneration. This guide
covers the local workflow, CI, and the conventions the codebase follows.

## Prerequisites

- **Docker Desktop** with the daemon running. The test suite executes
  inside a pinned `linux/amd64` container image so output is bit-identical
  regardless of host OS or CPU architecture.
- **Apple Silicon hosts:** enable Rosetta in Docker Desktop
  (Settings → General → "Use Rosetta for x86/amd64 emulation on Apple
  Silicon"). QEMU's x86_64 user-mode emulation crashes .NET 10's MSBuild
  with an `AccessViolationException` mid-build; Rosetta is stable and
  near-native speed.
- **Optional:** install the .NET SDK pinned by `global.json` (currently
  `10.0.300+`) on the host. The canonical workflow does not require a host
  SDK, but IDE intellisense and `dotnet test` shortcuts need it.

## Running tests

The wrapper script handles container build, source bind-mount, and a
host-side NuGet cache. The same command runs locally and in CI.

```bash
# Full scenario + spec suite (~2 minutes on Apple Silicon with Rosetta)
./scripts/test.sh

# Filter to a single class via TUnit treenode filter
./scripts/test.sh dotnet run --project src/Tests --configuration Release \
    -- --treenode-filter "/*/*/SkiaScenarioTests/*"

# Static-setting tests — separate single-threaded project
./scripts/test.sh dotnet run --project src/StaticSettingTests --configuration Release

# Interactive shell inside the container
./scripts/test.sh bash
```

The first invocation builds `morph-test:latest` (~1 minute) and warms a
NuGet cache under `./.nuget-cache/`. Subsequent runs reuse both. Set
`MORPH_REBUILD=1` to force a rebuild after editing `Dockerfile.test`.

## TUnit filter syntax

TUnit uses `--treenode-filter`, not the more familiar `--filter`. The path
format is:

```
/{assembly}/{namespace}/{class}/{method}
```

Parameter values do not appear in the filter path — they only appear in
the display name. Wildcards (`*`) work in any segment; `**` is only
allowed as the final segment.

To target a single parameterized scenario, temporarily narrow
`GetScenarioDirectories()` in the corresponding scenario-test file:

```csharp
.Where(_ => _.EndsWith(@"cover-letters\02\input.docx"))
```

Then combine with `--treenode-filter "/*/*/*ScenarioTests/*"` to skip the
spec tests. Revert the `Where` before opening a PR.

## Changing rendering output

When a deliberate change shifts converter output, regenerate the Verify
baselines inside the same container:

```bash
./scripts/regenerate-baselines.sh
```

The script deletes existing `results_*.verified.{png,json}` snapshots, runs
the suite once to produce `*.received.*` files, promotes those to
`*.verified.*`, then re-runs to confirm stability. Review the resulting
diff visually before committing.

## Adding a new scenario test

1. Create a directory under `src/Tests/Inputs/word/<scenario-name>/` and drop
   the source document as `input.docx`.
2. Seed the Word reference image (`expected_*.png`). The
   `src/RenderHelper/` project drives Microsoft Word via COM interop and
   targets `net481` (Windows-only). For a single scenario:
   ```pwsh
   dotnet build src/RenderHelper/RenderHelper.csproj
   vstest.console.exe src/RenderHelper/bin/Debug/net481/RenderHelper.dll `
       /TestCaseFilter:"FullyQualifiedName~<scenario-name>"
   ```
   Without Windows access, ask a maintainer to seed `expected_*.png` for
   the new scenario.
3. Run `./scripts/regenerate-baselines.sh` to populate the
   `results_skia.verified.*` and `results_imagesharp.verified.*` files
   for the new directory.

## Code style

Enforcement is automated:

- `.editorconfig` covers C# style — `var` everywhere, file-scoped
  namespaces, expression-bodied members, camelCase private fields (no
  underscore prefix), braces required on all control structures.
- `TreatWarningsAsErrors: true` — warnings break the build.
- `EnforceCodeStyleInBuild: true` — style violations break the build.
- `src/mdsnippets.json` runs content validation across markdown files at
  build time. The rules forbid informal pronouns and exclamation marks;
  technical-writing style is required. Plan files under
  `docs/superpowers/` and test inputs under `src/Tests/Inputs/` are
  excluded from the scan. Refer to `src/mdsnippets.json` for the exact
  configuration.

Use underscores (`_`) for unused lambda parameters: `_ => _.Method()`.

## Commit conventions

Use conventional commit subjects: `feat:`, `fix:`, `docs:`, `test:`,
`refactor:`, `build:`, `ci:`, `chore:`. Keep the subject under 70
characters; use the body for the rationale. Examples in `git log`.

## Continuous integration

GitHub Actions runs the full container suite on every pull request and
every push to `main` (`.github/workflows/test.yml`). On success, NuGet
packages (built incidentally via `IsPackable=true`) are uploaded as a
workflow artifact named `nupkgs`. On failure, any `*.received.*` files
are uploaded as `verify-received-files` so reviewers can inspect
rendering divergence.

There is no other CI provider. NuGet.org publishing is not currently
automated; the workflow artifact is the canonical source of release
candidates.

## Known papercuts

- **File mode flips on macOS:** Docker Desktop's bind-mount layer
  occasionally flips `.editorconfig` and `src/Shared.sln.DotSettings` to
  mode `755` after a container run. Content is unchanged. Restore with
  `git checkout -- <file>`.
- **Pre-existing `Arial Black` test failure:** the test
  `FontStyleFromNameTests.GetFontFamily_StyleSuffixStripped_ResolvesBaseFamily("Arial Black", "Arial Black")`
  fails inside the container because no Arial Black font face is bundled
  in `src/Fonts/`. The companion `Calibri Light` argument passes
  (`Calibri_300.ttf` provides that family). Fix is tracked separately —
  either bundle an Arial Black face or refactor the test to take a
  `FontDirectory` rather than relying on system font lookup.

## Getting help

Open an issue at <https://github.com/Papyrine/Morph/issues> with the
symptom, what was tried, and any relevant container or host info.
