Release 2.6.8: the Druid reworked: pearls thrown into tide pools that pulse, then called home through the enemies, with new sprites, a new look and five Armoire skins. Deploys the world servers and the client together (the pearls need both); nobody is disconnected.

## For players

### Druid
- **Pearls are thrown where you aim** (up to 8 tiles). Where one lands it splashes the enemies within 2 tiles (120 at T0 up to 380 at T8), then lies in a **tide pool** (reaching 2.5 tiles round it at T0 up to 3.5 at T8, for 3 to 5 seconds). Every half second the pool hurts the enemies in it straight through their defense (30 up to 120) and slows them, and mends the allies standing in it (5 up to 20 health; turrets too).
- **The pearl comes home:** when its pool's time is up it rushes back to you wherever you are, striking every enemy within about a tile of its way (180 up to 600), and heals you when it's back (50 up to 250), a fifth more for each enemy it hit, up to double. A faint lane on the ground from the pool to you shows where it will come, blinking in its last second.
- **Juggle the tide:** one pearl out at once up to T2, two from T3, three from T6; throwing one more calls the oldest home at once. Mana costs are as before (50 to 95).
- **New looks:** all nine pearls are redrawn (a wisp of magic, lightning, a vine, a halo, a ring of water, shadow, blood, a golden setting, chaos), and the throw, the pool, the recall and the homecoming each have their own effects.
- **Nerissa's Tidepearl** is a Druid's pearl too, above the T8 (420 splash, 130 a pulse, 650 on the way home, a 260 heal, three out at once), and its three waves of the tide now roll out from where it lands.
- **A new look and five skins:** the Druid is redrawn (an antlered hood, a sashed cloak, a pearl wand). The Armoire sells it five skins, each its own figure with something quiet round it: the **Deepwater Oracle** (coral crown, driftwood staff; bubbles rise round them and a ripple spreads at their feet), the **Autumn Warden** (antlers in red leaves, fur mantle, golden sickle; a leaf now and then falls from the antlers), the **Moonbloom Druid** (white hair, midnight robe, moonflower staff; a pool of moonlight and drifting motes), the **Mosshide Hermit** (toadstool cap, moss beard, lantern; three fireflies) at 200 gold, and the 16 by 16 **Archdruid** behind a stag's skull (eyes glowing in it, leaves circling, grass springing up in its footsteps) at 800. Dyes work on the Druid and on every skin.

### Website
- **The wiki's search boxes work again:** the filter on the Pets, Dungeons, gear, class and Necromantic Relic pages hid nothing you could see; now it hides every card that doesn't match.

## Details

### Druid: tide pearls

- **What and why:** asked to rework and finish the Druid. Its pearls only spawned an unseen substitute that fired one volley and healed the Druid: nothing to aim, nothing to play around. Now a pearl is a thrown pool you place, and its recall is a skillshot: the Druid positions so the enemies stand between them and their pools, and can throw again to call one home early.
- **World server:** `core/objects/TidePearl.cs` (an invisible anchor, 0x0711, as the forcefield's: its looks, pulses every 500 ms, recall at its end, gone with its Druid), `core/objects/player/Player.TidePearls.cs` (`AETidePearl`: the throw, landing after 380 ms, the oldest recalled past `pearls`; `PearlPulse`; `RecallPearl`: enemies within 1.1 tiles of the way, `TidePearl.OnWay`, struck as it passes, then `PearlHome`'s heal, +20% per enemy up to double), `Player.UseItem.cs` (refuses a pearl thrown onto a wall before the mana is spent), `Player.CruciblePowers.cs` (`NerissaTide`, from where it lands), `ActivateEffects.cs` (`TidePearl` in place of `PearlAbility`), `ActivateEffect.cs` (`pulseDamage`, `pulseHeal`, `pearls`). `AEPearlAbility`, Substitute1-9's behaviours and the Tidal Substitute's are gone.
- **Content:** the nine pearls in both `DoomedRealms.xml` copies (new activate, descriptions, no projectiles, the `druidPearls16x16` sprites; Substitute1-9 removed), the Druid's description; Nerissa's Tidepearl in `tools/build_crucible.py` (rerun: `CrucibleObjects.xml`, both copies; the Tidal Substitute gone, its type code kept in `tools/crucible/types.json`). Sprites: `tools/draw_pearls.py` writes `DruidPearls16x16.png`, registered in `tools/gen_rs_manifest.py` (rerun: `client-rs/assets/manifest.json`).
- **Client:** `particles/tide.rs` (relic looks 61-65: the throw, the splash and pool, one already down, the recall, the homecoming; the pool drawn with the ground marks: water, the tide's arms, a ripple each pulse, the hovering pearl, the lane home, our own's time left), wired in `particles.rs` and `particles/relic.rs` (`Pen::arms` shared); the server's fallback rings round a pool are left out. `hud.rs`: the pearl's tooltip and its one-line summary. 13 new lines in every `app/lang/*.json`, "Summons a {object}" removed.
- **Redeploy:** world servers and client. An older client keeps its own copy of the item XML, so until it updates it shows the old tooltips and sprites and sees only a ring at each pool's edge; the pearls still work for it, the server doing it all. Nobody is disconnected. Redis: nothing stored changes (the pearls keep their type codes).
- **Wiki:** `tools/wiki_gear.py` (the pearls' "On use" line), `website/wiki/classes.md` (the Druid's pearls), `crucible.md` (Nerissa's Tidepearl), `gear-statistics.md` (a note that the Druid's measured row is out of date).
- **Not done:** the numbers are first guesses for playtesting. The recall and homecoming were seen only through the tests and code: the scripted ability key fires in fewer than half the test client's runs. The hovering pearl in a pool is drawn as a shaded orb in its tier's colours, not its sprite.

