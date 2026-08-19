#!/usr/bin/env bash
#
# In-container micro-benchmarks. Isolates the things a VMM swap can actually change:
# bind-mount walk / read / write, the real rsync-in, container-local ext4 (control),
# and pure CPU (control). Prints one "RESULT<TAB>name<TAB>seconds" line per measurement.
#
# Controls matter as much as the treatments: if m_local_read or m_cpu move between
# backends, the run was not comparable and the bind-mount deltas mean nothing.

set -uo pipefail

REPS="${REPS:-3}"
src=/src
work=/work

now() { date +%s.%N; }
emit() { printf 'RESULT\t%s\t%s\n' "$1" "$2"; }
# Neither Git Bash nor this image ships bc; awk is in both.
dur() { awk -v a="$1" -v b="$2" 'BEGIN{printf "%.3f", b-a}'; }

# Median of REPS timings of a command, emitted as one RESULT line.
bench() {
    local name=$1; shift
    local times=()
    for _ in $(seq 1 "$REPS"); do
        local t0 t1
        t0=$(now); "$@" >/dev/null 2>&1; t1=$(now)
        times+=("$(dur "$t0" "$t1")")
    done
    local med
    med=$(printf '%s\n' "${times[@]}" | sort -g | awk '{a[NR]=$1} END{print (NR%2)?a[(NR+1)/2]:(a[NR/2]+a[NR/2+1])/2}')
    emit "$name" "$med"
    printf '    %s: %s\n' "$name" "$(printf '%s ' "${times[@]}")" >&2
}

echo "=== environment as the VM sees it ===" >&2
emit env_nproc          "$(nproc)"
emit env_mem_total_mib  "$(awk '/MemTotal/{printf "%.0f", $2/1024}' /proc/meminfo)"
emit env_kernel         "$(uname -r)"
# stat -f reports UNKNOWN for virtiofs, so take the name from /proc/mounts and keep
# stat's answer only as a fallback. This row is the mechanism under test: WSL 2 mounts
# the host tree over v9fs, Docker VMM over virtiofs.
emit env_fstype_src     "$(awk '$2=="/src"{print $3}' /proc/mounts | head -1)"
emit env_fstype_work    "$(awk '$2=="/work"{print $3}' /proc/mounts | head -1)"
emit env_statfs_src     "$(stat -f -c %T "$src")"

# ---------------------------------------------------------------- fixed workloads
# A pinned file list keeps the read benchmarks identical across backends and runs,
# rather than whatever a glob happens to expand to after a suite run rewrote things.
list=/tmp/pngs.txt
find "$src/src/Tests/Inputs" -name 'expected_*.png' 2>/dev/null | sort | head -300 > "$list"
bytes=$(du -cb $(cat "$list") 2>/dev/null | tail -1 | cut -f1)
emit workload_png_count "$(wc -l < "$list")"
emit workload_png_bytes "${bytes:-0}"

# Container-local copies of the same bytes, for the ext4 control.
localdir=/tmp/pngs-local
mkdir -p "$localdir"
tar cf - -T "$list" 2>/dev/null | tar xf - -C "$localdir" 2>/dev/null

read_list()  { xargs -a "$list" cat; }
read_local() { find "$localdir" -type f -print0 | xargs -0 cat; }
walk_src()   { find "$src" -type f -not -path "$src/.git/*" -not -path '*/bin/*' -not -path '*/obj/*'; }
stat_src()   { xargs -a "$list" stat -c %s; }

cpu_control() { head -c 400000000 /dev/zero | sha256sum; }

write_src() {
    local d="$src/.bench-scratch"
    rm -rf "$d"; mkdir -p "$d"
    for i in $(seq 1 500); do echo "bench $i" > "$d/f$i.txt"; done
    sync
    rm -rf "$d"
}

write_local() {
    local d=/tmp/bench-scratch
    rm -rf "$d"; mkdir -p "$d"
    for i in $(seq 1 500); do echo "bench $i" > "$d/f$i.txt"; done
    sync
    rm -rf "$d"
}

rsync_in() {
    rsync -a --delete \
        --exclude='/.git/' --exclude='/.nuget-cache/' \
        --exclude='bin/' --exclude='obj/' \
        "$src/" "$work/"
}

echo "=== micro-benchmarks (REPS=$REPS, median reported) ===" >&2

# Treatments: everything that crosses the host<->VM filesystem boundary.
bench mount_walk        walk_src
bench mount_stat_300    stat_src
bench mount_read_300    read_list
bench mount_write_500   write_src
bench mount_rsync_in    rsync_in

# Controls: must NOT move between backends.
bench local_read_300    read_local
bench local_write_500   write_local
bench cpu_sha256_400mb  cpu_control

echo "=== done ===" >&2
