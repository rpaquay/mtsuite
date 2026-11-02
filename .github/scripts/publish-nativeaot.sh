#!/usr/bin/env bash
# Publishes all tools as NativeAOT binaries for one runtime identifier, and zips them into
# publish/nativeaot. Must run on the target OS, since NativeAOT can't compile across operating systems.

set -euo pipefail

RID="${1:?Usage: publish-nativeaot.sh <runtime identifier>}"
cd "$(dirname "${BASH_SOURCE[0]}")/../.."

VERSION=$(grep 'public const string Product =' src/core-filesystem/VersionNumber.cs | sed -E 's/.*"([^"]+)".*/\1/')
PACKAGE="mtsuite-${VERSION}-${RID}-nativeaot"
ROOT="publish/nativeaot"
STAGE="$ROOT/$PACKAGE"
EXT=""
if [[ "$RID" == win-* ]]; then
  EXT=".exe"
fi

rm -rf "$STAGE" "$ROOT/$PACKAGE.zip"
mkdir -p "$STAGE"

for APP in mtinfo mtfind mtfindstr mtcopy mtmir mtdel mtcompact; do
  echo "Publishing $APP for $RID"
  # UseWindowsThreadPool=false: .NET 8 NativeAOT otherwise uses the Windows thread pool on Windows,
  # which ignores ThreadPool.SetMaxThreads, so --threads would have no effect.
  dotnet publish "src/$APP/$APP.csproj" -c Release -r "$RID" --nologo -o "$ROOT/obj/$RID/$APP" \
    -p:PublishAot=true \
    -p:PublishSingleFile=false \
    -p:PublishReadyToRun=false \
    -p:SelfContained=true \
    -p:IncludeAllContentForSelfExtract=false \
    -p:IncludeNativeLibrariesForSelfExtract=false \
    -p:EnableCompressionInSingleFile=false \
    -p:UseWindowsThreadPool=false
  cp "$ROOT/obj/$RID/$APP/$APP$EXT" "$STAGE/"
done

# The test suite doesn't run against NativeAOT binaries, so at least check each one starts and that
# a real directory walk succeeds.
for APP in mtinfo mtfind mtfindstr mtcopy mtmir mtdel mtcompact; do
  # --help exits with code 16 by design.
  RC=0
  "$STAGE/$APP$EXT" --help > /dev/null || RC=$?
  if [[ "$RC" -ne 16 ]]; then
    echo "Error: $APP --help exited with code $RC" >&2
    exit 1
  fi
done
"$STAGE/mtinfo$EXT" -p none src

# These builds aren't notarized, so macOS blocks them after a download unless the quarantine flag is removed.
if [[ "$RID" == osx-* ]]; then
  cp ".github/scripts/mtsuite - run this to remove quarantine.sh" "$STAGE/"
fi

if [[ "$RID" == win-* ]]; then
  powershell -NoProfile -Command "Compress-Archive -Path '$STAGE/*' -DestinationPath '$ROOT/$PACKAGE.zip'"
else
  (cd "$STAGE" && zip -q -9 -r "../$PACKAGE.zip" .)
fi

echo "Created $ROOT/$PACKAGE.zip"
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  echo "zip=$ROOT/$PACKAGE.zip" >> "$GITHUB_OUTPUT"
fi
