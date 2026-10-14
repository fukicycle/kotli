#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CLI_PROJECT="${REPO_ROOT}/src/Kotli.Cli/Kotli.Cli.csproj"
OUT_BASE="${1:-${REPO_ROOT}/artifacts}"

RIDS=(
    "linux-x64"
    "linux-arm64"
    "osx-x64"
    "osx-arm64"
    "win-x64"
    "win-arm64"
)

for rid in "${RIDS[@]}"; do
    echo "==> Publishing Kotli for ${rid}..."
    dotnet publish "${CLI_PROJECT}" \
        --configuration Release \
        --runtime "${rid}" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=None \
        -p:DebugSymbols=false \
        --output "${OUT_BASE}/${rid}"
done

echo "All platform binaries published successfully to ${OUT_BASE}"