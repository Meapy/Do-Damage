#!/bin/bash
# Installs or updates betterSkillys on a Linux server (Ubuntu 24.04), from the bundle that
# deploy/deploy.py unpacked into /opt/bsk/incoming. Runs as root:
#
#     bash /opt/bsk/incoming/deploy/install.sh [--import-data] [--force-data]
#
# - First run: installs Redis 7, the .NET 8 runtime, Caddy and Python, makes the bsk user and
#   /opt/bsk, a swap file if there is none, and the services (bsk-redis, bsk-account, bsk-world,
#   bsk-gateway@<slot>, caddy), and bsk-backup.timer when the config names a backup repository.
# - Later runs replace what changed, without disconnecting anyone:
#   - a new world server build goes in with a rolling deploy (rolling_restart.py request update);
#   - a new gateway starts in a free slot (a b c, ports gateway+0/1/2) and the old one hands over:
#     Caddy follows the gateways' /healthz, so new connections go to the new one while the old one
#     keeps its players until they change worlds (see bsk-gateway@.service);
#   - the account server restarts when its files or settings changed: sign-ins and account
#     requests pause for the few seconds it takes to start (the gateway holds https ones until
#     it's back); nobody in game notices;
#   - Caddy reloads only when the Caddyfile changed, and keeps open WebSockets through a reload
#     (stream_close_delay, Caddy 2.7 or later: an older Caddy is replaced from Caddy's own apt
#     repository, a restart that disconnects https players once).
#   Package upgrades never restart the game's services or Caddy (needrestart is told so).
# - --import-data copies the Redis data in the bundle (redis-data/) into /opt/bsk/redis/data,
#   with the servers stopped. It refuses to replace data already there unless --force-data.
set -euo pipefail

ROOT=/opt/bsk
IN=$ROOT/incoming
GW=$ROOT/gateway
IMPORT_DATA=0
FORCE_DATA=0
for a in "$@"; do
    case "$a" in
        --import-data) IMPORT_DATA=1 ;;
        --force-data) FORCE_DATA=1 ;;
        *) echo "unknown option $a" >&2; exit 2 ;;
    esac
done
[ "$(id -u)" = 0 ] || { echo "run as root" >&2; exit 1; }
[ -d "$IN" ] || { echo "no bundle in $IN" >&2; exit 1; }

say() { echo "== $*"; }

# --- packages, user, folders -------------------------------------------------------------
# needrestart (Ubuntu) restarts services using upgraded libraries after apt runs, unattended
# upgrades included: for these that would disconnect players. They pick the libraries up at
# their next deploy or restart instead.
if [ -d /etc/needrestart ]; then
    mkdir -p /etc/needrestart/conf.d
    cat > /etc/needrestart/conf.d/bsk.conf <<'NR'
# Written by betterSkillys' deploy/remote/install.sh: restarting these disconnects players.
$nrconf{override_rc}{qr(^bsk-)} = 0;
$nrconf{override_rc}{qr(^caddy)} = 0;
NR
fi

