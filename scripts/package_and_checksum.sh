#!/bin/bash
set -euo pipefail

PKG_NAME=noav1-1.0.0.zip
OUT_DIR=out/NoAv1Plugin

if [ ! -d "$OUT_DIR" ]; then
  echo "Build output not found in $OUT_DIR. Run build script first." >&2
  exit 1
fi

pushd "$OUT_DIR"
zip -r "../../$PKG_NAME" ./*
popd

MD5=$(md5sum "$PKG_NAME" | awk '{print $1}')

cat <<EOF
Created $PKG_NAME
MD5: $MD5
EOF
