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
#  - git clone --branch <JELLYFIN_REF> --depth 1 <JELLYFIN_REPO> jellyfin
#  - copy plugin source into jellyfin/src/Plugins/NoAv1Plugin
#  - dotnet publish the plugin from the jellyfin repo root so ProjectReferences resolve
#
# Notes:
#  - Default JELLYFIN_REF is "10.11.11" (set to "main" to build against latest main branch)
#  - The container image must have git and dotnet SDK (mcr.microsoft.com/dotnet/sdk:6.0 does)
#  - Running this script requires Podman or Docker. Replace podman with docker if preferred.

# Configuration
JELLYFIN_REF=${1:-${JELLYFIN_REF:-10.11.11}}
JELLYFIN_REPO=${JELLYFIN_REPO:-https://github.com/jellyfin/jellyfin.git}
OUT_DIR=${OUT_DIR:-out/NoAv1Plugin}
PLUGIN_SRC_DIR=src/NoAv1Plugin

echo "Building plugin against Jellyfin ref: ${JELLYFIN_REF} from ${JELLYFIN_REPO}"

mkdir -p "$OUT_DIR"

podman run --rm -v "$(pwd)":/src:z -w /src mcr.microsoft.com/dotnet/sdk:6.0 bash -lc "set -euo pipefail
# If jellyfin directory doesn't exist, clone the requested ref (branch or tag)
if [ ! -d jellyfin ]; then
  echo 'Cloning jellyfin repository...'
  git clone --depth 1 --branch \"${JELLYFIN_REF}\" \"${JELLYFIN_REPO}\" jellyfin
else
  echo 'Jellyfin directory already exists in mounted workspace. Fetching and checking out requested ref...'
  cd jellyfin
  git fetch --all --tags --prune
  git checkout \"${JELLYFIN_REF}\" || git checkout -b temp-build \"origin/${JELLYFIN_REF}\" || git checkout \"${JELLYFIN_REF}\"
  cd ..
fi

# Ensure plugin folder exists in jellyfin source tree and copy plugin source there
PLUGIN_TARGET_DIR=jellyfin/src/Plugins/NoAv1Plugin
mkdir -p \"${PLUGIN_TARGET_DIR}\"
# Copy plugin source (overwrite existing)
rsync -a --delete /src/${PLUGIN_SRC_DIR}/ \"${PLUGIN_TARGET_DIR}/\"

# Build/publish the plugin from within the jellyfin repo so ProjectReferences resolve
DOTNET_PROJECT_PATH=jellyfin/src/Plugins/NoAv1Plugin/NoAv1Plugin.csproj
if [ ! -f \"${DOTNET_PROJECT_PATH}\" ]; then
  echo 'ERROR: Project not found at' \"${DOTNET_PROJECT_PATH}\" >&2
  exit 2
fi

echo 'Running dotnet publish...'
dotnet publish \"${DOTNET_PROJECT_PATH}\" -c Release -o /src/${OUT_DIR}

echo 'Build complete. Published files are in /src/${OUT_DIR} inside the host workspace.'
"

echo "Published output available at: ${OUT_DIR}"
