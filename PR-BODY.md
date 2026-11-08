Release 2.4.7: party buffs reach further and new weapon art (deploys client and servers; nobody is disconnected)

## For players

- **Classes:** party buffs reach much further. Warrior helms, Bard lutes and Blademaster jackets went from 4-4.5 tiles at every tier to 6 at T0 rising to 8 at T8 (7 for untiered ones). Paladin seals (4.5 at every tier) and Priest tomes (3 to 6) now reach 5 at T0 up to 7 at T8 (6.5 untiered), and wisdom from 50 up stretches that further, by half again at 75. The low-tier Monk fountains and Ascendant halos reach at least 6 (they were 5-6). Anything that already reached further keeps its range. The Snake Charmer Pungi used to buff only the bard; it now gives its Speedy and +8 dexterity to everyone within 7 tiles, as the other lutes buff the party. Inspired's faster shots now count fully on the server.
- **Items:** new hand-drawn art for the dual blades, T0 Bronze Daggers to T14 Eclipse Blades, each tier now with a shot of its own (before, tiers shared shots, and Master, Sovereign and Eclipse Blades shared one icon); for the T15 weapons (Sword of Ascension, Dagger of Divine Wrath, Bow of the Firmament, Wand of Absolution, Staff of Endless Energy, Ame-no-Habakiri, Seraphic Blades), each with its own shot too (they shot their T14s'); for the T16 armors (Hide of the Firmament, Armor of Ascension, Robe of Absolution); and for the Enthroned Rings of Wrath (red gem) and Grace (purple-blue gem). The Fists of the Ascended keep their sprite.

## Details

### Wider party buffs

- Why: the helms, lutes and jackets reached 4-4.5 tiles at every tier (seals 4.5, low tomes 3), so a party had to stand on top of each other to share Berserk, Inspired, Armored or a seal's boost.
- `tools/rework_buff_auras.py` (new): on items of slot types 16, 12, 4, 28, 37, 130 and 126, every ConditionEffectAura, StatBoostAura (positive), HealNova, Halo, ClearConditionEffectAura, RemoveNegativeConditions and `target="player"` GenericActivate gets `range` = max(its range, a floor by tier). Without `useWisMod` the floor is 6, 6, 6.5, 6.5, 7, 7, 7.5, 7.5, 8 (T0-T8), or 7 untiered. With `useWisMod` it's 5, 5, 5.5, 5.5, 6, 6, 6.5, 6.5, 7, or 6.5 untiered. It edits both copies (server and client) of `EmbeddedData_EquipCXML.xml`/`Equip.xml`, `CustomItems.xml`, `DoomedRealms.xml` and `Resurrection.xml` as text, and running it again changes nothing. `port_doomed.py`, `build_resurrection.py` and `build_ascended_items.py` run it after writing their XML, so a regeneration keeps the change. In all, 98 abilities changed: 72 in Equip, 4 in CustomItems, 19 in DoomedRealms and 3 in Resurrection.
- Snake Charmer Pungi: ConditionEffectSelf Speedy and StatBoostSelf Dex become ConditionEffectAura and StatBoostAura (noStack), at range 7.
- Server: `Player.OtherHit` passed `SpeedMult / 100` (integer division, so 1 under Inspired's 125) to the projectile position; now `100f`. The lute's Inspired ShowEffect uses the lute's range instead of a fixed 4. A tome's shared self buffs (`TomeShareRange`) follow its heal range, so they reach further too.
- Wiki: the gear pages read the new ranges from the data. `website/wiki/classes.md` gets a paragraph on the party buffs' ranges.
- Not changed: Wizard's Spell of Life (HealNova range 1, effectively a self heal) and the self-only helms (Helm of the Immortal, Swift Bunny, Challenger) are left as they were.
- Redeploy: servers (world: XML and OtherHit) and the client (XML, for its tooltips). Nobody is disconnected. Nothing in Redis changes.

### Hand-drawn dual blades, T15 weapons, T16 armours and Enthroned rings

- The owner's art. `client/src/.../custom/images/BladesAndT15s8x8.png` (new, `bladesAndT15s8x8`, registered in `gen_rs_manifest.RS_ONLY_SETS`; manifest regenerated): 18 cells a row; cells 0-14 the dual blades T0-T14, 18-32 their shots; column 16 the seven T15 weapons a row each (cells 16, 34, 52, 70, 88, 106, 124), their shots beside them in column 17. `T16Armors8x8.png` replaced with the owner's armours.
- 22 new projectiles, `<item> Shot`, 0x5888-0x589d, in `CustomProj.xml` (both copies), each keeping its old shot's `AngleCorrection`. The 22 items point at the new icons and shots in `CustomItems.xml` and `DoomedRealms.xml` (both copies, patched line by line); `port_doomed.py` `TEXTURE_OVERRIDES` and `PROJECTILE_OVERRIDES` carry the Doomed Realms ones so a regenerate keeps them. The old shots (Bronze Proj, Master Proj, Kusanagi Slash, ...) stay for whatever else uses them.
- `tools/draw_t15_weapons.py` no longer writes `T16Armors8x8.png`; its T15 sheet comes out unchanged, but only the Fists of the Ascended still show their cell. `build_ascended_items.py` copies from both sheets, so rerunning it would copy the new armour art into the Enthroned pieces (not rerun here).
- SETUP-LOCAL.md, "Changes from upstream", has the layout.
- Redeploy: client (sheet and XML) and servers (XML: the new shot types). Nobody is disconnected; nothing in Redis changes. An older client shows the old sprites until it updates.
- Enthroned Rings of Wrath and Grace: the owner's two 8 by 8 rings on `EnthronedRings8x8.png` (new, `enthronedRings8x8`, in `RS_ONLY_SETS`), Wrath cell 0, Grace cell 1, in `CustomItems.xml` (both copies) and in `build_ascended_items.py` (`RINGS8`), whose 16 by 16 ring cells are still drawn but unused.

## Checks

- Wider party buffs: `dotnet build` of the WorldServer (0 errors); `dotnet test source\Tests` (169 passed); `cargo test -p bsk-data` (the client parses the XML, 9 passed); the script run twice (the second run changes nothing); `tools/build_wiki.py` built to a scratch folder, and the lute, helm, seal and tome cards show the new ranges (Nether Lute: "allies within 8 tiles"). Not tried in game on an isolated stack.
- Hand-drawn sprites: every one of the 24 items and 22 shots resolved through the regenerated manifest and rendered side by side with the owner's images (all in place); new type ids 0x5888-0x589d unique in both XML trees; `tools/draw_t15_weapons.py` rerun (T15 sheet unchanged); `dotnet test source\Tests` (169 passed); `cargo test --workspace` in client-rs (all passed; `bsk-data` parses the XML, 9 passed); `tools/build_wiki.py` to a scratch folder (the dual blade, T15 and ring icons come from the new sheets). Not looked at in game (no `--shot` on an isolated stack).
- Release 2.4.7: `cargo test --workspace` in client-rs (all passed), `dotnet build` of the WorldServer and App (0 errors), `dotnet test source\Tests` (169 passed), `python tools\build_site.py` (built).
