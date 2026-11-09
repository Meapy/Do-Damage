Release 2.4.8: the Warcaller rolls your loot on a reel, no potions back from a death; Dagger of Divine Wrath and Wand of Absolution show the right sprites; tomes give Swift, half of Speedy (deploys the client and the world server; nobody is disconnected)

## For players

- **Community goals:** claiming at the Warcaller now plays each loot roll on a reel, as the death spin does: it scrolls past the goal's loot and slows onto what you won, roll after roll ("Loot roll 3 of 14"), with everything won so far lined up below. Stop (or a click, Space, Enter) lands the roll turning, Skip all (or Escape) lands the rest. The chat lines ("Your 15 rewards went to the gift chest…", "struck it lucky!") and the window's "Claimed!" line wait until the last roll has landed, so nothing gives the rolls away. What you get is unchanged and already in your gift chest.
- **Items:** the Dagger of Divine Wrath and the Wand of Absolution, and their shots, showed each other's new art; each now shows its own.
- **Classes:** a Priest's tome gives Swift instead of Speedy: half Speedy's speed boost (+17.5% movement instead of +35%), for the same time, to the Priest and the players around them (Tome of Holy Guidance, Hallowed Language, Sacred Thought, the Night Sky, Nativity Tome and the other tomes that gave Speedy). The tooltip and the wiki say Swift.
- **Death:** health and magic potions (Minor, regular and Greater) are no longer among the items that can come back to your gift chest when a character dies; the other carried items keep the same chances.

## Details

### The Warcaller's loot reel

- Before, a claim's loot rolls were paid and only named in a line at the foot of the window. Now the server sends each roll with a reel to play, and the window plays it as the death screen plays the death spin.
- Server: `CommunityData.Reel` (Shared) builds a reel of 40 lines of the goal's loot at the roll's odds (the rarer weighted up a little to tease), with the prize at line 34. `CommunityGoals.Claim` keeps each roll's Overdrive level and sends the rolls (item, count, rarity, stop, reel of item and rarity) at the end of the `CommunityNotice` that answers the claim (`CommunityRollInfo`). Paying, recording and the server-wide announcement are unchanged.
- Client: `bsk-proto` reads the rolls as an optional tail of `CommunityNotice` (`CommunityRoll`, `CommunityReelSlot`); `community.rs` plays them over the window (`Reels`, `draw_reels`): 4.2 s for the first roll, 2.2 s each after when a claim has more than two, 1.1 s on each landed roll before the next, the tick sound per line passed and the landing sound by rarity. Stop / Next roll / Done and Skip all buttons; a click, Space or Enter steps on, Escape lands them all (again: puts the reel away). Closing the window puts it away. 7 new lines in all 10 language tables.
- Chat held back: from sending a claim, `game.rs` holds the server's own chat lines (no stars and no recipient: `SendInfo`'s "Your N rewards went…", the Warcaller's broadcasts such as "struck it lucky") in `claim_hold`; on the answer they go at once if it has no rolls (or was refused), else when `Community::reeling` turns false (last roll landed, or the window shut). With no answer in 5 s they go anyway. Players' chat is never held. The window's foot line ("Claimed! …") waits in the reel the same way.
- Compatibility: the rolls trail the message, so older clients read the notice as before (no reel); a new client on an older server gets no rolls and shows none.
- Redeploy: world server and client, together (the message gained a tail). Nobody is disconnected; nothing in Redis changes.
- Docs: changelog, the wiki's Community goals page ("Loot rolls"), SETUP-LOCAL.md "Changes from upstream".

### T15 dagger and wand sprites swapped back

- 2.4.7's hand-drawn `BladesAndT15s8x8.png` had the dagger's and the wand's rows the wrong way round (icon and shot, cells 34/35 and 70/71). The two rows are swapped in the image; the XML is unchanged (the Dagger of Divine Wrath still uses cells 34/35, the wand 70/71).
- Redeploy: client only (the sheet). Nobody is disconnected; nothing in Redis changes.

### No potions back from a death

