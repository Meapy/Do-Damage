Release 2.4.8: the Warcaller rolls your loot on a reel; Dagger of Divine Wrath and Wand of Absolution show the right sprites (deploys the client and the world server; nobody is disconnected)

## For players

- **Community goals:** claiming at the Warcaller now plays each loot roll on a reel, as the death spin does: it scrolls past the goal's loot and slows onto what you won, roll after roll ("Loot roll 3 of 14"), with everything won so far lined up below. Stop (or a click, Space, Enter) lands the roll turning, Skip all (or Escape) lands the rest. What you get is unchanged and already in your gift chest.
- **Items:** the Dagger of Divine Wrath and the Wand of Absolution, and their shots, showed each other's new art; each now shows its own.

## Details

### The Warcaller's loot reel

- Before, a claim's loot rolls were paid and only named in a line at the foot of the window. Now the server sends each roll with a reel to play, and the window plays it as the death screen plays the death spin.
- Server: `CommunityData.Reel` (Shared) builds a reel of 40 lines of the goal's loot at the roll's odds (the rarer weighted up a little to tease), with the prize at line 34. `CommunityGoals.Claim` keeps each roll's Overdrive level and sends the rolls (item, count, rarity, stop, reel of item and rarity) at the end of the `CommunityNotice` that answers the claim (`CommunityRollInfo`). Paying, recording and the server-wide announcement are unchanged.
- Client: `bsk-proto` reads the rolls as an optional tail of `CommunityNotice` (`CommunityRoll`, `CommunityReelSlot`); `community.rs` plays them over the window (`Reels`, `draw_reels`): 4.2 s for the first roll, 2.2 s each after when a claim has more than two, 1.1 s on each landed roll before the next, the tick sound per line passed and the landing sound by rarity. Stop / Next roll / Done and Skip all buttons; a click, Space or Enter steps on, Escape lands them all (again: puts the reel away). Closing the window puts it away. 7 new lines in all 10 language tables.
- Compatibility: the rolls trail the message, so older clients read the notice as before (no reel); a new client on an older server gets no rolls and shows none.
- Redeploy: world server and client, together (the message gained a tail). Nobody is disconnected; nothing in Redis changes.
- Docs: changelog, the wiki's Community goals page ("Loot rolls"), SETUP-LOCAL.md "Changes from upstream".

### T15 dagger and wand sprites swapped back

- 2.4.7's hand-drawn `BladesAndT15s8x8.png` had the dagger's and the wand's rows the wrong way round (icon and shot, cells 34/35 and 70/71). The two rows are swapped in the image; the XML is unchanged (the Dagger of Divine Wrath still uses cells 34/35, the wand 70/71).
- Redeploy: client only (the sheet). Nobody is disconnected; nothing in Redis changes.

## Checks

- Loot reel: `dotnet test source\Tests` community tests (11, with a new one for the reel: 40 lines, the prize at its stop, only the goal's loot); the world server builds. `cargo test -p bsk-proto -p bsk-client` (a golden test for the notice with and without rolls, a test of stepping and skipping the reel, the language tables). In game on an isolated stack (slot 7): `/goaladmin add 100000`, then Claim all on "Down with the Mad God" (13 rewards) played 14 rolls on the reel; a `--shot` at roll 3 showed a Rare 25 Shards of the Mad God landed, the rolls won so far below, Skip all and Next roll. Not looked at: the last roll's Done state in game, and the reel in German or Russian.
- Dagger and wand: the sheet's T15 column rendered after the swap (row 2 the dagger and its shot, row 4 the wand and its). Not looked at in game.