# (apt waits up to 10 minutes for unattended-upgrades to let go of its lock)
need=()
command -v curl >/dev/null || need+=(curl)
command -v redis-server >/dev/null || need+=(redis-server)
[ -d /usr/lib/dotnet/shared/Microsoft.AspNetCore.App ] || [ -d /usr/share/dotnet/shared/Microsoft.AspNetCore.App ] || need+=(aspnetcore-runtime-8.0)
command -v caddy >/dev/null || need+=(caddy)
command -v python3 >/dev/null || need+=(python3)
if [ ${#need[@]} -gt 0 ]; then
    say "installing ${need[*]}"
    export DEBIAN_FRONTEND=noninteractive
    apt-get -o DPkg::Lock::Timeout=600 update -q
    apt-get -o DPkg::Lock::Timeout=600 install -y -q "${need[@]}"
fi
# Caddy 2.7 or later, from Caddy's own repository: older ones (Ubuntu 24.04 has 2.6.2) close every
# WebSocket when they reload, and can't be told not to (stream_close_delay)
caddy_new_enough() {
    local v
    v=$(caddy version 2>/dev/null | sed -nE 's/^v?([0-9]+)\.([0-9]+).*/\1 \2/p')
    [ -n "$v" ] && set -- $v && { [ "$1" -gt 2 ] || { [ "$1" = 2 ] && [ "$2" -ge 7 ]; }; }
}
if [ -f $IN/Caddyfile ] && ! caddy_new_enough; then
    say "installing Caddy from caddyserver.com's repository (this restarts Caddy: https players reconnect)"
    export DEBIAN_FRONTEND=noninteractive
    command -v gpg >/dev/null || { apt-get -o DPkg::Lock::Timeout=600 update -q; apt-get -o DPkg::Lock::Timeout=600 install -y -q gnupg; }
    curl -1sLf https://dl.cloudsmith.io/public/caddy/stable/gpg.key | gpg --dearmor --yes -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
    curl -1sLf https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt -o /etc/apt/sources.list.d/caddy-stable.list
    apt-get -o DPkg::Lock::Timeout=600 update -q
    apt-get -o DPkg::Lock::Timeout=600 install -y -q -o Dpkg::Options::=--force-confold caddy
    caddy_new_enough || { echo "Caddy is still older than 2.7" >&2; exit 1; }
fi
# the distro's own Redis would take the port: ours is bsk-redis
if systemctl list-unit-files redis-server.service >/dev/null 2>&1; then
    systemctl disable --now redis-server.service >/dev/null 2>&1 || true
    systemctl mask redis-server.service >/dev/null 2>&1 || true
fi

id bsk >/dev/null 2>&1 || { say "adding the bsk user"; useradd --system --home-dir $ROOT --shell /usr/sbin/nologin bsk; }
mkdir -p $ROOT/redis/data $ROOT/build/live $ROOT/dist/client $ROOT/app $ROOT/worldserver $GW

# Two world servers run side by side during a rolling deploy (about 800 MB each): keep some swap
# as a cushion on a small VPS.
if [ -z "$(swapon --noheadings 2>/dev/null)" ] && [ ! -e /swapfile ] && ! grep -qi microsoft /proc/version; then
    say "adding a 2 GB swap file"
    fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile >/dev/null && swapon /swapfile
    grep -q '^/swapfile ' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

# Redis forks to save: without overcommit a save can fail when memory is short
if [ "$(sysctl -n vm.overcommit_memory 2>/dev/null)" != 1 ]; then
    echo 'vm.overcommit_memory = 1' > /etc/sysctl.d/90-bsk-redis.conf
    sysctl -q -p /etc/sysctl.d/90-bsk-redis.conf || true
fi

# --- configuration and tools (always safe to replace) --------------------------------------
# The settings (bsk.env) and each unit: what changed is restarted below in a way that kicks
# nobody. A changed bsk-world or bsk-redis unit waits for their next restart (restarting bsk-world
# kicks everyone).
env_changed=0
cmp -s $IN/bsk.env $ROOT/bsk.env || { install -m 644 $IN/bsk.env $ROOT/bsk.env; env_changed=1; }
rm -rf $ROOT/deploy && cp -r $IN/deploy $ROOT/deploy
rm -rf $ROOT/tools && cp -r $IN/tools $ROOT/tools
changed_units=""
for u in $IN/deploy/systemd/*.service $IN/deploy/systemd/*.timer; do
    dest=/etc/systemd/system/$(basename "$u")
    cmp -s "$u" "$dest" || { install -m 644 "$u" "$dest"; changed_units="$changed_units $(basename "$u")"; }
done
[ -n "$changed_units" ] && systemctl daemon-reload
unit_changed() { case " $changed_units " in *" $1 "*) return 0 ;; *) return 1 ;; esac; }
set -a; . $ROOT/bsk.env; set +a

# --- Redis --------------------------------------------------------------------------------
if [ $IMPORT_DATA = 1 ]; then
    [ -d $IN/redis-data ] || { echo "no redis-data/ in the bundle" >&2; exit 1; }
    if [ -n "$(ls -A $ROOT/redis/data)" ] && [ $FORCE_DATA = 0 ]; then
        echo "$ROOT/redis/data already has data: add --force-data to replace it" >&2
        exit 1
    fi
    say "importing the Redis data (servers stopped meanwhile)"
    systemctl stop bsk-world bsk-account bsk-redis 2>/dev/null || true
    if [ -n "$(ls -A $ROOT/redis/data)" ]; then
        mv $ROOT/redis/data "$ROOT/redis/data.before-import.$(date +%Y%m%d-%H%M%S)"
        mkdir -p $ROOT/redis/data
    fi
    cp -r $IN/redis-data/. $ROOT/redis/data/
    if [ -f $ROOT/redis/data/dump.rdb ] && [ ! -e $ROOT/redis/data/appendonlydir ] && [ ! -e $ROOT/redis/data/appendonly.aof ]; then
        # only a snapshot (a backup from deploy/backup.py): with AOF on, Redis would ignore it and
        # start empty. Load it with AOF off, then turn AOF on, which writes the AOF from it.
        say "loading the snapshot and writing Redis's AOF from it"
        chown -R bsk:bsk $ROOT/redis
        sock=$ROOT/redis/import.sock
        runuser -u bsk -- redis-server --port 0 --unixsocket $sock --dir $ROOT/redis/data --dbfilename dump.rdb \
            --appendonly no --appenddirname appendonlydir --appendfilename appendonly.aof --save "" \
            --daemonize yes --logfile $ROOT/redis/import.log
        for i in $(seq 120); do [ "$(redis-cli -s $sock ping 2>/dev/null)" = PONG ] && break; sleep 1; done
        echo "snapshot: $(redis-cli -s $sock dbsize) keys"
        redis-cli -s $sock config set appendonly yes >/dev/null
        for i in $(seq 300); do
            redis-cli -s $sock info persistence | grep -q '^aof_rewrite_in_progress:0' \
                && redis-cli -s $sock info persistence | grep -q '^aof_rewrite_scheduled:0' \
                && [ -f $ROOT/redis/data/appendonlydir/appendonly.aof.manifest ] && break
            sleep 1
        done
        redis-cli -s $sock shutdown save >/dev/null 2>&1 || true
        for i in $(seq 30); do [ -S $sock ] || break; sleep 1; done
        [ -f $ROOT/redis/data/appendonlydir/appendonly.aof.manifest ] || { echo "writing the AOF failed: see $ROOT/redis/import.log" >&2; exit 1; }
    fi
fi
chown -R bsk:bsk $ROOT/redis
systemctl enable --quiet bsk-redis
systemctl start bsk-redis

# --- account server -----------------------------------------------------------------------
# It holds nobody's connection: players in game are on the world servers. A restart pauses
# sign-ins and account requests for the few seconds it takes to start.
account_restart=0
if ! diff -rq --exclude=logs $IN/app $ROOT/app >/dev/null 2>&1; then
    say "account server changed: installing it (sign-ins pause while it restarts)"
    rm -rf /opt/bsk/app.new && cp -r $IN/app /opt/bsk/app.new && chown -R bsk:bsk /opt/bsk/app.new
    systemctl stop bsk-account 2>/dev/null || true
    if [ -d $ROOT/app/logs ]; then mv $ROOT/app/logs /opt/bsk/app.new/logs; fi
    rm -rf /opt/bsk/app.old && mv $ROOT/app /opt/bsk/app.old && mv /opt/bsk/app.new $ROOT/app && rm -rf /opt/bsk/app.old
    systemctl start bsk-account
fi
{ [ $env_changed = 1 ] || unit_changed bsk-account.service; } && account_restart=1
if [ -d $IN/client ]; then
    # published client releases, served at /client/ for players' clients to update from
    # only what's new (deploy.py sends what the server lacks), added beside the rest
    bash $IN/deploy/add-releases.sh $IN/client $ROOT/dist/client
fi

# --- website ------------------------------------------------------------------------------
if [ -d $IN/site ]; then
    # the website, which Caddy serves at the site's root
    rm -rf $ROOT/site.new && cp -r $IN/site $ROOT/site.new
    [ -d $ROOT/site ] && mv $ROOT/site $ROOT/site.old
    mv $ROOT/site.new $ROOT/site && rm -rf $ROOT/site.old
fi

# --- world server build (the service copies it into a slot) --------------------------------
world_changed=0
if ! diff -rq $IN/worldserver $ROOT/worldserver >/dev/null 2>&1; then
    world_changed=1
    rm -rf $ROOT/worldserver.new && cp -r $IN/worldserver $ROOT/worldserver.new
    rm -rf $ROOT/worldserver.old && mv $ROOT/worldserver $ROOT/worldserver.old && mv $ROOT/worldserver.new $ROOT/worldserver
    rm -rf $ROOT/worldserver.old
fi

# --- database backups (deploy/backup.py --setup makes the key; until then the timer only says so)
mkdir -p $ROOT/backup
install -m 644 $IN/backup.env $ROOT/backup/backup.env

chown -R bsk:bsk $ROOT
chown root:root $ROOT/bsk.env

# --- account server (re)start --------------------------------------------------------------
systemctl enable --quiet bsk-account bsk-world
if [ $account_restart = 1 ] || ! systemctl is-active --quiet bsk-account; then
    systemctl restart bsk-account
fi

# --- gateway: a new one in a free slot, then the old one hands over ------------------------
# The gateway (bsk-gateway@<slot>) and the web client it serves live in /opt/bsk/gateway/<slot>.
# Before 2026-10-17 it was a single bsk-gateway service on slot a's port: that one can't hand
# over, so replacing it disconnects its players once.
gw_port() { case $1 in a) echo $BSK_GATEWAY_PORT ;; b) echo $((BSK_GATEWAY_PORT + 1)) ;; c) echo $((BSK_GATEWAY_PORT + 2)) ;; esac; }
gw_up() { systemctl is-active --quiet "bsk-gateway@$1"; }
gw_health() { curl -s --max-time 2 "http://127.0.0.1:$(gw_port $1)/healthz" | tr -d '\n' || true; }
legacy_gw=0
[ -f /etc/systemd/system/bsk-gateway.service ] && legacy_gw=1
gw_old=$(cat $GW/active 2>/dev/null || true)
gw_new=""
if [ -n "$gw_old" ] && gw_up "$gw_old" && [ $env_changed = 0 ] && ! unit_changed bsk-gateway@.service \
        && cmp -s $IN/bin/bsk-gateway $GW/$gw_old/bsk-gateway && diff -rq $IN/web $GW/$gw_old/web >/dev/null 2>&1; then
    say "gateway unchanged (slot $gw_old)"
else
    # a free slot: not the active one, not one still handing over (slot a's port is the old
    # single gateway's while that runs)
    for s in a b c; do
        { [ "$s" = "$gw_old" ] || { [ $s = a ] && [ $legacy_gw = 1 ]; } || gw_up $s; } && continue
        gw_new=$s
        break
    done
    if [ -z "$gw_new" ]; then
        # the other two are both still handing over: close the one that started first (its
        # players' clients reconnect by themselves)
        for s in a b c; do
            [ "$s" = "$gw_old" ] && continue
            if [ -z "$gw_new" ] || [ "$(cat $GW/$s/draining-since 2>/dev/null || echo 0)" -lt "$(cat $GW/$gw_new/draining-since 2>/dev/null || echo 0)" ]; then
                gw_new=$s
            fi
        done
        say "gateway slot $gw_new is still handing over ($(gw_health $gw_new)): stopping it"
        systemctl stop bsk-gateway@$gw_new
    fi
    say "starting the new gateway in slot $gw_new (port $(gw_port $gw_new))"
    case "$gw_new" in a|b|c) ;; *) echo "bad gateway slot '$gw_new'" >&2; exit 1 ;; esac
    rm -rf "/opt/bsk/gateway/$gw_new"
    mkdir -p $GW/$gw_new
    install -m 755 $IN/bin/bsk-gateway $GW/$gw_new/bsk-gateway
    cp -r $IN/web $GW/$gw_new/web
    echo "BSK_GATEWAY_SLOT_PORT=$(gw_port $gw_new)" > $GW/$gw_new/slot.env
    chown -R bsk:bsk $GW/$gw_new
    systemctl start bsk-gateway@$gw_new
    for _ in $(seq 60); do
        curl -fs --max-time 2 -o /dev/null "http://127.0.0.1:$(gw_port $gw_new)/healthz" && break
        sleep 0.5
    done
    if ! curl -fs --max-time 2 -o /dev/null "http://127.0.0.1:$(gw_port $gw_new)/healthz"; then
        journalctl -u bsk-gateway@$gw_new -n 20 --no-pager || true
        systemctl stop bsk-gateway@$gw_new || true
        echo "the new gateway didn't start: the old one keeps running" >&2
        exit 1
    fi
fi

# --- Caddy --------------------------------------------------------------------------------
# It lists every gateway slot and follows their /healthz, so a gateway swap needs no reload.
if [ -f $IN/Caddyfile ]; then
    systemctl enable --quiet caddy
    if ! cmp -s $IN/Caddyfile /etc/caddy/Caddyfile; then
        say "Caddyfile changed: reloading Caddy (open WebSockets stay)"
        install -m 644 $IN/Caddyfile /etc/caddy/Caddyfile
        systemctl reload-or-restart caddy
    fi
    systemctl is-active --quiet caddy || systemctl start caddy
fi

if [ -n "$gw_new" ]; then
    # Caddy checks each slot every second: let it see the new one before the old one says 503
    sleep 3
    if [ $legacy_gw = 1 ]; then
        say "removing the old single gateway (its players reconnect)"
        systemctl disable --now bsk-gateway.service 2>/dev/null || true
        rm -f /etc/systemd/system/bsk-gateway.service
        systemctl daemon-reload
        rm -rf /opt/bsk/web /opt/bsk/bin
    fi
    if [ -n "$gw_old" ] && gw_up "$gw_old"; then
        say "gateway slot $gw_old hands over ($(gw_health $gw_old) bridges): its players move at their next world change, 3 hours at most"
        date +%s > $GW/$gw_old/draining-since
        systemctl kill --signal=SIGUSR1 bsk-gateway@$gw_old
    fi
    echo $gw_new > $GW/active
    systemctl enable --quiet bsk-gateway@$gw_new
    for s in a b c; do
        [ $s = $gw_new ] || systemctl disable --quiet bsk-gateway@$s 2>/dev/null || true
    done
fi

# --- world servers ------------------------------------------------------------------------
# a world server change is a rolling deploy, kicking nobody (last: it can wait for the previous
# one's hand-over to finish)
if systemctl is-active --quiet bsk-world; then
    if [ $world_changed = 1 ]; then
        say "rolling deploy of the new world server (players switch at their next Nexus trip)"
        runuser -u bsk -- /usr/bin/python3 $ROOT/tools/rolling_restart.py request update
    else
        say "world server unchanged"
    fi
else
    say "starting the world servers"
    systemctl start bsk-world
fi

# --- the Discord bot (bsk-discord) ----------------------------------------------------------
# With a discord.env in the bundle (deploy/discord.local.env on the deploying PC), or one already
# installed: a deploy without one (GitHub Actions) updates the bot's code and keeps its settings.
# Without either the bot is left as it is. It holds no player's connection, so restarting it is
# safe. Its packages (discord.py) go in a venv of its own; a failure there leaves the game's
# deploy done.
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
            say "the Discord bot's packages didn't install: the bot isn't updated (the game is)"
        fi
    fi
    if [ -x $ROOT/discord/venv/bin/python ]; then
        [ ! -f $IN/discord.env ] || cmp -s $IN/discord.env $ROOT/discord/discord.env || { install -m 600 -o bsk -g bsk $IN/discord.env $ROOT/discord/discord.env; bot_restart=1; }
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

if grep -q '^BSK_BACKUP_REPO=.' $ROOT/backup/backup.env; then
    systemctl enable --quiet --now bsk-backup.timer
else
    systemctl disable --quiet --now bsk-backup.timer 2>/dev/null || true
fi

say "done"
systemctl --no-pager --lines=0 status bsk-redis bsk-account bsk-world 'bsk-gateway@*' caddy | grep -E '^(●|  *Active)' || true
for s in a b c; do
    gw_up $s && echo "gateway slot $s (port $(gw_port $s)): $(gw_health $s)"
done
true
