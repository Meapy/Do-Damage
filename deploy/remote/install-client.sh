#!/bin/bash
# Installs a new desktop client release and the website that offers it, from the bundle
# deploy/deploy.py --client-only unpacked into /opt/bsk/incoming. Runs as root. The servers, the
# gateway and Caddy stay as they are (install.sh does the rest), so nobody notices but the
# players' clients, which update at their next portal; the Discord bot posts the release.
set -euo pipefail

ROOT=/opt/bsk
IN=$ROOT/incoming
[ "$(id -u)" = 0 ] || { echo "run as root" >&2; exit 1; }
[ -d $ROOT/dist ] && [ -d $ROOT/site ] || { echo "no install in $ROOT yet: deploy everything first" >&2; exit 1; }

# the same steps as install.sh's
if [ -d $IN/client ]; then
    echo "== installing the client releases ($(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' $IN/client/windows/latest.json 2>/dev/null | head -1))"
    rm -rf $ROOT/dist/client.new && cp -r $IN/client $ROOT/dist/client.new
    rm -rf $ROOT/dist/client.old && mv $ROOT/dist/client $ROOT/dist/client.old && mv $ROOT/dist/client.new $ROOT/dist/client
    rm -rf $ROOT/dist/client.old
    chown -R bsk:bsk $ROOT/dist/client
fi
if [ -d $IN/site ]; then
    echo "== installing the website"
    rm -rf $ROOT/site.new && cp -r $IN/site $ROOT/site.new
    [ -d $ROOT/site ] && mv $ROOT/site $ROOT/site.old
    mv $ROOT/site.new $ROOT/site && rm -rf $ROOT/site.old
    chown -R bsk:bsk $ROOT/site
fi
echo "== done"
