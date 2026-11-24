Release 2.6.7: the Mechanic's turrets have health that allies can heal and buff, reach further and fall off with range; an admin's /summon brings a player into the Crucible without using one of their climbs. Deploys the world servers and the client (the turret changes need both); nobody is disconnected.

## For players

### Mechanic
- **Turrets have health instead of a timer:** each turret has health (400 at T0 up to 1,000 at T8; the War Turret 700, Ferrogrim's Bastion Turret 1,100), shown in a health bar under it and in the ring round its plate, and wears it all down over the time it used to last (12 seconds at T0 up to 20 at T8). When its health runs out it blows up as before. Allies' abilities reach it as they reach players standing there: heals (a priest's tome, a vampire blast, Nerissa's tide), Healing (20 a second), Damaging (hits a quarter harder), Berserk (fires a quarter faster), Armored (wears a third slower), Invulnerable (doesn't wear), seals and tomes' boosts (maximum health, attack, dexterity, defense), and cleanses. A turret your priest keeps healing keeps firing.
- **Further, but weaker far away:** turrets reach about 11 tiles (were 9; the Bastion Turret 11.5 from 9.6). Their shots hit in full out to 3 tiles, then less the further they fly, down to half at the end of their reach.

## Details

### Admin /summon into the Crucible costs no climb

- **World server** (`World.cs`, `DoomedRealmsCommands.cs`, `CrucibleWorld.cs`): `World.Invite` takes `free: true`, which an admin's `/summon` passes and which marks the account as brought in free to that world (`IsComped`). When a climb begins, `CrucibleWorld.Begin` skips `CrucibleAccess.Spend` for those accounts, so a player with no climbs left can be summoned into the lobby by an admin and the climb doesn't count against their day. Players' own `/summon` and `/visit` are unchanged.
- Not covered: an admin's summon between two world servers (`RemoteVisits`), and a climb already under way (the Crucible takes nobody new then, admins included).
- Redeploy: world servers only, rolling, nobody disconnected. No Redis data changed.

### Mechanic: turrets with health, range and falloff

- **What and why:** asked for turrets with health that allies can heal and buff like players, wearing down over time instead of a timer, dying at 0, with a bit more range and damage that falls off over it.
- **World server:** `Turret.cs` is a vulnerable `StaticObject` with the blueprint's `hp` (`ActivateEffect.TurretHp`, default 600) and a `MaximumHealth` stat; each tick it wears `hp / duration` (Armored x2/3, defense boost -1% a point to -50%, Invulnerable/Invincible none, Bleeding +20/s, Healing -20/s unless Sick) and blows up at 0 (`Detonate`, as before). `Heal`, `Buff`, `Boost` (MaxHP raises max and health; Attack, Dexterity, Defense as percents; `noStack` refreshes) and `TurretsNear` take allies' abilities; `Player.Turrets.cs` `HealTurrets`, `BuffTurrets` (positive conditions only), `BoostTurrets` are called from HealNova, VampireBlast, ConditionEffectAura, StatBoostAura, the tomes' shared conditions and boosts, RemoveNegativeConditions (cleanse), Disco Lights (`Player.UseItem.Doomed.cs`) and Nerissa's tide (`Player.CruciblePowers.cs`). Damaging, Attack boosts and Weak scale its shots and Bastion slam; Berserk and Dexterity its fire rate; Paralyzed stops it firing. Looks 55/56 now carry how long its health lasts at its plain wear. Falloff: `Turret.Falloff` (full to 3 tiles, half at its reach), passed as `ServerPlayerShoot.FalloffReach` (not sent) into `ValidatedProjectile` and applied in `Player.EnemyHit`. Player shots no longer hurt a turret as a vulnerable static object. `Turret` is public now, for the tests.
- **Content:** both `DoomedRealms.xml` copies: the blueprints' `hp`, Tower1-9 and SpecialTower shots 680 ms (were 560); the Mechanic's description. `tools/build_crucible.py`: the Bastion Turret's `hp="1100"`, its shot 960 ms (was 800) and description; rerun, both `CrucibleObjects.xml` copies (the rest of its output unchanged).
- **Client:** `particles/turret.rs` (the plate's ring shows health; its last-seconds warning and the plate's end come from the health left), `view.rs` (a health bar under turrets), `bsk-world` `combat.rs` (`turret_falloff`, used for our own turret shots' hits), `hud.rs` (tooltip: HP and wear, falloff, "Allies' heals and buffs reach it"; the short line names its HP), translations in every `app/lang/*.json`.
- **Redeploy:** world servers and client together. An older client still sees turrets and their shots, but its plate disappears at the old time even if the turret was healed, it shows no health bar and its damage numbers ignore the falloff (the server's are right). Nobody is disconnected. No Redis data changed.
- **Wiki:** `tools/wiki_gear.py` (the blueprints' line: HP, wear, falloff, blast at 0 health), `website/wiki/classes.md` (the Mechanic's turrets), `crucible.md` (the Bastion Turret), `gear-statistics.md` (a note that the Mechanic's measured row predates this).
- **Docs:** SETUP-LOCAL.md "Changes from upstream"; AGENTS.md (a ground rule to write down testing pitfalls) and client-rs/AGENTS.md (the pitfalls met here).
- **Not done:** enemies don't target or damage turrets (only wear does); pets heal only their owner, not turrets. The balance (HP per tier, falloff to half) hasn't been playtested.

## Checks

- Turrets: `dotnet test source\Tests` (new `EveryTurretHasHealthAndReach`, `ATurretsShotWeakensOverItsReach`), `cargo test -p bsk-world` (`a_turrets_shot_weakens_over_its_reach`), `cargo test -p bsk-client` (translations), both builds, the wiki built to a scratch folder (the blueprints' lines read right). In game on an isolated stack: the Mechanic joined, but scripted ability presses never built a turret (MP stayed full), so the health bar, plate ring, heals and falloff weren't seen in game.

- `dotnet build source\WorldServer\WorldServer.csproj`: no errors. Not tried in game.
