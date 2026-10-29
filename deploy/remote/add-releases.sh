#!/bin/bash
# Adds published client release files from a bundle to the ones players' clients update from:
#
#     bash add-releases.sh <bundle's client/> /opt/bsk/dist/client
#
# deploy/deploy.py sends only what the server lacks (the blobs, each version's list) and the
# latest lists, so the releases are added beside what's there, never replaced as a whole. A
# blob is named by its content and a version's list never changes, so a file that's there
# stays; the latest lists go in last, each renamed into place, so a client never reads a list
# whose files aren't there yet.
set -euo pipefail
src=$1
dst=$2
mkdir -p "$dst"
added=0
while IFS= read -r -d '' f; do
    if [ ! -e "$dst/$f" ]; then
        install -D -m 644 "$src/$f" "$dst/$f"
        added=$((added + 1))
    fi
done < <(cd "$src" && find . -type f ! -name latest.json ! -name latest.json.sig -printf '%P\0')
while IFS= read -r -d '' f; do
    install -D -m 644 "$src/$f" "$dst/$f.new"
    mv -f "$dst/$f.new" "$dst/$f"
done < <(cd "$src" && find . -type f \( -name latest.json -o -name latest.json.sig \) -printf '%P\0')
chown -R bsk:bsk "$dst" 2>/dev/null || true
latest=$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' "$dst/windows/latest.json" 2>/dev/null | head -1)
echo "== client releases: $added new files; the latest is ${latest:-none}"
