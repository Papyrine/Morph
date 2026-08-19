#!/usr/bin/env bash
#
#   ./run-micro.sh <label>
#
# Runs just the micro-benchmark phase and appends micro_* rows to <label>.tsv.
#
# The script is piped in over stdin rather than bind-mounted from a /bench directory.
# Docker VMM shares no host path unless it is listed in Settings > Resources > File
# sharing, so mounting the scratchpad failed outright ("is not shared from the host")
# where WSL 2's auto-shares had made it work. stdin needs no share at all and behaves
# identically on both backends, which is what makes the two sides comparable.

set -uo pipefail

LABEL="${1:?usage: run-micro.sh <label>}"
OUT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mkdir -p "$OUT/results"
REPO_ROOT="${MORPH_REPO:-$(cd "$OUT/../.." && pwd)}"
IMAGE_TAG="${MORPH_TEST_IMAGE:-morph-test:latest}"

sha16() { if command -v sha256sum >/dev/null 2>&1; then sha256sum; else shasum -a 256; fi | cut -c1-16; }
HOST_ROOT="$REPO_ROOT"
NUGET_MOUNT="${REPO_ROOT}/.nuget-cache"
if command -v cygpath >/dev/null 2>&1; then
    export MSYS_NO_PATHCONV=1
    HOST_ROOT="$(cygpath -m "$REPO_ROOT")"
    NUGET_MOUNT="morph-nuget-cache"
fi
WORK_VOLUME="morph-work-$(printf '%s' "$HOST_ROOT" | sha16)"

echo "micro: label=$LABEL work=$WORK_VOLUME"

docker run --rm --init -i --platform=linux/amd64 \
    -v "${HOST_ROOT}:/src" \
    -v "${NUGET_MOUNT}:/nuget" \
    -v "${WORK_VOLUME}:/work" \
    -w /src \
    -e REPS="${MICRO_REPS:-3}" \
    "$IMAGE_TAG" bash -s < "$OUT/micro.sh" > "$OUT/results/${LABEL}.micro.txt" 2>"$OUT/results/${LABEL}.micro.err"

# Drop any stale micro_* rows so a re-run replaces rather than duplicates them.
if [[ -f "$OUT/results/${LABEL}.tsv" ]]; then
    grep -v '^micro_' "$OUT/results/${LABEL}.tsv" > "$OUT/results/${LABEL}.tsv.tmp" && mv "$OUT/results/${LABEL}.tsv.tmp" "$OUT/results/${LABEL}.tsv"
fi

awk -F'\t' '$1=="RESULT"{print "micro_" $2 "\t" $3}' "$OUT/results/${LABEL}.micro.txt" | tee -a "$OUT/results/${LABEL}.tsv"

rows=$(grep -c '^micro_' "$OUT/results/${LABEL}.tsv" 2>/dev/null || echo 0)
echo "micro rows recorded: $rows"
[[ "$rows" -eq 0 ]] && { echo "FAILED — stderr:"; tail -5 "$OUT/results/${LABEL}.micro.err"; exit 1; }
exit 0
