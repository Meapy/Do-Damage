Release 2.4.11: the Black Between, the Mysterious One's dungeon; Necromantic Boss drops in the Collection (deploys the client, servers and website; nobody is disconnected)

## For players

- **The Black Between:** a new endgame dungeon beyond Oryx 5, for players in Enthroned gear. The Mysterious One, the voice of Realm 2, and his children Vesper, the Dusk Star, and Aurel, the Dawn Blade, fight three trials on a platform floating in the void, each with survival phases, and before each sickening one he heals you to full. Every shot of theirs (bosses, copies, echoes, spectres, Dusk Mines and the Gravity Well) goes on through whoever it hits, so a couple of players in front can't take the hits for the group: everyone has to dodge. It has its own music and boss sounds.
- **The way in:** when Oryx the Ascended God falls, the Mysterious One's Rift opens in the Elysium a quarter of the time, and every time during the event The Voice in the Void (10 November to 1 December).
- **Loot:** life and mana potions (about Oryx 5's count), every Max potion at 2.5%, the Unstable Essence, and six new abilities with powers of their own (white bags, 1% each): Quiver of the Solar Cyclone, Trinity Trap, Helm of the Dawn Blade, Prism of the Dusk Veil, Cloak of the Planar Rift and Vial of Cascading Dusk.
- **Corruption:** the Cosmic Anvil he leaves behind spends an Unstable Essence to change a piece of Enthroned gear, for better or worse; corrupted gear has a purple edge. What it does is left for players to find out.
- **Oryx 3 and Oryx 5:** the gates that shut behind you in Oryx's Sanctuary and Oryx's Elysium now show as they close; before, they blocked the way unseen.
- **Boss health:** a boss scaled up for a bigger group (the Oryx chain's, the Black Between's) scales back down when players leave the dungeon, keeping the fraction of health it had.
- **Game:** imprinted gear's border is thinner; Darkness darkens your view; ground telegraphs show on raised floors (the Nexus islands) too.
- **Vault:** the Collection's Dungeons view shows the 19 unique items from Empowered Elder bosses under **Drops from Necromantic Boss**, and the Black Between's six abilities in a group of their own.

## How it works

- **Content** comes from one generator, `tools/build_black_between.py` (art in `tools/black_between/`): the sprites, the item and monster XML (server and client copies, types 0x8D00-0x8DFF), the map and `types.json`. The music and sounds come from `tools/black_between/sound.py` (MP3s committed under `web/music` and `web/sfx/monster`).
- **Shots pierce:** every enemy shot in the dungeon is `<MultiHit/>` (the generator's `foe()`; 18 shots gained it, the players' items are unchanged). The client then lets a shot that hits another player fly on to the next, and the server keeps each player's copy live (`OtherHit` and `PlayerHit` only disable a shot without MultiHit), so no server code changes. The client bundles the XML, so this needs the new client; an older one still stops the shot on the first player it reaches, for itself only.
- **The fight** is `BehaviorDb.BlackBetween.cs`, with its behaviours in `logic/behaviors/BlackBetween.cs`. The invisible Observatory Heart at the middle runs the arena (floors, starfall, the Constellation Path). Ground telegraphs use a new `ShowEffect` 40 (`GroundMark`), drawn by the Rust client; older clients see only the hits. The world gives players a sight radius of at least 35 there (`<MinSightRadius>`).
- **The way in:** `OpenTheBetween` on Oryx the Ascended God, with an event flag `mysteriousOne` (`ServerEvents.cs`, the event in `wServer.json`).
- **The abilities'** powers are `<RelicPower>`s worked in `Player.BetweenPowers.cs`.
- **Corruption:** `CorruptHandler.cs`, `Shared/resources/Corruption.cs`, the client's anvil tab (`forge/corrupt.rs`), and `ItemData` gains `Corruption` and `Unique` (JSON, left out when empty: no migration). **New messages:** `CORRUPT` (160) and `CORRUPT_RESULT` (161), numbered clear of yours, so the client and servers ship together.
- **Fixes beside it:** `PlaceObject` now gives placed objects an id and refreshes players' squares (the Oryx 3 and 5 gates); the client draws Darkness and puts ground marks on the floor they came at.
- **Boss scaling:** `BossStats` now takes a player's share of added health off again when they leave the world (gone out, dead, disconnected), keeping the boss's health fraction; walking out of range changes nothing, so it can't be used to shrink a boss.
- **The Collection:** `tools/build_collection.py` adds the Empowered Elder bosses' unique items; `collection.json` regenerated.
- **Translations:** the anvil's lines are in every language table. The wiki shows the dungeon, the gear and a Corruption section; the changelog and SETUP-LOCAL.md are updated.
- **Deploy:** client, servers and website. Nobody is disconnected and no Redis data changes.

## Checks

- Piercing shots: `build_black_between.py` regenerated (only the 18 `<MultiHit />` lines change), a client test that a MultiHit monster shot passes a player in front and hits the one behind while a plain one stops (`a_monsters_multi_hit_shot_goes_on_through_another_player`), the `bsk-data` and `bsk-world` tests, and the wiki build (the dungeon's card gains a Fight row).
- Server tests (197) and client tests (87) pass; `build_collection.py --check` and the wiki build pass.
- Boss scaling with a second player (a bot): 504,000 alone, 680,400 with two, smitten to 85.3%, and 430,007 / 504,000 (85.3%) once the bot left.
- On an isolated stack: a bot through the whole fight, and the client's screenshots of every phase, the way in after Oryx 5, the anvil and three corruptions, and each of the six abilities in use.
