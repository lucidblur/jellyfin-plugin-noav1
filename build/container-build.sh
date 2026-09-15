#!/bin/bash
set -euo pipefail

# Containerized build script — build the plugin using the official dotnet SDK image.
# Usage: ./build/container-build.sh

PROJECT_PATH=src/NoAv1Plugin/NoAv1Plugin.csproj
OUT_DIR=out/NoAv1Plugin

mkdir -p "$OUT_DIR"

podman run --rm -v "$(pwd)":/src:z -w /src mcr.microsoft.com/dotnet/sdk:6.0 \
  bash -lc "dotnet publish $PROJECT_PATH -c Release -o /src/$OUT_DIR"

echo "Published to $OUT_DIR"
