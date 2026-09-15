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
#  - The container image must have git and dotnet SDK (mcr.microsoft.com/dotnet/sdk:6.0 does)
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
  -v "$(pwd)":/src:z -w /src mcr.microsoft.com/dotnet/sdk:6.0 bash -lc "set -euo pipefail

# If this repository contains a .gitmodules file, initialize and populate submodules.
if [ -f .gitmodules ]; then
  echo 'Initializing/updating submodules...'
  git submodule update --init --recursive || true
fi

# If a jellyfin folder already exists (submodule or previous clone), use it and checkout the requested ref.
if [ -d jellyfin ]; then
  echo 'Using existing jellyfin directory. Fetching and checking out requested ref...'
  cd jellyfin
  git fetch --all --tags --prune || true
  # Try checking out the requested ref. Some tags use v-prefixed names; allow fallback.
  if git show-ref --verify --quiet "refs/heads/${JELLYFIN_REF}"; then
    git checkout "${JELLYFIN_REF}"
  elif git ls-remote --tags origin "refs/tags/${JELLYFIN_REF}" | grep -q .; then
    git checkout "${JELLYFIN_REF}"
  else
    # Try to switch to an origin branch if present, otherwise attempt to create a tracking branch
    git checkout -B build-target "origin/${JELLYFIN_REF}" || true
  fi
  cd ..
else
  echo 'Cloning jellyfin repository...'
  git clone --depth 1 --branch "${JELLYFIN_REF}" "${JELLYFIN_REPO}" jellyfin
fi

# Ensure plugin folder exists in jellyfin source tree and copy plugin source there
PLUGIN_TARGET_DIR="jellyfin/src/Plugins/NoAv1Plugin"
mkdir -p "${PLUGIN_TARGET_DIR}"

# Use tar to copy plugin source into the jellyfin checkout (avoids rsync dependency)
if [ -d "/src/${PLUGIN_SRC_DIR}" ]; then
  tar -C "/src/${PLUGIN_SRC_DIR}" -c . | tar -C "${PLUGIN_TARGET_DIR}" -xvf -
else
  echo "ERROR: plugin source directory /src/${PLUGIN_SRC_DIR} not found" >&2
  exit 2
fi

# Build/publish the plugin from within the jellyfin repo so ProjectReferences resolve
DOTNET_PROJECT_PATH="jellyfin/src/Plugins/NoAv1Plugin/NoAv1Plugin.csproj"
if [ ! -f "${DOTNET_PROJECT_PATH}" ]; then
  echo 'ERROR: Project not found at' "${DOTNET_PROJECT_PATH}" >&2
  exit 2
fi

echo 'Running dotnet publish...'
dotnet publish "${DOTNET_PROJECT_PATH}" -c Release -o /src/${OUT_DIR}

echo 'Build complete. Published files are in /src/${OUT_DIR} inside the host workspace.'
"

echo "Published output available at: ${OUT_DIR}"
