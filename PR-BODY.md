Release 2.6.3: skins sold at the Armoire, Scrying Glass's Ask to join and gear; deploys client and world servers (players are not disconnected)

## For players

- **Skins:** the **Armoire** in the Nexus (and in guild halls) sells skins now. It shows every skin of your character's class: the ones you own to put on, the rest with their price (200 gold, in red when you're short), the level a skin still needs, or "Realm Pass" for the pass's season skins. Pick one for sale and it asks first ("Buy Merlin for 200 gold?", with your gold); buying puts it straight on, and it's yours on every character of that class, as when bought on a class's page.
- **Scrying Glass:** the big view now shows the gear of the player you're watching, in the picture's corner under their level and class (hover an item for its tooltip, imprints and all). Its new **Ask to join** button sends them the `/visit` request: they get the Yes/No box, and a Yes takes you straight to them, into a realm too (which `/visit` itself doesn't reach). The reply ("Asked…", "didn't let you in", a cooldown) shows under the picture.

## Details

### Skins sold at the Armoire

- **World server** (`ReskinHandler.cs`): a `Reskin` for a skin the account doesn't own now buys it, with the account server's rules (`/account/purchaseSkin`): not a Realm Pass skin, the class at the skin's level (the best of the class's stats and the character's own level), and its `<Cost>` (200 gold by default), through `Database.PurchaseSkin` (an atomic `HINCRBY` on credits, given back if it goes below 0). The player's gold stat follows, a chat line says "Skin bought for 200 gold." or why not, and the skin goes on.
- **Client** (`npc.rs`, `main.rs`): the Armoire window lists every selectable skin of the class (`screens::class_skins`) in rows of five with its state under the name: nothing when owned, its price in gold (red when short), "Level N", or "Realm Pass"; locked ones are dimmed and can't be clicked. One for sale opens a question in the window with the skin, "Buy {name} for {cost} gold?", "Yours for good, on every character of this class." and the gold (or "Not enough gold", without a Buy button); Back or Escape returns to the shelves. Once the character wears the bought skin, the client adds it to the account's owned skins, so the character list and the Armoire show it owned without signing in again.
- One new line, translated in every `client-rs/app/lang/*.json`; the rest were there.
- `client-rs/CHANGELOG.md`, `website/wiki/getting-started.md` (where skins are bought), `SETUP-LOCAL.md` ("Changes from upstream").
- **Redeploys:** world servers and client; no message changes. Older clients only ever send `Reskin` for skins they own, so they behave as before. Nobody is disconnected. Redis: no migration; purchases write `credits` and `skins` as the account server does.

### Scrying Glass: Ask to join and the watched player's gear

- **World server** (`SpectatorHub.cs`, `Player.Visits.cs`, `MessageIds.cs`; `Shared/VisitRules.cs`, `Shared/VisitBook.cs`): a new `SPECTATE` action, 8, from the big view asks the one shown to let the viewer in, through `Player.AskToVisit(glass: true)`: the same request, box, cooldowns, `/yes` and `/no` as `/visit`. `VisitBook.Request.Glass` remembers it came from the glass, and `VisitRules.Refusal(realm: true)` then lets a Yes take the visitor into a realm too (refused when it's closing, `Place.Closed`, full, or they're there already); a realm trip isn't marked as "been in". Everything else is refused as `/visit` refuses it.
- **Client** (`glass.rs`, `main.rs`): an **Ask to join** button beside "Stay on this player" (hidden while the glass shows the Nexus), the server's reply lines since the ask shown under the picture for 8 s (green, or red for an error), and a box at the picture's foot with the watched player's level, class and four equipment slots (imprint marks, the item tooltip on hover). The gear comes from the public stats the feed already carried; no new message.
- One new line ("Ask to join"), translated in every `client-rs/app/lang/*.json`.
- `client-rs/CHANGELOG.md`, `website/wiki/getting-started.md` and `commands.md`, `SETUP-LOCAL.md` (under the Scrying Glass entry).
- **Redeploys:** world servers and client, together. An older server ignores action 8 (nothing happens on the click); older clients never send it. Nobody is disconnected. Redis: nothing new.

## Checks

- Scrying Glass: `dotnet build` of the solution; `dotnet test --filter Visit` (35, with a new `TheGlassReachesRealmsButNotTheRest`); `cargo test -p bsk-client` (110 with the i18n checks). Isolated stack (slot 2), two new accounts: a bot (`bsk-bot --say /realm2 --visit-answer yes`) in Realm 2, the client in the Nexus with `--npc ScryingGlass`. The big view showed the bot's Level 20 Wizard with its Energy Staff and robe in the gear box, and hovering the staff showed its tooltip. Clicking Ask to join: the bot logged the visit request "into Maelstrom", answered Yes, and the client's chat said "Asked Targetabc…" then "Targetabc let you in." and it arrived in Realm 2. With `--visit-answer no`: "Targetabc didn't let you in." showed in red under the picture.

- `dotnet build` of the world server; `cargo test -p bsk-client` (111, with the i18n checks and a new `the_armoire_asks_before_buying` test).
- Isolated stack (slot 1) on a copy of the PC's old data, the test account's character a level 20 Wizard with 450 gold and no skins, `--npc ReskinVendor`: the Armoire showed Classic worn, 14 skins at 200 gold and Frostcrown Archmage as "Realm Pass". Clicking Merlin asked "Buy Merlin for 200 gold?" with "You have 450 gold."; Buy put Merlin on, the gold went to 250 (Redis too), and chat said "Skin bought for 200 gold." Signed in again with 100 gold: Merlin owned and worn, the other prices in red, and Elder Wizard's question said "Not enough gold" with only Back; the gold stayed 100.
