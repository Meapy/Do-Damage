Release 2.6.10: the Platinum Sword reworked as the Ice Tomb's sword. Deploys the world server and client; nobody is disconnected.

## For players

### Items
- **The Platinum Sword, reworked:** the Ice Tomb's own sword now, a rare drop from each of the Frozen Court (0.25%, as the tomb's bows and wands); Oryx 4 and the Christmas Oryx no longer drop it. Each swing fires three: a platinum slash, 180-220, that cleaves through every enemy in its path out to 7 tiles, flanked by two frost shards, 35-50 each out to 6 tiles, that slow what they hit for 1.5 seconds. At 100% rate of fire instead of 150% (8 tiles before): up close on one enemy about 285 damage a second, between the T14 and T15 swords, and about 200 from the slash alone at range, the price of twice a sword's reach. Same look; swords already owned change with it.

## Details

### The Platinum Sword reworked
- **What and why:** a plain ported sword (170-190 at 150%, range 8: 270 a second from twice any sword's reach) dropped by Oryx 4 half the time. Now a mid-tier dungeon's prize that keeps its long reach (6-8 tiles, as the owner asked) and pays for it in damage, with a reason to stand close: a `<VolleyProjectiles>1,0,1</VolleyProjectiles>` volley (as the Elysian Staff's) of a `<MultiHit/>` silver slash (the Oryx Champion Slash sprite, with a pale trail) and two Iceblast shards with `Slowed` 1.5 s, `<ArcGap>14</ArcGap>`, so the shards meet one target only up close. The tooltip lists the slash; the shards are an `EffectInfo` ("Rime Shards").
- **Files:** `source/Shared/resources/xml/custom/DoomedRealms.xml` and the client's copy (`client/src/kabam/rotmg/assets/custom/xmls/DoomedRealms.xml`), `BehaviorDb.IceTomb.cs` (`IceTombTreasure()`), `logic/doomed/DoomedMonsters.cs` (Oryx 4 and Christmas Oryx tables), SETUP-LOCAL.md, the changelog, `website/wiki/`.
- **Deploys:** the world server and the client together (both read the sword's XML: a client not yet updated keeps firing the old single slash, which the server's rate check may refuse, until it updates). Nobody is disconnected.
- **Redis:** nothing migrated; the type code (0xD0F7) is kept, so owned swords take the new stats.

## Checks
- **Platinum Sword:** `dotnet test source\Tests` (563 passed) and `cargo test -p bsk-client` pass; both XML copies parse with the same sword block; the wiki builds (`tools/build_wiki.py --out <scratch>`) with the card reading "180-220 + 2 x 35-50 (Slowed 1.5s), range 7, hits several" and dropping from the three of the Court, the Elysian Staff's still "200-250 x 3". Seen in game on an isolated stack (slot 1): a Warrior firing at the vault's Training Dummy shows the silver slashes (the Oryx Champion Slash sprite, with a pale trail) between the cyan frost shards, and the tooltip reads 180-220, 3 shots, range 7, 100%, with the Rime Shards line.
