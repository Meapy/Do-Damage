#!/bin/bash
# Installs or updates the website machine's part, when the game runs on another machine
# ("website" in deploy/vps.local.json; SETUP-LOCAL.md, "Two machines"), from the bundle
# deploy/deploy.py unpacked into /opt/bsk/incoming. Runs as root:
#
#     bash /opt/bsk/incoming/deploy/install-site.sh [--retire-game]
#
# - the website (/opt/bsk/site) and Caddy: the website at the root, /play redirected to the game's
#   site, /api and /ws forwarded there (for clients built for this address);
# - the Discord bot (bsk-discord), as install.sh installs it;
# - with Testing in the bundle (worldserver/): the admin-only Testing world server (bsk-world),
#   on the game's Redis through bsk-redis-tunnel. A new build goes in with a rolling deploy.
# It refuses while this machine still runs the game's own Redis: --retire-game (the move to two
# machines, once) stops and masks this machine's bsk-redis, bsk-account, gateways and backups
# first. Their files stay in /opt/bsk.
set -euo pipefail

ROOT=/opt/bsk
IN=$ROOT/incoming
RETIRE=0
for a in "$@"; do
    case "$a" in
        --retire-game) RETIRE=1 ;;
        *) echo "unknown option $a" >&2; exit 2 ;;
    esac
done
[ "$(id -u)" = 0 ] || { echo "run as root" >&2; exit 1; }
[ -d "$IN" ] || { echo "no bundle in $IN" >&2; exit 1; }

say() { echo "== $*"; }

# --- the game's own services, from before the move --------------------------------------------
game_units="bsk-redis.service bsk-account.service bsk-gateway@a.service bsk-gateway@b.service bsk-gateway@c.service bsk-backup.timer bsk-backup.service"
if [ $RETIRE = 1 ]; then
    say "retiring this machine's game services (Redis, account server, gateways, backups)"
    for u in $game_units; do
        systemctl disable --now "$u" >/dev/null 2>&1 || true
    done
    # mask refuses while the unit's own file is in /etc/systemd/system (install.sh put it there):
    # move it aside (kept in $ROOT/retired-units), then mask, so nothing starts them on old data
    mkdir -p $ROOT/retired-units
    for u in $game_units; do
        f=/etc/systemd/system/$u
        if [ -f "$f" ] && [ ! -L "$f" ]; then mv "$f" $ROOT/retired-units/; fi
    done
    systemctl daemon-reload
    for u in $game_units; do
        systemctl mask "$u" >/dev/null
    done
fi
for u in $game_units; do
    if [ -e "/etc/systemd/system/$u" ] && [ "$(systemctl is-enabled "$u" 2>/dev/null)" != masked ]; then
        say "$u is still installed here, not masked: deploy once with --retire-game"
    fi
done
if systemctl is-active --quiet bsk-redis; then
    echo "this machine still runs the game's Redis (bsk-redis): run with --retire-game once the" >&2
    echo "data is on the game machine (SETUP-LOCAL.md, \"Two machines\")" >&2
    exit 1
fi

# --- packages -----------------------------------------------------------------------------
if [ -d /etc/needrestart ]; then
    mkdir -p /etc/needrestart/conf.d
    cat > /etc/needrestart/conf.d/bsk.conf <<'NR'
# Written by betterSkillys' deploy/remote/install-site.sh: restarting these disconnects players.
$nrconf{override_rc}{qr(^bsk-)} = 0;
$nrconf{override_rc}{qr(^caddy)} = 0;
NR
fi
need=()
command -v curl >/dev/null || need+=(curl)
command -v caddy >/dev/null || need+=(caddy)
command -v python3 >/dev/null || need+=(python3)
if [ -d $IN/worldserver ]; then
    [ -d /usr/lib/dotnet/shared/Microsoft.NETCore.App ] || [ -d /usr/share/dotnet/shared/Microsoft.NETCore.App ] || need+=(dotnet-runtime-8.0)
