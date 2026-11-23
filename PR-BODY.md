Release 2.6.7: an admin's /summon brings a player into the Crucible without using one of their climbs. Deploys the world servers; nobody is disconnected.

## For players

## Details

### Admin /summon into the Crucible costs no climb

- **World server** (`World.cs`, `DoomedRealmsCommands.cs`, `CrucibleWorld.cs`): `World.Invite` takes `free: true`, which an admin's `/summon` passes and which marks the account as brought in free to that world (`IsComped`). When a climb begins, `CrucibleWorld.Begin` skips `CrucibleAccess.Spend` for those accounts, so a player with no climbs left can be summoned into the lobby by an admin and the climb doesn't count against their day. Players' own `/summon` and `/visit` are unchanged.
- Not covered: an admin's summon between two world servers (`RemoteVisits`), and a climb already under way (the Crucible takes nobody new then, admins included).
- Redeploy: world servers only, rolling, nobody disconnected. No Redis data changed.

## Checks

- `dotnet build source\WorldServer\WorldServer.csproj`: no errors. Not tried in game.
