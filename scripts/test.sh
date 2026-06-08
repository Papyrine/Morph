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

# Build the image if it doesn't exist locally. Force a rebuild by passing
# MORPH_REBUILD=1 (used by CI and after editing Dockerfile.test).
if [[ "${MORPH_REBUILD:-0}" == "1" ]] || ! docker image inspect "$IMAGE_TAG" >/dev/null 2>&1; then
    echo ">>> Building ${IMAGE_TAG}" >&2
    docker build \
        --platform=linux/amd64 \
        -f "${REPO_ROOT}/Dockerfile.test" \
        -t "$IMAGE_TAG" \
        "$REPO_ROOT"
fi

mkdir -p "$NUGET_CACHE"

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
    -v "${REPO_ROOT}:/src" \
    -v "${NUGET_CACHE}:/nuget" \
    -w /src \
    -e GitHubToken \
    "$IMAGE_TAG" \
    "$@"
