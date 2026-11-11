Release 2.5.2: Candyland waves are smaller and bosses reliably follow them, and `/wiki` links to the Do Damage wiki (deploys the world servers and website; nobody is disconnected)

## For players

- **Dungeons:** Candyland rooms spawn six creatures at a time instead of ten. Clear the wave and
  leave the room to bring out a boss; the next wave waits until that boss is defeated.
- **Game:** Type `/wiki` in chat to get a link to the Do Damage wiki.

## Details

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

- `dotnet build source\WorldServer\WorldServer.csproj -c Debug -o build\candyland-check`
- `python tools\build_wiki.py --out build\wiki-candyland-check`
- `dotnet build source\WorldServer\WorldServer.csproj -c Debug -o build\ws-wiki-command-check`
- `python tools\build_wiki.py --out build\wiki-command-check`
