Release 2.4.6

## For players

## Details

### Retiring the old machine's game services

- `deploy/remote/install-site.sh` `--retire-game`: `systemctl mask` refuses while a unit's own file is in `/etc/systemd/system`, where install.sh put bsk-redis, bsk-account and the backup units, so on 2026-11-07 those were only disabled (the gateway instances were masked), and the backup timer, disabled without `--now`, was still running: it could have backed up the old machine's stale Redis beside the game machine's backups (it hadn't run since the move). The units' files now move to `/opt/bsk/retired-units` first, then everything is masked, `bsk-backup.service` included. Each deploy says so when one of them is still installed and not masked.
- Done by hand on dodmg.com (169.58.244.146) the same way: all seven units masked, the backup timer stopped; caddy, bsk-discord, bsk-redis-tunnel and Testing's bsk-world untouched.
- SETUP-LOCAL.md, "Two machines": says where the files go.
- Redeploy: nothing needed (the website machine already has it by hand; the next deploy brings the script). Players notice nothing.

## Checks

- `bash -n`; the same steps on dodmg.com: `systemctl is-enabled` shows `masked` for bsk-redis, bsk-account, bsk-gateway@a/b/c, bsk-backup.timer and .service, all inactive, with the other services still active.
