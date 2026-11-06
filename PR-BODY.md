Release 2.4.2. Servers only so far (a map change), so the merge deploys the world server; no client is published.

## For players

- **Nexus:** the four shared vault chests are back, with everything that was left in them when they went in 2.3.9: wade north across the spawn's koi pond to the new vault yard, where they stand in a row in front of the storehouse.

The wiki is updated with these changes.

## Details

### Shared vault chests back in the Nexus

- **What:** a vault yard on the spawn island's north shore, across the pond from the spawn deck: the garden's storehouse (`NG Storehouse`) with the four shared vault chests in a row before it on a paved yard, bamboo fences at its sides, two stone lanterns, open to the pond (waded to, as the other shores). The north shore keeps the island symmetric (the south-east quarter is the south-west's mirror).
- **Why:** the owner asked for the chests back after they were removed with the pond's bridges on 2026-11-05.
- **Files:** `tools/build_nexus_islands.py` (`VAULT_YARD`, `VAULT_CHESTS`, placed in `lay_spawn`; `check` now requires all four chests with their names instead of none), regenerated `source/Shared/resources/worlds/Exalt/Nexus Islands 1.jm`; wiki (`getting-started.md`, `items.md`), `SETUP-LOCAL.md`, changelog.
- **Data:** the chests carry their castle tiles as their names again (`build_nexus.CHESTS`: `61_70`, `79_70`, `61_74`, `79_74`), so `NexusWorld.LinkVaultChests` links them to the same `nexusChests` fields in Redis, and whatever was left in them comes back. Nothing is migrated.
- **Deploy:** map only, picked up by the world server's rolling deploy; nobody is disconnected. No client or gateway change.
- **Not done:** the contents on the VPS weren't looked at (the PC's copy of the data has the chests empty).

## Checks

- Shared vault chests: `build_nexus_islands.py` builds and passes its checks; on an isolated stack (slot 2) the world server logged `Nexus: 4 vault chests`, and the Rust client walked from the spawn across the pond into the yard (`--walk-to`, `--shot`).
