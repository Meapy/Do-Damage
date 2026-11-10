Release 2.4.11: show Necromantic Boss drops and keep Candyland bosses coming (deploys world servers and website; nobody is disconnected)

## For players

- **Vault:** the Collection's Dungeons view shows the 19 unique items from Empowered Elder bosses
  under **Drops from Necromantic Boss**; hovering an item shows that source.
- **Dungeons:** Candyland bosses return after each clear, so the dungeon keeps going while players stay inside.

## Details

### Necromantic Boss drops in the Collection

- `tools/build_collection.py` adds each Empowered Elder boss's unique untiered item from
  `resurrection.json` to a **Drops from Necromantic Boss** source group. Regenerated
  `source/Shared/resources/data/collection.json`; this adds them as a source group in the Dungeons
  view.
- Updated the player changelog, the Items wiki page and SETUP-LOCAL.md.
- Redeploy: world servers (to load the catalogue) and website (updated wiki); no client release or protocol change, no Redis migration, and nobody is disconnected.

### Endless Candyland bosses

- `BehaviorDb.Candyland.cs`: the Candyland boss spawner now begins another encounter when a player is within 16 tiles after the previous boss is defeated. It had been waiting for the room to be empty, leaving the dungeon with no boss if players stayed nearby.
- Redeploy: world servers only; no client release or protocol change, no Redis migration, and nobody is disconnected.

## Checks

- `python tools/build_collection.py --check` passes; verified all 19 items in `resurrection.json`
  are in the new group.
- `python tools/build_wiki.py --out build\collection-wiki-check` passes (22 pages built).
- `dotnet build source\WorldServer\WorldServer.csproj -c Debug -o build\wscheck` passes.
