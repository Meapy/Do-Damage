Release 2.4.3. Deploys the world server; the website and wiki update too. Nobody is disconnected.

## For players

- **Pets:** the pets realm and godland monsters drop are four times rarer: the realm's critters 0.0375-0.125% a kill (were 0.15-0.5%), the gods' and quest monsters' pets 0.125% (were 0.5%), the Abyssal Kraken's, the Wyrm Matriarch's and the Sumo Master's Baby 0.25% (were 1%) and the Lucky Ent God's Leprechaun 0.625%. Realm events, dungeons and the minigames drop theirs as before.

The wiki is updated with these changes.

## Details

### Realm and godland pet drops cut by 75%

Every pet generator in the lands' behaviours (`BehaviorDb.Shore.cs`, `Lowland.cs`, `Midland.cs`, `Highland.cs`, `Mountain.cs`) and in Realm2's (`BehaviorDb.Realm2.cs`, the `R2Mob` and `R2Quest` extras) drops at a quarter of its chance: 56 lines. Left as they were: the realm events (the Beer God in `Highland.cs`, and the events' own files: CubeGod, SkullShrine, Sphinx, Pentaract, LotLL, Hermit, RockDragon, GhostShip, Crystal, Cyclops, Woodland's Megamoth), the dungeons, and the pets in `DoomedExtraLoot` (Birdman Chief's Eagle, the minigames' sheep, chicken and turkey). The wiki reads the rates from the behaviours. World server only; no data in Redis changes.

## Checks

- Realm and godland pet drops: `dotnet build source\WorldServer\WorldServer.csproj -o build\wscheck` (0 errors); `tools/build_wiki.py` into a scratch folder, the Pets page showing the new rates (Brown Pup from the Hobbit Rogue at 0.037%).
