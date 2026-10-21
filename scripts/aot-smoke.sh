#!/usr/bin/env bash
#
# Publish src/AotSmoke trimmed for this machine and convert corpus documents with the result.
#
# WHAT IT CHECKS
#   1. The trimmed publish succeeds. The app is self-contained and trimmed, so the trimmer sees every
#      library at once, and TreatWarningsAsErrors turns any trim warning into a failed publish. The
#      libraries' IsAotCompatible analyzers catch most hazards at build time; this catches what they
#      cannot see, such as a type only reached by name.
#   2. The published executable converts each document to PNG (Skia and ImageSharp), PDF, HTML and
#      Markdown, and fails if any comes back empty. wordart reaches the PDF backend's optional raster
#      backend by name, the hazard the plugin factories had before they named their types literally.
#
# A native AOT publish (-p:PublishAot=true) also needs the platform's C toolchain (MSVC on Windows,
# clang on Linux), so it is not part of this check.
#
# Usage: ./scripts/aot-smoke.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$REPO_ROOT/src"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

RID="$(dotnet --info | awk '/^ *RID:/ { print $2; exit }')"
echo "Publishing AotSmoke trimmed for $RID"
dotnet publish "$SRC/AotSmoke/AotSmoke.csproj" -c Release -r "$RID" -o "$OUT"

EXE="$OUT/AotSmoke"
[ -f "$EXE.exe" ] && EXE="$EXE.exe"

for scenario in wordart resumes/05 complex_tables; do
    echo "== $scenario"
    "$EXE" "$SRC/Tests/Inputs/word/$scenario/input.docx" "$SRC/Fonts"
done

echo "Trimmed publish converts every document."
