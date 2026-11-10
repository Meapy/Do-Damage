Release 2.4.10: new item art, more frequent Necromantic Relics and Elder dungeon keys (deploys the client, servers and website; nobody is disconnected)

## For players

- **Items:** new art for the Enthroned gear: the Sword, Dagger, Bow, Wand, Katana, Dual Blades and Staff, the Hide, Plate and Robe, and the Boots. Each of those seven weapons has a shot of its own for its first volley, taking turns with Oryx's violet Rift as before. The Enthroned Helm and both rings keep their look, and the Enthroned Fists still fire the gold Radiance.
- **Items:** new art for the Monk's fists, all sixteen tiers from the Cloth Hand Wraps (T0) to the Fists of the Ascended (T15); the Enthroned Fists take the new T15 look, with their purple sparkles.
- **Elder dungeons:** Necromantic Relics now have a 5% drop chance per player who dealt at least 2% of an Elder boss's damage, up from 3%; each Elder dungeon also drops its own key at the regular dungeon's chance (1% where no regular key chance is configured).
- **Help calls:** `/help` asks eligible players in the Nexus or realms to join you and announces
  the call to everyone online once a minute; `/phelp` and `/ghelp` call your party or guild. Click
  the chat link to ask to join the caller.
- **Game:** timed portals and loot bags flash during their last five seconds; status effects,
  including temporary stat boosts, flash during their last two seconds.

## Details

### Enthroned gear: the owner's sprites and per-weapon shots

- New sheet `client/src/kabam/rotmg/assets/custom/images/EnthronedItems8x8.png` (`enthronedItems8x8`, 56×48, 7 cells a row, hand-drawn by the owner): top row the weapons (Sword, Wand, Bow, Dagger, Staff, Katana, Dual Blades), the row under it each one's shot, the fourth row the Hide, Robe and Plate, the sixth the Boots. Registered in `tools/gen_rs_manifest.py` (manifest regenerated).
- `tools/build_ascended_items.py`: those items' textures point at the new sheet; `OWN_SHOTS` adds seven projectile objects (0x5873-0x5877, 0x5879, 0x587a; 0x5878 was taken), each the weapon's first-volley `ObjectId` in place of the Enthroned Radiance. The Rift (second volley, piercing and ignoring defense) is unchanged, and so are all numbers. The Fists keep their sparkled T15 sprite and the Radiance; the Helm keeps its drawn piece. The old Staff (16×16), Robe and Boots cells are still drawn but unused. Regenerated: both `CustomItems.xml`, `AscendedItems8x8.png` (now without the weapons' and armours' sparkled cells).
- Redeploy: client (the new sheet) and world server (the XML). Nobody is disconnected; nothing in Redis (type codes of the items unchanged). An older client doesn't know the new sheet or shots, so it shows them wrongly until it updates.
- Docs: changelog (Items), the wiki's forge page, SETUP-LOCAL.md "Changes from upstream".
- Not checked: the shots in flight in the game (only the data resolved through the manifest, see Checks). Some of the shot cells have a lone dark pixel or 2×2 block in their bottom-left corner, as drawn.

### The Monk's fists: the owner's sprites

- New sheet `client/src/kabam/rotmg/assets/custom/images/FistsItems8x8.png` (`fistsItems8x8`, 128×8, hand-drawn by the owner): a cell per tier, T0 Cloth Hand Wraps to T15 Fists of the Ascended (0x5802-0x5810, 0x582a). Registered in `tools/gen_rs_manifest.py` (manifest regenerated).
- Both `CustomItems.xml`: those sixteen items' textures point at it (hand-written entries, outside any generator's marks). `tools/build_ascended_items.py` reads the new sheet for the Enthroned Fists' sparkled copy (`SOURCE_SHEETS`); `AscendedItems8x8.png` regenerated. The old tiles 0x70-0x7E of `CustomAssets8x8.png` and the T15 cell of `T15Weapons8x8.png` are unused now (left in place).
- Redeploy: client (the new sheet) and world server (the XML). Nobody is disconnected; nothing in Redis. An older client doesn't know the sheet and shows the fists wrongly until it updates.
- Docs: changelog (Items), SETUP-LOCAL.md (the Monk fist sprites entry), `tools/draw_t15_weapons.py`'s notes. The wiki's gear pages pick up the new icons from the data; no hand-written guide describes the fists' look.

