#!/bin/bash
set -euo pipefail

# Containerized build script that pulls the Jellyfin codebase (at a specified tag/branch)
# and builds the plugin against that source.
#
# Usage:
#   ./build/container-build.sh [JELLYFIN_REF]
#
# Or set environment variables:
#   JELLYFIN_REF=10.11.11 ./build/container-build.sh
#   JELLYFIN_REPO=https://github.com/jellyfin/jellyfin.git ./build/container-build.sh
#
# The script mounts the current repository into the container at /src and performs:
#  - git submodule update --init --recursive (if .gitmodules present)
#  - clone or update the jellyfin checkout at the requested ref
#  - copy plugin source into jellyfin/src/Plugins/NoAv1Plugin
#  - dotnet publish the plugin from the jellyfin repo root so ProjectReferences resolve
#
# Notes:
#  - Default JELLYFIN_REF is "v10.11.11" (set to "main" to build against latest main branch)
#  - The container image must have git and dotnet SDK (mcr.microsoft.com/dotnet/sdk:9.0 does)
#  - Running this script requires Podman or Docker. Replace podman with docker if preferred.

# Configuration
JELLYFIN_REF=${1:-${JELLYFIN_REF:-v10.11.11}}
JELLYFIN_REPO=${JELLYFIN_REPO:-https://github.com/jellyfin/jellyfin.git}
OUT_DIR=${OUT_DIR:-out/NoAv1Plugin}
PLUGIN_SRC_DIR=${PLUGIN_SRC_DIR:-src/NoAv1Plugin}

echo "Building plugin against Jellyfin ref: ${JELLYFIN_REF} from ${JELLYFIN_REPO}"

mkdir -p "$OUT_DIR"

# Run build inside the official dotnet SDK container. Export needed env vars so the inner
# script can use them safely without the host shell trying to expand them.
podman run --rm \
  -e JELLYFIN_REF="${JELLYFIN_REF}" \
  -e JELLYFIN_REPO="${JELLYFIN_REPO}" \
  -e OUT_DIR="${OUT_DIR}" \
  -e PLUGIN_SRC_DIR="${PLUGIN_SRC_DIR}" \
  -v "$(pwd)":/src:z -w /src mcr.microsoft.com/dotnet/sdk:9.0 bash /src/build/container-inner.sh

echo "Published output available at: ${OUT_DIR}"
