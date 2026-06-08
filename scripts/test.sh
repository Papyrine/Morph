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

mkdir -p "$NUGET_CACHE"

# docker.exe wants different path styles for host vs container arguments. On Git Bash /
# MSYS2 (Windows) it's a native Windows binary: the build context and bind-mount *sources*
# must be Windows-style paths (docker can't read a POSIX path like /d/Code/...), while
# *container* paths (/src, /nuget, -w /src) must NOT be POSIX-converted (else -w /src
# becomes C:/Program Files/Git/src). So translate the host paths with cygpath and disable
# MSYS's automatic argument mangling. On Linux/macOS cygpath is absent, the host paths are
# used unchanged, and the exported variable is a harmless no-op.
HOST_ROOT="$REPO_ROOT"
HOST_NUGET="$NUGET_CACHE"
if command -v cygpath >/dev/null 2>&1; then
    export MSYS_NO_PATHCONV=1
    HOST_ROOT="$(cygpath -m "$REPO_ROOT")"
    HOST_NUGET="$(cygpath -m "$NUGET_CACHE")"
fi

# Build the image if it doesn't exist locally. Force a rebuild by passing
# MORPH_REBUILD=1 (used by CI and after editing Dockerfile.test).
if [[ "${MORPH_REBUILD:-0}" == "1" ]] || ! docker image inspect "$IMAGE_TAG" >/dev/null 2>&1; then
    echo ">>> Building ${IMAGE_TAG}" >&2
    docker build \
        --platform=linux/amd64 \
        -f "${HOST_ROOT}/Dockerfile.test" \
        -t "$IMAGE_TAG" \
        "$HOST_ROOT"
fi

# If args were provided, use them verbatim. Otherwise let the image's
# CMD (full test suite) run.
#
# GitHubToken is forwarded (by name, so the value never appears on the command
# line) so the in-container solution build can satisfy SponsorCheck's GitHub
# Sponsors lookup; without it the build fails with SC102 (missing credential).
docker run \
    --rm \
    --init \
    --platform=linux/amd64 \
    -v "${HOST_ROOT}:/src" \
    -v "${HOST_NUGET}:/nuget" \
    -w /src \
    -e GitHubToken \
    "$IMAGE_TAG" \
    "$@"
