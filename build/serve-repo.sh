#!/bin/bash
set -euo pipefail

# Serves the packaged repo/ directory (manifest.json + plugin zip, from package-repo.sh) over
# HTTP so Jellyfin can fetch it as a custom repository. Foreground process -- run in the
# background yourself (e.g. `./build/serve-repo.sh &`) if you want it to keep running while you
# do other things.
#
# Usage:
#   ./build/package-repo.sh [BASE_URL]   # build the repo/ directory first, if you haven't
#   ./build/serve-repo.sh [PORT]

REPO_DIR=${REPO_DIR:-repo}
PORT=${1:-8095}

if [ ! -f "${REPO_DIR}/manifest.json" ]; then
  echo "ERROR: ${REPO_DIR}/manifest.json not found. Run ./build/package-repo.sh first." >&2
  exit 2
fi

echo "Serving ${REPO_DIR} on port ${PORT} (Ctrl+C to stop)"
exec python3 -m http.server "${PORT}" --directory "${REPO_DIR}"
