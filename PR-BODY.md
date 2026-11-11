Release 2.4.11: the Black Between, the Mysterious One's dungeon; Necromantic Boss drops in the Collection (deploys the client, servers and website; nobody is disconnected)

## For players

- **Oryx 3 and Oryx 5:** the gates that shut behind you in Oryx's Sanctuary and Oryx's Elysium now show as they close; before, they blocked the way unseen.
- **The Black Between:** a new dungeon beyond Oryx 5, for players in Enthroned gear: the Mysterious One (504,000 health, scaled by players), with music of its own and his children Vesper, the Dusk Star, and Aurel, the Dawn Blade, three trials on a platform in the void. His Rift opens when Oryx the Ascended God falls, a quarter of the time, and every time in the event The Voice in the Void (10 November to 1 December). He drops life and mana potions (about Oryx 5's count), each Max potion at 2.5%, the Unstable Essence (11.25%) and six new abilities at 1% each (white bags): the **Quiver of the Solar Cyclone** (its tornado arrow bursts for 2,000 and throws eight sunsparks), the **Trinity Trap** (Aurel's lightning, Vesper's veil and their father's rift, one after another), the **Helm of the Dawn Blade** (three flying swords of 1,200 that heal you for 8% of their damage), the **Prism of the Dusk Veil** (a decoy that casts Vesper's dusk bolts), the **Cloak of the Planar Rift** (the ground bursts where you land, five armour-piercing sunsparks, armored 2 s) and the **Vial of Cascading Dusk** (a splash that cascades into four, then four more).
- **Corruption:** the Cosmic Anvil, left where the Mysterious One fades, spends an Unstable Essence to change a piece of Enthroned gear, for better or worse (what it does is left for players to find out; the numbers are under Details). Corrupted gear has a purple edge.
- **Vault:** the Collection's Dungeons view shows the 19 unique items from Empowered Elder bosses
  under **Drops from Necromantic Boss**; hovering an item shows that source.

## Details

### The Black Between: the Mysterious One's dungeon (testing, admin only)

The owner's plan (`..Context-Txts/11082026/mystery.txt`, and the plan page shared with them): the Mysterious Voice of Realm 2 in person, three trials on a platform floating in the void, harder than Oryx 5, for players in full Enthroned gear.

- **How to get in:** `/dungeon The Black Between` (admins). `/dungeon`'s usage now lists it under "Being tested" (`DungeonCommand.Testing`, `DoomedRealmsCommands.cs`). No way in for players yet, so no changelog line or wiki page.
- **Generated** by `tools/build_black_between.py` (new), with the art in `tools/black_between/bosses.py` (the three bosses at 32x32, as on Oryx 3's sheet: Vesper, a dark-winged sorceress; Aurel, a winged knight whose sword flies at his command; their father, hooded in a starfield robe with a ring of the heavens behind him and one wing of each; Aurel's Solar Cyclone and Gauntlet Form, Vesper's Veil Copy, the spectres, the planted sword) and `small.py` (minions, shots, floor, walls of light, dusk mines, the Rift): the sheets `BlackBetween8x8/16x16/32x32.png` and `BlackBetweenChars8x8/32x32.png`, `BlackBetweenGrounds.xml` and `BlackBetweenObjects.xml` (server and client copies), and the map `worlds/Dungeons/The Black Between/The Black Between.jm` (48x72). Types 0x8D00-0x8DFF, kept in `tools/black_between/types.json`. `tools/gen_rs_manifest.py` registers the sheets and XML; manifest regenerated. The world is in `Dungeons.xml`.
- **The map:** the Constellation Approach, a causeway four tiles wide over Realm 2's void (`R2 Abyss`) with two pairs of Starbound Sentinels, from the spawn and its Nexus Portal; then the Observatory, a platform 17 tiles in radius with a rim, a diamond and a cross inlaid where the walls will rise, the Rift on its north rim.
- **The fight** (`BehaviorDb.BlackBetween.cs`): the herald (the father's look, invulnerable) walks out of the Rift when someone reaches the middle, sets the trials and watches. The invisible `Observatory Heart` at the centre runs the arena: the bosses order it into each phase; it spawns them at the centre, raises the walls, turns the floor and sends the starfall, all undone when its state ends.
  - **Trial I, Vesper** (240,000 health, 80 defence): volleys and rings; the **Fourfold Veil** at 75% (14 s) and 50% (16 s, faster), as in the owner's sketch: she and three copies at the diamond's points, the floor outside it burning, walls along the cross, starshards into the four quadrants, each marked by a red circle that fills in over 1.5 s (380 damage, Quiet); the Starfall Waltz (dusk mines), the Gravity Well (rings closing in from the edge), Nightfall below 20%.
  - **Interlude:** 10 s of starfall while Aurel comes down.
  - **Trial II, Aurel** (280,000, 100): crescents and sunbursts; the **Solar Cyclone** at 70% (his sprite a gold tornado, three dashes rim to rim through players, each marked in gold first, sunsparks spiralling out); the Zenith Leap; the **Sundering** at 40% (his sword planted in the middle with 60,000 health, his sprite empty-handed with blazing gauntlets, his reflection in the other half, then a wall of dawnlight north and south of the sword and across the causeway's mouth, two Dawn Templars from the sword every 6 s; break the sword and he's stunned and armour-broken for 5 s, else it ends after 30 s); High Noon below 15%. The sword's own square stays open (a wall object there would stop shots at it, and in the client an object that blocks walking blocks shots too), and its light slows anyone squeezing past.
  - **Trial III, the Mysterious One** (480,000, 110, immune as Oryx 3-5): Twin Heritage (his daughter's kit and his son's in turn); the Platform Breaks at 70% (15 s: its edge burns away to 12.5 tiles for the rest of the fight); the Eclipse (the veil's floor, dawnlight walls, starshards from Vesper's spectres); **the Black Between** at 40% (22 s: the platform burning void but for a Constellation Path that lights ahead and goes out behind); his children's spectres in turn; the Supernova at 10% (get more than 9 tiles from him in 5 s, 1,600 damage).
  - Health scales with players (`BossStats`, +35% each). No loot yet (the owner's call).
- **New behaviours** (`logic/behaviors/BlackBetween.cs`): `Starfall` (marked strikes: into a diamond, scattered, on players, or round the host), `PaintFloor` (turn the floor where a shape says, for the state), `ConstellationPath`, `CycloneDash`, `LeapStrike`, `BlinkAround`, and `Strike`'s helpers.
- **The ground mark** (client and server): `ShowEffect` 40, `GroundMark` (centre pos1, radius pos2.x, `Duration` ms). The Rust client draws it on the ground under everything (`particles.rs` `draw_marks`, from `view.rs` after the ground): a faint disc, the part filled so far, the outline, a flash when it lands; shown even with particles off. Older clients see only the throws and blasts.
- **Redeploy:** client (new sheets, XML, the ground mark) and servers (world server: XML, map, behaviours, `/dungeon`). Nobody is disconnected; no Redis changes.
- **Found in testing and fixed:** the herald stood too close to the Rift and hid it (the Rift now stands upright on the rim behind him, and he stays out in front after his speech); he wakes within 19 tiles, so anyone near the middle wakes him; Vesper and Aurel move to their places before their walls rise (the walls stood in their way); the Black Between's dark reaches the burnt rim, leaving no ring of floor between; softer floor tiles (a seam on every tile read as a grid).
- **Not done or off:** the way in for players, loot, wiki and changelog (later, once it's reachable). Numbers are the plan's starting point, to be tuned against Enthroned characters. The Supernova is "outrun it" rather than the plan's shadow cone. The walls and floor are centred on the Heart's tile; the Veil's burning floor can be crossed at a cost, its walls can't.

### The Black Between: the owner's first round of changes (still admin only)

From `..Context-Txts/11082026/mystery-updates1.txt` (and its sketch of the sword lanes).

- **Look:** the void is empty sky (no ground) under a new starry void sky (`voidsky.rs`, `<Background>8</Background>`), with rock hanging under the platform's and causeway's edges as under the Nexus islands (`islands.rs` now takes a style, `BB Underside` grounds). Rim of dressed stone banded in gold, dusk and dawn pylons round it; no inlaid lines. The Sentinels drift harmlessly (scenery; their stars do no damage, they can't be hit). No portal at the spawn: the Mysterious One's is the only way out.
- **Arena:** the causeway burns once the fight starts; bosses aim anywhere on the platform; survival phases silence everyone and put pets in stasis (the last of each boss sickens too). Pets in stasis now do nothing (`PetShoot`, `PetHeal`, `PetNova`, `PetStrike`); nothing else puts pets in stasis.
- **Herald:** stands on the Rift after summoning Vesper.
- **Vesper:** everyone moved into the quadrants as the Veil rises; no lasers down the walls; violet starshard marks and blasts; weaving bolts and bowing rings; more dusk mines.
- **Aurel:** divine lightning all fight; the leap in phase 1, the Sunwheel in phase 2; the Sword Lanes replace the Sundering (sword, reflection, Templars and the dawnlight wall through the middle are gone).
- **The Mysterious One:** the new shots, mines, lightning and sword lanes; everyone put on the Black Between's path, fired on while they follow it; the Supernova is his rift opening (a new ground mark style).
- **Files:** `tools/build_black_between.py`, `tools/black_between/small.py` and `bosses.py` (regenerated sheets, XML and map), `Dungeons.xml`, `BehaviorDb.BlackBetween.cs`, `logic/behaviors/BlackBetween.cs` (`SwordLanes`, `GatherIntoQuadrants`, `PetAura`, `Strike.Bolt` and `FireFrom`, `Starfall` lightning and mark style, `ConstellationPath` gathering players), the four pet behaviours, the client's `voidsky.rs`, `islands.rs`, `view.rs`, `ground.rs`, `particles.rs`.
- **Redeploy:** client and servers, as the rest of the dungeon. Nobody is disconnected.
- **Not done or off:** the lightning bolts are thin sparks (the game's Lightning effect) and easy to miss beside their marks; the sword lanes' timing and the survival tolls are untested by real players.

### The Black Between: the owner's second round of changes (still admin only)

From the bottom of `..Context-Txts/11082026/mystery-updates1.txt` and its four screenshots.

- **Look:** a rim of flecked stone (its gold band read as broken blocks round the stepped edge), no gold line under it, plain floor along and across the causeway's mouth.
- **Arena:** no walls on the cross (Veil and Eclipse); the Black Between darkens the whole platform even if its edge hasn't burnt; twice the starfall after Vesper.
- **Vesper:** no Dusk Shades; two counter-turning rings from her last Veil on.
- **Aurel:** twice the lightning (paralysing a moment), blinding sunbursts, and a twin tornado crossing each Cyclone dash at right angles; every dash runs rim to rim.
- **The Mysterious One:** his spectral Cyclone crosses the whole arena; his small rifts burst about the arena; no tornadoes during the Black Between (any spectral Cyclone still about is sent away); the Supernova only once he's in the middle; at 5% he fades away instead of dying, with a last word that he'll return.
- **Immunities:** all three bosses are immune as Oryx 3 to 5 are.
- **Found in testing:** a jump of many tiles in one tick (the Cyclone whisking him to each dash's start, the Zenith Leap) left clients drawing Aurel with his normal sprite mid-Cyclone, or double size after a leap: he now flies to each start and glides onto the leap's mark. The twin is summoned once (a `Spawn` left running sent another whenever the twin was away) and the Cyclone waits a moment for it.
- **Files:** `BehaviorDb.BlackBetween.cs`, `logic/behaviors/BlackBetween.cs` (`CycloneDash` twin and centre, whole chords; `Starfall` anywhere in the diamond; `GatherIntoQuadrants`), `tools/build_black_between.py` and `small.py` (regenerated). No client change.
- **Redeploy:** servers, and the client for the new art.

### The Black Between: the owner's third round (still admin only)

- No Realm Portal when Vesper or Aurel falls (`World.BossDied` opened one when the last quest monster in a dungeon died, and also counted the dungeon cleared): in the Black Between only the Mysterious One's going clears it, and he leaves his own portal.
- No gap between the platform's floor and the rock under it (`islands.rs`: no slab offset on a flat map).
- The father's crumbling meteors violet and gold in turn, not red; the Sentinels no longer shoot.
- Redeploy: servers and client. Checks: `bsk-bot` through the whole fight (no Realm Portal opened, no errors), the client at the platform's edge and in the Break.

### The Black Between: the owner's fourth round (still admin only)

- Everyone moved into the diamond as Vesper gathers for her Veil (and the father for his Eclipse), a few seconds before the floor outside it burns.
- Aurel's leap aims where its target is heading.
- The Mysterious One: 420,000 health (was 480,000); his black hole opens round him every 15 s from 40% (12 s below 10%) while he keeps attacking, and the 10% Supernova comes with his volleys, rings and starshards; his portal out reads "Part II: Coming Soon".
- Redeploy: servers. Checks: the client through to his fade and his portal (named "Part II: Coming Soon").

### The Black Between: Vesper's copies (still admin only)

- In the Veil her copies fire every 2.5 s (was 1.6 s); their bolts do 35 damage, armour-piercing (was 180). Her rings' orbs darken and slow for 1.5 s.
- **Darkness shows (client):** the Rust client showed only Darkness's icon; it now darkens the world but for a little light round you (`view.rs`, `death::vignette`), as the AIR client did. Every shot or aura that darkens (the Black Between's, and elsewhere in the game) now does something players can see. `tools/build_black_between.py` (`shot(piercing=)`), `BehaviorDb.BlackBetween.cs`. Redeploy: servers and client (XML). Checks: server tests, the generated XML.

### The Black Between: updates 3 (still admin only)

- Aurel redrawn like his sister (wings, flowing hair, a long coat), a man with a broad greatsword in hand.
- Richer default attacks (a weaving sunspark fan, a bowing ring of crescents), armour break on the crescents and the leap.
- His leap has an echo, his look and name, on another player (or across the arena when alone); which is him is random each leap; the echo strikes once and is gone.
- The father's way out: the Rift itself becomes a locked "Part II: Coming Soon" portal (no Nexus Portal; leave by the Nexus key).
- `ReturnToSpawn` never steps past the spawn (the father shuffled in the middle; any boss returning to a small radius did).
- Redeploy: servers and client (art, XML). Checks: the client at Aurel's arrival and leap (new art, bowing ring), a bot run logging an echo per leap across the arena, the client at the Rift after his fade (its name in the interact panel, the coming-soon message on entering), server tests.

### The Black Between: the way in and the Mysterious One's loot (updates 4, part 1)

- When Oryx 5 falls, a quarter of the time (every time during the November event "The Voice in the Void", 10 November to 1 December: `wServer.json`, `ServerEvent.mysteriousOne`), the Mysterious One's Rift opens in his temple a few seconds later: he steps out onto ground turned to the void, speaks, and the Rift leads into the Black Between. **This makes the dungeon reachable for players once released.**
- His loot: life and mana potions only (about Oryx 5's count), the eight Max potions at 2.5% each, and the Unstable Essence (white bag, 11.25%) for the Cosmic Anvil (next part).
- Imprinted gear's border: thinner, its colours at the edge with a gold line inside (client).
- Redeploy: servers and client. Checks: on the slot 3 stack, an Oryx 5 spawned in his Elysium and smitten: the Rift, the void ground and the Mysterious One's lines (screenshot), and `bsk-bot` entering the Rift into the Black Between.
- Still to do before release: the changelog and wiki for the dungeon (it's reachable now), the Cosmic Anvil and corruption, the new items.

### Corruption: the Cosmic Anvil (updates 4, part 2)

- The Mysterious One leaves the Cosmic Anvil when he fades. Its window (the Divine Workbench as the anvil, one tab) spends an Unstable Essence to corrupt a piece of Enthroned gear: damage, range, rate of fire (doubled) and stats each move by 1 to 3, up or down at even odds (up 60% of the time for a value below 0), between -6 and +6 (first -3 to +2, changed in the final touch-ups); a staff is reversed a tenth of the time (its shots fly from the far end of their range back to where they were fired). Corrupted gear has a purple edge and a Corruption section in its tooltip.
- **Message change:** `CORRUPT` (160) and `CORRUPT_RESULT` (161), client and server ship together. Item data gains `Corruption` and `Unique` (JSON fields, left out when empty; no migration).
- Files: `Shared/resources/Corruption.cs`, `ItemData.cs`, `Imprints.cs` (`ImprintShot.Of`), `Player.Imprints.cs`, `CorruptHandler.cs`, `CorruptResult.cs`, `MessageIds.cs`, `BlackBetween.cs` (`PlaceOnDeath`), `BehaviorDb.BlackBetween.cs`; generator (anvil art and object); client `forge.rs`, `forge/corrupt.rs`, `forge/imprints.rs`, `hud.rs`, `npc.rs`, `main.rs`, `game.rs`, `bsk-data` `imprint.rs` (+ a test), `bsk-proto` `messages.rs`, `combat.rs`, ten language tables.
- Checks: tests (server 169, client 82, data/world/proto); on the slot 3 stack the whole fight, the anvil window, three corruptions of an Enthroned Staff: the values, the purple edge in the window and the HUD, defense +15 from the stats value (screenshots). The reversed staff not seen (a tenth of the time).

### The Black Between: the family's gear (updates 4, part 3)

- Six new abilities, dropped by the Mysterious One in white bags at 1% each (past 3% of his damage): the **Quiver of the Solar Cyclone** (its tornado arrow bursts for 2,000 and throws eight sunsparks), the **Trinity Trap** (Aurel's lightning, Vesper's veil and their father's rift, one after another), the **Helm of the Dawn Blade** (three flying swords of 1,200 that heal you for 8% of their damage), the **Prism of the Dusk Veil** (a decoy that casts Vesper's dusk bolts), the **Cloak of the Planar Rift** (the ground bursts where you land, five armour-piercing sunsparks, armored 2 s) and the **Vial of Cascading Dusk** (a splash that cascades into four, then four more). Each is an ordinary item (`tools/build_black_between.py` `ITEM_SPECS`, `gear_item`; 8x8 art in `black_between/small.py` `ITEMS`; the Cyclone Arrow projectile) with a `<RelicPower>`, worked in the new `Player.BetweenPowers.cs`: `BetweenAbility` after the ability's own effects (`Player.UseItem.cs`, beside `RelicAbility`), `BetweenHit` when the ability's own shots hit (`Player.Projectiles.cs`, the shots that aren't the weapon's), `DecoyTick` from `Decoy.Tick` for the Prism. The server's extra shots go out as `ServerPlayerShoot`, as the decoy and nova abilities' do.
- Collection: the six join the vault's Collection under a The Black Between group (`tools/build_collection.py`, `collection.json` regenerated).
- Wiki: the dungeon now shows on the Dungeons page (`build_wiki.py`: how it opens, its boss); its gear on the gear and class pages; a Corruption section in the Imprints guide. Changelog: a "The Black Between" group.
- Redeploy: servers and client (the client needs the new items' XML and art). Nobody is disconnected.
- Not done: the showcase artifact of the dungeon and its gear.

### The Black Between: fixes after the gear (the owner's notes, `mystery-updates1.txt`)

- **Health:** every boss 20% more: Vesper 288,000, Aurel 336,000, the Mysterious One 504,000 (`BossStats` and the generator's table), still scaled by players.
- **Vesper:** her volleys' bolts silence for 1.5 s; her copies' bolts 50 (were 35, still through armour). From her last Veil on, the Gravity Well's orbs never stop coming in from the edge (the Heart stays in `T1 Well` through the Well and Nightfall), and a dusk mine falls about the arena every 1.3 s (`MinesEvery`).
- **Aurel:** twice the divine lightning in both his survival phases (the first Cyclone and the Sword Lanes); in the Sword Lanes he also fights from the middle (slashes, sunburst, the weaving fan, the crescent ring); his Noon turns the Sunwheel between its fight and its Cyclone. **His Cyclone ends when its last dash does** (`CycloneDoneTransition`), not on a timer (3 passes x 3.3 s + 0.8 s) that cut the last dash short: three dashes take about 11.2 s against the timer's 10.7 s, so his twin tornado was sent away mid-flight (it vanished) and he stopped wherever he was, out towards the rim. A 20 s timer stays as a safety net.
- **The Mysterious One:** pets are in stasis all through his trial.
- **Sight:** the Black Between gives every player a sight radius of 35 tiles at least (`<MinSightRadius>` in `Dungeons.xml`, `WorldResource.MinSightRadius`, `World.MinSightRadius`, `Player.SetSightRadius`), so nothing on the platform drops out of a wide window's view when it crosses to the far side. Other worlds are unchanged.
- **Music and sounds:** the dungeon's own music (`the_black_between`, a 91 s loop) and each family member's hit and death sounds (`vesper_`, `aurel_`, `mysterious_hit` / `_death`; the spectres, the echo, the herald and the visitant share them), synthesised by the new `tools/black_between/sound.py` (numpy and lameenc; its MP3s are committed under `web/music` and `web/sfx/monster`).
- **The Cosmic Anvil** no longer explains corruption: its window says only that no one knows what an Unstable Essence will do (ten language tables), the essence's description says the anvil knows what to do with it, and the wiki's Corruption section and the changelog leave out the numbers.
- Redeploy: servers and client. Nobody is disconnected.
- Not done: "when Aurel jumps the indicator is not there when she's cloned the first time" isn't reproduced. Both landings of his leap are marked (one mark each for him and his echo), and his twin tornado's lines are marked with his; the owner is asked which moment it was.

### The Black Between: final touch-ups 1 (the owner's notes)

- **Aurel** is armored through High Noon.
- **Corruption's odds:** each value moves 1 to 3, up or down at even odds; one below 0 goes up 60% of the time (`Corruption.Roll`, `Up`, `UpFromRed`). Testing had shown values never climbing out of the red with the old -3 to +2.
- **Art:** the six abilities redrawn at 8x8 with proper ramps (a gold quiver with the tornado whirling from its mouth, a gold-jawed trap holding the three colours, a winged gold helm, a lit dusk crystal, a gold-trimmed hooded cloak with the rift at its heart, a round flask under a gold stopper); the Unstable Essence a spiky shard star. The Cyclone Arrow is a new top-down whirl that spins as it flies (`<Rotation>`; the client can't animate a projectile's frames).
- **Quiver:** the arrow (now 9 tiles a second for 1.6 s) throws two sunsparks to either side every 0.2 s as it flies and bursts (2,000 round it, eight sunsparks) where it hits an enemy or where it comes down (`BetweenShot`, from `PlayerShoot` on slot 1).
- **Trinity Trap:** thrown (1 s), and each stage is a ground circle like the bosses' telegraphs, filling until it strikes: gold, violet, then the father's rift opening. **Fix:** the gear's blasts were `AreaBlast` effects, which clients always draw on the object sent with them (the player), so a stage showed at the player; they're now bursts at the place.
- **Helm:** Berserk to allies and Speedy for its wearer (6.5 s, 7.5 tiles), as the Helm of the Tribune; heals 6% of each sword's damage (was 8%), shown as a green "+N" over the player.
- **Prism:** every decoy casts (a timer of its own, `Decoy.NextCastAt`; before, one shared timer let only one cast); decoys travel 9 tiles at most.
- **Cloak:** ten sunsparks (were five), from where the teleport lands (the target position; the player's own position hadn't moved yet when the power ran).
- **Vial:** thrown (1 s); the first splash 2.5 tiles across, the next four 3 tiles at 3.5 tiles out, the last four 3 tiles at 6.5 tiles out, so they overlap; each ring a shade darker and bluer.
- **Descriptions** rewritten for what each does now; a number and its unit are joined by a no-break space so the tooltip never splits "6 s" across lines (what made the vial's read as broken).
- **Ground marks on raised floors (client):** marks were drawn only with the lowest floor, so on stacked floors (the Nexus islands) they sat under the raised ground. Each mark now keeps the height and floor it came at and is drawn with that floor (`particles.rs`, `view.rs`).
- Redeploy: servers and client. Nobody is disconnected.

### The Black Between: a heal before each sickening phase; no lightning under the players

- Before each sickening survival phase (Vesper's second Veil, Aurel's Sword Lanes, the father's Black Between and Supernova) the Mysterious One says "A heal, from your realm." (his herald says it while his children fight), and everyone on the platform is made invincible and healed to full (the usual "+N"), then the phase begins (`RealmHeal` in `BlackBetween.cs`; the father's two get a state of their own before them, `Black Heal` and `Nova Heal`, 1.8 s).
- Aurel's divine lightning no longer strikes marks under the players (the paralysing circles that followed you); the ones scattered about the arena stay.
- Redeploy: servers. Checks: on the slot 3 stack, Aurel driven into the Sword Lanes: the line in chat just before "Stand where my sword is not." The heal's "+N" not seen (the character was dead by then).

### The Black Between: the Constellation Path keeps clear of the Mysterious One

- The path of the father's Black Between could run through the middle of the platform, where he stands for the whole phase, which made it impossible to follow. Its walk now stays in the ring between 5 tiles from the middle and 3.5 from the edge (its lit squares, three wide, at least 4 tiles from him), and when it's boxed in it turns any way still in that ring rather than towards the middle, and not straight back on itself (`ConstellationPath`, `clear`, `InRing`).
- Redeploy: servers. Checks: the walk's rules simulated 2,000 times (never within 5 tiles of the middle, never doubling back in the phase's ~70 steps); on the slot 3 stack the phase begun (`--shot`): everyone put on the path's start south of the middle, the platform dark round it.

### Placed objects show (Oryx 3's and Oryx 5's gates)

- `PlaceObject` set a square's object without an object id and never told the players round it to look again, so clients were never sent it: Oryx 5's Holy Gates (as they shut, and in his Last Judgement) and Oryx 3's gate stood in the way unseen. It now gives each one an id and refreshes players' squares (`UpdateTiles`, as `RemoveObject` does); removal already worked (clients drop a square's object by its id). Found with the Black Between's walls of light.
- Redeploy: servers. The client needs nothing.

### Necromantic Boss drops in the Collection

- `tools/build_collection.py` adds each Empowered Elder boss's unique untiered item from
  `resurrection.json` to a **Drops from Necromantic Boss** source group. Regenerated
  `source/Shared/resources/data/collection.json`; this adds them as a source group in the Dungeons
  view.
- Updated the player changelog, the Items wiki page and SETUP-LOCAL.md.
- Redeploy: world servers (to load the catalogue) and website (updated wiki); no client release or protocol change, no Redis migration, and nobody is disconnected.

## Checks

- Final touch-ups 1: `dotnet build`, the client built; on the slot 3 stack (`--shot`), each item used: the trap thrown in the Nexus and in the Black Between (the throw, the gold burst, Vesper's circle filling, on raised ground too), the vial's cascade (rings bursting, the next filling a shade bluer), the quiver's side sparks in flight and its burst on a duck, the helm's swords with Berserk and Speedy on, the prism's decoy (18 hits), the cloak (hits after the teleport). No errors in the world log. The helm's "+N" not seen (full health); the corruption odds not rolled in game.

- The fixes after the gear: `dotnet build`; on the slot 3 stack Aurel spawned at the centre and smitten into his first Cyclone, its start and end logged (11.2 s for three dashes, so the old timer cut the last one), and screenshots of his leap marks and the Cyclone. The account server serves the new music and sounds (HTTP 200); they weren't heard (scripted clients open no sound output). The father's pet stasis, Vesper's silence and mines and the Sword Lanes' kit weren't played through.

- The family's gear: `dotnet build` of the world server; the wiki built to a scratch folder (the dungeon's card, the six items on their gear and class pages with the Mysterious One as their source). On the slot 3 stack, each item in a Wizard's ability slot (admin), used at the Nexus pond's ducks with the client (`--hold space`): the quiver (one arrow, 9 hits: the burst and its sunsparks), the helm (3 swords), the prism (18 hits from the decoy's bolts), the cloak (a sunspark hit after the teleport), the trap and the vial (the ducks round the target died; no client hits, as the damage is the server's). No errors in the world log.

- The Black Between: `dotnet build` of the world server, `dotnet test source/Tests` (169 passed), `cargo test` of `bsk-client` (82, with a new test for the ground marks), `bsk-data`, `bsk-world` and `bsk-proto`; the generator run and its sheets looked over; the wiki built to a scratch folder (nothing of the dungeon shows on it). On an isolated stack (slot 3): `bsk-bot` through the whole fight with `/tppos` and `/smite` (every phase's line in order, from the herald's speech to the announcement, no errors in the world log), and the client (`--shot`) at the intro, the Fourfold Veil (burning floor, the walls along the cross, the copies, the red marks filling), the Solar Cyclone, the Sundering (the sword, the wall, the reflection, the Templars), the Black Between (the void and the Constellation Path) and the Supernova (the 9-tile mark).
- Placed objects: Oryx's Elysium's and the Sanctuary's gates not looked at in game; the same code path as the Black Between's walls, which now show.
- The second round: `dotnet test source/Tests` (169), the client built; on the slot 3 stack `bsk-bot` through the whole fight to the father's fade (both his last lines, then the announcement; no errors in the world log), and the client: the Veil without walls, Aurel's Cyclone with its twin, the father's Twin Heritage (mines, weaving bolts, spectral Cyclone) and his fade into his rift at 3%.
- The first round of changes: `dotnet test source/Tests` (169), `cargo test` of `bsk-client`, `bsk-data`, `bsk-world`; on the slot 3 stack `bsk-bot` through the whole fight (no errors in the world log) and the client (`--shot`): the causeway over the void sky with the platform's undersides, pylons and the Sentinels' stars; the Veil (players moved into the quadrants, violet marks, no lasers) and the arena after it (walls and floor gone); Aurel's lightning and leap; the Sword Lanes (swords flying up and down their lanes); the Black Between (everyone on the path's start, the father firing); the Supernova's rift. Pet stasis and the survival tolls not seen in game (no pet on the test character).
- `python tools/build_collection.py --check` passes; verified all 19 items in `resurrection.json`
  are in the new group.
- `python tools/build_wiki.py --out build\collection-wiki-check` passes (22 pages built).
