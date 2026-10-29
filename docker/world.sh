#!/bin/sh
# The world container's command: what bsk-world.service does under systemd. ExecStart is
# rolling_restart.py serve (a world server, then the world proxy), ExecStop rolling_restart.py
# stop, which has each world server save everyone before it exits. docker stop sends SIGTERM:
# give it the time that takes (docker-compose.yml's stop_grace_period).
set -e
python3 /opt/bsk/docker/configure.py world /opt/bsk/worldserver/wServer.json
python3 /opt/bsk/tools/rolling_restart.py --from /opt/bsk/worldserver serve &
serve=$!
stopping=0
trap 'stopping=1' TERM INT
wait $serve || true
if [ $stopping = 1 ]; then
    python3 /opt/bsk/tools/rolling_restart.py stop
    wait $serve 2>/dev/null || true
    exit 0
fi
echo "the world proxy exited" >&2
exit 1
