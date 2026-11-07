Release 2.4.2. Deploys the world server and publishes the desktop client (new objects, the Elder sprite sheet and the Gameplay option); the website and wiki update too. Nobody is disconnected.

## For players

- **Nexus:** the four shared vault chests are back, with everything that was left in them when they went in 2.3.9: wade north across the spawn's koi pond to the new vault yard, where they stand in a row in front of the storehouse.
- **Community goals:** Strength in Numbers also counts the stat potions drunk (or maxed) from the potion storage, not only from the inventory.
- **Dungeons:** the Sprite World and the Elder Sprite World are laid out anew every run from small, medium or large islands, and Limon leaves a still 7x7 platform where he dies, so his loot and portals are always in reach. Every Elder dungeon allows teleporting now, and every Elder boss can drop its dungeon's Potion of Max at 5% (Attack from the Elder Pirate Cave, Defense from the Elder Spider Den, Speed from the Elder Beach Zone, Dexterity from the Elder Jungle and Wisdom from Elder Davy Jones are new; Elder Septavius, Stheno and Limon drop theirs at 5% instead of nearly every kill). New: the Elder Ivory Wyvern, opened by the Elder Lair of Draconis's altar, by the Ivory Wyvern half the time and by an Elder Ivory Wyvern Key (from the Elder Lair's dragons and the Elder community goal): twice the health, +10 defence, harder hits and a third faster attacks, with Potions of Max Life and Mana, the Elder dragons' armours and bow, and T13/T14/T6 gear.
- **Oryx:** a Rogue's or Trickster's teleport or a Samurai's dash in the Oryx dungeons can't jump a shut gate any more, so Oryx 3 and 5 can't be woken early or joined once sealed.
- **Options:** a new Gameplay tab with "Drink stat potions from loot bags" (off by default): the plain stat potions in a bag you stand on are drunk by themselves, a maxed stat's go to potion storage, and Potions of Max and of Oryx are left alone.

The wiki is updated with these changes.

## Details

### Shared vault chests back in the Nexus

- **What:** a vault yard on the spawn island's north shore, across the pond from the spawn deck: the garden's storehouse (`NG Storehouse`) with the four shared vault chests in a row before it on a paved yard, bamboo fences at its sides, two stone lanterns, open to the pond (waded to, as the other shores). The north shore keeps the island symmetric (the south-east quarter is the south-west's mirror).
- **Why:** the owner asked for the chests back after they were removed with the pond's bridges on 2026-11-05.
- **Files:** `tools/build_nexus_islands.py` (`VAULT_YARD`, `VAULT_CHESTS`, placed in `lay_spawn`; `check` now requires all four chests with their names instead of none), regenerated `source/Shared/resources/worlds/Exalt/Nexus Islands 1.jm`; wiki (`getting-started.md`, `items.md`), `SETUP-LOCAL.md`, changelog.
- **Data:** the chests carry their castle tiles as their names again (`build_nexus.CHESTS`: `61_70`, `79_70`, `61_74`, `79_74`), so `NexusWorld.LinkVaultChests` links them to the same `nexusChests` fields in Redis, and whatever was left in them comes back. Nothing is migrated.
- **Deploy:** map only, picked up by the world server's rolling deploy; nobody is disconnected. No client or gateway change.
- **Not done:** the contents on the VPS weren't looked at (the PC's copy of the data has the chests empty).

### Sprite World: random layouts, a still platform where Limon dies

- **What:** the Sprite World and Elder Sprite World get a `<RandomLayout>` (passage and corridor 2, Limon's island the goal). Limon flies, so he could die over Space or on the conveyor floor; he now leaves a 7x7 platform of (Elder) White Alpha Square where he dies. `ChangeGroundOnDeath` skips squares off the map and sets each tile's description with its id.
- **Files:** `Dungeons.xml`, `DoomedRealmsWorlds.xml`, `ChangeGroundOnDeath.cs`, `BehaviorDb.SpriteWorld.cs`, `SpriteWorldTests.cs`, `SETUP-LOCAL.md`, changelog.
- **Deploy:** world server only.

### Elder dungeons: teleport, a Potion of Max from every boss; drink stat potions from loot bags

- **What:** every Elder dungeon allows teleporting (eight had `DisableTeleport` from Doomed Realms). Each Elder boss drops its dungeon's Potion of Max at 5%. Rust client: Options has a Gameplay tab with "Drink stat potions from loot bags" (off by default); the tabs take a row of their own when they don't fit beside the title. Translated into every table.
- **Files:** `tools/port_doomed_monsters.py`, `tools/port_doomed_worlds.py` and their output (`DoomedMonsters.cs`, `DoomedRealmsWorlds.xml`), the Beach Zone, Davy Jones, Forbidden Jungle, Pirate Cave and Spider Den behaviours, `ElderDungeonsTests.cs`, `client-rs/app/src/main.rs`, `options.rs`, `app/lang/*.json`, `tools/build_wiki.py`, wiki guides.
- **Deploy:** world server (drops, teleport) and the client (the setting).

### Oryx 3 and 5: no teleporting past their shut gates

- **What:** a Teleport ability (Rogue, Trickster) or a Samurai dash in an Oryx chain stage is refused unless the target can be walked to.
- **Files:** `Player.UseItem.cs`, `World.cs`, `SETUP-LOCAL.md`, changelog.
- **Deploy:** world server only.

### Strength in Numbers counts potions drunk from potion storage

- **What:** drinking (or maxing) from the potion storage counts towards the community goals' potion event. Personal quests are unchanged.
- **Files:** `UseStorageHandler.cs`, `community.json`, changelog.
- **Deploy:** world server only.

### The Elder Ivory Wyvern

- **What:** a new Elder dungeon built from the regular map (Elder tiles, walls and braziers) and the regular Wyvern's behaviours, now one builder for both: twice the health, +10 defence, harder hits, a third faster attacks. The Elder Lair of Draconis's altar opens it instead of the Ivory Wyvern; the regular Wyvern drops its portal half the time. Loot: T13 weapon, T14 armour, T6 ability and ring, Potion of Max Life and Mana (5% each), the four Elder dragons' armours and bow (1.5% each), Elder Ivory Wyvern and Elder Draconis Keys (2%). Elder Ivory Wyvern Key (0x7c04, portal 0x7c03) drops from the Elder Lair's dragons (1%) and is in the Elder community goal's loot; not sold in the Bazaar.
- **Files:** `tools/make_elder.py` (finds objects whatever their case), `tools/elder/types.json`, `tools/port_doomed_worlds.py` (`BS_ONLY_ELDER_WORLDS`), regenerated `ElderObjects.xml`, `ElderGrounds.xml`, `ElderAssets8x8.png`, `CustomObjects.xml`, `The Elder Ivory Wyvern.jm`; `BehaviorDb.Draconis.cs`, `BehaviorDb.Wyvern.cs`, `collection.json`, `community.json`, `tools/build_wiki.py`, `tools/wiki_data.py`, `website/wiki/dungeons.md`.
- **Deploy:** world server and client together (new objects, Elder sprite sheet).

## Checks

- Shared vault chests: `build_nexus_islands.py` builds and passes its checks; on an isolated stack (slot 2) the world server logged `Nexus: 4 vault chests`, and the Rust client walked from the spawn across the pond into the yard (`--walk-to`, `--shot`).
- Sprite World: `SpriteWorldTests` (40 layouts each, Limon reachable; the platform ground walkable and still).
- Elder dungeons: `ElderDungeonsTests` checks each boss's potion and the teleport; client tests (translations).
- Elder Ivory Wyvern: `ElderDungeonsTests`, wiki built.
- Release: client tests, `python tools\build_site.py` and the server builds run on the release branch before the pull request.
