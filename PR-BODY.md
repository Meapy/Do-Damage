Release 2.4.11: show Necromantic Boss drops and restore Candyland waves (deploys world servers and website; nobody is disconnected)

## For players

- **Vault:** the Collection's Dungeons view shows the 19 unique items from Empowered Elder bosses
  under **Drops from Necromantic Boss**; hovering an item shows that source.
- **Dungeons:** Candyland rooms spawn waves of ten creatures. Clear the wave and leave the room for
  the next boss; defeated bosses no longer leave a realm portal behind.

## Details

### Necromantic Boss drops in the Collection

- `tools/build_collection.py` adds each Empowered Elder boss's unique untiered item from
  `resurrection.json` to a **Drops from Necromantic Boss** source group. Regenerated
  `source/Shared/resources/data/collection.json`; this adds them as a source group in the Dungeons
  view.
- Updated the player changelog, the Items wiki page and SETUP-LOCAL.md.
- Redeploy: world servers (to load the catalogue) and website (updated wiki); no client release or protocol change, no Redis migration, and nobody is disconnected.

### Candyland waves and bosses

- `BehaviorDb.Candyland.cs`: each of the six map spawners now summons ten random Candy Creatures
  when a player enters, then respawns a wave after it is cleared. Its boss spawner waits until
  local Candy Creatures are gone and all players leave the room before choosing a boss; this
  repeats after each boss. Removed the Candy Gnome's portal-on-death behavior.
- Redeploy: world servers only; no client release or protocol change, no Redis migration, and nobody is disconnected.

## Checks

- `python tools/build_collection.py --check` passes; verified all 19 items in `resurrection.json`
  are in the new group.
- `python tools/build_wiki.py --out build\collection-wiki-check` passes (22 pages built).
- `dotnet build source\WorldServer\WorldServer.csproj -c Debug -o build\wscheck` passes.
