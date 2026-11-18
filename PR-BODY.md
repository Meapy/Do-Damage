Release 2.6.1: Necromantic items drop half as often; deploys servers and website (players are not disconnected)

## For players

- **Dungeons:** the untiered item an Empowered Elder boss drops (the Necromantic weapons, armor, rings and the rest) now drops at half the chance: 3% for each player who did enough damage, down from 6%.

## Details

### Necromantic item drop rate

- **What changed:** `UT_CHANCE` in `tools/build_resurrection.py` went from 0.06 to 0.03, and `source/Shared/resources/data/resurrection.json` was regenerated (only each boss's last `loot` chance changed). The XML the generator also writes was left as it is, so the community sprite taken in earlier is kept. The wiki's Necromantic Relic page reads the chance from the data, so it follows on the next site build.
- **Deploys:** world server and website. Nobody is disconnected; no Redis changes.

## Checks

- `python tools/build_resurrection.py`, then the diff of the JSON checked: 28 bosses, each last loot chance 0.06 to 0.03, nothing else.
