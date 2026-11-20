Release 2.6.2: skins cost 200 gold (they were all free by mistake), Realm Pass skins only from the pass, a [Gilded] chat title and the pass's Gilded perks tab, the sword classes take 5% less damage (was 15%); deploys client, servers and website (players are not disconnected)

## For players

- **Skins:** every skin was unlocked for everyone by mistake, the Realm Pass's season skins too. Each skin now costs **200 gold** (it said 300 where it showed a price at all), bought on a class's page when making a new character, once the class has reached the skin's level; the skins that said "Special" are sold the same way. Whatever skin one of your living characters is wearing stays yours, so nobody loses the look they play. The Realm Pass's skins (Frostcrown Archmage, Winterguard, Aurora Ranger) come only from claiming them on the Gilded track: a character wearing one its account never claimed goes back to its class's own look.
- **Realm Pass:** a Gilded pass now gives a **[Gilded] title** before your name in the chat, in gold, with a soft light that runs across it every few seconds and a small glint as it leaves. The pass window has a new **Gilded perks** tab: everything a Gilded pass does besides its track (twice the `/visit` and `/summon` trips, a second chance in the Arena, luckier community goal loot, the chat title and the track itself), each card marked Active once your pass is Gilded.
- **Classes:** Frontline, the sword classes' passive (Warrior, Knight, Paladin, Pirate), now takes **5%** off every hit instead of 15%. The sword's healing (1.5% of its damage, at most 40 health a second), the Paladin's healing of allies and the Pirate's lucky dodge are unchanged.

## Details

### Skins priced, and the given ones taken back

- `source/Shared/resources/data/init.xml` had `<SkinsUnlocked />`, so every account was handed every skin on registering, on every sign-in (`Database.Verify`) and for guests, Realm Pass skins included; the client's character list and the server's checks then treated them as owned. The setting and its three uses are gone (`NewAccounts.cs`, `Database.cs`).
- `SkinDesc.Cost` defaults to 200 (no skin XML sets `<Cost>`). `/account/purchaseSkin` refuses unknown types, skins already owned, pass skins, a class below the skin's level and too little gold; `Database.PurchaseSkin` no longer adds the price to `totalCredits` (spending counted as gold earned), takes the gold atomically (puts it back if it went below 0), and adds the skin to the account's current list. `CreateCharacter` reloads the account's skins before checking and refuses unknown types instead of throwing.
- **Redis migrated, once:** `Database.TakeBackGivenSkins`, run by the account server at start (`CoreService`), guarded by the key `migrations:skinsPriced` (set NX, so it runs once on the shared Redis). Each account keeps the skins its living characters wear (not pass skins) and the pass skins its pass state shows claimed this season (free or Gilded tiers taken whose reward is a skin); the rest of its `skins` list is dropped. A living character wearing a pass skin its account didn't claim has its `skin` set to 0. The log says how many accounts and skins. Nobody could have bought a skin before (all were owned), so nothing paid for is lost.
- **World server:** a character loads in its skin only when its account owns it (`Player.cs`), so a pass skin can't be worn without the pass even if a character's saved skin says otherwise (e.g. a player online through the migration, whose character saves its old skin on leaving). The Armoire (`ReskinHandler`) already checked ownership.
- **Client** (`screens.rs`): skins show 200 gold (`skin_cost`, the server's default), "Special" skins are buyable like the rest, and a tile's price turns red when the gold is short of that skin's price (it compared with 300).
- Redeploys: account server (the migration and the purchase checks), world servers, client. Older clients still work: they show 300 for unpriced skins and "Special" ones unbuyable, and the server charges 200.

### [Gilded] chat title and the Gilded perks tab

- `Text` gains a trailing `Title` byte (0 none, 1 Gilded), set in `ChatManager.Say` from `Player.Pass.IsGilded`. Older clients ignore bytes past the fields they know (the Rust client's decoder and the AIR client both do); the Rust client reads it as `title: Option<u8>` (`crates/proto/src/messages.rs`), so older servers work too. Only local chat (the `Say` path) carries it; guild, party, all and tells go through the inter-server channel and don't yet.
- `hud.rs`: `ChatLine.title`, a `[Gilded] ` piece before the name (translated, as "Gilded" is), drawn by `draw_gilded_tag`: gold letters a light sweeps across every 4.2 s (0.9 s sweep) and a small sparkle as it leaves, still the rest of the time. The title is kept through a client update's hand-over (`update::ResumeChat.title`, defaulting to none). `pass::mix` and `pass::sparkle` are shared with the chat.
- `pass.rs`: a fourth tab, **Gilded perks** (`--pass perks`), a card each for the trips (`TRIPS`/`GILDED_TRIPS`, the server's `VisitRules`), the Arena save, the community goals' boost (when the pass data has one), the chat title and the Gilded track; lit gold with a slow glow and "Active" when Gilded, "Gilded only" otherwise.
- 14 new lines in every `client-rs/app/lang/*.json`.
- Redeploys: world servers and client. Players are not disconnected.

### Frontline cut 15% to 5%

- `Player.FrontlineCutPercent` (server, `Player.Frontline.cs`) and `FRONTLINE_CUT_PERCENT` (Rust client, `crates/world/src/combat.rs`, which predicts the damage numbers it shows) go from 15 to 5, still rounded to the nearest point. Tests updated: `FrontlineTests`, `frontline_rounds_as_the_server_does` (100 to 95, 160 to 152, 10 to 9).
- The class card's Frontline line (`screens.rs`) says 5%, in every `lang/*.json`; `website/wiki/classes.md` and SETUP-LOCAL.md say 5%.
- Redeploys: world servers and client. Until players update, an old client shows hits on sword classes about 10% smaller than the server deals (health comes from the server; only the floating numbers are off). Nobody is disconnected; no Redis data changes.

### Wiki

- `website/wiki/realm-pass.md`: the fourth tab, the chat title, pass skins only from the track. `website/wiki/getting-started.md`: skins cost 200 gold and where to buy them.

## Checks

- `dotnet build -c Release` of `source/` succeeds.
- `cargo test -p bsk-client -p bsk-proto` (translation checks, chat pieces with and without the Gilded title).
- `python tools/build_wiki.py --out <scratch>` builds; the Realm Pass page shows the new tab and title.
- Frontline at 5%: `dotnet test source\Tests --filter FrontlineTests`, `cargo test -p bsk-world frontline` and `cargo test -p bsk-client` pass; not tried in game.
- Isolated stack (slot 3) on a copy of the PC's old data, where every account held every skin: the account server's first start logged `Skins: 5 accounts kept 12 of the 3355 skins they had`; one account kept exactly the 9 skins its living characters wore. A restart skipped it (the key was set).
- `/account/purchaseSkin` on that stack: a pass skin refused, a plain Wizard skin bought for 200 (450 → 250 gold), buying it again refused, a skin with 150 gold refused, an unknown type refused, `totalCredits` untouched.
- The client on that stack: the Wizard's page shows the bought skin owned, the rest at 200 gold or their level, Frostcrown Archmage as Realm Pass. After `/passadmin gilded`, a chat line shows `[Gilded] <Tester> ...` in gold; the Gilded perks tab shows five Active cards (English and German). A character saved in Frostcrown Archmage, unowned, entered in its own look and saved skin 0.
