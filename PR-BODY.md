Release 2.4.7: party buffs reach further (deploys client and servers; nobody is disconnected)

## For players

- **Classes:** party buffs reach much further. Warrior helms, Bard lutes and Blademaster jackets went from 4-4.5 tiles at every tier to 6 at T0 rising to 8 at T8 (7 for untiered ones). Paladin seals (4.5 at every tier) and Priest tomes (3 to 6) now reach 5 at T0 up to 7 at T8 (6.5 untiered), and wisdom from 50 up stretches that further, by half again at 75. The low-tier Monk fountains and Ascendant halos reach at least 6 (they were 5-6). Anything that already reached further keeps its range. The Snake Charmer Pungi used to buff only the bard; it now gives its Speedy and +8 dexterity to everyone within 7 tiles, as the other lutes buff the party. Inspired's faster shots now count fully on the server.

## Details

### Wider party buffs

- Why: the helms, lutes and jackets reached 4-4.5 tiles at every tier (seals 4.5, low tomes 3), so a party had to stand on top of each other to share Berserk, Inspired, Armored or a seal's boost.
- `tools/rework_buff_auras.py` (new): on items of slot types 16, 12, 4, 28, 37, 130 and 126, every ConditionEffectAura, StatBoostAura (positive), HealNova, Halo, ClearConditionEffectAura, RemoveNegativeConditions and `target="player"` GenericActivate gets `range` = max(its range, a floor by tier). Without `useWisMod` the floor is 6, 6, 6.5, 6.5, 7, 7, 7.5, 7.5, 8 (T0-T8), or 7 untiered. With `useWisMod` it's 5, 5, 5.5, 5.5, 6, 6, 6.5, 6.5, 7, or 6.5 untiered. It edits both copies (server and client) of `EmbeddedData_EquipCXML.xml`/`Equip.xml`, `CustomItems.xml`, `DoomedRealms.xml` and `Resurrection.xml` as text, and running it again changes nothing. `port_doomed.py`, `build_resurrection.py` and `build_ascended_items.py` run it after writing their XML, so a regeneration keeps the change. In all, 98 abilities changed: 72 in Equip, 4 in CustomItems, 19 in DoomedRealms and 3 in Resurrection.
- Snake Charmer Pungi: ConditionEffectSelf Speedy and StatBoostSelf Dex become ConditionEffectAura and StatBoostAura (noStack), at range 7.
- Server: `Player.OtherHit` passed `SpeedMult / 100` (integer division, so 1 under Inspired's 125) to the projectile position; now `100f`. The lute's Inspired ShowEffect uses the lute's range instead of a fixed 4. A tome's shared self buffs (`TomeShareRange`) follow its heal range, so they reach further too.
- Wiki: the gear pages read the new ranges from the data. `website/wiki/classes.md` gets a paragraph on the party buffs' ranges.
- Not changed: Wizard's Spell of Life (HealNova range 1, effectively a self heal) and the self-only helms (Helm of the Immortal, Swift Bunny, Challenger) are left as they were.
- Redeploy: servers (world: XML and OtherHit) and the client (XML, for its tooltips). Nobody is disconnected. Nothing in Redis changes.

## Checks

- Wider party buffs: `dotnet build` of the WorldServer (0 errors); `dotnet test source\Tests` (169 passed); `cargo test -p bsk-data` (the client parses the XML, 9 passed); the script run twice (the second run changes nothing); `tools/build_wiki.py` built to a scratch folder, and the lute, helm, seal and tome cards show the new ranges (Nether Lute: "allies within 8 tiles"). Not tried in game on an isolated stack.
