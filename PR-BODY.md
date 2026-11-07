Release 2.4.6

## For players

- **Browser:** playing at play.dodmg.com picks up the settings and saved sign-in a browser kept at dodmg.com/play before the move (2.4.4), once, by itself.

## Details

### Retiring the old machine's game services

- `deploy/remote/install-site.sh` `--retire-game`: `systemctl mask` refuses while a unit's own file is in `/etc/systemd/system`, where install.sh put bsk-redis, bsk-account and the backup units, so on 2026-11-07 those were only disabled (the gateway instances were masked), and the backup timer, disabled without `--now`, was still running: it could have backed up the old machine's stale Redis beside the game machine's backups (it hadn't run since the move). The units' files now move to `/opt/bsk/retired-units` first, then everything is masked, `bsk-backup.service` included. Each deploy says so when one of them is still installed and not masked.
- Done by hand on dodmg.com (169.58.244.146) the same way: all seven units masked, the backup timer stopped; caddy, bsk-discord, bsk-redis-tunnel and Testing's bsk-world untouched.
- SETUP-LOCAL.md, "Two machines": says where the files go.
- Redeploy: nothing needed (the website machine already has it by hand; the next deploy brings the script). Players notice nothing.

### Browser settings carried over from dodmg.com

- Since 2.4.4 browser play is at play.dodmg.com, and browsers keep saved data per address, so players found their settings (and remembered login) gone.
- `website/storage-handover.html` (new, on dodmg.com): in a frame, it posts this site's `betterSkillys-rs.` keys to `https://play.<its host>` only (postMessage's target origin: no other page gets them). `noindex`, and `tools/build_site.py` leaves such pages out of the sitemap.
- `client-rs/web/index.html`: before the game starts (the wasm download runs meanwhile), the first load on an https address with a parent domain loads `https://<parent>/storage-handover.html` in a hidden frame, waits up to 3 s for its message from that origin and frame, and stores what it sent: the settings replace any made here since the move (what players asked for), the login and guest keys only where there's none here. `betterSkillys-rs.handover` marks it done (not set when no answer came, so it tries again next time). Same site (dodmg.com), so the frame sees the first-party storage in Chrome, Firefox and Safari.
- Redeploy: the gateway (the web client) and the website. Nobody is disconnected.

## Checks

- `bash -n`; the same steps on dodmg.com: `systemctl is-enabled` shows `masked` for bsk-redis, bsk-account, bsk-gateway@a/b/c, bsk-backup.timer and .service, all inactive, with the other services still active.
- Browser settings: in the built-in browser (Chromium), with `bsk.localhost` as the old site and `play.bsk.localhost` as the new one (same site, different origins, as dodmg.com and play.dodmg.com), the real `takeOver` from index.html took the old settings over the new one's and the remembered login, left an unrelated key behind, and set the marker; a second visit changed nothing; a login made on the new site stayed; another site (`evil.localhost`) framing the hand-over page got no message. Website builds, the page outside the sitemap. Not tried on the live sites before the deploy, nor in Firefox or Safari.
