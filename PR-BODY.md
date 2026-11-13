Release 2.5.6

## For players

- **Game:** the screen shake from an earthquake (Empowered bosses' phases, such as the Marble Colossus's Earthsplitter in the Lost Halls, Feargus, Geb and the Shatters' bosses) lasts about two and a half seconds and settles, instead of shaking harder and harder until you left the dungeon. Another earthquake starts it again.

## Details

### Earthquake shake stops

The client's `ShowEffect` Earthquake (14) ported the AIR client's `Camera.startJitter`: the shake started once, grew to half a tile over ten seconds and only stopped on a world change, so the Empowered signature phases that use it (`EmpoweredSignatures.cs`) left the screen shaking for the rest of the fight.

- **Client** (`client-rs/app/src/particles.rs`): `jitter` rises to a quarter of a tile in 0.2 s and fades to nothing by 2.5 s, then clears; each Earthquake restarts it. Test `flash_and_jitter` covers the stop and the restart.
- **Server:** no change, only the comment in `BossRemains.cs`.
- Files: `client-rs/app/src/particles.rs`, `source/WorldServer/core/resurrection/BossRemains.cs`, `client-rs/CHANGELOG.md`, SETUP-LOCAL.md.
- **Redeploy:** the client only. Nobody is disconnected; nothing in Redis. Players keep the endless shake until they update.

## Checks

- **Earthquake shake stops:** `cargo test -p bsk-client particles` (7 passed). Not looked at in a live fight.
