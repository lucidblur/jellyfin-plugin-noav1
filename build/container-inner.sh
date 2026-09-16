#!/bin/bash
set -euo pipefail

# Runs inside the dotnet SDK container (mounted at /src) via container-build.sh.
# Expects JELLYFIN_REF, JELLYFIN_REPO, OUT_DIR, PLUGIN_SRC_DIR in the environment.

cd /src

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
dotnet publish "${DOTNET_PROJECT_PATH}" -c Release -o "/src/${OUT_DIR}"

# `dotnet publish` also copies every transitive ProjectReference output (MediaBrowser.*,
# Jellyfin.*, etc.) into OUT_DIR. Jellyfin's plugin loader puts every DLL found in a plugin's
# folder into that plugin's own AssemblyLoadContext, so shipping copies of server assemblies
# we merely build against would load a second, distinct set of those types alongside the
# host's and break DI/type identity. Only the plugin's own assembly should ever be installed.
echo 'Trimming publish output to only the plugin assembly...'
find "/src/${OUT_DIR}" -maxdepth 1 -type f ! -name 'NoAv1Plugin.dll' ! -name 'NoAv1Plugin.pdb' -delete
find "/src/${OUT_DIR}" -mindepth 1 -maxdepth 1 -type d -exec rm -rf {} +

echo "Build complete. Published files are in /src/${OUT_DIR} inside the host workspace."
