Release 2.6.11

## For players

## Details

### Working notes brought up to date

The four AGENTS.md guides checked against the code and rewritten: nothing players see changes, and
nothing redeploys.

- `client-rs/AGENTS.md`: the testing notes regrouped by topic (pitfalls, the bot, the scripted
  flags, editing the test copy's data) instead of a dated line each; duplicates and
  contradictions merged (window size, which character plays, when the world server is ready); the
  fixed `--effect` limit dropped; the build line fixed (`cargo run -p bsk-client`); undocumented
  flags added (client `--look`, `--champions`, `--display-of`; bot `--char`, `--vault`).
- `source/AGENTS.md`: sections merged (quests, pass and commands), testing tips gathered under
  "Build and test", the `alive.<id>` set and the test copy's redis-cli example (no longer the
  owner's account) added.
- `client/AGENTS.md`: cut to what still matters for a deprecated client: its `src/` as the game's
  data, and the essentials if the owner asks for a change; the AIR test client and the old
  address dropped.
- `AGENTS.md`: how to pick the release branch when old ones linger, rebasing after a release is
  squashed mid-session, naming the Doomed Realms content, folding testing pitfalls into the notes
  they belong with.

## Checks

- Every path, file and flag the guides name checked against the tree and the argument parsers.
