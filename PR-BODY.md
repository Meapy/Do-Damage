Release 2.6.11

## For players

### Dungeons
- **The Frozen Court fights like the Tomb of the Ancients' gods, harder:** the Crucible floors, banners, caches, shades and last stands stay, and each of the three now runs on the Tomb's turns. Every new turn opens with a taunt and 3 s behind a blue shield (untouchable). At 40% two **relics** come out (one more at 25%), shards of ice that circle their maker 2 tiles out and fire a three-shot fan every 2-2.4 s until shot down or their maker falls: Kaldrun's slow (Rime Relic, 5,000 health, 45 defence), Ysolde's darken (Aurora Relic, 4,000, 25), Hrimgar's bleed (Reaver Relic, 4,500, 35). Below 6% each **rages**: glowing red, chasing players, its whole kit plus its last stand's attacks and two more relics; the last one standing still makes its last stand below 15% and rages in it below 6%. Each also has a new turn: **Kaldrun** (as Bes) a ring of 20 snowflakes every 6 s from 80% and Armored while his relics are out; **Ysolde** (as Nut) a turning cross of quieting violet ribbons from 80%, and Aurora's Grace now keeps coming every 30 s through her relic turns; **Hrimgar** (as Geb) calls up to three hounds and lobs hailstones from 75%. His **Frenzy** at 50%, which he skipped (see Details), now comes every time.

## Details

### The Frozen Court on the Tomb's turns

- **What and why:** the owner asked for the Ice Tomb's bosses to be like the Tomb of the Ancients' and chose to keep the floors and add the Tomb's phases, a harder Tomb. Each boss: health turns with a 3 s Invulnerable "blue shield" (`CourtTurn`), relics at 40% and 25% (`Spawn` on entering the turn, `Relic`: orbit the maker, fire, suicide when it's gone), a rage below 6% (`Rage` and `Last Rage`, the latter ordering the Court Heart into the last stand if it isn't already). Kaldrun 80 Rime Wall / 60 Bulwark / 40, 25 Relics / 15 / 6; Ysolde 80 Ribbons / 50 Grace / 40, 25 Relics (Grace every 30 s in each, `GraceCycle`) / 15 / 6; Hrimgar 75 Pack / 50 Frenzy / 40, 25 Relics / 15 / 6. Health, defence and per-player scaling (`BossStats`) unchanged.
- **Bug fixed:** a behaviour's states share one list of names, the last winning. Hrimgar's `ReaverCycle` built "Hunt", "Leap" and so on three times, so his first hunt's leap went into his Fight 3 copy, which has no 50% transition: he never reached his Frenzy. The cycles are now named per turn; `BehaviorStatesTests.FrozenCourtStatesAreTheirOwn` checks the three's names are unique and their turns exist. Noted in source/AGENTS.md.
- **Fits the hall:** the hall's floor is 19.5 tiles across from the middle, thrones at 13.5, pillars at 15 (`IceCourt.cs`, `TombCourtPlan`); the floors reach 18. Ysolde circles at 9.5 (11 before) while her relics are out and the relics stay within 2.4 of their maker, so they clear the thrones; everything moves with `ValidateAndMove`, so a raging boss chasing to the edge keeps its relics on the floor. The map (`Ice Tomb.jm`) is unchanged.
- **Files:** `source/WorldServer/logic/db/BehaviorDb.IceTomb.cs`, `tools/build_ice_tomb.py` (`RELICS`), `tools/ice_tomb/art.py` (`relic`, `relics`), regenerated `IceTombObjects.xml` (both copies), `IceTombChars16x16.png` (three rows added at the end, nothing moved) and `tools/ice_tomb/types.json` (0xCA86-0xCA88), `source/Tests/BehaviorStatesTests.cs`, `source/AGENTS.md`, the changelog, `website/wiki/dungeons.md`.
- **Deploys:** the world server and the client together (the relics are new objects; a client without them can't draw them). Nobody is disconnected. Redis: nothing.
- **Not done:** not played in game; the numbers (relic health, rage kits) may want tuning after a run.

### Working notes brought up to date

The four AGENTS.md guides checked against the code and rewritten: nothing players see changes, and
nothing redeploys.

- `client-rs/AGENTS.md`: the testing notes regrouped by topic (pitfalls, the bot, the scripted
  flags, editing the test copy's data) instead of a dated line each; duplicates and
  contradictions merged (window size, which character plays, when the world server is ready); the
  fixed `--effect` limit dropped; the build line fixed (`cargo run -p bsk-client`); undocumented
  flags added (client `--look`, `--champions`, `--display-of`; bot `--char`, `--vault`).
- `source/AGENTS.md`: sections merged (quests, pass and commands), testing tips gathered under
  "Build and test", the `alive.<id>` set and the test copy's redis-cli example (no longer the
  owner's account) added.
- `client/AGENTS.md`: cut to what still matters for a deprecated client: its `src/` as the game's
  data, and the essentials if the owner asks for a change; the AIR test client and the old
  address dropped.
- `AGENTS.md`: how to pick the release branch when old ones linger, rebasing after a release is
  squashed mid-session, naming the Doomed Realms content, folding testing pitfalls into the notes
  they belong with.

## Checks

- Frozen Court: the world server builds; `dotnet test` (BehaviorStates, IceTomb, ArenaSeason2, ElderDungeons: 110 passed); `build_ice_tomb.py` rerun changes only the relics (map unchanged); the relic sprites looked at; `build_wiki.py` builds.
- Every path, file and flag the guides name checked against the tree and the argument parsers.
