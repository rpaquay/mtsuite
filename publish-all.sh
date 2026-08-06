#!/usr/bin/env bash
# Copyright 2015 Renaud Paquay All Rights Reserved.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Locate dotnet executable
if [ -x "./.dotnet/dotnet" ]; then
  DOTNET="./.dotnet/dotnet"
elif command -v dotnet >/dev/null 2>&1; then
  DOTNET="dotnet"
else
  echo "Error: dotnet CLI not found." >&2
  exit 1
fi

# Extract product version from VersionNumber.cs
VERSION_FILE="src/core-filesystem/VersionNumber.cs"
if [ ! -f "$VERSION_FILE" ]; then
  echo "Error: Version file not found at $VERSION_FILE" >&2
  exit 1
fi

VERSION=$(grep 'public const string Product =' "$VERSION_FILE" | sed -E 's/.*"([^"]+)".*/\1/')
if [ -z "$VERSION" ]; then
  echo "Error: Could not extract version from $VERSION_FILE" >&2
  exit 1
fi

echo "================================================================="
echo " Publishing mtsuite v${VERSION} for all target platforms"
echo "================================================================="

# Target platforms
PLATFORMS=(
  "linux-x64"
  "win-x64"
  "osx-x64"
  "osx-arm64"
  "win-arm64"
)

# The applications in mtsuite
APPS=(
  "mtcompact"
  "mtcopy"
  "mtdel"
  "mtfind"
  "mtfindstr"
  "mtinfo"
  "mtmir"
)

PUBLISH_ROOT="src/publish"
mkdir -p "$PUBLISH_ROOT"

for RID in "${PLATFORMS[@]}"; do
  echo ""
  echo "-----------------------------------------------------------------"
  echo " Building & Publishing for: $RID"
  echo "-----------------------------------------------------------------"

  # Publish solution for the target RID
  $DOTNET publish src/mtsuite.sln -c Release -r "$RID" --nologo

  # Target directory per platform (unzipped folder)
  PACKAGE_NAME="mtsuite-${VERSION}-${RID}"
  OUT_DIR="$PUBLISH_ROOT/$PACKAGE_NAME"
  rm -rf "$OUT_DIR"
  mkdir -p "$OUT_DIR"

  # Copy the binaries to target folder
  IS_WINDOWS=false
  if [[ "$RID" == win-* ]]; then
    IS_WINDOWS=true
  fi

  for APP in "${APPS[@]}"; do
    if [ "$IS_WINDOWS" = true ]; then
      BIN_NAME="${APP}.exe"
    else
      BIN_NAME="${APP}"
    fi

    BIN_PATH="src/${APP}/bin/Release/net8.0/${RID}/publish/${BIN_NAME}"
    if [ ! -f "$BIN_PATH" ]; then
      echo "Error: Expected binary not found: $BIN_PATH" >&2
      exit 1
    fi

    cp "$BIN_PATH" "$OUT_DIR/"
    chmod +x "$OUT_DIR/$BIN_NAME"
  done

  # Create flat zip file
  ZIP_FILE="$PUBLISH_ROOT/${PACKAGE_NAME}.zip"
  rm -f "$ZIP_FILE"

  (cd "$OUT_DIR" && zip -q -9 -r "$SCRIPT_DIR/$ZIP_FILE" .)

  ZIP_SIZE=$(du -h "$ZIP_FILE" | cut -f1)
  echo "Created directory: $OUT_DIR"
  echo "Created archive:   $ZIP_FILE ($ZIP_SIZE)"
done

echo ""
echo "================================================================="
echo " Publish completed successfully! Generated Packages:"
echo "================================================================="
ls -lhd "$PUBLISH_ROOT"/*/ "$PUBLISH_ROOT"/*.zip
echo "================================================================="
