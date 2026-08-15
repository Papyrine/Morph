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

# TUnit reports a failure as "failed <TestName> (<duration>)" followed by the exception on the
# next line. Print the names of failures that are NOT Verify snapshot mismatches.
#
# That distinction is the whole point. In pass 1 every snapshot mismatch is expected — the
# baselines were just deleted — so a real defect failing among them is invisible, and in pass 2 a
# mismatch cannot happen at all. A non-Verify failure is an assertion or an exception, and those
# leave no .received.* file behind, so nothing on disk records them either: once the run's output
# scrolls past, the only evidence is the failure count. That is how an intermittent 2-test failure
# in a confirming run went unidentified (2026-08-15) despite the names being in the log at the time.
#
# VerifiedMarkdownSnapshotsKeepTheirBom is excluded from the FIRST pass only, where it always
# fails and correctly so: step 1 has just deleted every *.verified.md, and that guard asserts the
# set is non-empty precisely so a moved glob or search root cannot pass vacuously. It cannot tell
# "deleted on purpose a moment ago" from "the glob broke", and should not try — it is a guard for
# the committed tree. It is NOT excluded from the confirming pass, where the snapshots are back.
unexpected_failures() {  # $1 = log file, $2 = "first" to allow the regeneration-window failure
    local ignore='^$'
    if [[ "${2:-}" == "first" ]]; then
        ignore='VerifiedMarkdownSnapshotsKeepTheirBom'
    fi
    awk -v ignore="$ignore" \
        '/^failed /{name=$0; if ((getline detail) > 0 && detail !~ /VerifyException/ && name !~ ignore) print "   " name}' \
        "$1"
}

# Run the suite, streaming as usual but keeping the output so failures can be named afterwards.
# Returns the suite's exit status; the log path is left in RUN_LOG.
run_suite() {  # $1 = description
    RUN_LOG="$(mktemp -t morph-regen-XXXXXX.log)"
    local status=0
    ./scripts/test.sh 2>&1 | tee "$RUN_LOG" || status=$?
    return "$status"
}

echo ">>> Removing existing Verify baselines under ${TESTS_DIR}"
snapshots '*.verified.*' | while IFS= read -r verified; do
    rm -f "$verified"
done

echo ">>> Running test suite to produce .received.* files (failures are expected)"
run_suite || true
FIRST_LOG="$RUN_LOG"

FIRST_UNEXPECTED="$(unexpected_failures "$FIRST_LOG" first)"
if [[ -n "$FIRST_UNEXPECTED" ]]; then
    echo
    echo ">>> NOTE: the first pass had failures that are NOT snapshot mismatches." >&2
    echo "$FIRST_UNEXPECTED" >&2
    echo "    These produce no .received.* file, so they promote nothing and are easy to miss" >&2
    echo "    among the expected mismatches. Full output: ${FIRST_LOG}" >&2
    echo
else
    rm -f "$FIRST_LOG"
fi

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
if ! run_suite; then
    CONFIRM_LOG="$RUN_LOG"
    echo
    echo ">>> The confirming run FAILED. Every baseline was just promoted, so a snapshot" >&2
    echo "    mismatch is impossible here — these are assertions or exceptions:" >&2
    echo
    unexpected_failures "$CONFIRM_LOG" >&2 ||
        echo "   (none matched the expected shape — read the log)" >&2
    echo
    echo "    Full output retained at: ${CONFIRM_LOG}" >&2
    exit 1
fi
rm -f "$RUN_LOG"

echo
echo "Baselines regenerated. Review with:"
echo "   git status"
echo "   git diff --stat"