### Druid: new look and skins

- **What and why:** asked to redraw the Druid and add skins, a 16 by 16 among them, each its own figure (not a recolour), restrained, and working with dyes.
- **Files:** `tools/draw_druid.py` (new) draws them all and writes the Druid's block (index 14) of `players.png` and `playersMask.png`, `DruidSkins8x8.png`, `DruidSkinsMask8x8.png`, `DruidSkins16x16.png` and `DruidSkinsMask16x16.png` (client `custom/images`), and both `DruidSkins.xml` copies (types 0xE210-0xE214, `<Cost>` 200 and 800, `<SkinLook>`); `--preview` writes dyed comparisons. `tools/gen_rs_manifest.py`: `druidSkins8`/`druidSkins16` with their masks (`RS_ONLY_CHARS` takes an optional mask path), `DruidSkins.xml` (rerun: `client-rs/assets/manifest.json`). Client: `auras.rs` (`SkinLook` Oracle, Warden, Moonbloom, Hermit, Archdruid and `sixteen()`; their effects), `view.rs` (reads `<SkinLook>` beside `<PassSkin>`; only 16-pixel skins are drawn at double scale). `<SkinLook>` is a new tag so the class screen and Armoire don't take these for Realm Pass skins (`<PassSkin>` marks those).
- **Masks:** red is the main dye, green the accent; the mask's amount carries the shading (lit 255, base 216, shadow 160, deep 110), since the client puts dye times amount in place of the pixel. Druid: hood and cloak main, sash accent. Oracle: robe main, hair accent. Warden: hood and mantle main, fur trim accent. Moonbloom: robe main, sleeves and train accent. Hermit: cap main, beard accent. Archdruid: leaf cloak main, moss accent.
- **Redeploy:** world servers (they load the skins' XML: without it the Armoire can't sell them) and client. Older clients don't know the skin types and draw the class's own sprite on a player wearing one. Nobody is disconnected; Redis: nothing changes.
- **Wiki:** `website/wiki/classes.md` (the Druid's skins). The generator doesn't list skins.
- **Not done:** the four smaller skins' effects were seen only in code; the Archdruid's in game.

### Wiki filters

- **What and why:** the owner found the Pets page's search did nothing. `wiki.js` sets `hidden` on the cards that don't match, but `.it` (and `.dg`) set `display: grid`, which beats the browser's `[hidden]` rule, so every card stayed on screen. `website/style.css` now has `[hidden] { display: none !important; }`.
- **Redeploy:** the website only (`build_site.py`); nobody is disconnected.

## Checks

- `dotnet test source\Tests`: 530 passed, with `EveryPearlIsATidePearl` (every pearl and Nerissa's a `TidePearl`, rising by tier, no projectiles) and `ARecalledPearlStrikesAlongItsWay`.
- `cargo test -p bsk-client`: passed (every relic look, 61-65 too, shows and then goes; every translation line used).
- `python tools\build_wiki.py`: the pearl pages show the new text and sprites.
- `cargo test -p bsk-client` again with the skins (`looks_by_name_and_steps_by_distance` checks the new looks and `sixteen`); `dotnet test source\Tests`: 530 passed with `DruidSkins.xml` loaded.
- `python tools\draw_druid.py --preview`: the Druid and every skin dyed red and gold, and blue and white, shade as they should.
- Isolated stack (slot 1): the Druid's class page lists the new Druid and the five skins with prices; a Druid wearing the Archdruid in the Nexus stands as tall as the other classes, its staff and a circling leaf showing.
- Isolated stack (slot 1), a Druid with the Pearl of Total Chaos in a vault with Pirates: the throw onto the floor landed a pool on a Pirate (380 splash, 95 MP spent), the pool drew; aimed off the floor it was refused ("You can't throw your pearl there"); the tooltip and the new inventory sprites show.
- Wiki filters: on a local build, a nonsense search left 0 cards shown and clearing it brought all back, on the Pets (both lists), Dungeons, Necromantic Relic, a gear page (Swords, Rings) and a class page (Wizard).
