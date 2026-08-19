#!/usr/bin/env bash
#
# Measure the Morph container test loop under one Docker Desktop VMM backend.
#
#   ./bench.sh <label> [quick|full]
#
# Run it once per backend (e.g. `wsl2` then `dockervmm`) and diff the two TSVs. Every
# measurement lands in $OUT/<label>.tsv as "name<TAB>seconds"; the full console log of
# every command lands in $OUT/<label>.log so a surprising number can be traced back.
#
# Phases, and why each is here:
#   env       what the VM actually got (cores, RAM, kernel, fs types) -- the memory
#             allocation differs per backend and would otherwise silently confound
#             everything downstream
#   cold      image build + fresh /work volume + cold build: what the switch costs once
#   warmup    excluded from results; pays resource-saver resume and first-build costs so
#             the timed runs are all steady-state
#   micro     bind-mount walk/read/write/rsync (treatments) vs ext4 + CPU (controls)
#   narrow    the 40-test iteration loop the repo optimises for (~15.5s on record)
#   full      the headline suite (~2m15s on record)
#   direct    MORPH_DIRECT=1: the suite against the bind mount with no copy (~4m34s on
#             record). This is the phase that decides whether the copy machinery in
#             scripts/container-run.sh still earns its keep.

set -uo pipefail

LABEL="${1:?usage: bench.sh <label> [quick|full]}"
MODE="${2:-full}"
OUT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="${MORPH_REPO:-$(cd "$OUT/../.." && pwd)}"
RESULTS="$OUT/results"
mkdir -p "$RESULTS"
TSV="$RESULTS/${LABEL}.tsv"
LOG="$RESULTS/${LABEL}.log"

case "$MODE" in
    quick) NARROW_REPS=3; FULL_REPS=1; DIRECT_REPS=0 ;;
    full)  NARROW_REPS=3; FULL_REPS=3; DIRECT_REPS=1 ;;
    *) echo "mode must be quick or full" >&2; exit 1 ;;
esac

: > "$TSV"
: > "$LOG"

now()  { date +%s.%N; }
# Neither Git Bash nor the test image ships bc; awk is in both.
dur()  { awk -v a="$1" -v b="$2" 'BEGIN{printf "%.3f", b-a}'; }
rec()  { printf '%s\t%s\n' "$1" "$2" >> "$TSV"; printf '  %-26s %s\n' "$1" "$2"; }
note() { printf '\n=== %s ===\n' "$1" | tee -a "$LOG"; }

# Time a command, append its output to the log, record the elapsed seconds.
timed() {
    local name=$1; shift
    local t0 t1 rc
    printf '\n--- %s: %s\n' "$name" "$*" >> "$LOG"
    t0=$(now)
    "$@" >> "$LOG" 2>&1
    rc=$?
    t1=$(now)
    rec "$name" "$(dur "$t0" "$t1")"
    [[ $rc -ne 0 ]] && rec "${name}_exit" "$rc"
    return 0
}

# Repeat a timed command, then record the median under <name>_median.
timed_reps() {
    local name=$1 reps=$2; shift 2
    [[ $reps -eq 0 ]] && return 0
    local times=()
    for i in $(seq 1 "$reps"); do
        local t0 t1
        printf '\n--- %s rep %s: %s\n' "$name" "$i" "$*" >> "$LOG"
        t0=$(now); "$@" >> "$LOG" 2>&1; t1=$(now)
        local d; d=$(dur "$t0" "$t1")
        times+=("$d")
        rec "${name}_r${i}" "$d"
    done
    local med
    med=$(printf '%s\n' "${times[@]}" | sort -g | awk '{a[NR]=$1} END{print (NR%2)?a[(NR+1)/2]:(a[NR/2]+a[NR/2+1])/2}')
    rec "${name}_median" "$med"
}

# ------------------------------------------------------------------ mount plumbing
# Derived exactly as scripts/test.sh does, so the micro-benchmarks hit the very same
# named volumes the real runs use rather than a fresh one that would measure cold I/O.
sha16() { if command -v sha256sum >/dev/null 2>&1; then sha256sum; else shasum -a 256; fi | cut -c1-16; }
HOST_ROOT="$REPO_ROOT"
NUGET_MOUNT="${REPO_ROOT}/.nuget-cache"
if command -v cygpath >/dev/null 2>&1; then
    export MSYS_NO_PATHCONV=1
    HOST_ROOT="$(cygpath -m "$REPO_ROOT")"
    NUGET_MOUNT="morph-nuget-cache"