### Elder dungeon keys

- Each of the 14 Elder dungeon keys now drops from loot in its own dungeon. The key uses its regular dungeon's drop chance where configured (including the Snake Pit and Sprite World keys at 10% and the Davy Jones and Undead Lair keys at 0.9%); otherwise it drops at 1%. The Elder Tomb Key chance is now 1%, matching the Tomb key. The Elder Draconis Key stays at 1% on Elder Draconis dragon loot, and the Elder Ivory Wyvern Key is 1% on the Elder Wyvern; neither drops in the other dungeon.
- Redeploy: world server and website (updated wiki); no one is disconnected. No client, protocol or Redis change.
- Docs: changelog, wiki, SETUP-LOCAL.md.

### Help calls

- Added `/help`, `/phelp` and `/ghelp`. From a dungeon or instance, `/help` sends summon requests to eligible online players currently in the Nexus or a realm, announces the call to everyone online across world servers, and can be used once a minute; the party and guild variants limit recipients to those groups. Requests are limited to players connected to the same world-server process and use the existing summon rules, one request per recipient, the 30 s answer window and the usual cooldowns. Each recipient sees a clickable chat link that sends `/visit <name>` to request entry; `/summon cancel` withdraws the whole help call.
- The Rust client styles and handles the link; other clients still see the text and can accept the summon request or type `/visit <name>`. No client protocol or persistent Redis data change; an expiring Redis key tracks the cooldown, and the announcement uses the existing inter-server chat channel.
- Redeploy: world servers; nobody is disconnected.
- Docs: changelog, SETUP-LOCAL.md, wiki commands page.

### Status effects and expiring objects

- The world server sends remaining durations for timed status effects (including stat boosts) and
  portals as integer stats; loot bags already send their remaining lifetime as health. The Rust
  client flashes status icons during their last two seconds and loot bags and timed portals during
  their last five seconds. Permanent effects and portals that do not expire do not flash.
- The new stats are optional and older clients ignore them. No Redis migration.
- Redeploy: world server and Rust client; nobody is disconnected.
- Docs: changelog and SETUP-LOCAL.md.

## Checks

- Enthroned gear: `build_ascended_items.py` and `gen_rs_manifest.py` rerun; every Enthroned item and shot resolved through the manifest to a non-empty cell and rendered side by side; `build_wiki.py` shows the new icons on the gear pages; server tests (`dotnet test`, 177 passed).
- Monk's fists: every Fists item (T0-T15 and the Enthroned Fists) resolved through the manifest to a non-empty cell and rendered side by side; `build_wiki.py` draws the new icons on the Martial Art gear page; server tests (`dotnet test`, 177 passed).
- Elder dungeon keys: Elder loot regression tests verify each key's source and chance; `build_wiki.py` shows the key on its matching dungeon page.
- Help calls: `dotnet test` covers multi-recipient requests, answers, withdrawal and the one-minute `/help` cooldown; `cargo test -p bsk-client` covers clickable link rendering and normal chat behavior.
- Status effects and expiring objects: `dotnet test source\Tests\Tests.csproj --no-restore` (197 passed); from `client-rs`, `cargo test -p bsk-proto -p bsk-world -p bsk-client` (145 passed).

### Necromantic Relic drop rate

- Raised the per-eligible-player drop chance from 3% to 5%; the 2% damage threshold is unchanged. `tools/build_resurrection.py` is the source and regenerates `source/Shared/resources/data/resurrection.json`. The wiki reads the generated value. No Redis migration or client protocol change.
- Redeploy: world server and website (updated wiki); no one is disconnected.
- Docs: changelog, wiki, SETUP-LOCAL.md.

- Checks: regenerated resurrection data and verified the wiki reports 5%.
