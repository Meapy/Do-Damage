Release 2.6.10: the Platinum Sword reworked as the Ice Tomb's sword. Deploys the world server and client; nobody is disconnected.

## For players

### Items
- **The Platinum Sword, reworked:** the Ice Tomb's own sword now, a rare drop from each of the Frozen Court (0.25%, as the tomb's bows and wands); Oryx 4 and the Christmas Oryx no longer drop it. Each swing fires three: a platinum slash, 215-255, that cleaves through every enemy in its path, flanked by two frost shards, 45-60 each, that slow what they hit for 1.5 seconds. At 100% rate of fire and a sword's reach (about 4 tiles) instead of 150% at 8 tiles: up close on one enemy about 340 damage a second, the T15 Sword of Ascension's, the slash alone at range about the T13's. Same look; swords already owned change with it.

## Details

### The Platinum Sword reworked
- **What and why:** a plain ported sword (170-190 at 150%, range 8: 270 a second from twice any sword's reach) dropped by Oryx 4 half the time. Now a mid-tier dungeon's prize with a reason to stand close: a `<VolleyProjectiles>1,0,1</VolleyProjectiles>` volley (as the Elysian Staff's) of a `<MultiHit/>` Beam Slash and two Iceblast shards with `Slowed` 1.5 s, `<ArcGap>14</ArcGap>`, so the shards meet one target only up close. The tooltip lists the slash; the shards are an `EffectInfo` ("Rime Shards").
- **Files:** `source/Shared/resources/xml/custom/DoomedRealms.xml` and the client's copy (`client/src/kabam/rotmg/assets/custom/xmls/DoomedRealms.xml`), `BehaviorDb.IceTomb.cs` (`IceTombTreasure()`), `logic/doomed/DoomedMonsters.cs` (Oryx 4 and Christmas Oryx tables), SETUP-LOCAL.md, the changelog, `website/wiki/`.
- **Deploys:** the world server and the client together (both read the sword's XML: a client not yet updated keeps firing the old single slash, which the server's rate check may refuse, until it updates). Nobody is disconnected.
- **Redis:** nothing migrated; the type code (0xD0F7) is kept, so owned swords take the new stats.

## Checks
- **Platinum Sword:** `dotnet test source\Tests` (563 passed) and `cargo test -p bsk-client` pass; both XML copies parse with the same sword block; the wiki builds (`tools/build_wiki.py --out <scratch>`) with the card reading "215-255 + 2 x 45-60 (Slowed 1.5s), range 4.2, hits several" and dropping from the three of the Court, the Elysian Staff's still "200-250 x 3". Not tried in game.
