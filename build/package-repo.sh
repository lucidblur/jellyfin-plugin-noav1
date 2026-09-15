#!/bin/bash
set -euo pipefail

# Packages the built plugin as an installable zip plus a repository manifest.json, so it can
# be served over HTTP and added as a custom repository in the Jellyfin admin UI
# (Dashboard > Plugins > Repositories > Add Repository).
#
# Usage:
#   ./build/build-container.sh              # build first, if you haven't
#   ./build/package-repo.sh [BASE_URL]
#
# BASE_URL is the address the zip/manifest will be served from (default: http://localhost:8095).
# It must be reachable from wherever the Jellyfin *server* actually runs: if the server is in a
# separate container, "localhost" from this shell won't be reachable from inside it — use the
# host's LAN IP, or host.docker.internal / host.containers.internal, instead.

OUT_DIR=${OUT_DIR:-out/NoAv1Plugin}
REPO_DIR=${REPO_DIR:-repo}
PLUGIN_JSON=${PLUGIN_JSON:-src/NoAv1Plugin/plugin.json}
BASE_URL=${1:-${BASE_URL:-http://localhost:8095}}

if [ ! -f "${OUT_DIR}/NoAv1Plugin.dll" ]; then
  echo "ERROR: ${OUT_DIR}/NoAv1Plugin.dll not found. Run ./build/container-build.sh first." >&2
  exit 2
fi

NAME=$(jq -r '.name' "${PLUGIN_JSON}")
GUID=$(jq -r '.guid' "${PLUGIN_JSON}")
VERSION=$(jq -r '.version' "${PLUGIN_JSON}")
DESCRIPTION=$(jq -r '.description' "${PLUGIN_JSON}")
OWNER=$(jq -r '.owner' "${PLUGIN_JSON}")
CATEGORY=$(jq -r '.category' "${PLUGIN_JSON}")
TARGET_ABI=$(jq -r '.targetAbi' "${PLUGIN_JSON}")

mkdir -p "${REPO_DIR}"
ZIP_NAME="noav1-${VERSION}.zip"
ZIP_PATH="${REPO_DIR}/${ZIP_NAME}"

rm -f "${ZIP_PATH}"
# Only the whitelisted assembly (plugin.json "assemblies") ever gets loaded by the server, so
# only it needs to ship. -j stores it flat (no directory entries) inside the zip.
zip -q -j "${ZIP_PATH}" "${OUT_DIR}/NoAv1Plugin.dll"

CHECKSUM=$(md5sum "${ZIP_PATH}" | cut -d' ' -f1)
TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
SOURCE_URL="${BASE_URL%/}/${ZIP_NAME}"

jq -n \
  --arg name "${NAME}" \
  --arg guid "${GUID}" \
  --arg description "${DESCRIPTION}" \
  --arg owner "${OWNER}" \
  --arg category "${CATEGORY}" \
  --arg version "${VERSION}" \
  --arg targetAbi "${TARGET_ABI}" \
  --arg sourceUrl "${SOURCE_URL}" \
  --arg checksum "${CHECKSUM}" \
  --arg timestamp "${TIMESTAMP}" \
  '[{
    name: $name,
    guid: $guid,
    description: $description,
    overview: $description,
    owner: $owner,
    category: $category,
    versions: [{
      version: $version,
      changelog: "Development build.",
      targetAbi: $targetAbi,
      sourceUrl: $sourceUrl,
      checksum: $checksum,
      timestamp: $timestamp
    }]
  }]' > "${REPO_DIR}/manifest.json"

echo "Wrote ${ZIP_PATH} and ${REPO_DIR}/manifest.json"
echo ""
echo "Serve them, e.g.:"
echo "  python3 -m http.server 8095 --directory ${REPO_DIR}"
echo ""
echo "Then in Jellyfin: Dashboard > Plugins > Repositories > Add Repository, using:"
echo "  ${BASE_URL%/}/manifest.json"
