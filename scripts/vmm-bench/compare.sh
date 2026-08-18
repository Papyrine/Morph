#!/usr/bin/env bash
#
#   ./compare.sh <baseline-label> <treatment-label>
#
# Diffs two bench.sh TSVs into one table. Rows are grouped so the controls sit together
# and can be read first: if local_read/local_write/cpu moved, the two runs were not
# comparable and no other row means anything.

set -uo pipefail

A="${1:?usage: compare.sh <baseline> <treatment>}"
B="${2:?usage: compare.sh <baseline> <treatment>}"
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

awk -F'\t' -v la="$A" -v lb="$B" '
    FNR==NR { a[$1]=$2; next }
            { b[$1]=$2 }
    END {
        # Ordered so the reader hits controls and environment before any conclusion.
        n=split("ENV|micro_env_nproc|micro_env_mem_total_mib|micro_env_kernel|micro_env_fstype_src|micro_env_fstype_work|micro_env_statfs_src|dd_backend_settings|" \
                "CONTROLS (must not move)|micro_local_read_300|micro_local_write_500|micro_cpu_sha256_400mb|" \
                "STARTUP|container_start_median|" \
                "BIND MOUNT|micro_mount_walk|micro_mount_stat_300|micro_mount_read_300|micro_mount_write_500|micro_mount_rsync_in|" \
                "SUITE|narrow_median|full_median|direct_full_median|" \
                "ONE-OFF SWITCHING COST|cold_image_build_or_noop|cold_work_sync_and_build|cold_work_sync_and_build_warmnuget|image_build_switching_cost", k, "|")

        printf "%-30s %14s %14s %10s  %s\n", "measurement", la, lb, "delta", "ratio"
        printf "%-30s %14s %14s %10s  %s\n", "------------------------------", "--------------", "--------------", "----------", "-----"
        for (i=1; i<=n; i++) {
            key=k[i]
            if (!(key in a) && !(key in b)) {
                if (key ~ /^[A-Z]/) { printf "\n%s\n", key }   # section header
                continue
            }
            va=(key in a)?a[key]:"-"; vb=(key in b)?b[key]:"-"
            if (va+0>0 && vb+0>0 && va ~ /^[0-9.]+$/ && vb ~ /^[0-9.]+$/)
                printf "%-30s %14s %14s %+10.3f  %.2fx\n", key, va, vb, vb-va, vb/va
            else
                printf "%-30s %14s %14s %10s  %s\n", key, va, vb, "", ""
        }
    }
' "$DIR/results/$A.tsv" "$DIR/results/$B.tsv"
