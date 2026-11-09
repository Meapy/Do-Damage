Release 2.4.10: new art for the Enthroned gear (deploys the client and the servers; nobody is disconnected)

## For players

- **Items:** new art for the Enthroned gear: the Sword, Dagger, Bow, Wand, Katana, Dual Blades and Staff, the Hide, Plate and Robe, and the Boots. Each of those seven weapons has a shot of its own for its first volley, taking turns with Oryx's violet Rift as before. The Enthroned Fists, Helm and both rings keep their look (the Fists still fire the gold Radiance).

## Details

### Enthroned gear: the owner's sprites and per-weapon shots

- New sheet `client/src/kabam/rotmg/assets/custom/images/EnthronedItems8x8.png` (`enthronedItems8x8`, 56×48, 7 cells a row, hand-drawn by the owner): top row the weapons (Sword, Wand, Bow, Dagger, Staff, Katana, Dual Blades), the row under it each one's shot, the fourth row the Hide, Plate and Robe, the sixth the Boots. Registered in `tools/gen_rs_manifest.py` (manifest regenerated).
- `tools/build_ascended_items.py`: those items' textures point at the new sheet; `OWN_SHOTS` adds seven projectile objects (0x5873-0x5877, 0x5879, 0x587a; 0x5878 was taken), each the weapon's first-volley `ObjectId` in place of the Enthroned Radiance. The Rift (second volley, piercing and ignoring defense) is unchanged, and so are all numbers. The Fists keep their sparkled T15 sprite and the Radiance; the Helm keeps its drawn piece. The old Staff (16×16), Robe and Boots cells are still drawn but unused. Regenerated: both `CustomItems.xml`, `AscendedItems8x8.png` (now without the weapons' and armours' sparkled cells).
- Redeploy: client (the new sheet) and world server (the XML). Nobody is disconnected; nothing in Redis (type codes of the items unchanged). An older client doesn't know the new sheet or shots, so it shows them wrongly until it updates.
- Docs: changelog (Items), the wiki's forge page, SETUP-LOCAL.md "Changes from upstream".
- Not checked: the shots in flight in the game (only the data resolved through the manifest, see Checks). Some of the shot cells have a lone dark pixel or 2×2 block in their bottom-left corner, as drawn.

## Checks

- Enthroned gear: `build_ascended_items.py` and `gen_rs_manifest.py` rerun; every Enthroned item and shot resolved through the manifest to a non-empty cell and rendered side by side; `build_wiki.py` shows the new icons on the gear pages; server tests (`dotnet test`).
