Release 2.5.3: out of combat speed and single-stat max potion sprites (deploys the client and the servers; nobody is disconnected)

## For players

- **Game:** out of combat you walk faster: +20 Speed (shown in green beside your Speed) whenever no enemy has hit you and you haven't hit one for 5 seconds. It goes the moment either happens and comes back after 5 quiet seconds.
- **Items:** each of the eight single-stat Potions of Max has its own sprite.

## Details

### Out of combat speed

`Player.Combat.cs` keeps when the player was last in combat: hurt by an enemy (`PlayerHit`, or `Player.Damage` from an `Enemy`) or hurting one (`EnemyHit`, or `Enemy.Damage` credited to them, which also covers their bleeds and pet hits credited to them). `Player.Tick` flips the state back after 5 s (`CombatMs`) and recalculates the stats. `BoostStatManager.ApplyOutOfCombat` adds +20 Speed (`Player.OutOfCombatSpeed`) as a stat boost, flat after the Apotheosis scaling. The client needs no change: it reads the Speed stat and its bonus as before, so older clients get it too.

- Redeploy the world server. No Redis change, nobody disconnected.
- A world change gives a new player object, so you arrive in a new world out of combat.
- Ground damage (lava and the like) doesn't count as combat.

### Single-stat max potion sprites

Each of the eight Potions of Max uses its own supplied sprite, Life to Wisdom, appended to `drLofiObj2` at tiles `0x100`–`0x107` by `tools/port_doomed.py` from `tools/doomed/single_stat_max_potions.png`. Both generated XML copies reference their tile. Redeploy the client and the world server; no Redis change.

## Checks

- **Out of combat speed:** world server builds. On an isolated stack with the Rust client: in the Nexus a Mechanic showed SPD 30 +20; a Wizard in the realm showed SPD 76 +26 before fighting and 56 +6 while hitting spawned Pirates. Not seen in game: the bonus coming back after a fight. In the vault test, hits on the Training Dummy kept arriving about every 1.6 s after the scripted fire stopped, so the player stayed in combat. That return path is the same tick and recalculation that starts the bonus.
