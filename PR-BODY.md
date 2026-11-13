Release 2.5.8: the Marble Colossus stays in its room (deploys the world server; nobody is disconnected)

## For players

- **Dungeons:** the Marble Colossus (and its Elder version) in the Lost Halls stays inside its boss room: it no longer follows players out through the doors.

## Details

### Marble Colossus stays in its room

- **What changed:** new behavior `StayInRoom(radius)` pulls its host back to within `radius` tiles of its spawn point after the movement behaviours have run. The Colossus uses 12 (the round boss room is 31 tiles across, centred on where it rises).
- **Files:** `source/WorldServer/logic/behaviors/StayInRoom.cs`, `source/WorldServer/logic/db/BehaviorDb.LostHalls.cs`, `client-rs/CHANGELOG.md`.
- **Redeploy:** the world server (rolling; nobody is disconnected). Nothing in Redis.
- **Not done or known to be off:** not tried in game yet.

## Checks

- WorldServer builds (`dotnet build`, 0 warnings, 0 errors).
