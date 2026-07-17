#!/usr/bin/env bash
#
# Regenerate all Verify scenario baselines inside the deterministic
# linux/amd64 container.
#
# WHAT IT DOES
#   1. Deletes every *.verified.* Verify snapshot under src/Tests/ — the Skia and
#      ImageSharp scenario page PNGs/JSON and export snapshots under Inputs/, plus the
#      spec-test and sample snapshots that live alongside the test sources. Build output
#      (bin/, obj/) is skipped, as are the expected_*.png Word references (no .verified.
#      infix).
#   2. Runs the test suite via scripts/test.sh — every scenario fails because
#      .verified.* is missing, producing .received.* files.
#   3. Promotes every *.received.* to *.verified.* in place.
#   4. Runs the suite a second time to confirm everything now passes.
#
# Use this only when a rendering change is intentional and the diff has
# been reviewed visually. The result is a large binary commit; review
# carefully.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

TESTS_DIR="src/Tests"

# Verify snapshots carry the ".verified." infix and live both under src/Tests/Inputs/ (the
# Skia/ImageSharp scenario page PNGs + result JSON and the HTML/Markdown/PDF export snapshots)
# AND alongside the test sources elsewhere under src/Tests/ (spec-test and sample snapshots).
# Match both; skip build output. The Word references (expected_*.png) have no ".verified."
# infix, so they are never matched.
snapshots() {  # $1 = infix glob, e.g. '*.verified.*'
    find "$TESTS_DIR" -type f -not -path "*/bin/*" -not -path "*/obj/*" -name "$1"
}

echo ">>> Removing existing Verify baselines under ${TESTS_DIR}"
snapshots '*.verified.*' | while IFS= read -r verified; do
    rm -f "$verified"
done

echo ">>> Running test suite to produce .received.* files (failures are expected)"
./scripts/test.sh || true

RECEIVED_COUNT=$(snapshots '*.received.*' | wc -l | tr -d ' ')
echo ">>> Found ${RECEIVED_COUNT} received file(s) to promote"

if [[ "$RECEIVED_COUNT" == "0" ]]; then
    echo "ERROR: no .received.* files produced. The test run failed before Verify" >&2
    echo "       could write them. Check the suite output above." >&2
    exit 1
fi

echo ">>> Promoting *.received.* -> *.verified.*"
snapshots '*.received.*' | while IFS= read -r received; do
    verified="${received/.received./.verified.}"
    mv "$received" "$verified"
done

echo ">>> Re-running test suite to confirm baselines are stable"
./scripts/test.sh

echo
echo "Baselines regenerated. Review with:"
echo "   git status"
echo "   git diff --stat"
