Release 2.5.2: the Elder Shatters, smaller Candyland waves whose bosses reliably follow them, `/wiki` and a new Potion of Maxy sprite (deploys the client, the servers and the website; nobody is disconnected)

## For players

- **The Elder Shatters:** a new Elder dungeon. The Forgotten King leaves its portal half the time, and an Elder Shatters Key (1% from its Bridge Sentinel and Twilight Archmage, 2% from its Forgotten King) opens it anywhere. It is the Shatters by brazier light with every monster in its Elder version (twice the health, harder hits, faster attacks), and each boss has a phase of its own:
  - **The Bridge Sentinel's Judgement,** below 45% health: armoured, it spins a spiral of slowing blades one way and heavy bolts the other, and throws Blobombs at you between its swarms.
  - **The Twilight Archmage's Eclipse:** he throws fire portals as well as ice ones while he casts, and where the regular Archmage stands spent after his last blast, he calls an Eclipse (two spirals turning against each other, rings of heavy orbs, firebombs) and goes round his generators again.
  - **The Forgotten King remembered:** at a tenth of his health he doesn't die. He comes out of hiding for a last stand, fire and ice turning round him, with his guardians back at his side.
- **Its loot:** a Potion of Max Defense, Mana and Life (5% each, one per boss), T13 weapons, T14 armour, T6 abilities and rings, the Shatters' own rings at twice their chance, and three rings found nowhere else (1% each, white bag, soulbound): **Bracer of the Elder Guardian** (below half your health, +12 defense for 5 seconds, every 20 seconds), **The Eclipsed Gemstone** (each kill restores 15 mana) and **The Remembered Crown** (using your ability gives +8 attack for 3 seconds).
- **The Shatters' bosses are quests:** in the Shatters and the Elder Shatters the quest arrow leads to the Bridge Sentinel, then the Twilight Archmage, then the Cursed Crown and the Forgotten King it becomes, each with its name and health at the top of the screen. The Realm Portal out opens where the King falls (it used to come from his loot balloon), and clearing the Shatters now counts for quests and community goals. The Elder Shatters also counts towards Raid Night.
- **Raising them:** all three leave remains for a Necromantic Relic, which makes 22 bosses. Empowered, each drops what its loot balloon held and an item of its own (6%): **Snare of the Bridge Sentinel** (a trap that also throws a Blobomb: 700 damage within 3 tiles, 2 seconds later), **Twilight Cataclysm** (a spell whose casts alternate fire, a 450 burn, and ice, 250 and a slow) and **Shroud of the Forgotten King** (a cloak: for 5 seconds after using it your shots deal a fifth more, ignoring defense). The Collection lists all six.
- **Dungeons:** Candyland rooms spawn six creatures at a time instead of ten. Clear the wave and
  leave the room to bring out a boss; the next wave waits until that boss is defeated.
- **Game:** Type `/wiki` in chat to get a link to the Do Damage wiki.
- **Items:** The Potion of Maxy has a new sprite.

## Details

### Potion of Maxy sprite

The supplied 8×8 sprite is overlaid on unused tile `0x7A` of `drLofiObj2` by
`tools/port_doomed.py` from `tools/doomed/potion_maxy.png`; its texture override points the Potion
of Maxy at that sheet tile. Both generated `DoomedRealms.xml` copies reference the new tile.
Redeploy the client and world server; nobody is disconnected and no Redis data changes.

### The Elder Shatters

