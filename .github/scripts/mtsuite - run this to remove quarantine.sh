#!/bin/bash
# Removes the quarantine flag that macOS puts on downloaded files, so that macOS doesn't block the mtsuite
# tools in this folder as unverified. These builds aren't signed or notarized by Apple, so only run this
# if you trust where you downloaded them from. It doesn't change anything else, and doesn't need sudo.
#
# Run it from Terminal, since macOS opens .sh files in an editor when you double-click them:
#   bash "mtsuite - run this to remove quarantine.sh"

set -u

FLAG="com.apple.quarantine"
FOLDER="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT="$(basename "${BASH_SOURCE[0]}")"
FAILED=0

for FILE in mtinfo mtfind mtfindstr mtcopy mtmir mtdel mtcompact "$SCRIPT"; do
  if [[ ! -e "$FOLDER/$FILE" ]]; then
    echo "Not found: $FILE"
  elif ! xattr -p "$FLAG" "$FOLDER/$FILE" > /dev/null 2>&1; then
    echo "Already clear: $FILE"
  elif xattr -d "$FLAG" "$FOLDER/$FILE"; then
    echo "Removed: $FILE"
  else
    echo "Failed: $FILE"
    FAILED=1
  fi
done

exit "$FAILED"
