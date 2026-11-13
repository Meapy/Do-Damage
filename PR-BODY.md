Release 2.5.8: the Marble Colossus stays in its room and a stronger Superior staff (deploys the client and the world server; nobody is disconnected)

## For players

- **Dungeons:** the Marble Colossus (and its Elder version) in the Lost Halls stays inside its boss room: it no longer follows players out through the doors.
- **Items:** Superior (Oryx the Celestial Mad God's staff) does 60-95 damage a shot (was 45-75), every fifth volley pierces and ignores defense, and it drops 1% of the time (was 0.4%).

## Details

### Marble Colossus stays in its room

- **What changed:** new behavior `StayInRoom(radius)` pulls its host back to within `radius` tiles of its spawn point after the movement behaviours have run. The Colossus uses 12 (the round boss room is 31 tiles across, centred on where it rises).
- **Files:** `source/WorldServer/logic/behaviors/StayInRoom.cs`, `source/WorldServer/logic/db/BehaviorDb.LostHalls.cs`, `client-rs/CHANGELOG.md`.
- **Redeploy:** the world server (rolling; nobody is disconnected). Nothing in Redis.
- **Not done or known to be off:** not tried in game yet.
### Superior staff buffed

- **What changed:** `Superior` (0x70a7) has five `<Projectile>`s with `<AlternateProjectiles/>`, so its volleys take them in turn (client and server each count volleys, as for the Enthroned weapons): four plain at 60-95 and a fifth with `<ArmorPiercing/>`, so every fifth volley (all four of its shots) ignores defense. Oryx the Celestial Mad God's drop is `0.01` (was `0.004`). Description says so.
- **Files:** `source/Shared/resources/xml/prod/EmbeddedData_EquipCXML.xml`, `source/WorldServer/logic/db/BehaviorDb.OryxSanctuary.cs`, `client-rs/CHANGELOG.md`.
- **Redeploy:** the world server (rolling) and the client (it reads the item's XML for its volleys and tooltip). Nothing in Redis.
- **Not done or known to be off:** not tried in game; the wiki's measured gear tables aren't re-measured.

## Checks

- WorldServer builds (`dotnet build`, 0 warnings, 0 errors).
