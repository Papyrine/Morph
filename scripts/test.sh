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
# - The working tree is mounted at /src. By default the run happens against a
#   container-local COPY of it and changed files are synced back afterwards, because a
#   Windows host exposes the mount over 9p/drvfs and that halves the suite's speed
#   (4m34s -> 2m15s measured; see scripts/container-run.sh for the numbers and for what
#   gets synced back). Set MORPH_DIRECT=1 to work in the mounted tree instead — the old
#   behaviour, and the right one on a Linux host where the mount is already native.
# - NuGet packages are cached between runs to skip restore: in ./.nuget-cache on
#   macOS/Linux, or a Docker named volume (morph-nuget-cache) on Windows, where a
#   host bind mount hits MAX_PATH. Reset with `rm -rf ./.nuget-cache` or
#   `docker volume rm morph-nuget-cache` respectively.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE_TAG="${MORPH_TEST_IMAGE:-morph-test:latest}"
NUGET_CACHE="${MORPH_NUGET_CACHE:-${REPO_ROOT}/.nuget-cache}"

# docker.exe wants different path styles for host vs container arguments. On Git Bash /
# MSYS2 (Windows) it's a native Windows binary: the build context and bind-mount *sources*
# must be Windows-style paths (docker can't read a POSIX path like /d/Code/...), while
# *container* paths (/src, /nuget, -w /src) must NOT be POSIX-converted (else -w /src
# becomes C:/Program Files/Git/src). So translate the host paths with cygpath and disable
# MSYS's automatic argument mangling. On Linux/macOS cygpath is absent, the host paths are
# used unchanged, and the exported variable is a harmless no-op.
HOST_ROOT="$REPO_ROOT"
NUGET_MOUNT="$NUGET_CACHE"
if command -v cygpath >/dev/null 2>&1; then
    export MSYS_NO_PATHCONV=1
    HOST_ROOT="$(cygpath -m "$REPO_ROOT")"
    # On Windows a host bind mount for the NuGet cache intermittently loses packages whose
    # extracted paths exceed MAX_PATH (260): NuGet reports NETSDK1064 "package ... was not
    # found ... maximum path length restrictions". Use a Docker-managed named volume (Linux
    # ext4 inside the VM) instead — reliable and still persistent across runs.
    NUGET_MOUNT="${MORPH_NUGET_VOLUME:-morph-nuget-cache}"
else
    mkdir -p "$NUGET_CACHE"
fi

# Build the image if it doesn't exist locally. Force a rebuild by passing
# MORPH_REBUILD=1 (used by CI and after editing Dockerfile.test).
if [[ "${MORPH_REBUILD:-0}" == "1" ]] || ! docker image inspect "$IMAGE_TAG" >/dev/null 2>&1; then
    # The image's Playwright browser layer is keyed on this version alone, so bumping an
    # unrelated package no longer invalidates it and re-downloads ~180MB of Chromium.
    # src/Directory.Packages.props stays the single source of truth; it is read here rather
    # than COPYed into the image (see Dockerfile.test).
    PLAYWRIGHT_VERSION="$(sed -n 's/.*Microsoft\.Playwright" Version="\([^"]*\)".*/\1/p' "${REPO_ROOT}/src/Directory.Packages.props")"
    if [[ -z "$PLAYWRIGHT_VERSION" ]]; then
        echo "Could not read the Microsoft.Playwright version from src/Directory.Packages.props" >&2
        exit 1
    fi

    echo ">>> Building ${IMAGE_TAG} (Playwright ${PLAYWRIGHT_VERSION})" >&2
    docker build \
        --platform=linux/amd64 \
        -f "${HOST_ROOT}/Dockerfile.test" \
        --build-arg "PLAYWRIGHT_VERSION=${PLAYWRIGHT_VERSION}" \
        -t "$IMAGE_TAG" \
        "$HOST_ROOT"
fi

# If args were provided, use them verbatim. Otherwise let the image's
# CMD (full test suite) run.
#
# SponsorCheck is intentionally not referenced inside the container (the
# csproj references are gated on MORPH_TEST_CONTAINER, set in the image), so no
# GitHub credential is forwarded — the in-container build only compiles and
# tests, it never packs.
#
# An interactive shell wants the live tree (and its .git), not a copy whose edits only
# reappear on exit, so `bash`/`sh` opt out of the container-local copy automatically.
DIRECT="${MORPH_DIRECT:-0}"
case "${1:-}" in
    bash|sh) DIRECT=1 ;;
esac

if [[ "$DIRECT" == "1" ]]; then
    docker run \
        --rm \
        --init \
        --platform=linux/amd64 \
        -v "${HOST_ROOT}:/src" \
        -v "${NUGET_MOUNT}:/nuget" \
        -w /src \
        "$IMAGE_TAG" \
        "$@"
else
    # Invoked through `bash` rather than as an executable: the exec bit does not survive a
    # clone on a Windows filesystem.
    docker run \
        --rm \
        --init \
        --platform=linux/amd64 \
        -v "${HOST_ROOT}:/src" \
        -v "${NUGET_MOUNT}:/nuget" \
        -w /src \
        "$IMAGE_TAG" \
        bash /src/scripts/container-run.sh "$@"
fi
