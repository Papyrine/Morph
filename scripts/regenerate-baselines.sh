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
