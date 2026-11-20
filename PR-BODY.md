Release 2.6.3: skins sold at the Armoire; deploys client and world servers (players are not disconnected)

## For players

- **Skins:** the **Armoire** in the Nexus (and in guild halls) sells skins now. It shows every skin of your character's class: the ones you own to put on, the rest with their price (200 gold, in red when you're short), the level a skin still needs, or "Realm Pass" for the pass's season skins. Pick one for sale and it asks first ("Buy Merlin for 200 gold?", with your gold); buying puts it straight on, and it's yours on every character of that class, as when bought on a class's page.

## Details

### Skins sold at the Armoire

- **World server** (`ReskinHandler.cs`): a `Reskin` for a skin the account doesn't own now buys it, with the account server's rules (`/account/purchaseSkin`): not a Realm Pass skin, the class at the skin's level (the best of the class's stats and the character's own level), and its `<Cost>` (200 gold by default), through `Database.PurchaseSkin` (an atomic `HINCRBY` on credits, given back if it goes below 0). The player's gold stat follows, a chat line says "Skin bought for 200 gold." or why not, and the skin goes on.
- **Client** (`npc.rs`, `main.rs`): the Armoire window lists every selectable skin of the class (`screens::class_skins`) in rows of five with its state under the name: nothing when owned, its price in gold (red when short), "Level N", or "Realm Pass"; locked ones are dimmed and can't be clicked. One for sale opens a question in the window with the skin, "Buy {name} for {cost} gold?", "Yours for good, on every character of this class." and the gold (or "Not enough gold", without a Buy button); Back or Escape returns to the shelves. Once the character wears the bought skin, the client adds it to the account's owned skins, so the character list and the Armoire show it owned without signing in again.
- One new line, translated in every `client-rs/app/lang/*.json`; the rest were there.
- `client-rs/CHANGELOG.md`, `website/wiki/getting-started.md` (where skins are bought), `SETUP-LOCAL.md` ("Changes from upstream").
- **Redeploys:** world servers and client; no message changes. Older clients only ever send `Reskin` for skins they own, so they behave as before. Nobody is disconnected. Redis: no migration; purchases write `credits` and `skins` as the account server does.

## Checks

- `dotnet build` of the world server; `cargo test -p bsk-client` (111, with the i18n checks and a new `the_armoire_asks_before_buying` test).
- Isolated stack (slot 1) on a copy of the PC's old data, the test account's character a level 20 Wizard with 450 gold and no skins, `--npc ReskinVendor`: the Armoire showed Classic worn, 14 skins at 200 gold and Frostcrown Archmage as "Realm Pass". Clicking Merlin asked "Buy Merlin for 200 gold?" with "You have 450 gold."; Buy put Merlin on, the gold went to 250 (Redis too), and chat said "Skin bought for 200 gold." Signed in again with 100 gold: Merlin owned and worn, the other prices in red, and Elder Wizard's question said "Not enough gold" with only Back; the gold stayed 100.
