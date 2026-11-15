Release 2.5.12: Empowered Forgotten King tuning; deploys the world server (rolling, nobody is disconnected)

## For players

- **Dungeons:** the Empowered Forgotten King can always be hurt once he has risen: his hiding, crystal and phase shields no longer make him untouchable. His Last Court phase (the four pillars) hits half as hard.

## Details

### Empowered Forgotten King: always vulnerable, softer Last Court

- `Empowered.AlwaysVulnerable` (true for the Forgotten King once his 2.5 s rise is over): `Enemy.Damage` ignores Invincible, Stasis and Invulnerable for him, and `Arena.Shield` (the phases' short shields) does nothing. His Elder behaviour's intro and crystal states still run; they just don't protect him.
- The Last Court's pillar shots take `EmpoweredSignatures.LastCourtDamage` (0.5): `Strike.FireFrom` / `Arena.ShootFrom` gained a `damageScale` argument.
- Files: `core/objects/Enemy.cs`, `core/resurrection/Empowered.cs`, `EmpoweredPhases.cs`, `EmpoweredSignatures.cs`, `logic/behaviors/BlackBetween.cs`.

Deploys the world server only (rolling). No client change, no Redis data.

## Checks

- World server builds; `dotnet test source/Tests`, 290 passed. Not played through in game on an isolated stack.
