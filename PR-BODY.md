Release 2.5.8: the Marble Colossus stays in its room and a stronger Superior staff (deploys the client and the world server; nobody is disconnected)

## For players

- **Dungeons:** the Marble Colossus (and its Elder version) in the Lost Halls stays inside its boss room: it no longer follows players out through the doors.
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

## Checks

- WorldServer builds (`dotnet build`, 0 warnings, 0 errors).