fi
if [ ${#need[@]} -gt 0 ]; then
    say "installing ${need[*]}"
    export DEBIAN_FRONTEND=noninteractive
    apt-get -o DPkg::Lock::Timeout=600 update -q
    apt-get -o DPkg::Lock::Timeout=600 install -y -q "${need[@]}"
fi

id bsk >/dev/null 2>&1 || { say "adding the bsk user"; useradd --system --home-dir $ROOT --shell /usr/sbin/nologin bsk; }
mkdir -p $ROOT/build/live

# --- units ----------------------------------------------------------------------------------
changed_units=""
for u in $IN/deploy/systemd/*.service; do
    dest=/etc/systemd/system/$(basename "$u")
    cmp -s "$u" "$dest" || { install -m 644 "$u" "$dest"; changed_units="$changed_units $(basename "$u")"; }
done
systemctl daemon-reload
unit_changed() { case " $changed_units " in *" $1 "*) return 0 ;; *) return 1 ;; esac; }

# --- website and Caddy ----------------------------------------------------------------------
if [ -d $IN/site ]; then
    rm -rf $ROOT/site.new && cp -r $IN/site $ROOT/site.new
    [ -d $ROOT/site ] && mv $ROOT/site $ROOT/site.old
    mv $ROOT/site.new $ROOT/site && rm -rf $ROOT/site.old
fi
rm -rf $ROOT/tools && cp -r $IN/tools $ROOT/tools
if [ -f $IN/Caddyfile ]; then
    systemctl enable --quiet caddy
    if ! cmp -s $IN/Caddyfile /etc/caddy/Caddyfile; then
        say "Caddyfile changed: reloading Caddy (open WebSockets stay)"
        install -m 644 $IN/Caddyfile /etc/caddy/Caddyfile
        systemctl reload-or-restart caddy
    fi
    systemctl is-active --quiet caddy || systemctl start caddy
fi

# --- Testing (the admin-only world server) ----------------------------------------------------
if [ -d $IN/worldserver ]; then
    install -m 644 $IN/bsk.env $ROOT/bsk.env
    world_changed=0
    if ! diff -rq $IN/worldserver $ROOT/worldserver >/dev/null 2>&1; then
        world_changed=1
        rm -rf $ROOT/worldserver.new && cp -r $IN/worldserver $ROOT/worldserver.new
        rm -rf $ROOT/worldserver.old
        [ -d $ROOT/worldserver ] && mv $ROOT/worldserver $ROOT/worldserver.old
        mv $ROOT/worldserver.new $ROOT/worldserver && rm -rf $ROOT/worldserver.old
    fi
    chown -R bsk:bsk $ROOT/worldserver $ROOT/build $ROOT/tools
    if [ -f $ROOT/.ssh/redis_tunnel ]; then
        systemctl enable --quiet bsk-redis-tunnel bsk-world
        unit_changed bsk-redis-tunnel.service && systemctl restart bsk-redis-tunnel
        systemctl is-active --quiet bsk-redis-tunnel || systemctl start bsk-redis-tunnel
        if systemctl is-active --quiet bsk-world; then
            if [ $world_changed = 1 ]; then
                say "rolling deploy of Testing"
                runuser -u bsk -- /usr/bin/python3 $ROOT/tools/rolling_restart.py request update
            else
                say "Testing unchanged"
            fi
        else
            say "starting Testing"
            systemctl start bsk-world
        fi
    else
        say "no $ROOT/.ssh/redis_tunnel yet: Testing waits for its tunnel (SETUP-LOCAL.md, \"Two machines\")"
    fi
fi

# --- the Discord bot (as install.sh) ----------------------------------------------------------
if [ -f $IN/discord.env ] || [ -f $ROOT/discord/discord.env ]; then
    mkdir -p $ROOT/discord
    chown bsk:bsk $ROOT/discord
    bot_restart=0
    if [ ! -x $ROOT/discord/venv/bin/python ] || ! cmp -s $ROOT/tools/discord/requirements.txt $ROOT/discord/requirements.txt; then
        say "setting up the Discord bot's Python packages"
        if { python3 -c 'import ensurepip' 2>/dev/null \
                || DEBIAN_FRONTEND=noninteractive apt-get -o DPkg::Lock::Timeout=600 install -y -q python3-venv; } \
            && rm -rf $ROOT/discord/venv && python3 -m venv $ROOT/discord/venv \
            && $ROOT/discord/venv/bin/pip install -q --disable-pip-version-check -r $ROOT/tools/discord/requirements.txt; then
            cp $ROOT/tools/discord/requirements.txt $ROOT/discord/requirements.txt
            bot_restart=1
        else
            say "the Discord bot's packages didn't install: the bot isn't updated"
        fi
    fi
    if [ -x $ROOT/discord/venv/bin/python ]; then
        if [ -f $IN/discord.env ]; then
            cmp -s $IN/discord.env $ROOT/discord/discord.env || { install -m 600 -o bsk -g bsk $IN/discord.env $ROOT/discord/discord.env; bot_restart=1; }
        elif [ -f $IN/discord-where.env ]; then
            # the settings already there, with where the releases and the site are now
            before=$(sha256sum $ROOT/discord/discord.env)
            while IFS='=' read -r k v; do
                [ -n "$k" ] || continue
                if grep -q "^$k=" $ROOT/discord/discord.env; then
                    sed -i "s#^$k=.*#$k=$v#" $ROOT/discord/discord.env
                else
                    echo "$k=$v" >> $ROOT/discord/discord.env
                fi
            done < $IN/discord-where.env
            [ "$before" = "$(sha256sum $ROOT/discord/discord.env)" ] || bot_restart=1
        fi
        code=$(cat $ROOT/tools/discord/*.py | sha256sum | cut -d' ' -f1)
        [ "$code" = "$(cat $ROOT/discord/code.sha256 2>/dev/null)" ] || { echo "$code" > $ROOT/discord/code.sha256; bot_restart=1; }
        unit_changed bsk-discord.service && bot_restart=1
        systemctl enable --quiet bsk-discord
        if [ $bot_restart = 1 ]; then
            say "restarting the Discord bot"
            systemctl restart bsk-discord
        else
            systemctl is-active --quiet bsk-discord || systemctl start bsk-discord
        fi
    fi
fi
rm -f $IN/discord.env

chown -R bsk:bsk $ROOT/site 2>/dev/null || true
say "done"
systemctl --no-pager --lines=0 status caddy bsk-discord bsk-redis-tunnel bsk-world 2>/dev/null | grep -E '^(●|  *Active)' || true
true
