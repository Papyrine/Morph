# Backend benchmark harness

Measures what a Docker Desktop VMM backend costs the container test loop. Built to answer
"is Docker VMM worth switching to on Windows"; it is not VMM-specific, and works for any
A/B where the variable is the daemon underneath (a Docker Desktop upgrade, WSL 2 vs
Hyper-V, a `.wslconfig` change).

The measured answer for Docker VMM vs WSL 2 lives in `CLAUDE.md` under
*Windows: Docker VMM vs the WSL 2 backend*. The raw runs behind it are in `results/`.

## Running it

One run per backend, then diff them:

```bash
./scripts/vmm-bench/bench.sh wsl2 full        # on the WSL 2 backend
# ... switch the backend in Docker Desktop, then ...
./scripts/vmm-bench/bench.sh dockervmm full   # on Docker VMM
./scripts/vmm-bench/compare.sh wsl2 dockervmm
```

`full` is roughly 25 minutes per backend; `quick` drops to one full-suite sample and skips
`MORPH_DIRECT`, about 12. `./scripts/vmm-bench/run-micro.sh <label>` re-runs just the
micro-benchmarks, which take about a minute and carry most of the signal per second spent.

Results land in `results/<label>.tsv`, with the console output of every command in
`results/<label>.log` so a surprising number can be traced back to what produced it.

## Reading the output

**Check the controls first.** `micro_local_read_300`, `micro_local_write_500` and
`micro_cpu_sha256_400mb` all run on container-local ext4 or pure CPU, so a backend swap
must not move them. If they moved, the two runs were not comparable and no other row means
anything. Everything under `BIND MOUNT` is the treatment — that is the only surface a VMM
swap actually changes.

`ENV` rows are recorded because they are the confounds that bite:

- **`micro_env_mem_total_mib`** — what the VM really got. WSL 2 sizes itself from
  `.wslconfig` (absent: about half the host), while Docker VMM and Hyper-V take
  Settings > Resources > Memory, which defaults to 2 GB. Comparing those two defaults
  measures RAM starvation, not the hypervisor. Match them before believing a run.
- **`micro_env_fstype_src`** — `v9fs` under WSL 2, `virtiofs` under Docker VMM. This is
  the mechanism under test.
- **`dd_backend_settings`** — read live from Docker Desktop's settings API rather than
  trusted from the label, because a run labelled `dockervmm` that is still on WSL 2 is the
  one failure mode that silently invalidates everything. `useLibkrun` is the field that
  discriminates; `wslEngineEnabled` still reads `true` under Docker VMM and means nothing.

## Gotchas this harness already worked around

- **Docker VMM shares no host path unless it is listed** in Settings > Resources > File
  sharing. `micro.sh` is therefore piped in over stdin rather than bind-mounted from a
  second directory, which is what `run-micro.sh` exists for.
- **The PowerShell helpers are files, not `-Command` strings.** Windows PowerShell 5.1
  mis-parses the named-pipe constructor when the script arrives as one quoted argument
  from Git Bash.
- **Neither Git Bash nor the test image ships `bc`**, so all arithmetic is `awk`.
- **A fresh VM disk empties the NuGet cache volume**, and the first build then pays a
  2.1 GB restore. That contaminates the cold rows on whichever side was switched *to*;
  re-measure them once the cache is warm before comparing.

## Files

| | |
| --- | --- |
| `bench.sh` | the harness — env, cold costs, warmup, micro, narrow/full/direct suite runs |
| `micro.sh` | runs inside the container; bind-mount treatments vs ext4 and CPU controls |
| `run-micro.sh` | the micro phase alone, delivered over stdin |
| `compare.sh` | diffs two result TSVs into one table, controls first |
| `probe-backend.ps1` | reads the live backend/memory/cpu settings from Docker Desktop |
| `dump-settings.ps1` | dumps Docker Desktop's full settings JSON, for diagnosis |
