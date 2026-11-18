Release 2.6.1: Necromantic items drop half as often; deploys servers and website (players are not disconnected)

## For players

- **Dungeons:** the untiered item an Empowered Elder boss drops (the Necromantic weapons, armor, rings and the rest) now drops at half the chance: 3% for each player who did enough damage, down from 6%.
- **Dungeons:** Empowered Malus' blood moon ritual heals him far less: each cultist soul still standing when the chant ends gives back 1% of his health, down from 2.5% (at most 3% a ritual instead of 7.5%). Players on their own could not out-damage it.

## Details

### Necromantic item drop rate

- **What changed:** `UT_CHANCE` in `tools/build_resurrection.py` went from 0.06 to 0.03, and `source/Shared/resources/data/resurrection.json` was regenerated (only each boss's last `loot` chance changed). The XML the generator also writes was left as it is, so the community sprite taken in earlier is kept. The wiki's Necromantic Relic page reads the chance from the data, so it follows on the next site build.
- **Deploys:** world server and website. Nobody is disconnected; no Redis changes.

### Empowered Malus' ritual heal

- **What changed:** `EmpoweredSignatures.RitualHealShare` (new, 0.01) replaces the 0.025 per surviving soul in the Blood Moon Ritual (`source/WorldServer/core/resurrection/EmpoweredSignatures.cs`). The ritual is his only heal; it runs every 17 seconds below 40% with him shielded for 6.5 of them, so a whole ritual left standing healed about 29,000 of a solo Malus' ~385,000 health, more than one player deals in the time between. `source/Tests/EmpoweredPhaseTests.cs` caps a full ritual's heal at 3%. The Necromantic Relic wiki guide now gives the amount.
- **Deploys:** world server and website. Nobody is disconnected; no Redis changes.

## Checks

- `python tools/build_resurrection.py`, then the diff of the JSON checked: 28 bosses, each last loot chance 0.06 to 0.03, nothing else.
- `dotnet test source\Tests --filter EmpoweredPhaseTests`: 6 passed. Not tried in game.
