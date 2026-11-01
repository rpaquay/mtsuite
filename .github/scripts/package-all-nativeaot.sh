#!/usr/bin/env bash
# Combines the per-platform zip files found under a directory into one zip file with one folder per
# platform, keeping the executable permissions of the binaries.

set -euo pipefail

INPUT="${1:?Usage: package-all-nativeaot.sh <input directory> <output directory> <expected zip count>}"
OUTPUT="${2:?Usage: package-all-nativeaot.sh <input directory> <output directory> <expected zip count>}"
EXPECTED="${3:?Usage: package-all-nativeaot.sh <input directory> <output directory> <expected zip count>}"

ZIPS=()
while IFS= read -r ZIP; do
  ZIPS+=("$ZIP")
done < <(find "$INPUT" -type f -name 'mtsuite-*-nativeaot.zip' | sort)
if [[ "${#ZIPS[@]}" -ne "$EXPECTED" ]]; then
  echo "Error: expected $EXPECTED zip files in $INPUT, found ${#ZIPS[@]}" >&2
  exit 1
fi

VERSION=$(basename "${ZIPS[0]}" | sed -E 's/^mtsuite-([0-9.]+)-.*/\1/')
PACKAGE="mtsuite-${VERSION}-all-nativeaot"
mkdir -p "$OUTPUT"
OUTPUT=$(cd "$OUTPUT" && pwd)
STAGE="$OUTPUT/$PACKAGE"
rm -rf "$STAGE" "$STAGE.zip"
mkdir -p "$STAGE"

for ZIP in "${ZIPS[@]}"; do
  PLATFORM=$(basename "$ZIP" | sed -E 's/^mtsuite-[0-9.]+-(.*)-nativeaot\.zip$/\1/')
  unzip -q "$ZIP" -d "$STAGE/$PLATFORM"
done

(cd "$STAGE" && zip -q -9 -r "../$PACKAGE.zip" .)

echo "Created $STAGE.zip"
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  echo "zip=$STAGE.zip" >> "$GITHUB_OUTPUT"
fi
