Release 2.4.6

## For players

- **Browser:** playing at play.dodmg.com picks up the settings and saved sign-in a browser kept at dodmg.com/play before the move (2.4.4), once, by itself.
- **Pets:** every pet drops half as often as before, wherever it drops (realm, godlands, events, dungeons, Doomed monsters; the Arena's pets are halved again on top of its own halving). A Lil' Oryx from Oryx goes from 1% to 0.5%, a realm critter from 0.0375-0.125% to 0.019-0.0625%. The wiki shows the new chances.
- **Game:** sound stays steady in big fights: a volley hitting many monsters is one hit sound, at most 3 copies of a sound and 16 effects play at once, and the mix is limited instead of clipping, so it no longer swings between too loud and too quiet.

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

### Steady sound in big fights

- Every hit, death and shot sound started its own voice with nothing to stop them stacking: with many monsters, dozens overlapped, summed past full scale and clipped (the "loud, then quiet" players heard), and each one decoded its MP3 on the audio thread, which could also make it stutter.
- `client-rs/app/src/audio.rs`: effects are decoded once when they arrive and kept as samples; they play through one mixer of their own behind a limiter (`LimitSettings::gaming()`, -3 dBFS); `Voices` drops the same effect again within 45 ms, a 4th copy of one effect still playing, and anything past 16 effects at once. Music is unchanged (its own path, decoded as it plays).
- Redeploy: the client (desktop and the gateway's web client). Nobody is disconnected.

### Pets drop half as often

- `source/WorldServer/logic/loot/Loots.cs`: `Loot.PetDropRate` (0.5) multiplies the chance of any item with a PermaPet activate (a pet's generator) in `Loot.Handle`, both the per-player roll and the OnePlayerLoot roll, everywhere; in the arenas `ArenaPools.PetRate` (0.5) still applies on top, as before. Every monster's drops go through `Loot.Handle` (Doomed-ported ones too, via `DoomedSetup.Drops`), so no behaviour file or generated file changed. Mystery boxes, the death spin, shops and the Pen Keeper are not drops and are unchanged.
- `tools/wiki_data.py`: `drops()` applies `PetDropRate` (read from Loots.cs) to pets' chances, so the Pets guide and gear pages show what really drops.
- SETUP-LOCAL.md, "Pets: single use, drops and attacks": notes the rate.
- Redeploy: the world server (rolling, nobody is disconnected) and the website (wiki). No Redis data changes.

## Checks

- `bash -n`; the same steps on dodmg.com: `systemctl is-enabled` shows `masked` for bsk-redis, bsk-account, bsk-gateway@a/b/c, bsk-backup.timer and .service, all inactive, with the other services still active.
- Browser settings: in the built-in browser (Chromium), with `bsk.localhost` as the old site and `play.bsk.localhost` as the new one (same site, different origins, as dodmg.com and play.dodmg.com), the real `takeOver` from index.html took the old settings over the new one's and the remembered login, left an unrelated key behind, and set the marker; a second visit changed nothing; a login made on the new site stayed; another site (`evil.localhost`) framing the hand-over page got no message. Website builds, the page outside the sitemap. Not tried on the live sites before the deploy, nor in Firefox or Safari.
- Steady sound: client tests (81, two new for the voice limits), desktop and wasm builds. Not listened to in a big fight on an isolated stack.
- Pets drop half as often: world server builds; the wiki built before and after: the Pets guide's chances all halved (Oryx's Lil' Oryx 1% to 0.5%, Bes 2% to 1%), no other page's text changed. Not tried on an isolated stack (a chance change, too rare to measure by killing).