- **What changed:** `BehaviorDb.Shatters.cs` is one builder for the Shatters and the Elder Shatters (`ShattersBehaviours(db, elder)`), as the other Elder dungeons are. The regular Shatters is unchanged except that the Forgotten King drops the Elder Shatters Portal at 50%.
- **Hidden objects:** the Shatters runs on objects that watch each other by name (switches, gates, bridge closers, timers, chest spawners). `tools/make_elder.py` (`SCRIPTED_DUNGEONS`) gives every object its behaviours define an Elder copy, 76 in all, and `BehaviorDb.Elder.cs` `ElderGrounds` names the Elder grounds (with their brazier-lit copies) for the bridges and lava the behaviours change. `ReplaceTile` takes several grounds; a state's null children are skipped, for `elder ? x : null`.
- **Map and world:** `worlds/Doomed/The Elder Shatters.jm` from the regular map (`port_doomed_worlds.bs_elder_map`, run by hand as before; braziers keep off lava, the void and bridges), the world added to `DoomedRealmsWorlds.xml` by hand to what the script writes, the portal (0x7c05) and key (0x7c06) in both `CustomObjects.xml`.
- **Necromantic:** three bosses in `tools/build_resurrection.py`. Their loot is in balloons a chest spawner leaves, so `resurrection.json` names the balloon (`lootFrom`) and `BossResurrection.Initialize` gives the Empowered form its loot. The king timer and the Sentinel's start know the Empowered forms, which would otherwise stay invulnerable or wait for a bridge that is already shut.
- **Quest monsters:** `<Quest/>` on the Bridge Sentinel, the Twilight Archmage, the Forgotten King and the Cursed Crown, in both copies of the Shatters' XML (their Elder copies take it; the three Empowered forms have it). The Crown has it so a quest monster is left between the Archmage's death and the King's arrival: without it the dungeon counted as cleared at the Archmage and a Realm Portal opened there. With a quest monster dying last, `World.BossDied` opens the Realm Portal and counts the clear (it never did for the Shatters), so the King's balloon no longer drops a second one.
- **Raid Night:** `community.json` now includes the Elder Shatters in the raid goal's dungeon filter. The weekly goal selection stays in Redis; no data migration is needed. Redeploy the world server; no client or website change.
- **Items:** six in `Resurrection.xml` (0x8C3D to 0x8C42), their powers in `Player.RelicPowers.cs`.
- **Generated files:** `ElderGrounds.xml`, `ElderObjects.xml` and `ElderAssets8x8.png` were regenerated whole (existing type codes unchanged; sprite indexes of later dungeons moved with the sheet; "Elder The Dragon's Treasure Cache" is now "Elder Dragon's Treasure Cache"). Of `Resurrection.xml`, `resurrection.json` and `Resurrection8x8.png` only the new objects were taken: a full run of `build_resurrection.py` also rewrites the 19 older items (their bases were changed by hand since) and adds each Elder dungeon's key to its Empowered boss's second roll. `collection.json` regenerated.
- **Redeploy:** world server and client together (new sprites and XML). An older client in the Elder Shatters would not know its tiles. Nobody is disconnected; nothing in Redis changes.
- **Wiki:** a part on the Shatters and the Elder Shatters in `dungeons.md`, the three bosses on the Necromantic Relic page, `wiki_data.chest_spawners` follows a shared builder's spawners.
- **Not done or not checked:** the Elder Shatters Key is not sold in the Bazaar (its Elder pavilion has a stall for each of its 19 keys) and is not in a community goal's loot. Not seen in game: a relic used on the remains (the remains themselves appear), the six items' powers, and the King's last stand (the test's hits took him past it in one blow; a test covers its state).

### Candyland wave pacing
`BehaviorDb.Candyland.cs`: after a wave is cleared, its room waits for players to leave before
starting the boss spawn. The next wave waits for the boss to be defeated and players to return.
Waves are six Candy Creatures instead of ten. Updated the Candyland section of the website wiki.
Redeploy the world servers and website. No client, protocol or Redis change.

### Wiki command
`WorldServer/core/commands/player/Command.Wiki.cs` adds `/wiki`, which sends players a link to
`https://www.dodmg.com/wiki`. Added it to the generated command list and its description to the
website wiki. Redeploy the world servers and website. No client, protocol or Redis change.

## Checks

- **Potion of Maxy sprite:** both XML copies parse and match; a conversion check confirms the item
  points to `drLofiObj2:0x7A`, and an isolated `write_assets` check confirms the source sprite is
  overlaid on that tile. `python tools\build_wiki.py --out build\wiki-maxy-check` succeeds; the wiki
  has no Potion of Maxy item icon to update. Full Doomed port regeneration remains unverified:
  `python tools\port_doomed.py --dr "..\Doomed Realms" --plan` stops on the existing
  `ElderPirateCave` map lookup.
- **The Elder Shatters:** `dotnet test source\Tests` (new: every Shatters object has its Elder version and behaviour, the Empowered bosses drop their balloons' loot and items, each boss's Elder phase and the King's single-state last stand, Potions of Max, the key, teleport). `python tools\build_wiki.py`, `tools\build_collection.py`. On an isolated stack with the Rust client built from this branch: the dungeon loads with its Elder tiles and braziers; the bridge shuts and the obelisks fall in order; the Elder Sentinel turns armoured below 45% with its Judgement line in the chat, dies and leaves its remains, balloon and loot, and the Archmage's name and health take its place at the top of the screen; the Elder Archmage goes round his generators, stays open through his Eclipse, starts a second round, dies and leaves his; the Elder King's crystals, guardians and portals appear, he dies and leaves his; no exceptions in the world server's log. The regular Shatters ran the same script the same way.
- `dotnet build source\WorldServer\WorldServer.csproj -c Debug -o build\candyland-check`
- `python tools\build_wiki.py --out build\wiki-candyland-check`
- `dotnet build source\WorldServer\WorldServer.csproj -c Debug -o build\ws-wiki-command-check`
- `python tools\build_wiki.py --out build\wiki-command-check`
