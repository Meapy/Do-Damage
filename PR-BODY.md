Release 2.5.11

## For players

- **Dungeons:** Empowered bosses (raised with a Necromantic Relic) can no longer be held back by going invisible (the Rogue's cloak, or anything else that makes you Invisible). Their phases open and play on, and the checks that move each fight along still find a cloaked player: the Forgotten King's last stand fires at whoever stands within 7 tiles of him, which a cloaked player used to sit through untouched. Their aimed shots still lose you while you're cloaked. Every soul shot an Empowered boss fires (the bolts, fast bolts, orbs and shards of its Soul Nova and its phases) now leaves you Quiet for 1 second.
- **Marketplace:** every new listing is announced. A gold **Marketplace** line in chat tells every player on every world server who listed what and for how much ("Tester listed Health Potion for 1,234 fame."), and the Discord server's new **#marketplace** channel gets the same within 30 seconds, with how long the listing lasts. Players who ignore the seller don't see their lines in game.

## Details

### Invisibility no longer holds back an Empowered boss's fight

Invisibility hid players from every check a boss's fight makes, so a cloaked player could stall it:

- the Empowered layer (`core/resurrection/Empowered.cs`) skipped its whole tick, phases included, while no *visible* player was within 20 tiles: a fully cloaked group froze the phases (no Soul Harvest, Storm, Death Spiral, Last Breath, nor the signature phases), and damage dealt meanwhile went through phase thresholds without their shield;
- the bosses' own behaviours gate their phases on players being near or gone (`PlayerWithinTransition`, `NoPlayerWithinTransition`; in the Doomed Realms engine `IsEntityPresent`, `IsEntityNotPresent`, `EntityLesserThan`). Every one of the 25 bosses' areas has such gates, in both engines: Arachna, Malphas, Davy Jones, Limon, Ruthven, Mixcoatl, Septavius and Stheno's Doomed definitions, and the hand-written Tomb, Shatters, Lost Halls, Draconis, Davy Jones, Jungle, Abyss and others. The Shatters one the owner saw: the Forgotten King's "diedie" stand fires at whoever is within 7 tiles (`dieshoot`) and waits otherwise (`diewait`); cloaked, he waited.

The fix is central rather than boss by boss, so it covers every boss, its minions and both engines:

- `World.EmpoweredBosses` lists the Empowered bosses raised there (`BossResurrection.RaiseAt`), and `BossResurrection.InEmpoweredFight(host)` says whether a monster is within 40 tiles of one still fighting.
- `IPlayer.IsVisibleToEnemy(ignoreInvisible)`: with it, the Invisible effect doesn't hide a player (hidden admins, paused players and the newbie period still do). `EntityUtils.GetNearestEntity` takes it through.
- `PlayerWithinTransition` and `NoPlayerWithinTransition` see cloaked players in an Empowered fight; so do the Doomed engine's presence conditions (`DoomedRuntime`: `IsEntityPresent`, `IsEntityNotPresent`, `EntityLesserThan`, through a `gate` flag on its nearest-entity helpers).
- `Empowered.Tick`'s "someone to fight" counts cloaked players.
- Aiming is unchanged: `Shoot`, chasing and the Doomed attacks still lose an invisible player, so the cloak still dodges aimed shots. Fixed-angle patterns (the King's stand) were never aimed.
- Outside an Empowered fight nothing changes (Elder and regular bosses behave as before).

Files: `core/worlds/World.cs`, `core/resurrection/BossResurrection.cs`, `core/resurrection/Empowered.cs`, `core/objects/player/Player.cs`, `Player.Effects.cs`, `core/objects/Decoy.cs`, `Pet.cs`, `utils/EntityUtils.cs`, `logic/transitions/PlayerWithinTransition.cs`, `NoPlayerWithinTransition.cs`, `logic/doomed/DoomedRuntime.cs`; the wiki's Necromantic Relic page.

Deploys the world server only (rolling, nobody is disconnected). No Redis data.

### Every Empowered soul shot silences for a second

The four soul shots every Empowered form carries (projectiles 90 to 93: the soul bolt, the fast bolt, the orb and the shard) put Quiet on for 1 second, on top of what they had (the fast bolt still slows for 2). The Wailing Souls' own bolts and the bosses' signature shots are unchanged.

`tools/build_resurrection.py` adds it (`SOUL_QUIET`). Running the generator today rewrites far more than this (the committed `Resurrection.xml` and `resurrection.json` have drifted from it: other items reordered, the Elder dungeon keys added to the Empowered loot), so the two `Resurrection.xml` copies (`source/Shared/resources/xml/custom/` and the client's `custom/xmls/`) were patched with exactly what the generator now writes for these shots, and nothing else. Someone should look at that drift before the generator is next run.

Deploys the world server and the client (the client applies a hit's conditions as it's hit; an older client shows the Quiet once the server's flags arrive).

### New listings announced in game and on Discord

- World server: `Marketplace.Sell` and `SellFromVault` call `Announce` once a listing is in: `ChatManager.MarketListed` publishes a `ChatType.Market` chat message to every world server, each sending its players a `Marketplace` line (`Player.SendMarketNotif`, gold name), not to those ignoring the seller; and `MarketStore.RecordListed` pushes the listing onto the Redis list `market.feed` (newest first, trimmed to 100). A hidden admin's listings aren't announced. An older world server during a rolling deploy ignores the new chat type.
- Account server: `discord/market` (`DiscordController`, behind the bot's secret) answers the feed's listings after a given id, oldest first, and the newest id (`after=-1`: the newest only, for a first start).
- Bot: `market_loop` polls it every 30 s and posts to #marketplace (`DISCORD_MARKET_CHANNEL`, else the guild's channel named marketplace), up to 15 listings to an embed, mentions off; the last id posted is `market_after` in its state file. `setup_server.py` adds #marketplace (INFO, read-only) and writes `DISCORD_MARKET_CHANNEL`.
- Docs: SETUP-LOCAL.md ("Discord", "Marketplace listings"), AGENTS.md, the wiki's Marketplace page.

Deploys the world server (rolling), the account server (sign-ins pause), and the Discord bot on the website machine; nobody is disconnected. No client change (the line is an ordinary chat line). Redis gains `market.feed`; nothing migrated.

**For the owner:** create the channel: `python tools\discord\setup_server.py --env deploy\discord.local.env --apply` (look at its plan first without `--apply`). The bot finds the channel by name, so the `DISCORD_ENV` secret on GitHub needn't change; if you'd rather pin the id, copy the new `DISCORD_MARKET_CHANNEL` line into that secret too.

## Checks

- Invisibility: built the world server; on an isolated stack (slot 13000, a copy of the data), a bot with `/god` and `/eff Invisible` in its vault raised `/empower Pirate 50` beside itself: Soul Harvest and Soul Storm were announced and their bolts and the Soul Nova fired (logged with `--shots`) while it stayed invisible. The old code returned before the phases whenever no visible player was within 20 tiles (not run again to compare). The Forgotten King's stand itself wasn't played through in game.
- Quiet: `EmpoweredPhaseTests.EverySoulShotSilences` (all 25 Empowered forms, shots 90 to 93); `dotnet test source/Tests`, 289 passed.
- Wiki: `tools/build_wiki.py` into a scratch folder; the Necromantic Relic page shows both notes.
- Marketplace announcements: `MarketStoreTests.TheFeedKeepsTheNewestListingsForTheDiscordBot` (`dotnet test source/Tests`, 290 passed); world and account servers build. On an isolated stack (slot 150, a copy of the data, a test `discordSecret`), a `bsk-bot` ran `/sell 1 1234 3` and got `<Marketplace> Tester listed Health Potion for 1,234 fame.` in chat; `discord/market` then answered that listing (`after=0`), nothing after it, only the newest id with `after=-1`, and "Not allowed." for a wrong secret; `dodmg.market_messages` made the embed from that answer. Not posted to a real Discord channel, and the in-game line not seen from a second world server.