fi
WORK_VOLUME="morph-work-$(printf '%s' "$HOST_ROOT" | sha16)"
IMAGE_TAG="${MORPH_TEST_IMAGE:-morph-test:latest}"

NARROW=(dotnet run --project src/Tests --configuration Release --
        --treenode-filter "/*/*/HtmlExporterTests/*")

cd "$REPO_ROOT" || exit 1

echo "label=$LABEL mode=$MODE repo=$REPO_ROOT" | tee -a "$LOG"
echo "work volume: $WORK_VOLUME" | tee -a "$LOG"

# ------------------------------------------------------------------------- env
note "environment"
rec label "$LABEL"
rec host_time "$(date -Is)"
rec git_head "$(git rev-parse --short HEAD)"
rec git_dirty_before "$(git status --porcelain | wc -l)"
rec dd_version "$(docker version --format '{{.Server.Platform.Name}}' 2>/dev/null | tr ' ' '_')"
{ docker version; docker info; } >> "$LOG" 2>&1

# The backend Docker Desktop is actually running, read from its own settings API rather
# than assumed from the label -- a mislabelled run is the one failure mode that would
# quietly invalidate the whole comparison.
BACKEND="$(powershell.exe -NoProfile -File "$(cygpath -w "$OUT/probe-backend.ps1")" 2>/dev/null | tr -d '\r' | tail -1)"
rec dd_backend_settings "${BACKEND:-unknown}"

# --------------------------------------------------------------------- cold costs
note "cold costs (image + fresh work volume)"
if [[ "${BENCH_COLD:-1}" == "1" ]]; then
    docker volume rm "$WORK_VOLUME" >> "$LOG" 2>&1
    timed cold_image_build_or_noop bash scripts/test.sh dotnet --version
    timed cold_work_sync_and_build bash scripts/test.sh "${NARROW[@]}"
fi

# ------------------------------------------------------------------------ warmup
note "warmup (not counted)"
timed warmup_narrow bash scripts/test.sh "${NARROW[@]}"

# ------------------------------------------------------------------- container start
note "container start latency"
timed_reps container_start 5 docker run --rm --platform=linux/amd64 "$IMAGE_TAG" true

# ------------------------------------------------------------------------- micro
note "micro-benchmarks"
# Delegated so there is exactly one copy of the stdin-piping trick. Mounting a second
# host path to deliver the script fails outright under Docker VMM, which shares nothing
# that is not listed in Settings > Resources > File sharing.
MICRO_REPS="${MICRO_REPS:-3}" bash "$OUT/run-micro.sh" "$LABEL" >> "$LOG" 2>&1
grep '^micro_' "$TSV" | while IFS=$'\t' read -r n v; do printf '  %-26s %s\n' "$n" "$v"; done

# ------------------------------------------------------------------------- macro
note "narrow run (40 tests) x $NARROW_REPS"
timed_reps narrow "$NARROW_REPS" bash scripts/test.sh "${NARROW[@]}"

note "full suite x $FULL_REPS"
timed_reps full "$FULL_REPS" bash scripts/test.sh

if [[ $DIRECT_REPS -gt 0 ]]; then
    note "full suite, MORPH_DIRECT=1 (no copy, straight off the bind mount) x $DIRECT_REPS"
    timed_reps direct_full "$DIRECT_REPS" env MORPH_DIRECT=1 bash scripts/test.sh
fi

# ------------------------------------------------------------------------ cleanup
note "cleanup"
rec git_dirty_after "$(git status --porcelain | wc -l)"
git status --porcelain >> "$LOG" 2>&1
rm -rf "$REPO_ROOT/.bench-scratch"
# The suite regenerates tracked files (compare.md, compare-all-images.md); put them back
# so the next backend starts from an identical tree. Only ever done when the tree was
# already clean when we started -- otherwise this would discard the user's own edits.
if [[ "$(grep -c '^git_dirty_before\t0$' "$TSV")" == "1" ]]; then
    git checkout -- . >> "$LOG" 2>&1
    rec git_dirty_restored "$(git status --porcelain | wc -l)"
else
    rec git_dirty_restored "skipped_tree_was_dirty_at_start"
fi

echo
echo "results: $TSV"
echo "log:     $LOG"
