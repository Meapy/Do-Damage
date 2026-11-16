Release 2.5.12: Empowered Forgotten King tuning and a bigger arena; deploys the world server (rolling, nobody is disconnected)

## For players

- **Dungeons:** the Empowered Forgotten King can always be hurt once he has risen: his hiding, crystal and phase shields no longer make him untouchable. His Last Court phase (the four pillars) hits half as hard.
- **Dungeons:** raising the Forgotten King opens up his arena: the floor spreads out from him over about 2 seconds, up to 30 tiles to each side and 19 south, turning the magma and broken walls round his island into the island's cobblestone. His lava traps (which eat the floor at 60% and 40% health) are removed, so the arena doesn't shrink.

- **Items:** assassin poisons can be thrown twice as far (28 tiles), their cloud is 25% wider and they do 50% more damage (impact and poison). The Infernal Brimstone Poison now does 3,000.
- **Items:** health and magic potion stacks hold up to 10 instead of 6.
- **Marketplace:** purchases are announced like new listings: a gold **Marketplace** line in chat ("Buyer bought Health Potion from Seller for 1,234 fame.") and a line in Discord's **#marketplace**.

## Details

### Empowered Forgotten King: always vulnerable, softer Last Court

- `Empowered.AlwaysVulnerable` (true for the Forgotten King once his 2.5 s rise is over): `Enemy.Damage` ignores Invincible, Stasis and Invulnerable for him, and `Arena.Shield` (the phases' short shields) does nothing. His Elder behaviour's intro and crystal states still run; they just don't protect him.
- The Last Court's pillar shots take `EmpoweredSignatures.LastCourtDamage` (0.5): `Strike.FireFrom` / `Arena.ShootFrom` gained a `damageScale` argument.
- Files: `core/objects/Enemy.cs`, `core/resurrection/Empowered.cs`, `EmpoweredPhases.cs`, `EmpoweredSignatures.cs`, `logic/behaviors/BlackBetween.cs`.

Deploys the world server only (rolling). No client change, no Redis data.

### Empowered Forgotten King: bigger arena

- `EmpoweredGround.Widen`, called from `BossResurrection.RaiseAt` for the Forgotten King only: Elder magma (no object on it) and Shattered Walls within 30 tiles sideways, 9 north (the entrance walls stop it) and 19 south of him become Elder Dark Cobblestone, in 11 waves over 2.2 s while he rises; his `king lava1`/`king lava2` traps leave the world. Gold blast rings play meanwhile (no Earthquake: clients before 2.5.6 shake for good).
- Files: `core/resurrection/EmpoweredGround.cs`, `BossResurrection.cs`. World server only (rolling). No client change, no Redis data.

### Assassin poisons: farther, wider, stronger

- `tools/rework_poisons.py` edits every slot-18 `PoisonGrenade` in the server's and client's item XML (radius x1.25, `impactDamage` and `totalDamage` x1.5, new `throwRange="28"`, which also marks it done so a rerun changes nothing); `port_doomed.py` runs it after writing `DoomedRealms.xml`, so the Doomed poisons keep it (18 prod items, 4 Doomed ones).
- `ActivateEffect.ThrowRange` is read by `Player.AEPoisonGrenade`, which clamps the throw to it instead of the shared 14-tile `MAX_ABILITY_DIST` (`ClampAbilityTarget` gained a `maxDist` argument). Other abilities keep 14.
- Wiki: Infernal Brimstone Poison 2,000 to 3,000; note on the Assassin row of Gear statistics.
- Deploys the world server (rolling) and the website. The item tooltips read the new radius and damage from the XML; no client change. No Redis data.

### Marketplace purchases announced

- World server: `Marketplace.Buy` calls `AnnounceSale` after a purchase goes through: the same `ChatManager.MarketListed` chat line to every world server (not for those ignoring the buyer; none for a hidden admin buyer), and a `market.feed` entry with `kind` "sold", the buyer, and a fresh id from `MarketStore.NextId()` so the feed stays ordered for the bot's `after` cursor (a listing and its sale don't share an id).
- Bot: `dodmg.market_line` words a sold entry ("X bought Y from Z for N fame"); the account server's `discord/market` needed no change. An older bot shows a sale as a listing, so deploy the bot with the world server.
- Deploys the world server and the Discord bot. No client change, nobody is disconnected.
- Tested: `MarketStoreTests.ASaleGoesInTheFeedAfterTheListing`; `bot.py --dry-run` shows the sale post.

### Health and magic potion stacks hold up to 10

- Raised `MaxStackablePotions` in `Shared/resources/data/init.xml` from 6 to 10; this applies to both the health and magic potion stacks. Existing character counts are kept and aren't automatically increased.
- World server only (rolling deploy, nobody is disconnected). No client change or Redis migration.

## Checks

- World server builds; `dotnet test source/Tests`, 290 passed. Not played through in game on an isolated stack.
- Bigger arena: world server builds; not yet played through in game on an isolated stack.
- Potion stack limit: world server builds and tests pass; existing counts are unchanged.
