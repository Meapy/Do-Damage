# Parity with the AIR client

Everything the AIR client (`client/src`) lets a player do, and where the Rust client stands. The
Rust client replaces the AIR one only when nothing here is left at "To do" (or knowingly dropped).
Update the status when a feature lands.

Sources: the screens (`com/company/assembleegameclient/screens`, `kabam/rotmg/ui/view`), the
server messages it handles (`GameServerConnection.mapMessages`), the account server calls it makes
(`sendRequest`), and the panels of interactive objects (`getPanel`).

## Account and screens

| Feature | AIR | Status |
|---|---|---|
| Sign in, remember the login | `AccountScreen`, `WebLoginDialog` | Done |
| Register | `/account/register` | Done |
| Change password | `/account/changePassword` | Done |
| Forgot password / recovery | `/account/forgotPassword` | Dropped: the server has no mail |
| Character list, play, delete | `CharacterSelectionAndNewsScreen` | Done |
| Buy a character slot (gold or fame) | `BuyCharacterRect`, `/account/purchaseCharSlot` | Done |
| New character, locked classes | `NewCharacterScreen` | Done |
| Skins: pick one for a new character, buy skins | `CharacterSkinView`, `/account/purchaseSkin` | Done |
| Class unlock purchase | `/char/purchaseClassUnlock` | Dropped: the server answers "Invalid Endpoint" |
| Server choice | `ServersScreen` | Done: a Servers screen (button or the "Server:" pill on the character list) with each server's load, and arrows on the pill |
| Legends (fame list: week, month, all time) | `LegendsView`, `/fame/list` | Done |
| Death summary (fame breakdown) | `FameView`, `/char/fame` | Done |
| Graveyard (an account's dead characters, each opening its fame summary) | `Graveyard`, `<News>` | Done |
| Rank stars: the account's rank and its legend, each character's class quest, each class's five quests and next goal, the quests left | `FameUtil`, `RankText`, `RankToolTip`, `CurrentCharacterRect`, `CharacterBox`, `ClassToolTip` | Done |
| Choose a name (accounts without one) | `ChooseNameRegisterDialog`, `/account/setName` | Dropped: the server answers "Unknown Error"; names come from registering |
| Credits screen, news | `CreditsScreen`, `/app/globalNews` | Dropped |
| Map editor | `NewEditorScreen` | Dropped |

## In the game

| Feature | AIR | Status |
|---|---|---|
| World, walls, objects, ground, 3D | `Map`, `Square`, objects | Done |
| Particles and effects (`ShowEffect`, hit sparks, death bursts, flashes, camera shake) | `particles/*` | Done |
| Objects' own effects (`<Effect>`: custom particles, XML particles, circles, explosions, fountains, vents, vortexes, sky beams, hearts, snowflakes) | `ParticleEffect.fromProps` | Done |
| Condition effects: icons over players and monsters; grey (paused, stasis, petrified), red (cursed), faint (invisible), low-health pulse; the world blurred (drunk), dark (blind) or grey (paused); hallucinating, confused, unstable, paralyzed | `ConditionEffect`, `Map.getFilterIndex`, `Player.getTexture` | Done |
| Other players' names (party, guild and chosen-name colours, rank star); realm portals' names and player counts | `Player.drawName`, `Portal.draw` | Done |
| Area attacks (`Aoe`) | `onAoe` | Done (the server applies the damage; the client draws the blast) |
| Shooting, abilities, hits | `Player`, `Projectile` | Done |
| HUD: minimap, bars, equipment, inventory, backpack, stats, potions | `HUDView` | Done |
| Minimap zoom (buttons, - and =, the mouse wheel), player tooltips and menus on the minimap; the map's name | `MiniMap` | Done |
| Nearby players' details on hover: rank, health, magic, gear | `PlayerToolTip` | Done |
| Your rank at the top right, above your gold (maps that show it), speakers' rank stars in the chat | `GameSprite.rankText_`, `TextBoxLine` | Done |
| Item tooltips, compared with what's worn in that slot (better green, worse red) | `EquipmentToolTip` | Done |
| Items: swap, drop, use, equip, bags; Ctrl+click to the other page, Ctrl+right-click to break down; health and magic potions into their stacks (drag or double-click); cloth and dyes in their colours | `ItemGridMediator`, `BreakdownSlot` | Done |
| Chat, slash commands, speech balloons; editing the line (cursor, selection, words, copy, cut, paste, undo) | `TextBox`, `onText` | Done |
| The quest's top three damage dealers under the boss bar | (new: `QuestDamage`) | Done |
| Tells: Tab cycles who to whisper; G guild and P party chat; Page Up/Down history | `TextBox` | Done |
| Notifications over objects, global notifications (keys) | `onNotification`, `onGlobalNotification` | Done |
| Quest arrow | `QuestObjId`, `QuestArrow` | Done |
| Boss health bar, with the share of damage we dealt | `BossHealthBar`, `DamageCounter` | Done |
| Dungeon keys view | `KeysView` | Done |
| Legendary and mythical drop pop-ups | `LegendaryPopUpSignal` | Dropped: the AIR client never sends these signals |
| Portals, Nexus, Escape menu | `PortalPanel`, `onReconnect` | Done |
| Player menu: trade, invite, teleport, whisper, lock, ignore; nearby players | `PlayerMenu`, `PartyPanel`, `EditAccountList` | Done (opened from the nearby list, the minimap and names in the chat) |
| Trading | `TradePanel`, `onTrade*` | Done |
| Party: invite, join | `PartyPanel`, `onInvitedToParty` | Done |
| Guild: invite and join | `GuildInvitePanel`, `onInvitedToGuild` | Done |
| Guild: create, renounce, board (read and write), member list (promote, demote, remove), hall portal | `Guild*Panel`, `/guild/*` | Done |
| Merchants and shops (and their balloons) | `SellableObjectPanel`, `Merchant`, `onBuyResult` | Done |
| Market: search, offers (sorted), buy, sell for fame, your offers, take back | `MemMarket`, `onMarket*Result` | Done |
| Forge | `ForgePanel` | Dropped: no map places a Forge (the server's `/forge` works from chat) |
| Potion storage: store, take, drink, drink to the maximum, upgrade | `PotionsPanel`, `/account/grabPotions` | Done |
| Character changer, the Armoire (skins) | `CharacterChangerPanel`, `ReskinPanel` | Done |
| Name changer | `NameChangerPanel`, `onNameResult` | Done |
| Stat NPC, bounty board, money changer | `StatNPCPanel`, `BountyBoardPanel`, `MoneyChangerPanel` | Dropped: no map places them |
| Arena wave and clock | `onImminentArenaWave`, `ArenaMenu` | Done |
| Pictures from the server (`Pic`) | `onPic` | Dropped: the server never sends `Pic` |
| Platform stats and file downloads (`ClientStat`, `File`) | `onClientStat`, `onFile` | Dropped: Kongregate/Steam stats only, and the server never sends `File` |
| Keys: switch inventory tabs (B), center on player (X), frame rate (L), vault, realm, guild hall and world-party shortcuts | `MapUserInput` | Done |
| Options: key bindings, graphics (particles, shadows, bars, balloons, others' shots, damage numbers, rotation), sound | `Options`, `Parameters` | Done (kept in `settings.txt` or the browser's storage) |
| Sound effects and music (shots, hits, deaths, loot, level up, potions, items, errors, buttons, `PlaySound`, map music) | `SoundEffectLibrary`, `Music` | Done |
| Emotes (in chat and speech balloons, animated ones too) | `Emotes`, `EmoteGraphicHelper` | Done |
| Death: back to the character list with the summary | `onDeath` | Done |
