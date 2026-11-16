Release 2.5.13

## For players

- **Classes:** the Ascendant's halos raise a golden forcefield instead of healing. You and every ally standing in it take less from each hit while it lasts (shots, damaging ground, everything): 2% with the Worn Halo, 1% more each tier, up to 10% with the T8 Halo of the Archangel. Each field is 1.5 tiles smaller than the old healing circle (4.5 tiles at T0-T1, up to 6.5 from T4) and lasts as long as the halo's heal did (8 to 24 seconds). It looks the part: the halo falls from the sky in a column of light, shockwaves race out, a shimmering hex-lattice dome opens with turning rune rings and a wall of light rising off its rim, it flares round anyone it shields from a blow, and it shatters as it fades. The Ascendant still earns assist loot for every player it covers. The class and halo descriptions are rewritten to match.

## Details

### Ascendant halos: forcefields instead of healing

- **What changed:** the halo ability's `Halo` activate is now `Forcefield` (`ActivateEffects.Forcefield`): `range` the radius, `amount` the percent off each hit, `duration` its time. `Forcefield.cs` (was `Halo.cs`) shields every player in it each tick (`Player.Shield`, held 250 ms so the edge doesn't flicker); `Player.ShieldedDamage` takes the percent off each hit after defense, rounded to the nearest point, in `PlayerHit`, `Player.Damage` and both ground-damage paths. Overlapping fields don't add up: the strongest counts. The caster gets assist credit (`BuffedBy`) for each player in it, a second at a time. Its server-side cooldown (4 s) is kept.
- **Numbers:** T0 Worn 2% / 4.5 tiles, T1 Rusty 3% / 4.5, T2 Bronze 4% / 5, T3 Silver 5% / 5.5, T4 Golden 6% / 6.5, T5 Eternal Peace 7% / 6.5, T6 God's Blessing 8% / 6.5, T7 Seraphim 9% / 6.5, T8 Archangel 10% / 6.5. Durations and mana unchanged.
- **Look (Rust client, `particles/relic.rs`):** `ShowEffect` 41 looks 30 (cast), 31 (a player in it hit, at most every 250 ms each) and 32 (already up when a player comes into view, sent then). The percent rides in `ObjectType`, which the client uses to take the same cut off the damage numbers it shows for your own hits (`bsk_world::combat::shielded`, tested against the server's rounding). New decals `Forcefield` and `FieldFlare`, an aura for the rising wall, a shatter when the aura ends. Older clients don't know these looks; the server also sends a golden Trap ring at its edge every 1.5 s for them, which the new client leaves out.
- **Data:** `tools/port_doomed.py` (`FORCEFIELDS`, halo and class `DESCRIPTIONS`) converts the T0-T6 halos; both `DoomedRealms.xml` copies were changed by hand to what it writes, because the port currently stops early on an unrelated world (`ElderPirateCave` missing from `port_doomed_worlds`). The T7 and T8 halos are hand-written in both `CustomItems.xml` copies. `tools/rework_buff_auras.py` no longer floors the halos' range (re-running it changes nothing).
- **Wiki:** `tools/wiki_gear.py` describes the forcefield; `website/wiki/classes.md` mentions shielding for assist loot.
- **Deploys:** client, servers and website. Ship client and server together (new looks and the `Forcefield` activate name): an old client still plays, but sees only the gold rings and its tooltip doesn't describe the halo. Nobody is disconnected; no Redis data changes.
- **Not done:** the halos keep their `spell/major_heal` sound.

## Checks

- **Ascendant forcefields:** `dotnet build` of WorldServer; `cargo test -p bsk-client` (100 passed, including the language tables) and `cargo test -p bsk-world` (including `forcefield_rounds_as_the_server_does`); `python tools/rework_buff_auras.py` changes nothing; `python tools/build_wiki.py` into a scratch folder, the Halo gear page and the Ascendant class page read right. On an isolated stack (slot 121), a level 20 Ascendant cast the Halo of God's Blessing in a vault: `--shot`s at 1280x800 and full screen show the dome, lattice, rune rings and rim round the caster. The damage cut itself wasn't measured in game (the test stack keeps the character topped up).