- Why: the death keep picked any carried slot, so a character could get health and magic potions back from dying.
- Server: `Player.DeathSpin.cs` `RollDeathKeep` leaves out the six health and magic potions (`DeathKeepExcluded`: Minor, regular and Greater Health and Magic Potion) from the items it picks. The number of items rolled is unchanged, so a death with fewer other items can give back fewer. The potion stacks (the potion slots) were never part of it.
- Redeploy: world server only. Nobody is disconnected; nothing in Redis changes.
- Docs: changelog ("Death"), the wiki's Progression page ("Getting your items back"), SETUP-LOCAL.md "Death spin".

### Tomes give Swift, half of Speedy

- Why: tomes share Speedy with the whole party since 2.4.1; the owner asked for the tomes' Speedy to be half as effective.
- Server: new condition `Swift` (61, `ConditionEffectIndex.cs`). `Player.UseItem.cs` `SelfCondition` turns a tome's (slot type 4) `ConditionEffectSelf` Speedy into Swift, for the Priest (`AEConditionEffectSelf`) and the players it reaches (`ShareTomeCondition`, assist credit as Swift). Speed: `StatsManager.MoveSpeed` x1.175 (Speedy x1.35), `GetSpeed` (the speed check) x1.2 (Speedy x1.4), `Entity.GetSpeedMultiplier` x1.25 (Speedy x1.5); Speedy wins when both are on. The item XML is unchanged, so the Benediction imprint still skips Speedy for a speed tome.
- Client: `condition.rs` `SWIFT` (and `EFFECTS` grows to 62 with Mana Deplete, 60, no icon, and Swift, 61, Speedy's icon); `movement.rs` x1.175 under Swift; `hud.rs` shows a tome's Speedy as "Swift" with a line "Swift: half the speed boost of Speedy" (1 new line in all 10 language tables).
- Wiki: `tools/wiki_gear.py` writes "Swift (half Speedy's speed boost)" for a tome's Speedy (tome gear page, Priest-like class pages, Gear statistics); `website/wiki/classes.md` explains Swift.
- Compatibility: an older client doesn't know condition 61, so it shows no icon and walks at normal speed under Swift (slower than the server allows: no rubber-banding); its tooltip still says Speedy.
- Redeploy: world server and client (together for the speed to show). Nobody is disconnected; nothing in Redis changes.
- Docs: changelog ("Classes"), the wiki, SETUP-LOCAL.md ("Tomes buff the players around the priest").

## Checks

- Loot reel: `dotnet test source\Tests` community tests (11, with a new one for the reel: 40 lines, the prize at its stop, only the goal's loot); the world server builds. `cargo test -p bsk-proto -p bsk-client` (a golden test for the notice with and without rolls, a test of stepping and skipping the reel, the language tables). In game on an isolated stack (slot 7): `/goaladmin add 100000`, then Claim all on "Down with the Mad God" (13 rewards) played 14 rolls on the reel; a `--shot` at roll 3 showed a Rare 25 Shards of the Mad God landed, the rolls won so far below, Skip all and Next roll. Chat held back, on the same stack: mid-reel shot (roll 2 of 14) with no reward lines in the chat; then a claim of 11 rolls sent at 13:14:10 and Skip all at 13:14:18, and the log put "Your 15 rewards went…", "struck it lucky" and the bonus goal line in the chat at 13:14:18; a shot after showed Done with all 11 rolls below and the lines in the chat and at the foot. Not looked at: the reel in German or Russian.
- Dagger and wand: the sheet's T15 column rendered after the swap (row 2 the dagger and its shot, row 4 the wand and its). Not looked at in game.
- No potions back from a death: the world server builds (0 errors). Not tried in game (no death on an isolated stack).
- Tome Swift: the world server builds (0 errors); `cargo test --workspace` in client-rs passes (`speed_formula` checks Swift x1.175 and that Speedy wins over it; `tome_buffs_show_their_reach` checks a tome's Speedy shows as Swift; the language tables). The wiki built to a scratch folder shows Swift on the tome page, Gear statistics and the Priest-like class pages. Not tried in game on an isolated stack.
