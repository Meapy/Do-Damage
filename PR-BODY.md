Release 2.5.6

## For players

- **Community goals:** the bonus goals that follow a goal finished early are easier: a quick finish makes the next one at most twice its usual size (was three times), and a chain of quick finishes tops out at 2.5 times (was 4). The bonus goals running when this deploys shrink to match by themselves, and a goal finished early comes back at most twice as hard the next time it's drawn (was three times).
- **Game:** the screen shake from an earthquake (Empowered bosses' phases, such as the Marble Colossus's Earthsplitter in the Lost Halls, Feargus, Geb and the Shatters' bosses) lasts about two and a half seconds and settles, instead of shaking harder and harder until you left the dungeon. Another earthquake starts it again.

## Details

### Earthquake shake stops

The client's `ShowEffect` Earthquake (14) ported the AIR client's `Camera.startJitter`: the shake started once, grew to half a tile over ten seconds and only stopped on a world change, so the Empowered signature phases that use it (`EmpoweredSignatures.cs`) left the screen shaking for the rest of the fight.

- **Client** (`client-rs/app/src/particles.rs`): `jitter` rises to a quarter of a tile in 0.2 s and fades to nothing by 2.5 s, then clears; each Earthquake restarts it. Test `flash_and_jitter` covers the stop and the restart.
- **Server:** no change, only the comment in `BossRemains.cs`.
- Files: `client-rs/app/src/particles.rs`, `source/WorldServer/core/resurrection/BossRemains.cs`, `client-rs/CHANGELOG.md`, SETUP-LOCAL.md.
- **Redeploy:** the client only. Nobody is disconnected; nothing in Redis. Players keep the endless shake until they update.

### Bonus goals eased

Bonus goals after an early finish were too hard to finish: each quick finish could make the next goal three times its size, compounding to four.

- **Numbers** (`source/Shared/resources/data/community.json`, defaults in `CommunityData.cs`): `early.maxStep` 3 → 2, `early.maxHeat` 4 → 2.5. `maxStep` also caps how much a goal finished early grows for its next run.
- **Current ones** (`CommunityData.Recap`, `Runs`): the heat stored in `community:week:<yyyyMMdd>:early` is read at today's limits, each run's step from the one before held to `maxStep`, the whole to `maxHeat`. In `CommunityGoals.Refresh`, when a server first reads a running bonus goal whose `goal` in Redis is bigger than that gives, it lowers it (logged "eased from ... to ..."); it never raises one. So the deploy eases this week's running bonus goals on its own; nothing to do by hand. A goal lowered past a tier's line is claimable at once; the announcement comes with the next count.
- **Not changed:** the scales (`community:scale`) already grown by earlier early finishes stay as they are; `adapt` brings them back down week by week.
- Files: `community.json`, `Shared/community/CommunityData.cs`, `WorldServer/core/community/CommunityGoals.cs`, `Tests/CommunityDataTests.cs`, `website/wiki/community-goals.md`, `client-rs/CHANGELOG.md`, SETUP-LOCAL.md.
- **Redeploy:** the world server (and the website for the wiki). Nobody is disconnected. Redis: running bonus goals' `goal` field lowered once by the new server. During the rolling hand-over the old world server keeps the old goal until it stops.

## Checks

- **Earthquake shake stops:** `cargo test -p bsk-client particles` (7 passed). Not looked at in a live fight.
- **Bonus goals eased:** `dotnet test source/Tests` (279 passed; `CommunityDataTests` cover `Recap` and old heats of 3 and 4 read at the new limits); the WorldServer builds; the wiki builds. Not tried on an isolated stack, and the live bonus goals' sizes weren't read before the change.
