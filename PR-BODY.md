Release 2.5.1: the Inferno's gate, Ambrosia for Large pets, an Account Fame board, ninja stars that throw again, white bag chime, balloon and damage-number options, and a round of fixes and balance (deploys the client, the servers and the website; nobody is disconnected)

## For players

- **Oryx 4 and Oryx 5:** Oryx's Inferno has a gate across the last stretch of the Burning Path, before the Pyre, that opens only once all ten of the Infernal Court are dead, so nobody can run past them to Oryx 4. Each Infernal shard drops 14% of the time (was 7%: twice as often). The Oryx 4 and Oryx 5 event (their 20% softer hits) is over.
- **Items:** the Infernal Brimstone Poison loses its stasis and strips 4% of enemies' defense for 12 seconds over its 6 tiles, as the orbs do (half the tier 8 orb's 8%). The Orb of the Drowned Tide's Undertow only slows now; its own armour shred (10% for 14 seconds) lowers defense as the other orbs' do, with no Armor Broken. The Spider Den's Sword Shield (Elder Arachna): 88-112 damage (was 175-225), +7 Defense (was 17) and a 3.5 second cooldown, half a second under its 4 second stun. Corrupted gear's edge is black (was purple). The ninja stars (Midnight Star, Kageboshi, Clover, Ilex, Unshuriken, the Crystal Shield and the rest) work again: hold the ability key for their effect, let go to throw the star. The Soul Mask no longer makes you invisible (Berserk for the group and Armored stay), and the Tome of Purification no longer clears Speedy.
- **Pets:** the Pen Keeper raises a Medium pet to Large for an **Ambrosia** from your inventory (it was 25 Shards of the Mad God), and trades 25 of the shards he keeps for an Ambrosia. In the Oryx community goal's rolls the Lil' Oryxes are as rare as they are strong: Lil' Oryx 2.5 (epic), Wine Cellar 1.6 (epic), Celestial 0.7 and Infernal 0.5 (legendary), Ascended 0.3 (divine).
- **Loot:** Bes, Nut and Geb each drop a Potion of Mana (and half again in the Elder Tomb). A white bag landing near you plays a bright chime and lights the edges of the world view in white and blue for a moment. The pD Armor (a staff item) no longer drops in the Nightmare Colony.
- **Hall of Champions:** a new board, **Account Fame**: all your living characters' fame together, +3% / +2% / +1% loot (within the boards' 4% cap).
- **Realm:** the Garnet and Jade Statues event counts as done only once both statues have fallen, whichever goes first.
- **Game:** arriving through /visit, /summon or a /help call makes you invulnerable for 3 seconds; a teleport the server refuses (an invisible or paused player) doesn't start the 10 second cooldown. Imprinting a corrupted item keeps its corruption. A Cosmic Anvil placed with /spawn works. Drinking stat potions from loot bags takes Potions of Oryx too, and only from loot bags (never the gift chest or vault chests). Viewing a Legend or a gravestone shows its summary without playing the death spin. The quest arrow no longer leaves a stray picture of a building or wall block at the screen's edge after a world change. The vault's, the Divine Forge's and the Marketplace's searches empty when you change worlds, and the character list's when you come back from playing. Forging tiered gear under 125 fame (below T10 on the weapons' scale) no longer counts for quests, the Master Smith achievement or the forge community goal.
- **Options:** Gameplay has **Other players' damage numbers** (off unless turned on), so your own hits on a boss stand out. Visuals: **Other players' shots and spells** hides their spell effects with their shots; **Speech balloons** is a dropdown list to tick whose balloons show (players' chat, guild, party, whispers, monsters, characters in town).

## Details

### Imprints keep corruption
`ImprintHandler.WithImprint` rebuilt the item's data with only its stack and imprints, dropping `Corruption`, `Unique` and `PetUpgrade`. It copies them now. Servers only.

### The Cosmic Anvil anywhere
`DoomedEconomy.NearObject` only looked at tile objects painted into the map; it now also looks at the world's `StaticObjects` (a spawned anvil) within the forge reach. Servers only.

### Oryx 4: the Gate and Infernal shards
`tools/build_hash_cellar.py` places two rows of `O4 Wall Gate` (a burnt copy of the O3 Wall Gate) across the last leg, rows 42-43, and an `O4 Gate Deleter 1` at (32, 48); regenerated the map, `HashCellarObjects.xml`/`HashCellarGrounds.xml` (server and client), the sheets and `types.json` (two new types; the generator was checked to reproduce the committed files first). `BehaviorDb.HashCellar.cs` gives the deleter the O3 gate's behaviour: it waits 3 s, then opens the gate (`RemoveObject`, 8 tiles) when no Infernal Officer, Brimstone Major, Cinder Cleric or Doom Cardinal is within 80 tiles. Infernal shards 0.07 → 0.14 (`BehaviorDb.OryxDoomed.cs`). Client and servers (the client bundles the XML and sheets).

### The damage event is off
`wServer.json`: `damageEvent` 0, `damageEventEnemies` empty. The wiki's paragraph on it is gone. Servers only.

### Infernal Brimstone Poison, Sword Shield, Drowned Tide
`tools/port_doomed.py` `GEAR_TUNING`: the Doom Poison drops its `StasisBlast` and gains `ArmorShred` (amount 4, 12 s, radius 6); the Sword Shield 88-112, Defense 7, `<Cooldown>3.5</Cooldown>`. The Doomed Realms checkout isn't on this PC, so the generated `DoomedRealms.xml` (server and client) was edited by hand to exactly what the script now writes; rerun `port_doomed.py` where it is to confirm. The Undertow relic power no longer applies Armor Broken (`Player.RelicPowers.cs`), and its tooltip says so (`tools/build_resurrection.py` and both `Resurrection.xml`s by hand: **the committed resurrection files have drifted from `build_resurrection.py`** (shot names, aura ranges, Elder keys), so regenerating would undo those changes; worth reconciling). Client and servers.

### Mana potions from the Tomb; no pD Armor
`BehaviorDb.Tomb.cs`: each of Bes, Nut and Geb drops a Potion of Mana at 100% (1% threshold), plus 50% more in the Elder Tomb (`ElderLoot`). `BehaviorDb.ParasiteChambers.cs`: the pD Armor (`<Admin/>`) is out of the Nightmare Colony's drops. Two other `<Admin/>` items are still in drop lists and were left as they are: Jade Storm (Mountain Temple) and Strike Amulet (a generated Doomed Realms monster). Servers only.

### Ambrosia and the Pen Keeper
`Player.PetPen.cs`: act 5 (Medium to Large) takes an Ambrosia from the inventory instead of 25 kept shards; a new act 6 trades 25 kept shards (`ShardsForAmbrosia`, still `PetPenData.ShardsNeeded`) for an Ambrosia in the first free inventory slot. The client's Pen Keeper window (`petpen.rs`) has the new button and counts Ambrosia. An older client's "Raise to Large" now gets the server's "takes an Ambrosia" message, and it can't trade. Ambrosia's description says what it's for (`EmbeddedData_EquipCXML.xml` and the client's `Equip.xml`). No migration: kept shards stay kept. Client and servers.

### Lil' Oryx rolls
`community.json`, the Oryx goal's loot weights, ordered by the pets' petDps (1001 / 1503 / 2000 / 2501 / 3000). A week's draw keeps only its goals' ids in Redis, so every roll after the deploy uses the new weights, this week's included. Servers only.

### Account Fame board
`ChampionTab.AccountFame` (9), appended last, as tabs are indexed; `ChampionSweep` ranks `AliveFame` (already summed for Guild Living) with the best character as the tiebreak. Loot reward 3/2/1%. Older world servers ignore the new tab when they read the rewards. The client reads the board list from the account server, so the tab just appears; there's **no statue for it in the Hall** (the Nexus map would need one). Account server and world servers.

### The statues event
`RealmManager.OnEnemyKilled`: the Garnet or Jade Statue's death counts the event ("The Statues") only when the other isn't alive within 50 tiles; the Jade Statue is now a part of the event, so it stays running while either lives. Servers only.

### Safe arrivals and teleport cooldowns
`Player.ShieldArrival` marks a visitor, a summoned player and a /help responder (and an admin's cross-world /summon), and `Player.Init` makes them invulnerable for 3 s on arrival; an admin's same-world /summon applies it directly. Ability teleports and boss moves (`TeleportPosition`) are untouched. Client: `Game::teleport` remembers the cooldown it started and gives it back if a "Cannot teleport..." or "Target does not exist" error arrives within 3 s (the server never started its own). Client and servers.

### Forging counts
`ForgeHandlers`: a forged item with a tier counts for quests and the weekly forged stat only at 125 fame or more (`ForgeRecipes.CountedTierFame`, T10 on the weapons' price scale); untiered items always count. Servers only.

### Client: auto-drink, death spin, searches, corruption edge, white bag
- Auto-drink only drinks from containers with `<Loot/>` (the loot bags) and accepts potions whose every activate is a small `IncrementStat` (the Potion of Oryx's eight); Potions of Max still don't qualify.
- `show_fame` skips the `Spin` screen when the summary came from the Legends, Recent Deaths or the graveyard.
- `Game::maps_entered` counts worlds; a new one clears the vault's, forge's and market's searches; starting to play clears the character list's.
- `hud::CORRUPTED_EDGE` (black) for the slot edge; the tooltip's Corruption words stay purple.
- `whitebag.rs`: a white bag (`Loot Bag 6`, `Loot Bag 6 Boost`) within 10 tiles plays `white_bag` (`source/Shared/resources/web/sfx/white_bag.mp3`, a bell "ting" synthesised by `tools/make_white_bag_sound.py`; `make_ping_sounds.py` now also finds the ffmpeg bundled by `pip install imageio-ffmpeg`) instead of `loot_appears`, and lights the world view's edges for 1.4 s.

### Client: Options
`Settings.bubbles` is now a mask (`options::bubble`: players, guild, party, whispers, monsters, NPCs), saved as `speechBalloons`; an old `textBubbles` line reads as all or none. Each `Bubble` carries its kind. `others_damage` (`othersDamageNumbers`, default off) hides `Damage` messages' numbers when neither the shooter nor the target is us (our own shots' numbers are drawn locally, our abilities' are credited to us). With "Other players' shots and spells" off, `ShowEffect`s whose target is another player aren't drawn. Eight new lines in every language table; two old ones gone.

### Held abilities (the ninja stars)
The Rust client always sent an ability's `UseItem` with use type 1 (`START_USE`) and nothing when the key came up, so the eight `<MultiPhase/>` abilities (`ShurikenAbility`) switched on their held effect (Midnight Star's Speedy, with its mana drain) and never threw. Now (`Game::use_ability`, `release_ability`, main.rs watching the Ability key) the key going down sends use type 1, and coming up sends use type 2 (`END_USE`: the server takes `MpEndCost` and ends the effect) and, with the mana for it, throws the item's projectiles (`shoot_ability`, as an ability's `Shoot` does), as the AIR client's `useAltWeapon` did. The server already handled both use types and accepts an ability's shot at its cooldown; it now runs only the held effect on a release, without `MpCost` or Benediction again (`Player.Activate`), so the Crystal Shield's -30 Speed isn't applied twice. An older client still never releases (as before). The wiki describes these abilities as "hold for X (draining mana); let go to throw it" (`tools/wiki_gear.py`). Client and servers.

### Soul Mask and Tome of Purification
`tools/port_doomed.py` `GEAR_TUNING` drops the Soul Mask's `ConditionEffectSelf` Invisible (and both `DoomedRealms.xml` by hand, as above); the Tome of Purification loses its `ClearConditionEffectAura` of Speedy (`checkExistingEffect="Hexed"`) in both prod XMLs. Client and servers.

### Stray pictures from the quest arrow
The client kept `Game::quest` (the server's `QuestObjId`) across world changes, and the server sends none where there's no quest (the Nexus). The old id then named some other object in the new world (a Nexus building's model, a wall block), and `draw_quest_arrow` drew that object's picture, at its own size, at the view's edge, where it stayed. `MapInfo` now clears the quest, the quest box and arrow only take an enemy, and the arrow's picture is `object_icon` fitted to 34 pixels. Client only.

### Dev Test.bat takes its slot over
`tools/testclient/iso_stack.py stop --take-over` also stops a test stack another checkout or worktree left on the slot's ports (start refused while one held them). It only stops a test stack's processes (`iso-redis-server.exe`, `dotnet App.dll` or `WorldServer.dll`, the world proxy) found listening on the slot's ports, and names anything else and leaves it running. `Dev Test.bat` stops with it before starting. Development only: nothing deploys.

### Wiki
Pets (Ambrosia), Champions (Account Fame), Endgame (the gate, the poison, the shards, the event gone), Forge (shards, forging counts), Imprints (black edge), Commands (invulnerable arrivals), Getting started (Options). Generated pages pick up the gear, drops and dungeon cards.

### Deploy
Client, servers (account and world) and website. Nobody is disconnected; no Redis migration (`penShards` keeps its meaning as the kept shards).

## Checks

- `dotnet build betterSkillys.sln -c Release`: no errors. `dotnet test`: 202 passed (the champions reward test counts Account Fame among the loot boards).
- `cargo test --workspace`: all pass (88 in the client, with a `whitebag` test); `cargo build`: no warnings.
- `python tools/build_wiki.py --out <scratch>`: Sword Shield (88-112, +7 Def, 3.5 s), Infernal Brimstone Poison (-4% defense, no stasis), Drowned Tide's Undertow text, pD Armor with no drop source.
- Isolated stack (slot 3, dev account made admin in its own Redis): walked the Burning Path, the gate stood closed with the Court alive and opened after `/killall` of the four Court kinds (walked through to the Pyre); the Pen Keeper traded 25 of 30 kept shards for an Ambrosia and offered "Raise to Large (1 Ambrosia)"; the white bag flash shown (screenshot with the flash held on in a throwaway build); Options' Visuals with the balloon list open and Gameplay's new switch.
- Updates 2: client tests pass; `build_wiki.py` shows the Soul Mask without invisibility, the Tome of Purification without the Speedy clear and the ninja stars' hold-and-throw. On the isolated stack the quest arrow over Oryx 5 draws his small picture. The ninja stars weren't played (the test account's character is a Wizard), and the stray picture didn't come back on demand (it needs the old id to match an object in the new world).
- Dev Test: `iso_stack.py --slot 1 stop --take-over` stopped the `bb-pierce` worktree's slot 1 stack (Redis, account and world servers) and left its ports free.
- Not tested live: the statues event, invulnerable arrivals and the refused-teleport cooldown (code read and built only), the Account Fame board's ranking (unit tests only), the chime's sound (synthesised, not listened to).
