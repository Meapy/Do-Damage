Release 2.5.8: the Marble Colossus stays in its room, a stronger Superior staff and monsters that fight as they did in the original game (deploys the client and the world server; nobody is disconnected)

## For players

- **Dungeons:** the Marble Colossus (and its Elder version) in the Lost Halls stays inside its boss room: it no longer follows players out through the doors.
- **Dungeons:** some monsters fight as they did in the original game again (Elder versions too): the Snake Pit's hidden dart throwers fire bleeding darts east and west across the room every 3 s or so while you fight the Snakepit Guard; answer the wounded Thessal wrongly or not at all within 12 s and she rages for 6 s (rings of Yellow Walls and Thunder Swirls, Super Trident crosses, Coral Bombs) before leaving; the Ghost of Roger (Davy Jones' Locker) follows you, and below 40% health charges with Skull Bolt spreads and calls two Violent Spirits; the Undead Lair's Vampires and Vampire Kings bite with their heavy Green Bolt (100/120) within 3 tiles.
- **Realm:** the Weretiger fires three claws (was one), the enraged Desert Werewolf a stronger fireball every 0.6 s, the Werelion's ring its Black Missiles (35, were the 22 Grey ones), a Drake Baby below 65% shoots back while calling its mother, the Giant Crab's beams aim where you are going, and the Warrior Wasp leads its shots one time in five (was every shot).
- **Items:** Superior (Oryx the Celestial Mad God's staff) does 60-95 damage a shot (was 45-75), each hit strips 1% of the enemy's defense for 5 seconds (up to 10 stacks, 10%; it falls off if you stop hitting), and it drops 1% of the time (was 0.4%).

## Details

### Marble Colossus stays in its room

- **What changed:** new behavior `StayInRoom(radius)` pulls its host back to within `radius` tiles of its spawn point after the movement behaviours have run. The Colossus uses 12 (the round boss room is 31 tiles across, centred on where it rises).
- **Files:** `source/WorldServer/logic/behaviors/StayInRoom.cs`, `source/WorldServer/logic/db/BehaviorDb.LostHalls.cs`, `client-rs/CHANGELOG.md`.
- **Redeploy:** the world server (rolling; nobody is disconnected). Nothing in Redis.
- **Not done or known to be off:** not tried in game yet.
### Superior staff buffed

- **What changed:** `Superior` (0x70a7) does 60-95 and has `<ArmorShred percent="1" max="10" duration="5"/>` (new `Item.ArmorShred`): each of its hits that leaves the enemy alive calls `Enemy.StackShred`, adding a stack of 1% (up to 10) and renewing all of them for 5 s, on top of `ShredArmor` (the mystic orb's shred: the defense stat itself is lowered, so the client's damage numbers follow). With no hit for 5 s every stack goes; a hit after that starts at one. Oryx the Celestial Mad God's drop is `0.01` (was `0.004`). Description says so.
- **Files:** `source/Shared/resources/xml/prod/EmbeddedData_EquipCXML.xml`, `source/Shared/resources/Item.cs`, `source/WorldServer/core/objects/Enemy.cs`, `source/WorldServer/core/objects/player/Player.Projectiles.cs`, `source/WorldServer/logic/db/BehaviorDb.OryxSanctuary.cs`, `client-rs/CHANGELOG.md`.
- **Redeploy:** the world server (rolling; nobody is disconnected). The item's XML is also in the client's data, but it ignores the new element, so the client only needs the new description for its tooltip. Nothing in Redis.
- **Not done or known to be off:** not tried in game; the wiki's measured gear tables aren't re-measured.

### Behaviours checked against the original server's

- **What changed:** our behaviours were compared, Shoot by Shoot, with the official release 7.0 server data (about 2013) in dginovker/Open-RotMG-Server (`data/reference/release-7.0`, branch `docs/mpgh-behavior-reference`), reading its `behavior.cc` for the defaults (unset `cooldown` 1 s, unset `angle` 9°, `type="auto"` spreads starting at `defaultAngle`). Of its 736 monsters with behaviours, 191 shoot as ours do and 271 differ; most of those are bosses we run a later official rework of (Tomb, Oryx 1-3, Abyss, Mad Lab, Stheno, Ghost Ship and so on) or our own changes, kept as they are: the 2013 versions would be a step back. The 42 with attacks there and no behaviour here are never spawned. Taken: slips where ours fires the wrong projectile, count or chance with the same projectile data (Warrior Wasp, Weretiger, Desert Werewolf, Werelion, Drake Baby, Giant Crab, Lair Vampire, Lair Vampire King), and three that did less than the original (the Snakepit Dart Throwers never fired, and are now ordered half a second after the Guard rises, as ordered in the same tick they saw no Guard and went back to sleep; the wounded Thessal's wrong answer did nothing, the Ghost of Roger stood still with one attack). Details in SETUP-LOCAL.md, "Behaviours checked against the original server's". The test bot's new `--shots` logs every enemy volley.
- **Files:** `source/WorldServer/logic/db/BehaviorDb.Midland.cs`, `BehaviorDb.Lowland.cs`, `BehaviorDb.UndeadLair.cs`, `BehaviorDb.SnakePit.cs`, `BehaviorDb.OceanTrench.cs`, `BehaviorDb.DavyJones.cs`; `client-rs/bot/src/main.rs` (test tool only), `client-rs/AGENTS.md`, `client-rs/CHANGELOG.md`, `SETUP-LOCAL.md`.
- **Redeploy:** the world server (rolling; nobody is disconnected). The client's code is unchanged (the bot isn't shipped); nothing in Redis. The wiki doesn't describe monster attacks, so it's unchanged.
- **Not done or known to be off:** only `Shoot`s were compared mechanically; movement, grenades, tosses and minions only where read by hand for the monsters above. Ghost of Roger keeps our Lost Soul spawning (3, every 5 s) rather than the original's (4, every second), as our Lost Souls shoot and the original's didn't.

## Checks

- WorldServer builds (`dotnet build`, 0 warnings, 0 errors).
- **Behaviours checked against the original server's:** WorldServer and the bot build; `dotnet test source\Tests` 283 passed (it loads every behaviour against the game data). On an isolated stack (slot 130) with `bsk-bot --shots`, each changed monster `/spawn`ed in a vault (bosses with raised health and `--fight`): Weretiger 3 shots 9° apart; Desert Werewolf projectile 0, then 1 once enraged; Drake Baby single shots, then 3 at 40° while calling; Giant Crab, Warrior Wasp firing; Lair Vampire and King projectiles 0 and 1; Snakepit Dart Throwers 2 darts 180° apart (0° and 180°), 8 volleys in about 20 s while the Guard fought (once only before the spawner fix); wounded Thessal, after 12 s: 15-shot rings of projectiles 3 and 0 and 4-shot crosses of 2, Coral Bombs thrown and bursting; Ghost of Roger single Roger Blasts, then below 40% 5-shot Skull Bolt spreads and Violent Spirits. No exceptions in the world server's log. Not seen: the Werelion's Black Missile ring, which fires only while nobody is within 10 tiles.
