# Containerise Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run Morph's scenario and unit tests inside a single `linux/amd64` Docker image so that Skia/ImageSharp rendering output is bit-identical regardless of the host OS or CPU architecture, then regenerate Verify baselines once against that image and wire up CI to use the same image.

**Architecture:** A single `Dockerfile.test` at the repo root produces a long-lived test image (.NET 10 SDK + the few apt packages SkiaSharp's `NoDependencies` package and ICU need). A `scripts/test.sh` wrapper mounts the working tree as a volume so iteration is fast on developer machines, and uses a host-side NuGet cache so package restore survives container restarts. Apple Silicon developers run the image under QEMU emulation (Rosetta on Docker Desktop). A GitHub Actions workflow builds the same image with `buildx` GHA cache so local and CI never diverge. Baseline regeneration is handled by a second script that nukes all `*.verified.*` files, runs the suite once to produce `*.received.*`, then promotes them — performed in a **separate commit** from the infrastructure so reviewers can tell apart "tooling changed" from "baselines reset".

**Tech Stack:** Docker (buildx), `mcr.microsoft.com/dotnet/sdk:10.0`, .NET 10 (SDK 10.0.300), TUnit, Verify, Verify.ImageMagick, SkiaSharp 3.119.2 (Linux.NoDependencies native pack), SixLabors.ImageSharp 4, GitHub Actions.

**Important note for the executor:** This plan is split into two phases that **must be committed separately**:
- **Phase A — Infrastructure (Tasks 1–9):** adds Dockerfile, scripts, workflow, doc updates. Leaves baselines untouched. Tests will fail in container at the end of Phase A — that is expected.
- **Phase B — Baseline reset (Task 10):** the one-time regeneration of every `*.verified.*` file inside the container. Produces a massive diff (thousands of binary file changes). Lives in its own commit / PR so it can be reviewed as a deliberate baseline reset rather than mixed in with infra changes.

---

## File Structure

**New files:**
- `Dockerfile.test` (repo root) — image definition. Single stage, SDK image, apt deps, env vars. ~20 lines.
- `.dockerignore` (repo root) — exclude `bin/`, `obj/`, `.git/`, `*.received.*`, `.nuget-cache/`. Keeps build context tiny so volume mounts are the source of truth.
- `scripts/test.sh` — developer wrapper. Builds image if missing, runs container with source + NuGet cache volume mounts, passes through args.
- `scripts/regenerate-baselines.sh` — one-time baseline reset wrapper. Deletes existing `results_*.verified.*` files under `src/Tests/Inputs/`, runs the suite (expected to fail), promotes `*.received.*` → `*.verified.*`.
- `.github/workflows/test.yml` — CI workflow. Builds image with buildx + GHA cache, runs tests, uploads `*.received.*` as artifacts on failure for debugging.

**Modified files:**
- `claude.md` — replace the "Build & Test Commands" section to point at the container wrapper as the canonical way to run tests, with a note that bare `dotnet` invocations still work on Windows where the baselines were originally captured.
- `.gitignore` — add `/.nuget-cache/` so the host-side NuGet cache doesn't get committed.

**Untouched (intentionally):**
- `src/Tests/ModuleInitializer.cs` — already has `DeterministicRendering = true` and a pinned en-AU culture; the container inherits those.
- `src/RenderHelper/` — `net481`/COM/Windows-only, can never run in a Linux container; out of scope.
- `expected_*.png` files under `src/Tests/Inputs/` — these are Word reference images, independent of which engine we run; they do not get regenerated.
- `src/Tests/Inputs/**/results_*.verified.json` and `results_*#page_*.verified.png` — regenerated in Phase B (Task 10), not earlier.

---

## Phase A — Infrastructure

### Task 1: Add `.dockerignore`

**Files:**
- Create: `/Users/brandtvavasour/Documents/Papyrine/Morph/.dockerignore`

- [ ] **Step 1: Write the .dockerignore**

Create `.dockerignore` at the repo root with:

```
# Build artifacts
**/bin/
**/obj/
**/TestResults/
**/BenchmarkDotNet.Artifacts/

# Local Verify state — never copy into the image build context
**/*.received.*
**/*.current.*

# Host-side NuGet cache (we volume-mount this, not bake it in)
.nuget-cache/

# Editor / IDE / SCM
.git/
.github/
.vs/
.idea/
.claude/
**/*.suo
**/*.user
**/*.DotSettings.user

# Docs / assets the test image doesn't need
docs/
tools/
readme.md
license.txt
src/RenderHelper/bin/
src/RenderHelper/obj/
```

- [ ] **Step 2: Sanity-check that it parses**

The file is plain text; just confirm it exists.

Run: `test -f .dockerignore && echo OK`
Expected: `OK`

---

### Task 2: Add `Dockerfile.test`

**Files:**
- Create: `/Users/brandtvavasour/Documents/Papyrine/Morph/Dockerfile.test`

- [ ] **Step 1: Write the Dockerfile**

Create `Dockerfile.test` at the repo root with:

```dockerfile
# syntax=docker/dockerfile:1.7
#
# Morph test image — produces bit-identical Skia/ImageSharp output across
# host OSes by pinning a single linux/amd64 .NET 10 SDK environment.
#
# This image is intentionally lightweight: source code, NuGet cache, and
# test output are all volume-mounted from the host at runtime, not baked in.
# That keeps iteration fast and keeps the image cache hit rate high.

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0

# SkiaSharp.NativeAssets.Linux.NoDependencies bundles freetype/expat itself,
# but .NET globalization (we pin culture to en-AU in ModuleInitializer) still
# needs ICU at runtime, and fontconfig is required for some text diagnostics
# even when fonts are loaded explicitly via FontDirectory.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libfontconfig1 \
        libicu-dev \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Pin culture at the OS level too so anything that reads LANG matches the
# en-AU pin in ModuleInitializer.cs:13.
ENV LANG=en_AU.UTF-8 \
    LC_ALL=en_AU.UTF-8 \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_GENERATE_ASPNET_CERTIFICATE=false \
    NUGET_PACKAGES=/nuget \
    DOTNET_NUGET_SIGNATURE_VERIFICATION=false \
    # Verify uses this to skip launching the local diff tool on test failure.
    Verify_DisableDiff=true

WORKDIR /src

# No COPY of source — the wrapper script mounts the working tree as a volume.
# Default command runs the full scenario + spec suite.
CMD ["dotnet", "run", "--project", "src/Tests", "--configuration", "Release"]
```

- [ ] **Step 2: Build the image and confirm it succeeds**

Run: `docker build --platform=linux/amd64 -f Dockerfile.test -t morph-test:latest .`
Expected: Builds cleanly. Final line ends with `naming to docker.io/library/morph-test:latest` or similar. If the `10.0` tag is unavailable, fall back to `10.0-preview` (the build will fail with an explicit "manifest not found" message).

- [ ] **Step 3: Verify the image's .NET SDK matches `global.json`**

Run: `docker run --rm --platform=linux/amd64 morph-test:latest dotnet --version`
Expected: `10.0.300` (or a later `10.0.x` if `rollForward: latestFeature` picks one).

- [ ] **Step 4: Commit**

```bash
git add Dockerfile.test .dockerignore
git commit -m "feat(test): add linux/amd64 Dockerfile for deterministic test runs"
```

---

### Task 3: Add `scripts/test.sh` wrapper

**Files:**
- Create: `/Users/brandtvavasour/Documents/Papyrine/Morph/scripts/test.sh`

- [ ] **Step 1: Create the scripts directory**

Run: `mkdir -p scripts`
Expected: directory exists (already may from `mkdir -p`).

- [ ] **Step 2: Write the wrapper**

Create `scripts/test.sh` with:

```bash
#!/usr/bin/env bash
#
# Run Morph's test suite inside the deterministic linux/amd64 container.
#
# Usage:
#   ./scripts/test.sh                        # runs the full scenario + spec suite
#   ./scripts/test.sh dotnet build src -c Release
#   ./scripts/test.sh dotnet run --project src/StaticSettingTests
#   ./scripts/test.sh bash                   # interactive shell inside the container
#
# Notes:
# - The working tree is mounted at /src; any file the container writes
#   (test output, .received.* files, bin/obj artifacts) appears on the host.
# - NuGet packages are cached in ./.nuget-cache on the host so repeated
#   runs skip restore. Delete that directory to force a clean restore.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE_TAG="${MORPH_TEST_IMAGE:-morph-test:latest}"
NUGET_CACHE="${MORPH_NUGET_CACHE:-${REPO_ROOT}/.nuget-cache}"

# Build the image if it doesn't exist locally. Force a rebuild by passing
# MORPH_REBUILD=1 (used by CI and after editing Dockerfile.test).
if [[ "${MORPH_REBUILD:-0}" == "1" ]] || ! docker image inspect "$IMAGE_TAG" >/dev/null 2>&1; then
    echo ">>> Building ${IMAGE_TAG}" >&2
    docker build \
        --platform=linux/amd64 \
        -f "${REPO_ROOT}/Dockerfile.test" \
        -t "$IMAGE_TAG" \
        "$REPO_ROOT"
fi

mkdir -p "$NUGET_CACHE"

# If args were provided, use them verbatim. Otherwise let the image's
# CMD (full test suite) run.
docker run \
    --rm \
    --init \
    --platform=linux/amd64 \
    -v "${REPO_ROOT}:/src" \
    -v "${NUGET_CACHE}:/nuget" \
    -w /src \
    "$IMAGE_TAG" \
    "$@"
```

- [ ] **Step 3: Make it executable**

Run: `chmod +x scripts/test.sh`
Expected: no output. Confirm with `ls -l scripts/test.sh` — `-rwxr-xr-x` etc.

- [ ] **Step 4: Smoke-test the wrapper**

Run: `./scripts/test.sh dotnet --info`
Expected: prints .NET SDK info including `OS Name: debian`, `OS Platform: Linux`, `Architecture: x64`, RID `linux-x64`. If you're on Apple Silicon, this will be QEMU-emulated and noticeably slower than native — that's expected.

- [ ] **Step 5: Update `.gitignore`**

Find this exact block in `.gitignore` (currently around lines 19–22):

```
*.lock
src/Tests/Inputs/**/input/
```

Replace with:

```
*.lock
src/Tests/Inputs/**/input/

# Host-side NuGet cache used by scripts/test.sh
/.nuget-cache/
```

- [ ] **Step 6: Commit**

```bash
git add scripts/test.sh .gitignore
git commit -m "feat(test): add container wrapper script with NuGet cache"
```

---

### Task 4: Confirm the test project actually builds inside the container

This is a checkpoint task — no new files, just verification before we promise developers the wrapper works.

- [ ] **Step 1: Trigger a full Release build via the wrapper**

Run: `./scripts/test.sh dotnet build src --configuration Release`
Expected: builds the entire solution. The first run is slow (cold NuGet restore + cold compile). Subsequent runs hit the cache. Build should succeed with zero errors. Warnings as errors is enabled (`TreatWarningsAsErrors: true`), so any warning will fail the build — if so, stop and report rather than silencing.

- [ ] **Step 2: Run the static-setting tests**

These are a separate project that must run single-threaded.

Run: `./scripts/test.sh dotnet run --project src/StaticSettingTests --configuration Release`
Expected: tests pass. They do not depend on rendered baselines, so they should pass even without baseline regeneration.

- [ ] **Step 3: Note the failure that's coming next**

We will not commit anything from Task 4. It's purely a checkpoint.

---

### Task 5: Run the full scenario suite in the container and observe the expected mismatches

This is also a checkpoint task. The scenario tests will fail because the existing `*.verified.*` files were captured on macOS Skia/ImageSharp and the container's output is going to diverge. We need to confirm the **shape** of the failure looks like baseline drift (not a build break or a native-library load failure).

- [ ] **Step 1: Run the suite, capturing output**

Run: `./scripts/test.sh 2>&1 | tee /tmp/morph-container-first-run.log` from the repo root.
Expected: the suite runs to completion. A large number of scenario tests fail. The static-setting tests, spec tests, and any non-scenario tests should pass.

- [ ] **Step 2: Inspect the failure mode**

Run: `grep -E "(fail|error|exception)" /tmp/morph-container-first-run.log | head -30`
Expected:
- The failures should mention `Verify` and reference `results_skia` or `results_imagesharp` files.
- You should NOT see `DllNotFoundException`, `SkiaSharp` native load errors, `Unable to load shared library libSkiaSharp`, or `MagickImage` initialization errors.
- If you DO see a native-load error, stop. That means `libfontconfig1` or `libicu-dev` wasn't sufficient — investigate before regenerating any baselines.

- [ ] **Step 3: Confirm received-PNG files were produced for the failing scenarios**

Run: `find src/Tests/Inputs -name "*.received.png" | wc -l`
Expected: a non-zero number (hundreds — one per failing page). These are what Task 10 will promote to `.verified.png`.

- [ ] **Step 4: Clean up the received files before continuing — they'll be regenerated in Task 10**

Run: `find src/Tests/Inputs -name "*.received.*" -delete`
Expected: silent (no errors).

- [ ] **Step 5: No commit. Move on.**

---

### Task 6: Add the GitHub Actions workflow

**Files:**
- Create: `/Users/brandtvavasour/Documents/Papyrine/Morph/.github/workflows/test.yml`

- [ ] **Step 1: Write the workflow**

Create `.github/workflows/test.yml` with:

```yaml
name: Test

on:
  push:
    branches: [main]
  pull_request:

# Cancel in-progress runs if a new push lands on the same branch.
concurrency:
  group: test-${{ github.ref }}
  cancel-in-progress: true

jobs:
  test:
    name: linux/amd64 container suite
    runs-on: ubuntu-latest
    timeout-minutes: 60

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Build test image (with GHA cache)
        uses: docker/build-push-action@v6
        with:
          context: .
          file: Dockerfile.test
          platforms: linux/amd64
          tags: morph-test:latest
          load: true
          cache-from: type=gha,scope=morph-test
          cache-to: type=gha,scope=morph-test,mode=max

      - name: Run scenario + spec tests
        run: ./scripts/test.sh
        # Wrapper sees the image already exists locally so it won't rebuild.

      - name: Run static-setting tests
        run: ./scripts/test.sh dotnet run --project src/StaticSettingTests --configuration Release

      - name: Upload received PNGs on failure
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: verify-received-files
          path: |
            src/Tests/Inputs/**/*.received.*
          if-no-files-found: ignore
          retention-days: 14
```

- [ ] **Step 2: Lint the YAML locally (optional but cheap)**

Run: `docker run --rm -v "$PWD:/work" -w /work cytopia/yamllint:latest .github/workflows/test.yml` if yamllint is convenient. If not, skip — GitHub will surface syntax errors when you push.
Expected: no errors. The workflow uses standard action versions.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/test.yml
git commit -m "ci(test): run scenario suite in linux/amd64 container"
```

---

### Task 7: Update `claude.md` build/test guidance

**Files:**
- Modify: `/Users/brandtvavasour/Documents/Papyrine/Morph/claude.md` (the "Build & Test Commands" section)

- [ ] **Step 1: Read the current Build & Test Commands section**

Run: `sed -n '1,200p' claude.md` and locate the section under `## Build & Test Commands` (it currently starts with the `dotnet build src --configuration Release` block).

- [ ] **Step 2: Apply the edit**

Replace the existing section with the following. Use the Edit tool — match the existing first line of the block (the comment about always passing `src` to `dotnet build`) as `old_string` and provide this as `new_string`:

```markdown
## Build & Test Commands

Tests must produce bit-identical output across machines because the suite
compares rendered PNGs to checked-in baselines. To guarantee that, we run
all tests inside a pinned `linux/amd64` Docker image. **This is the
canonical way to run tests — local invocations of `dotnet test` will
produce subpixel drift on a different OS/CPU and fail Verify diffs.**

```bash
# Run the full scenario + spec suite in the container
./scripts/test.sh

# Run a specific test class (TUnit treenode filter)
./scripts/test.sh dotnet run --project src/Tests --configuration Release \
    -- --treenode-filter "/*/*/SkiaScenarioTests/*"

# Run the static-setting tests (separate single-threaded project)
./scripts/test.sh dotnet run --project src/StaticSettingTests --configuration Release

# Open an interactive shell inside the container
./scripts/test.sh bash

# Build only (no tests)
./scripts/test.sh dotnet build src --configuration Release
```

On Apple Silicon, the container runs under QEMU emulation; expect a 3–5×
slowdown vs. native. That is intentional — the alternative is per-arch
baselines, which doubles maintenance cost.

### Regenerating baselines after a rendering change

When a deliberate change to the converter is expected to shift output,
regenerate `*.verified.*` files **inside the container** using:

```bash
./scripts/regenerate-baselines.sh
```

The script deletes existing `results_*.verified.*` snapshots, runs the
suite once (expected to fail and produce `*.received.*` files), then
promotes the received files to verified. Commit the resulting binary
diff in its own commit — never mix a baseline reset with code changes.

### Running outside the container

Non-scenario unit/spec tests do not depend on rendering output and can
be run directly with `dotnet test src/Tests --treenode-filter ...` for
fast iteration. But any test that touches `SkiaScenarioTests` or
`ImageSharpScenarioTests` **must** use the container, or the Verify
comparison will fail on machines whose Skia/ImageSharp output diverges
from the baseline machine.

### TUnit filter syntax (`--treenode-filter`)
```

Then leave the existing TUnit filter docs untouched (they continue from this point onward). Specifically, keep the existing `/{assembly}/{namespace}/{class}/{method}` explanation and example commands — just prefix the `dotnet run` commands in those examples with `./scripts/test.sh` where they're describing how to invoke the suite.

If matching the whole section in a single Edit call is awkward, do it in two Edits: first replace the "Build (always pass the `src` directory..." block with the new "Tests must produce bit-identical output..." opener and the four containerized invocations; then replace each subsequent bare `dotnet run --project src/Tests ...` in the TUnit filter examples with `./scripts/test.sh dotnet run --project src/Tests ...`.

- [ ] **Step 3: Spot-check the edited file**

Run: `grep -n "scripts/test.sh" claude.md | head`
Expected: at least four matches, showing the wrapper is referenced in the new section and in the updated TUnit examples.

- [ ] **Step 4: Commit**

```bash
git add claude.md
git commit -m "docs: document containerized test workflow in claude.md"
```

---

### Task 8: Add `scripts/regenerate-baselines.sh`

**Files:**
- Create: `/Users/brandtvavasour/Documents/Papyrine/Morph/scripts/regenerate-baselines.sh`

- [ ] **Step 1: Write the script**

Create `scripts/regenerate-baselines.sh` with:

```bash
#!/usr/bin/env bash
#
# Regenerate all Verify scenario baselines inside the deterministic
# linux/amd64 container.
#
# WHAT IT DOES
#   1. Confirms the user is on a clean git working tree (refuses otherwise —
#      a baseline reset must be its own commit).
#   2. Deletes every results_*.verified.json and results_*#page_*.verified.png
#      under src/Tests/Inputs/ (NOT the expected_*.png Word references).
#   3. Runs the test suite via scripts/test.sh — every scenario fails because
#      .verified.* is missing, producing .received.* files.
#   4. Promotes every *.received.* to *.verified.* in place.
#   5. Runs the suite a second time to confirm everything now passes.
#
# Use this only when a rendering change is intentional and the diff has
# been reviewed visually. The result is a large binary commit; review
# carefully.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

if [[ -n "$(git status --porcelain)" ]]; then
    echo "ERROR: working tree is dirty. Commit or stash changes first — a baseline" >&2
    echo "       reset must live in its own commit." >&2
    git status --short >&2
    exit 1
fi

INPUTS_DIR="src/Tests/Inputs"

echo ">>> Removing existing Verify baselines under ${INPUTS_DIR}"
# Match Verify's two file kinds: the per-page PNG snapshots and the JSON
# scenario result. Leave expected_*.png (Word references) alone.
find "$INPUTS_DIR" -name "results_*.verified.png" -delete
find "$INPUTS_DIR" -name "results_*.verified.json" -delete

echo ">>> Running test suite to produce .received.* files (failures are expected)"
./scripts/test.sh || true

RECEIVED_COUNT=$(find "$INPUTS_DIR" -name "*.received.*" | wc -l | tr -d ' ')
echo ">>> Found ${RECEIVED_COUNT} received file(s) to promote"

if [[ "$RECEIVED_COUNT" == "0" ]]; then
    echo "ERROR: no .received.* files produced. The test run failed before Verify" >&2
    echo "       could write them. Check the suite output above." >&2
    exit 1
fi

echo ">>> Promoting *.received.* -> *.verified.*"
find "$INPUTS_DIR" -name "*.received.*" | while IFS= read -r received; do
    verified="${received/.received./.verified.}"
    mv "$received" "$verified"
done

echo ">>> Re-running test suite to confirm baselines are stable"
./scripts/test.sh

echo
echo "Baselines regenerated. Review with:"
echo "   git status"
echo "   git diff --stat"
echo
echo "Then commit in isolation — do NOT mix with code changes."
```

- [ ] **Step 2: Make it executable**

Run: `chmod +x scripts/regenerate-baselines.sh`
Expected: no output.

- [ ] **Step 3: Commit (do not run it yet)**

```bash
git add scripts/regenerate-baselines.sh
git commit -m "feat(test): add baseline regeneration script"
```

---

### Task 9: Phase A sanity check

Final checkpoint before the disruptive Phase B. No new files, just verification.

- [ ] **Step 1: List the new infrastructure**

Run: `git log --oneline -8`
Expected: roughly five new commits — `.dockerignore` + Dockerfile, test.sh wrapper, CI workflow, claude.md docs, regenerate script.

- [ ] **Step 2: Confirm the container still runs**

Run: `./scripts/test.sh dotnet --info | head -5`
Expected: prints `.NET SDK / Version: 10.0.300` (or compatible) and `OS Platform: Linux`. If this fails, fix Phase A before touching baselines.

- [ ] **Step 3: STOP and decide whether to proceed**

Phase A produces a fully-functional container workflow, even though scenario tests will still fail against macOS-generated baselines. Many teams choose to merge Phase A first, get reviewers' sign-off on the infrastructure, and run Phase B as a separate PR. Recommend: open a PR for Phase A now, get it reviewed, then proceed to Task 10 on a follow-up branch.

---

## Phase B — One-time baseline reset

### Task 10: Regenerate every scenario baseline against the container

**Files:**
- Modify (rewrite contents of, in bulk): all `src/Tests/Inputs/**/results_skia.verified.json`
- Modify (rewrite contents of, in bulk): all `src/Tests/Inputs/**/results_imagesharp.verified.json`
- Modify (rewrite contents of, in bulk): all `src/Tests/Inputs/**/results_skia#page_*.verified.png`
- Modify (rewrite contents of, in bulk): all `src/Tests/Inputs/**/results_imagesharp#page_*.verified.png`

This task replaces hundreds of binary baseline files. It must run on a clean working tree and be committed in isolation.

- [ ] **Step 1: Confirm clean working tree**

Run: `git status --short`
Expected: empty output.

- [ ] **Step 2: Run the regeneration script**

Run: `./scripts/regenerate-baselines.sh`
Expected: the script prints its phases, runs the suite twice. The first invocation will fail (intentional, prints "Test failed" lines for every scenario). The second invocation should pass cleanly. Final output: `Baselines regenerated. Review with: ...`.

If the second run still fails for any scenario, **stop**. That means determinism is leaking somewhere (a race, a non-deterministic RNG, an OS-dependent fallback path). Open the failing `*.received.png` next to its `*.verified.png` and diff them. Common culprits to investigate:
- A scenario whose input has a font not present in `src/Fonts/`, causing a SixLabors fallback path.
- A scenario whose conversion involves emoji or RTL text (HarfBuzz shaping has determinism caveats).
- A test that depends on `DateTime.Now` or similar.

Do not commit a half-regenerated state.

- [ ] **Step 3: Inspect the diff scope**

Run: `git status --short | wc -l`
Expected: a large number — a couple thousand files is normal (320 scenarios × 2 backends × ~1–3 pages each + JSON metadata).

Run: `git diff --stat | tail -20`
Expected: most changes are `Bin 12345 -> 12378 bytes` lines on PNG files, plus small text diffs on `*.verified.json` files (the diff metric vs. Word will have shifted slightly).

- [ ] **Step 4: Visually spot-check a handful of scenarios**

Pick 5–10 scenarios at random:

Run: `ls src/Tests/Inputs | shuf | head -5` (or `sort -R` on macOS).

For each, eyeball the new `results_skia#page_0001.verified.png` next to the old one (use `git show HEAD:<path>` or your preferred image diff tool). The new image should look like a slightly different rasterization of the same document — same layout, same line breaks, same content. If layout shifts dramatically (text wrapping differently, missing glyphs, swapped fonts), that's a determinism bug, not just a subpixel reset.

- [ ] **Step 5: Commit the baseline reset in isolation**

```bash
git add src/Tests/Inputs
git commit -m "test: regenerate scenario baselines for linux/amd64 container

One-time baseline reset following the move to running scenario tests
inside a pinned linux/amd64 Docker image. Previous baselines were
captured on macOS Skia/ImageSharp; container output diverges by a few
subpixels per glyph, requiring fresh Verify snapshots.

No converter logic changed. The diff is rasterization drift only."
```

- [ ] **Step 6: Run the suite one more time from scratch to prove it passes**

Run: `./scripts/test.sh`
Expected: clean pass, no failures, no `.received.*` files produced.

Run: `find src/Tests/Inputs -name "*.received.*" | wc -l`
Expected: `0`.

- [ ] **Step 7: Open the PR**

Open a separate PR for this commit. Title it something like `test: regenerate scenario baselines for linux/amd64 container` and link the Phase A PR in the body. The reviewer's job is to confirm the diff looks like subpixel drift, not layout drift — point them at the visual spot-check pattern from Step 4.

---

## Self-Review

**Spec coverage check:**
- Containerization scoped to Skia + ImageSharp scenario tests + spec tests + static-setting tests → covered (Tasks 4–5, 9 cover all three projects).
- Single linux/amd64 image strategy → covered (Task 2, `--platform=linux/amd64` pinned in Dockerfile, wrapper, and workflow).
- Dockerfile → Task 2.
- Test wrapper script → Task 3.
- Baseline regeneration → Tasks 8 + 10, split so the script is reviewable independently of its first invocation.
- CI workflow → Task 6, uses the same Dockerfile so local and CI cannot diverge.
- claude.md updates → Task 7.

**Placeholder scan:** No `TBD`, no "implement later", no "similar to". The Edit in Task 7 is described concretely with fallback strategy if matching is awkward; not a placeholder, but flagged as a judgment call.

**Type / interface consistency:** Image tag `morph-test:latest` is used identically in `Dockerfile.test`'s tag in CI, in `scripts/test.sh`, and in the workflow's `tags:`. Env var `MORPH_TEST_IMAGE` / `MORPH_REBUILD` / `MORPH_NUGET_CACHE` are only consumed in `test.sh` and don't appear elsewhere — fine. Volume mount path `/src` matches the `WORKDIR` in the Dockerfile and the `-w /src` in the wrapper.

**Known caveats the executor should re-verify:**
- The SDK image tag `mcr.microsoft.com/dotnet/sdk:10.0` may need to be `10.0-preview` depending on Microsoft's tagging when this runs. Task 2 Step 2 catches this with a fallback note.
- ImageSharp determinism inside the container is "by-virtue-of-running-the-same-binary" rather than "by-explicit-config". If a future ImageSharp upgrade introduces non-determinism (parallel rasterization with non-deterministic reduction, for example), the baseline reset in Task 10 will keep producing different `.received.png` on re-runs. That would surface in Task 10 Step 2 and the executor should stop.
