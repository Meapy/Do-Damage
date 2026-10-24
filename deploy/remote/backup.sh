#!/bin/bash
# Backs up the game's Redis data to a private git repository, run by bsk-backup.timer as the bsk
# user (see deploy/backup.py). Each run takes a fresh snapshot (redis-cli --rdb: a consistent
# dump.rdb, made without touching the files Redis writes), checks it, and commits and pushes it
# with the deploy key in /opt/bsk/backup, which can write to that repository only.
#
# Restore: python deploy/backup.py --pull <folder>, then
#          python deploy/deploy.py --import-data <folder> --force-data
set -euo pipefail

ROOT=/opt/bsk
B=$ROOT/backup
set -a; . $ROOT/bsk.env; [ -f $B/backup.env ] && . $B/backup.env; set +a

if [ -z "${BSK_BACKUP_REPO:-}" ]; then
    echo "no backup repository set (backup_repo in the deploy config): nothing to do"
    exit 0
fi
[ -f $B/deploy_key ] || { echo "no deploy key in $B: run python deploy/backup.py --setup" >&2; exit 1; }
export GIT_SSH_COMMAND="ssh -i $B/deploy_key -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new -o UserKnownHostsFile=$B/known_hosts"
git_() { git -C $B/repo -c user.name="Do Damage backup" -c user.email="backup@$(hostname)" "$@"; }

if [ ! -d $B/repo/.git ]; then
    git clone -q "$BSK_BACKUP_REPO" $B/repo
fi
git_ remote set-url origin "$BSK_BACKUP_REPO"
# another machine (an old VPS, a restore test) may have pushed meanwhile
git_ fetch -q origin
git_ rev-parse -q --verify origin/main >/dev/null && git_ reset -q --soft origin/main

redis-cli -p "$BSK_REDIS_PORT" --rdb $B/repo/dump.rdb.new >/dev/null 2>$B/rdb.log || { cat $B/rdb.log >&2; exit 1; }
if command -v redis-check-rdb >/dev/null; then
    redis-check-rdb $B/repo/dump.rdb.new >/dev/null || { echo "the snapshot doesn't check out: not pushed" >&2; exit 1; }
fi
mv $B/repo/dump.rdb.new $B/repo/dump.rdb

keys=$(redis-cli -p "$BSK_REDIS_PORT" dbsize)
cat > $B/repo/README.md <<EOF
# Do Damage: database backups

The game's Redis data (accounts, characters, vaults, guilds, the market), written by
\`deploy/remote/backup.sh\` on the VPS every 6 hours. Each commit is one snapshot; \`dump.rdb\`
is the latest. Keep this repository private: it holds players' emails and password hashes.

Restore onto a server (from the game's repository):

    python deploy/backup.py --pull <folder>          # this repository, or --at <commit> for an older one
    python deploy/deploy.py --import-data <folder> --force-data

Last snapshot: $(date -u '+%Y-%m-%d %H:%M UTC') from $(hostname), $keys keys, $(stat -c %s $B/repo/dump.rdb) bytes.
EOF

git_ add dump.rdb README.md
git_ commit -q -m "Snapshot $(date -u '+%Y-%m-%d %H:%M UTC'): $keys keys"
git_ push -q origin HEAD:main
echo "pushed a snapshot of $keys keys ($(stat -c %s $B/repo/dump.rdb) bytes)"
